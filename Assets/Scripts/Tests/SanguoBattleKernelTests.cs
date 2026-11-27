using System;
using System.Collections.Generic;

using SanGuoXEngine.Battle;
using SanGuoXEngine.Data;

using Xunit;

namespace SanGuoXEngine.Tests;

/// <summary>
/// The M4-2 battle kernel's pure rules: hit-shape geometry, health intake, the state machine's
/// transition and lock rules, motor arrival/facing, the layout→spawn-plan conversion and the
/// stage's spawn/outcome flow.
/// </summary>
public sealed class SanguoBattleKernelTests
{
    // ---------------------------------------------------------------- hit shapes

    [Fact]
    public void CircleHit_UsesRadius()
    {
        Assert.True(SanguoHitShapes.InCircle(new Float2(0, 0), new Float2(3, 4), 5f));
        Assert.False(SanguoHitShapes.InCircle(new Float2(0, 0), new Float2(3, 4), 4.9f));
    }

    [Fact]
    public void ConeHit_WithinRadiusAndHalfAngle()
    {
        var forward = new Float2(0, 1);
        Assert.True(SanguoHitShapes.InCone(new Float2(0, 0), forward, new Float2(0, 3), 5f, MathF.PI / 4));
        Assert.True(SanguoHitShapes.InCone(new Float2(0, 0), forward, new Float2(2, 2), 5f, MathF.PI / 4));   // 45°, on the edge
        Assert.False(SanguoHitShapes.InCone(new Float2(0, 0), forward, new Float2(3, -1), 5f, MathF.PI / 4)); // behind
        Assert.False(SanguoHitShapes.InCone(new Float2(0, 0), forward, new Float2(0, 6), 5f, MathF.PI / 4));  // too far
        // on the origin everything counts (the attacker is standing on the target)
        Assert.True(SanguoHitShapes.InCone(new Float2(0, 0), forward, new Float2(0, 0), 5f, MathF.PI / 4));
    }

    [Fact]
    public void BoxHit_OrientedHalfExtents()
    {
        Assert.True(SanguoHitShapes.InBox(new Float2(0, 0), new Float2(0, 1), new Float2(0, 2), 3f, 1f));     // ahead, narrow
        Assert.False(SanguoHitShapes.InBox(new Float2(0, 0), new Float2(0, 1), new Float2(1.2f, 2), 3f, 1f)); // sideways out
        Assert.False(SanguoHitShapes.InBox(new Float2(0, 0), new Float2(0, 1), new Float2(0, -1), 3f, 1f));   // behind: forward-only box
        Assert.True(SanguoHitShapes.InBox(new Float2(0, 0), new Float2(1, 0), new Float2(2, 0.5f), 3f, 1f));  // rotated frame
    }

    // ---------------------------------------------------------------- health

    [Fact]
    public void Health_DefenceReduces_AndDeathFiresOnce()
    {
        SanguoHealth health = new(100, atk: 0, def: 30);
        int deaths = 0;
        health.Died += () => deaths++;
        Assert.Equal(70, health.ApplyHit(attackerAtk: 100, multiplier: 1f));
        Assert.Equal(170, health.ApplyHit(attackerAtk: 100, multiplier: 2f));      // 200-30=170 damage dealt → dead
        Assert.True(health.Dead);
        Assert.Equal(0, health.ApplyHit(1000));                                   // corpse takes nothing
        health.ApplyHit(1000);
        Assert.Equal(1, deaths);
    }

    [Fact]
    public void Health_FromMonsterValueRow()
    {
        SanguoHealth health = SanguoHealth.FromValue(new SanguoMonsterValue
        {
            GroupID = 101103, Difficulty = 1, Hp = 95, Atk = 58, Def = 24,
        });
        Assert.Equal(95, health.MaxHp);
    }

    // ---------------------------------------------------------------- state machine

    [Fact]
    public void StateMachine_AttackLocks_ThenReturnsToIdle()
    {
        SanguoStateMachine fsm = new();
        Assert.True(fsm.TryStartAttack(0.8f));
        Assert.Equal(SanguoActorState.Attack, fsm.State);
        Assert.True(fsm.Busy);
        Assert.False(fsm.TryStartAttack(0.5f));                     // no re-entry while swinging
        fsm.Tick(0.5f);
        Assert.True(fsm.Busy);                                      // 0.3s left
        fsm.Tick(0.3f);
        Assert.Equal(SanguoActorState.Idle, fsm.State);
        Assert.False(fsm.Busy);
    }

    [Fact]
    public void StateMachine_HurtInterruptsAttack_ButNotDeath()
    {
        SanguoStateMachine fsm = new();
        fsm.TryStartAttack(1f);
        fsm.TakeHurt();
        Assert.Equal(SanguoActorState.Hurt, fsm.State);
        fsm.Kill();
        Assert.True(fsm.Dead);
        fsm.TakeHurt();                                             // corpse doesn't flinch
        Assert.Equal(SanguoActorState.Dead, fsm.State);
        Assert.False(fsm.TryStartAttack(1f));
    }

