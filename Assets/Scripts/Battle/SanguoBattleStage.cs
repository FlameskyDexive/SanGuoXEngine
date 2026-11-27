using System;
using System.Collections.Generic;

using SanGuoXEngine.Data;

using XEngine.Vector;

namespace SanGuoXEngine.Battle;

/// <summary>One resolved spawn: which monster, how many, arranged where and facing where.</summary>
public sealed class SanguoSpawnGroup
{
    public int LevelItemID;
    public int MonsterID;
    public int Count;
    public Float2 Centre;
    public Float2 Forward;
    public int Rows;
    public int Cols;
    public float Gap;
    public int LeaderID;
    public bool ActiveDefault;
}

/// <summary>The spawn markers of the layout (LevelItemType 1): where the player's heroes enter.</summary>
public sealed class SanguoSpawnMarker
{
    public int LevelItemID;
    public Float2 Position;
    public Float2 Forward;
}

/// <summary>
/// Pure conversion of a <see cref="SanguoLevelLayout"/> into the concrete spawn plan the stage
/// instantiates: every layout item becomes either a spawn marker (type 1) or one spawn group per
/// monster weight (type 2, weighted picks collapse to equal groups so the stage can rotate them).
/// </summary>
public static class SanguoLevelSpawn
{
    public static (List<SanguoSpawnMarker> markers, List<SanguoSpawnGroup> groups) Plan(SanguoLevelLayout layout)
    {
        List<SanguoSpawnMarker> markers = [];
        List<SanguoSpawnGroup> groups = [];
        foreach (SanguoLevelItem item in layout.Items)
        {
            if (item.LevelItemType == 1)
            {
                markers.Add(new SanguoSpawnMarker
                {
                    LevelItemID = item.LevelItemID,
                    Position = new Float2(item.PosX, item.PosZ),
                    Forward = NormalizeOr(item.DirX, item.DirZ, new Float2(0f, 1f)),
                });
                continue;
            }
            if (item.LevelItemType != 2 || item.MonsterIDs.Count == 0) continue;
            int count = Math.Max(1, item.Count);
            for (int m = 0; m < item.MonsterIDs.Count; m++)
            {
                // weighted alternatives become equal-probability groups; Count splits across them
                int share = m == 0
                    ? count - (count / item.MonsterIDs.Count) * (item.MonsterIDs.Count - 1)
                    : count / item.MonsterIDs.Count;
                groups.Add(new SanguoSpawnGroup
                {
                    LevelItemID = item.LevelItemID,
                    MonsterID = item.MonsterIDs[m],
                    Count = Math.Max(1, share),
                    Centre = new Float2(item.PosX, item.PosZ),
                    Forward = NormalizeOr(item.DirX, item.DirZ, new Float2(0f, 1f)),
                    Rows = Math.Max(1, item.Row),
                    Cols = Math.Max(1, item.Col),
                    Gap = item.Gap > 0f ? item.Gap : 2f,
                    LeaderID = item.LeaderID,
                    ActiveDefault = item.LeaderID == 0 && item.RefreshCount == 1,
                });
            }
        }
        return (markers, groups);
    }

    /// <summary>The world positions of a group's members: a Rows×Cols grid around the centre,
    /// forward-facing (row 0 nearest the centre, advancing against the forward direction).</summary>
    public static List<Float2> GridPositions(SanguoSpawnGroup group)
    {
        List<Float2> positions = [];
        Float2 forward = NormalizeOr(group.Forward.X, group.Forward.Y, new Float2(0f, 1f));
        Float2 right = new(forward.Y, -forward.X);
        int perRow = Math.Max(1, group.Cols);
        for (int i = 0; i < group.Count; i++)
        {
            int row = i / perRow;
            int col = i % perRow;
            float along = group.Gap * row;
            float side = group.Gap * (col - (perRow - 1) * 0.5f);
            positions.Add(group.Centre + forward * along + right * side);
        }
        return positions;
    }

    private static Float2 NormalizeOr(float x, float y, Float2 fallback)
    {
        Float2 v = new(x, y);
        return Float2.Length(v) > 1e-4f ? Float2.Normalize(v) : fallback;
    }
}

/// <summary>
/// The 桃园 battle stage: loads the level-1011 spawn layout, fields the player's four heroes at
/// the spawn marker, spawns the monster waves, and reports the outcome (win: every enemy dead;
/// lose: every hero dead). Pure state — the scene-side presenter instantiates the prefabs and
/// feeds <see cref="Tick"/> from the frame loop.
/// </summary>
public sealed class SanguoBattleStage
{
    public const int PlayerCamp = 2;
    public const int EnemyCamp = 1;

