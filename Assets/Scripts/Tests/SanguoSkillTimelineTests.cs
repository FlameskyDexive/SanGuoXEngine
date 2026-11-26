using System;
using System.Collections.Generic;

using SanGuoXEngine.Battle;
using SanGuoXEngine.Data;

using Xunit;

namespace SanGuoXEngine.Tests;

/// <summary>
/// M4-3's pure skill rules: the event timeline's ordering and due windows, the combo switch
/// windows (SkillSwitch_Table) and the missile damage frames + shape resolution.
/// </span>
/// </summary>
public sealed class SanguoSkillTimelineTests
{
    private static SanguoSkill Skill(params SanguoSkillEvent[] events) => new()
    {
        ID = 100001,
        SwitchAllTime = 500,
        Events = [.. events],
        Switches =
        [
            new SanguoSkillSwitch { SwitchStartTime = 0, SwitchDuration = 750, SwitchMinPlayTime = 0, SwitchPutIn = 8, SwitchPutOut = 100009 },
            new SanguoSkillSwitch { SwitchStartTime = 200, SwitchDuration = 400, SwitchMinPlayTime = 350, SwitchPutIn = 1, SwitchPutOut = 100002 },
        ],
    };

    // ---------------------------------------------------------------- timeline

    [Fact]
    public void Timeline_FiresEventsInTimeOrder()
    {
        SanguoCastState cast = new(Skill(
            new SanguoSkillEvent { EventTime = 550, EventType = 1, EventIntParam = 900002, EventParam = 1 },
            new SanguoSkillEvent { EventTime = 200, EventType = 1, EventIntParam = 900001, EventParam = 2 }));
        // one big tick past both events: they come out sorted, not in declaration order
        List<SanguoSkillEvent> due = cast.Advance(1000f);
        Assert.Equal(2, due.Count);
        Assert.Equal(900001, due[0].EventIntParam);
        Assert.Equal(900002, due[1].EventIntParam);
        Assert.True(cast.Finished);
    }

    [Fact]
    public void Timeline_SplitsEventsAcrossTicks()
    {
        SanguoCastState cast = new(Skill(
            new SanguoSkillEvent { EventTime = 200, EventType = 1, EventIntParam = 900001, EventParam = 0 },
            new SanguoSkillEvent { EventTime = 550, EventType = 1, EventIntParam = 900002, EventParam = 0 }));
        Assert.Empty(cast.Advance(150f));                       // not yet
        List<SanguoSkillEvent> first = cast.Advance(100f);      // 250 ms: the 200 event fires
        Assert.Single(first);
        Assert.Equal(900001, first[0].EventIntParam);
        List<SanguoSkillEvent> second = cast.Advance(350f);     // 600 ms: the 550 event fires
        Assert.Single(second);
        Assert.Equal(900002, second[0].EventIntParam);
    }

    [Fact]
    public void Timeline_DurationAtLeastSwitchAllTime()
    {
        SanguoCastState cast = new(Skill());                     // no events
        Assert.False(cast.Finished);
        cast.Advance(499f);
        Assert.False(cast.Finished);
        cast.Advance(2f);                                        // past SwitchAllTime=500
        Assert.True(cast.Finished);
    }

    // ---------------------------------------------------------------- combo chain windows

    [Fact]
    public void ChainNext_OnlyInsideTheWindow()
    {
        SanguoSkill skill = Skill();
        Assert.Equal(0, SanguoSkillRules.ChainNext(skill, inputKey: 1, elapsedMs: 100f));  // before MinPlayTime
        Assert.Equal(100002, SanguoSkillRules.ChainNext(skill, inputKey: 1, elapsedMs: 350f));
        Assert.Equal(100002, SanguoSkillRules.ChainNext(skill, inputKey: 1, elapsedMs: 750f));  // 350+400 window end
        Assert.Equal(0, SanguoSkillRules.ChainNext(skill, inputKey: 1, elapsedMs: 800f));  // window closed
        Assert.Equal(100009, SanguoSkillRules.ChainNext(skill, inputKey: 8, elapsedMs: 100f)); // the dodge key, open from 0
        Assert.Equal(0, SanguoSkillRules.ChainNext(skill, inputKey: 2, elapsedMs: 400f));  // key with no entry
    }