    [Fact]
    public void StateMachine_DodgeCancelsRun_ButNotSwing()
    {
        SanguoStateMachine fsm = new();
        Assert.True(fsm.TryRun());
        Assert.True(fsm.TryDodge());
        Assert.Equal(SanguoActorState.Dodge, fsm.State);
        fsm.Tick(0.4f);
        Assert.Equal(SanguoActorState.Idle, fsm.State);
        Assert.True(fsm.TryStartAttack(1f));
        Assert.False(fsm.TryDodge());                               // a swing cannot be dodged out of
    }

    // ---------------------------------------------------------------- motor

    [Fact]
    public void Motor_SeeksArrives_AndFacesMovement()
    {
        SanguoMotor motor = new()
        {
            Position = new Float2(0, 0),
            Speed = 10f,
            ArriveRadius = 0.2f,
        };
        motor.FaceTowards(new Float2(0, 10));
        motor.MoveTo(new Float2(0, 10));
        Assert.True(motor.Tick(1f));                                // moved 10 of 10 → arrives
        Assert.False(motor.Tick(1f));                               // no destination anymore
        Assert.Equal(10f, motor.Position.Y, 2);
        Assert.Equal(1f, motor.Facing.Y, 2);                        // facing +Y
    }

    [Fact]
    public void Motor_TurnTakesTheShortestArc()
    {
        SanguoMotor motor = new() { Position = new Float2(0, 0), TurnSpeedRadians = MathF.PI };  // 180°/s
        motor.Facing = new Float2(0, 1);                            // facing +Y
        motor.MoveTo(new Float2(0, -5));                            // target directly behind
        motor.Tick(0.5f);                                           // half a second: turns 90°
        Assert.Equal(1f, Float2.Length(motor.Facing), 2);           // still unit length
        motor.Tick(0.5f);
        Assert.Equal(-1f, motor.Facing.Y, 2);                       // fully facing -Y
    }

    // ---------------------------------------------------------------- level spawn plan

    [Fact]
    public void LevelSpawn_PlansMarkersAndGrids()
    {
        SanguoLevelLayout layout = new()
        {
            LevelID = 1011,
            Items =
            [
                new SanguoLevelItem { LevelItemID = 1001, LevelItemType = 1, PosX = 111.4f, PosZ = 777.8f, DirX = 0, DirZ = -1 },
                new SanguoLevelItem
                {
                    LevelItemID = 2010, LevelItemType = 2, PosX = 100, PosZ = 700, DirX = 0, DirZ = 1,
                    Count = 8, Row = 4, Col = 2, Gap = 2, LeaderID = 2001, RefreshCount = -1,
                    MonsterIDs = [101103], MonsterWeights = [100],
                },
            ],
        };
        (List<SanguoSpawnMarker> markers, List<SanguoSpawnGroup> groups) = SanguoLevelSpawn.Plan(layout);

        SanguoSpawnMarker marker = Assert.Single(markers);
        Assert.Equal(111.4f, marker.Position.X, 2);
        Assert.Equal(777.8f, marker.Position.Y, 2);
        Assert.Equal(-1f, marker.Forward.Y);

        SanguoSpawnGroup group = Assert.Single(groups);              // one monster id → one group
        Assert.Equal((2010, 101103, 8), (group.LevelItemID, group.MonsterID, group.Count));
        List<Float2> positions = SanguoLevelSpawn.GridPositions(group);
        Assert.Equal(8, positions.Count);
        // grid: 2 columns × 4 rows, gap 2 — first row straddles the centre line, second row is 2 m ahead
        Assert.Equal(-1f, positions[0].X - 100f, 2);                 // col 0 of 2: -(2-1)/2 = -0.5*gap
        Assert.Equal(1f, positions[1].X - 100f, 2);
        Assert.Equal(0f, positions[0].Y - 700f, 2);                  // row 0 at the centre
        Assert.Equal(2f, positions[2].Y - 700f, 2);                  // row 1 advances 2 m
    }

    // ---------------------------------------------------------------- battle stage