    private readonly SanguoActorRegistry _registry = new();
    private readonly List<SanguoFieldActor> _heroes = [];
    private readonly List<SanguoFieldActor> _enemies = [];
    private readonly List<SanguoFieldActor> _livingScratch = new(256);
    private int _shoveStep;
    private readonly Dictionary<int, SanguoMonster> _monsterData;
    private readonly Dictionary<int, SanguoMonsterValue> _valueData;
    private readonly List<SanguoSpawnGroup> _pendingGroups = [];
    private readonly List<SanguoSpawnMarker> _markers;

    public SanguoFieldActor? Leader { get; private set; }
    public bool Won { get; private set; }
    public bool Lost { get; private set; }
    public IReadOnlyList<SanguoFieldActor> Enemies => _enemies;
    public int SpawnGroupCount => _pendingGroups.Count;
    public IReadOnlyList<SanguoFieldActor> Heroes => _heroes;

    /// <summary>Returns the authored attack value for a monster group. The battle driver uses
    /// this lookup for contact damage so the live presentation follows the imported data table.</summary>
    public int AttackOf(int monsterId)
        => monsterId == 900001
            ? 180
            : _valueData.TryGetValue(monsterId, out SanguoMonsterValue? value) && value.Atk > 0
            ? value.Atk
            : 20;

    /// <summary>Raised per resolved spawn so the presenter can instantiate the right prefab.</summary>
    public event Action<SanguoFieldActor, Float2, Float2>? ActorSpawned;

    public SanguoBattleStage(SanguoLevelLayout layout, SanguoMonsterTable monsters,
        SanguoMonsterValueTable values)
    {
        _monsterData = new Dictionary<int, SanguoMonster>();
        foreach (SanguoMonster monster in monsters.Monsters) _monsterData[monster.ID] = monster;
        _valueData = new Dictionary<int, SanguoMonsterValue>();
        foreach (SanguoMonsterValue value in values.Values)
            if (!_valueData.ContainsKey(value.GroupID)) _valueData[value.GroupID] = value;

        (List<SanguoSpawnMarker> markers, List<SanguoSpawnGroup> groups) = SanguoLevelSpawn.Plan(layout);
        _markers = markers;
        _pendingGroups = groups;
    }

    /// <summary>Spawns the player's four heroes at the first spawn marker (IDs map to the four
    /// playable heroes' data; the presenter binds the prefabs). Returns the hero actors.</summary>
    public List<SanguoFieldActor> SpawnHeroes(IReadOnlyList<int> heroIds, IReadOnlyList<string> names,
        IReadOnlyList<SanguoMonsterValue> heroValues)
    {
        Float2 position = _markers.Count > 0 ? _markers[0].Position : new Float2(100f, 700f);
        Float2 forward = _markers.Count > 0 ? _markers[0].Forward : new Float2(0f, 1f);
        List<SanguoFieldActor> spawned = [];
        for (int i = 0; i < heroIds.Count && i < names.Count; i++)
        {
            SanguoMonsterValue value = i < heroValues.Count
                ? heroValues[i]
                : new SanguoMonsterValue { GroupID = heroIds[i], Difficulty = 1, Hp = 1000, Atk = 100, Def = 20 };
            SanguoFieldActor actor = new()
            {
                ID = heroIds[i],
                Name = names[i],
                Camp = PlayerCamp,
                MonsterType = 51,
                Health = SanguoHealth.FromValue(value),
                ShoveRadius = 0.5f,
            };
            actor.Position = position + new Float2(forward.Y, -forward.X) * (i - (heroIds.Count - 1) * 0.5f) * 1.5f;
            actor.Motor.FaceTowards(position + forward * 10f);
            actor.Health.Died += () => OnActorDied(actor);
            _registry.Register(actor);
            _heroes.Add(actor);
            spawned.Add(actor);
            ActorSpawned?.Invoke(actor, actor.Position, actor.Motor.Facing);
        }
        Leader = spawned.Count > 0 ? spawned[0] : null;
        return spawned;
    }

    /// <summary>Spawns the demo's Lu Bu boss from the same kernel registry as wave enemies.
    /// The scene driver supplies the real Hero_LvBu prefab while the kernel owns targeting,
    /// health and win/lose accounting.</summary>
    public SanguoFieldActor SpawnBoss(Float2 position, Float2 forward)
    {
        var boss = new SanguoFieldActor
        {
            ID = 900001,
            ModelID = 10109,
            Name = "LuBu_Boss",
            Camp = EnemyCamp,
            MonsterType = 51,
            Health = new SanguoHealth(5000, 180, 80),
            ShoveRadius = 1.2f,
        };
        boss.Position = position;
        boss.Motor.FaceTowards(position + forward * 10f);
        boss.Health.Died += () => OnActorDied(boss);
        _registry.Register(boss);
        _enemies.Add(boss);
        ActorSpawned?.Invoke(boss, boss.Position, boss.Motor.Facing);
        return boss;
    }