    [Fact]
    public void ChainWindowOpen_ScanForAnyKey()
    {
        SanguoSkill skill = Skill();
        Assert.True(SanguoSkillRules.ChainWindowOpen(skill, 400f));
        Assert.False(SanguoSkillRules.ChainWindowOpen(skill, 850f));   // past every window
    }

    // ---------------------------------------------------------------- missile damage frames

    [Fact]
    public void MissileLands_WithinWindowOnInterval()
    {
        SanguoMissile missile = new()
        {
            DamageStartFrame = 3, DamageEndFrame = 12, DamageIntervalFrame = 5,
        };
        Assert.False(SanguoSkillRules.MissileLandsThisFrame(missile, 1));
        Assert.True(SanguoSkillRules.MissileLandsThisFrame(missile, 3));   // start
        Assert.False(SanguoSkillRules.MissileLandsThisFrame(missile, 5));  // not on the interval
        Assert.True(SanguoSkillRules.MissileLandsThisFrame(missile, 8));   // start+5
        Assert.False(SanguoSkillRules.MissileLandsThisFrame(missile, 11));
        Assert.True(SanguoSkillRules.MissileLandsThisFrame(missile, 12));  // end frame always lands
        Assert.False(SanguoSkillRules.MissileLandsThisFrame(missile, 13)); // past the window
    }

    // ---------------------------------------------------------------- hit resolution

    [Fact]
    public void ResolveHits_ConeseTheOppositeCamp_Only()
    {
        var origin = new Float2(0, 0);
        var forward = new Float2(0, 1);
        SanguoFieldActor ahead = KernelActor(1, new Float2(0, 3));       // enemy ahead in the cone
        SanguoFieldActor behind = KernelActor(1, new Float2(0, -2));     // enemy behind
        SanguoFieldActor friendly = KernelActor(2, new Float2(0, 3));    // same camp as the attacker
        SanguoFieldActor dead = KernelActor(1, new Float2(0, 3));
        dead.Health.ApplyHit(100000);

        SanguoMissile cone = new() { ShapeType = 2, ShapeParam1 = 5f, ShapeParam2 = 45f };  // 5 m, ±45°
        List<SanguoHit> hits = SanguoSkillRules.ResolveHits(
            cone, origin, forward, [ahead, behind, friendly, dead], attackerCamp: 2, attackerAtk: 100, multiplier: 1f);

        SanguoHit hit = Assert.Single(hits);
        Assert.Same(ahead, hit.Target);
        Assert.Equal(100 - 20, hit.Damage);                              // atk 100 - def 20, min 1
        Assert.Equal(20, ahead.Health.Hp);                               // 100 hp - the 80 damage
    }

    [Fact]
    public void ResolveHits_CircleAndBox()
    {
        SanguoFieldActor target = KernelActor(1, new Float2(2, 1));
        SanguoMissile circle = new() { ShapeType = 1, ShapeParam1 = 3f };
        Assert.Single(SanguoSkillRules.ResolveHits(circle, new Float2(0, 0), new Float2(0, 1),
            [target], attackerCamp: 2, attackerAtk: 50, multiplier: 1f));

        SanguoMissile box = new() { ShapeType = 3, ShapeParam1 = 3f, ShapeParam2 = 1f };  // 3 m ahead, 1 m each side
        SanguoFieldActor insideBox = KernelActor(1, new Float2(0.5f, 2));
        Assert.Single(SanguoSkillRules.ResolveHits(box, new Float2(0, 0), new Float2(0, 1),
            [insideBox], attackerCamp: 2, attackerAtk: 50, multiplier: 1f));
        SanguoFieldActor sideways = KernelActor(1, new Float2(2, 1.2f));
        Assert.Empty(SanguoSkillRules.ResolveHits(box, new Float2(0, 0), new Float2(0, 1),
            [sideways], attackerCamp: 2, attackerAtk: 50, multiplier: 1f));
    }

    private static SanguoFieldActor KernelActor(int camp, Float2 position)
    {
        var actor = new SanguoFieldActor
        {
            ID = 1,
            Camp = camp,
            Health = new SanguoHealth(100, atk: 10, def: 20),
            ShoveRadius = 0.5f,
        };
        actor.Position = position;
        return actor;
    }
}
