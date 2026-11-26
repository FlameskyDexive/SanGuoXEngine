using System;
using System.Collections.Generic;

using SanGuoXEngine.Data;

namespace SanGuoXEngine.Battle;

/// <summary>One hit the kernel resolved: who takes how much from whom.</summary>
public readonly struct SanguoHit
{
    public readonly SanguoFieldActor Target;
    public readonly int Damage;

    public SanguoHit(SanguoFieldActor target, int damage)
    {
        Target = target;
        Damage = damage;
    }
}

/// <summary>
/// The state of one skill cast: the skill being played, the elapsed cast time in milliseconds
/// (SkillData's event times are milliseconds) and the cursor into the time-ordered event list.
/// Pure state — the caller advances it from the frame loop and plays the returned due events.
/// </summary>
public sealed class SanguoCastState
{
    public SanguoSkill Skill { get; }
    public float ElapsedMs { get; private set; }
    private readonly List<SanguoSkillEvent> _ordered;
    private int _cursor;

    public SanguoCastState(SanguoSkill skill)
    {
        Skill = skill;
        _ordered = new List<SanguoSkillEvent>(skill.Events ?? []);
        _ordered.Sort((a, b) => a.EventTime.CompareTo(b.EventTime));
    }

    /// <summary>Total cast duration: the last event time, but at least the switch window
    /// (<see cref="SanguoSkill.SwitchAllTime"/> is the lock the original uses).</summary>
    public float DurationMs
    {
        get
        {
            float last = _ordered.Count > 0 ? _ordered[^1].EventTime : 0f;
            return MathF.Max(last, Skill.SwitchAllTime);
        }
    }

    /// <summary>Advances the cast clock and returns the events whose time just crossed.</summary>
    public List<SanguoSkillEvent> Advance(float deltaMs)
    {
        ElapsedMs += deltaMs;
        List<SanguoSkillEvent> due = [];
        while (_cursor < _ordered.Count && _ordered[_cursor].EventTime <= ElapsedMs)
            due.Add(_ordered[_cursor++]);
        return due;
    }

    public bool Finished => ElapsedMs >= DurationMs;
}

/// <summary>
/// The rules that turn skill data into gameplay: event timelines, the switch/cancel windows of
/// the combo chains (SkillData.SkillSwitch_Table: SwitchPutIn is the input key, SwitchPutOut the
/// next skill, the window spans [SwitchMinPlayTime, SwitchMinPlayTime + SwitchDuration]) and the
/// missile hit resolution (a missile lands at its DamageStartFrame at 30 fps and re-hits every
/// DamageIntervalFrame until DamageEndFrame; the shape comes from MissileData's ShapeType).
/// </summary>
public static class SanguoSkillRules
{
    /// <summary>The input keys the jws2 switch tables use (SwitchPutIn): 1..4 chain the normal
    /// attack combo, 8/9/… map to dodge/jump/skill buttons; the kernel only treats the keys as
    /// opaque ints and exposes the attack chain key used by the demo.</summary>
    public const int AttackChainKey = 1;

    /// <summary>The next skill id when <paramref name="inputKey"/> is pressed at
    /// <paramref name="elapsedMs"/> into the cast — 0 when no window matches (too early, too
    /// late, or no entry for that key).</summary>
    public static int ChainNext(SanguoSkill skill, int inputKey, float elapsedMs)
    {
        foreach (SanguoSkillSwitch entry in skill.Switches ?? [])
        {
            if (entry.SwitchPutIn != inputKey) continue;
            float windowStart = entry.SwitchMinPlayTime;
            float windowEnd = windowStart + MathF.Max(1f, entry.SwitchDuration);
            if (elapsedMs >= windowStart && elapsedMs <= windowEnd && entry.SwitchPutOut > 0)
                return entry.SwitchPutOut;
        }
        return 0;
    }

    /// <summary>Whether the cast can be cancelled into a chain at this moment (any key).</summary>
    public static bool ChainWindowOpen(SanguoSkill skill, float elapsedMs)
    {
        foreach (SanguoSkillSwitch entry in skill.Switches ?? [])
        {
            if (entry.SwitchPutOut <= 0) continue;
            float windowStart = entry.SwitchMinPlayTime;
            float windowEnd = windowStart + MathF.Max(1f, entry.SwitchDuration);
            if (elapsedMs >= windowStart && elapsedMs <= windowEnd) return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves one missile tick: which candidates the missile's shape hits THIS frame. A missile
    /// with damage frames [3,12]/interval 5 lands on frames 3, 8, 12 — each candidate is reported
    /// at most once per landing frame here; the CALLER tracks per-target already-hit sets so a
    /// multi-frame missile damages each victim once per landing.
    /// </summary>
    public static bool MissileLandsThisFrame(SanguoMissile missile, int frame)
    {
        if (missile.DamageEndFrame > 0 && frame > missile.DamageEndFrame) return false;
        if (frame < Math.Max(1, missile.DamageStartFrame)) return false;
        int interval = Math.Max(1, missile.DamageIntervalFrame);
        // landing frames: start, start+interval, … and the end frame itself
        return frame == missile.DamageEndFrame || (frame - missile.DamageStartFrame) % interval == 0;
    }

    /// <summary>The candidates the missile's shape covers, resolved into hits against living
    /// actors of the opposite camp. <paramref name="multiplier"/> scales the attacker's attack.</summary>
    public static List<SanguoHit> ResolveHits(SanguoMissile missile, Float2 origin, Float2 forward,
        IReadOnlyList<SanguoFieldActor> candidates, int attackerCamp, int attackerAtk, float multiplier)
    {
        List<SanguoHit> hits = [];
        foreach (SanguoFieldActor candidate in candidates)
        {
            if (candidate.Dead || candidate.Camp == attackerCamp) continue;
            bool hit = missile.ShapeType switch
            {
                2 => SanguoHitShapes.InCone(origin, forward, candidate.Position,
                    missile.ShapeParam1, DegreesToRadians(missile.ShapeParam2)),
                3 => SanguoHitShapes.InBox(origin, forward, candidate.Position,
                    missile.ShapeParam1, missile.ShapeParam2),
                // ShapeType 1 (and unknown shapes): circle of ShapeParam1, default 1.5 m
                _ => SanguoHitShapes.InCircle(origin, candidate.Position,
                    missile.ShapeParam1 > 0f ? missile.ShapeParam1 : 1.5f),
            };
            if (hit) hits.Add(new SanguoHit(candidate, candidate.Health.ApplyHit(attackerAtk, multiplier)));
        }
        return hits;
    }

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;

    private static float RadiusFallback(this SanguoMissile missile) => 1.5f;
}