    /// <summary>Spawns the pending group whose turn it is. Groups spawn in layout order; each call
    /// spawns ONE group (the presenter calls this when it wants the next wave on the field).</summary>
    public SanguoSpawnGroup? SpawnNextGroup()
    {
        if (_pendingGroups.Count == 0) return null;
        SanguoSpawnGroup group = _pendingGroups[0];
        _pendingGroups.RemoveAt(0);
        if (!_monsterData.TryGetValue(group.MonsterID, out SanguoMonster? monster))
        {
            // unknown monster: skip the group entirely
            return SpawnNextGroup();
        }
        SanguoMonsterValue value = _valueData.TryGetValue(group.MonsterID, out SanguoMonsterValue? v)
            ? v
            : new SanguoMonsterValue { GroupID = group.MonsterID, Difficulty = 1, Hp = 100, Atk = 20, Def = 5 };
        List<Float2> positions = SanguoLevelSpawn.GridPositions(group);
        for (int i = 0; i < group.Count && i < positions.Count; i++)
        {
            var actor = new SanguoFieldActor
            {
                ID = group.MonsterID,
                ModelID = monster.ModelID,
                Name = $"M{group.MonsterID}_{i}",
                Camp = EnemyCamp,
                MonsterType = monster.MonsterType,
                Health = SanguoHealth.FromValue(value),
                ShoveRadius = 0.5f,
            };
            actor.Position = positions[i];
            actor.Motor.FaceTowards(actor.Position + group.Forward * 10f);
            actor.Health.Died += () => OnActorDied(actor);
            _registry.Register(actor);
            _enemies.Add(actor);
            ActorSpawned?.Invoke(actor, actor.Position, actor.Motor.Facing);
        }
        return group;
    }

    private void OnActorDied(SanguoFieldActor actor)
    {
        actor.StateMachine.Kill();
        _registry.Unregister(actor);
        EvaluateOutcome();
    }

    private void EvaluateOutcome()
    {
        if (Won || Lost) return;
        bool allEnemiesDead = _enemies.Count > 0;
        foreach (SanguoFieldActor enemy in _enemies)
            if (!enemy.Dead) allEnemiesDead = false;
        bool allHeroesDead = _heroes.Count > 0;
        foreach (SanguoFieldActor hero in _heroes)
            if (!hero.Dead) allHeroesDead = false;
        // pending groups still hold enemies to spawn — the stage is not won until they are out
        if (allHeroesDead && _heroes.Count > 0) Lost = true;
        if (allEnemiesDead && _pendingGroups.Count == 0) Won = true;
    }

    /// <summary>Advances every actor's state machine/motor and the gentle shove separation.</summary>
    public void Tick(float deltaTime)
    {
        foreach (SanguoFieldActor hero in _heroes) TickActor(hero, deltaTime);
        foreach (SanguoFieldActor enemy in _enemies) TickActor(enemy, deltaTime);
        Shove();
        EvaluateOutcome();
    }

    private static void TickActor(SanguoFieldActor actor, float deltaTime)
    {
        if (actor.Dead) return;
        actor.StateMachine.Tick(deltaTime);
        actor.Motor.Tick(deltaTime);
    }

    /// <summary>Gentle XZ separation: overlapping living actors push each other apart by half the
    /// overlap each, so crowds don't collapse into one point (jws2's shove, simplified).</summary>
    private void Shove()
    {
        PopulateLivingScratch();
        // Separation is a presentation correction. Keep simulation, targeting and attacks at
        // 60 Hz, but solve the O(N²) crowd pass at 15 Hz so a 234-actor wave does not consume the
        // entire editor frame on repeated tiny overlaps.
        if ((++_shoveStep & 3) != 0) return;
        List<SanguoFieldActor> all = _livingScratch;
        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                SanguoFieldActor a = all[i];
                SanguoFieldActor b = all[j];
                Float2 offset = b.Position - a.Position;
                float distance = Float2.Length(offset);
                float minDistance = a.ShoveRadius + b.ShoveRadius;
                if (distance >= minDistance) continue;
                if (distance < 1e-4f)
                {
                    // exactly stacked: nudge along a stable pseudo-random direction
                    offset = new Float2(0.01f * (i + 1), 0.01f * (j + 1));
                    distance = Float2.Length(offset);
                }
                Float2 direction = offset / distance;
                float push = (minDistance - distance) * 0.5f;
                a.Position -= direction * push;
                b.Position += direction * push;
            }
        }
    }

    private void PopulateLivingScratch()
    {
        _livingScratch.Clear();
        foreach (SanguoFieldActor hero in _heroes) if (!hero.Dead) _livingScratch.Add(hero);
        foreach (SanguoFieldActor enemy in _enemies) if (!enemy.Dead) _livingScratch.Add(enemy);
    }
}