    [Fact]
    public void BattleStage_SpawnsHeroesAndWaves_AndReportsOutcome()
    {
        SanguoLevelLayout layout = new()
        {
            LevelID = 1011,
            Items =
            [
                new SanguoLevelItem { LevelItemID = 1001, LevelItemType = 1, PosX = 100, PosZ = 700, DirX = 0, DirZ = 1 },
                new SanguoLevelItem
                {
                    LevelItemID = 2010, LevelItemType = 2, PosX = 110, PosZ = 710, DirX = 0, DirZ = -1,
                    Count = 2, Row = 1, Col = 2, Gap = 2, MonsterIDs = [101103, 101104], MonsterWeights = [50, 50],
                },
            ],
        };
        SanguoMonsterTable monsters = new()
        {
            Monsters =
            [
                new SanguoMonster { ID = 101103, MonsterType = 2, Camp = 1, ModelID = 22056, Speed = 8 },
                new SanguoMonster { ID = 101104, MonsterType = 2, Camp = 1, ModelID = 22082, Speed = 8 },
            ],
        };
        SanguoMonsterValueTable values = new()
        {
            Values = [new SanguoMonsterValue { GroupID = 101103, Difficulty = 1, Hp = 10, Atk = 5, Def = 0 }],
        };

        SanguoBattleStage stage = new(layout, monsters, values);
        int spawns = 0;
        stage.ActorSpawned += (_, _, _) => spawns++;

        List<SanguoFieldActor> heroes = stage.SpawnHeroes(
            new[] { 100001, 100012, 100058, 100100 },
            new[] { "GuanYu", "ZhaoYun", "LiuBei", "ZhangFei" },
            new[]
            {
                new SanguoMonsterValue { GroupID = 100001, Difficulty = 1, Hp = 1000, Atk = 100, Def = 20 },
                new SanguoMonsterValue { GroupID = 100012, Difficulty = 1, Hp = 1000, Atk = 100, Def = 20 },
                new SanguoMonsterValue { GroupID = 100058, Difficulty = 1, Hp = 1000, Atk = 100, Def = 20 },
                new SanguoMonsterValue { GroupID = 100100, Difficulty = 1, Hp = 1000, Atk = 100, Def = 20 },
            });
        Assert.Equal(4, heroes.Count);
        Assert.Equal(4, spawns);
        Assert.NotNull(stage.Leader);
        Assert.Equal("GuanYu", stage.Leader!.Name);

        // wave 1: two weighted alternatives split Count=2 → one group of one monster each
        Assert.NotNull(stage.SpawnNextGroup());
        Assert.NotNull(stage.SpawnNextGroup());
        stage.Tick(0.016f);
        Assert.Equal(6, spawns);
        Assert.Equal(2, stage.Enemies.Count);

        // kill both enemies → pending groups empty → win
        foreach (SanguoFieldActor enemy in stage.Enemies) enemy.Health.ApplyHit(100000);
        Assert.True(stage.Won);
        Assert.False(stage.Lost);

        // the next spawn attempt finds nothing
        Assert.Null(stage.SpawnNextGroup());
    }

    [Fact]
    public void BattleStage_LosesWhenAllHeroesDie()
    {
        SanguoLevelLayout layout = new()
        {
            LevelID = 1011,
            Items = [new SanguoLevelItem { LevelItemID = 1001, LevelItemType = 1, PosX = 0, PosZ = 0, DirX = 0, DirZ = 1 }],
        };
        SanguoBattleStage stage = new(layout, new SanguoMonsterTable(), new SanguoMonsterValueTable());
        stage.SpawnHeroes(
            new[] { 100001 },
            new[] { "GuanYu" },
            new[] { new SanguoMonsterValue { GroupID = 100001, Difficulty = 1, Hp = 10, Atk = 10, Def = 0 } });
        stage.Heroes[0].Health.ApplyHit(100000);
        Assert.True(stage.Lost);
        Assert.False(stage.Won);
    }

    [Fact]
    public void BattleStage_UsesImportedMonsterAttackValue()
    {
        SanguoLevelLayout layout = new()
        {
            LevelID = 1011,
            Items = [new SanguoLevelItem
            {
                LevelItemID = 1001,
                LevelItemType = 2,
                PosX = 0,
                PosZ = 0,
                DirX = 0,
                DirZ = 1,
                Count = 1,
            }],
        };
        SanguoBattleStage stage = new(
            layout,
            new SanguoMonsterTable
            {
                Monsters = [new SanguoMonster { ID = 101103, MonsterType = 2, Camp = 1 }],
            },
            new SanguoMonsterValueTable
            {
                Values = [new SanguoMonsterValue { GroupID = 101103, Difficulty = 1, Hp = 100, Atk = 37, Def = 5 }],
            });

        Assert.Equal(37, stage.AttackOf(101103));
        Assert.Equal(20, stage.AttackOf(999999));
    }

    [Fact]
    public void Registry_FindsNearestLivingEnemy()
    {
        SanguoActorRegistry registry = new();
        SanguoFieldActor near = ActorAt(1, 0);
        SanguoFieldActor far = ActorAt(20, 0);
        SanguoFieldActor dead = ActorAt(2, 0);
        dead.Health.ApplyHit(100000);
        registry.Register(near);
        registry.Register(far);
        registry.Register(dead);
        Assert.Same(near, registry.FindNearest(new Float2(0, 0), enemyCamp: 1));
    }

    private static SanguoFieldActor ActorAt(float x, float z)
    {
        var actor = new SanguoFieldActor
        {
            ID = 1,
            Camp = 1,
            Health = new SanguoHealth(100, 10, 0),
        };
        actor.Position = new Float2(x, z);
        return actor;
    }
}
