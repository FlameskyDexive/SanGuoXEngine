using System;
using System.Collections.Generic;

using SanGuoXEngine.Battle;
using SanGuoXEngine.Animation;
using SanGuoXEngine.Data;

using XEngine.Animation;
using XEngine.Runtime;
using XEngine.Runtime.Resources;
using XEngine.Vector;

namespace SanGuoXEngine.Battle;

/// <summary>
/// Scene-side binding of a <see cref="SanguoFieldActor"/>: writes the kernel's motor result into
/// the Transform, plays the state animations (heroes carry a real Animator with the M2
/// controllers; monster placeholders animate by code only), and applies the hit flash.
/// The kernel stays pure — this component is the only one that touches scene objects.
/// </summary>
public sealed class SanguoActorPresenter : MonoBehaviour
{
    /// <summary>The ground height the battle acts on (the 桃园 plaza sits at y≈180).</summary>
    public float BaseY = 180f;

    public SanguoFieldActor Kernel = new();

    private SanguoAnimator? _animator;
    private bool _animatorBroken;
    private SanguoActorState _lastPlayed = SanguoActorState.Idle;

    public override void OnAddedToScene()
    {
        _animator = GetComponent<SanguoAnimator>();
        PlayState(SanguoActorState.Idle, 0f);
    }

    public override void LateUpdate()
    {
        if (Kernel == null) return;
        Transform.Position = new Float3(Kernel.Position.X, BaseY, Kernel.Position.Y);
        // the model's forward: the battle kernel's +Y maps to the scene's -Z facing? no — the
        // kernel's facing IS the scene's XZ direction; the model faces -Z at identity, so the
        // node yaw turns -Z onto the kernel's facing.
        Float2 facing = Kernel.Facing;
        if (Float2.Length(facing) > 1e-4f)
        {
            float yaw = MathF.Atan2(facing.X, facing.Y);        // kernel facing angle from +Z…
            Transform.LocalEulerAngles = new Float3(0f, yaw * 180f / MathF.PI, 0f);
        }
        if (Kernel.StateMachine.State != _lastPlayed) PlayState(Kernel.StateMachine.State, 0.1f);
    }

    /// <summary>Plays the skill's animation by name (the controller's states are the skills'
    /// AnimName entries); returns false when the animator lacks it.</summary>
    public bool PlaySkillClip(string animName)
    {
        if (_animator == null || _animatorBroken) return false;
        try
        {
            return _animator.Play(animName, 0.05f);
        }
        catch (Exception)
        {
            _animatorBroken = true;                             // no controller bound: stay silent
            return false;
        }
    }

    public void PlayHurt()
    {
        if (_animator == null || _animatorBroken) return;
        try
        {
            _animator.InsertHurt("hurt");
        }
        catch (Exception)
        {
            _animatorBroken = true;
        }
    }

    private void PlayState(SanguoActorState state, float fade)
    {
        _lastPlayed = state;
        if (_animator == null || _animatorBroken) return;
        string clip = state switch
        {
            SanguoActorState.Idle => "idle",
            SanguoActorState.Run => "run",
            SanguoActorState.Dead => "die",
            _ => "",
        };
        if (clip.Length == 0) return;
        try
        {
            if (!_animator.Play(clip, fade)) _animatorBroken = true;   // no controller → stop asking
        }
        catch (Exception)
        {
            _animatorBroken = true;
        }
    }
}

/// <summary>
/// Drives the 桃园 battle from the live frame loop: loads the data assets, fields the stage, moves
/// the player's leader with WASD (camera-relative), chains attacks with J, dodges with K, switches
/// heroes with 1–4, runs the monster AI (seek nearest hero, melee in range) and follows with the
/// battle camera. Also owns the offline stats window.
/// </summary>
public sealed class SanguoBattleDriver : MonoBehaviour
{
    private static readonly Guid SkillsGuid = new("9f40488a-6bd5-1dc6-81e4-387a8c75f7bd");
    private static readonly Guid CallBulletsGuid = new("3817ef63-b281-d226-a06d-1f6271583246");
    private static readonly Guid MissilesGuid = new("b9be78e2-fbb1-dc87-838d-39b29f9b94e9");
    private static readonly Guid MonstersGuid = new("858a3555-7815-74a1-f1f2-34a229e1b974");
    private static readonly Guid ModelsGuid = new("a0438efe-498e-e824-ea17-116c3ec8950a");
    private static readonly Guid ValuesGuid = new("c02e2542-06fc-9769-7bc9-547feca0eb7b");
    private static readonly Guid LayoutGuid = new("d435194f-f8cb-3612-f43e-56855330d375");
    // The source package does not contain the original YellowArmy prefab yet. The imported
    // xiaobingyuanjing FBX is the closest real Yellow Turban soldier asset in this port and keeps
    // the enemy presentation on the model path while the per-monster asset table is completed.
    private static readonly Guid EnemyViewPrefabGuid = new("e4251061-3dcd-e554-cc2f-6348e0f66f65");
    // The battle kernel keeps the complete 234-enemy wave, but the editor Play view only needs
    // the actors that can occupy the current encounter.  A hard presentation budget prevents
    // every logic actor from allocating a full model hierarchy and running a presenter every
    // frame.  The first wave groups are authored closest to the hero spawn, so they are the
    // useful initial views while the remaining actors stay fully simulated in the kernel.
    private const int MaxEnemyViewCount = 32;

    private static readonly (string Name, Guid Prefab)[] HeroPrefabs =
    [
        ("GuanYu", new Guid("5353fc0f-8c49-ea97-c3b7-7e7658450133")),
        ("ZhaoYun", new Guid("d55f045b-fcc9-bb0d-6416-8ae50ba362bb")),
        ("LiuBei", new Guid("bed0629d-fe30-90df-bed2-d85bc376fb97")),
        ("ZhangFei", new Guid("56834c41-4392-85c8-1d22-dd1a868ee794")),
    ];

    private readonly Dictionary<int, SanguoSkill> _skills = new();
    private readonly Dictionary<int, SanguoCallBullet> _callBullets = new();
    private readonly Dictionary<int, SanguoMissile> _missiles = new();
    private readonly Dictionary<SanguoFieldActor, SanguoCastState> _casts = new();
    private readonly Dictionary<SanguoFieldActor, SanguoActorPresenter> _presenters = new();
    private readonly Dictionary<SanguoFieldActor, float> _aiCooldowns = new();
    private readonly Dictionary<SanguoFieldActor, HashSet<int>> _hitByBullet = new();
    private readonly List<(SanguoCallBullet bullet, SanguoMissile missile, SanguoFieldActor owner, float elapsedMs, HashSet<int> hit)> _liveBullets = [];
    private readonly List<SanguoFieldActor> _finishedCasts = new(32);
    private readonly List<SanguoHit> _hitScratch = new(32);
    private PrefabAsset? _enemyViewPrefab;
    private int _enemyViewCount;

    private SanguoBattleStage? _stage;
    private Camera? _battleCamera;
    private SanguoFieldActor? _leader;
    private int _leaderIndex;
    private float _battleTimeMs;
    private float _fixedAccumulator;
    private const float FixedStep = 1f / 60f;
    private bool _started;
    private bool _over;

    public override void OnEnable()
    {
        if (_started) return;
        _started = true;
        StartBattle();
    }

    private void StartBattle()
    {
        SanguoSkillTable skills = Load<SanguoSkillTable>(SkillsGuid);
        foreach (SanguoSkill skill in skills.Skills) _skills[skill.ID] = skill;
        SanguoCallBulletTable bullets = Load<SanguoCallBulletTable>(CallBulletsGuid);
        foreach (SanguoCallBullet bullet in bullets.CallBullets) _callBullets[bullet.ID] = bullet;
        SanguoMissileTable missiles = Load<SanguoMissileTable>(MissilesGuid);
        foreach (SanguoMissile missile in missiles.Missiles) _missiles[missile.ID] = missile;
        SanguoMonsterTable monsters = Load<SanguoMonsterTable>(MonstersGuid);
        SanguoMonsterModelTable models = Load<SanguoMonsterModelTable>(ModelsGuid);
        SanguoMonsterValueTable values = Load<SanguoMonsterValueTable>(ValuesGuid);
        SanguoLevelLayout layout = Load<SanguoLevelLayout>(LayoutGuid);

        var enemyViewReference = new AssetRef<PrefabAsset>(EnemyViewPrefabGuid);
        enemyViewReference.EnsureLoaded();
        _enemyViewPrefab = enemyViewReference.Res;

        _stage = new SanguoBattleStage(layout, monsters, values);
        _stage.ActorSpawned += SpawnActorView;

        // the four playable heroes enter at the spawn marker
        List<SanguoMonsterValue> heroValues = [];
        foreach ((string name, _) in HeroPrefabs)
            heroValues.Add(new SanguoMonsterValue { GroupID = 0, Difficulty = 1, Hp = 1200, Atk = 120, Def = 25 });
        List<SanguoFieldActor> heroes = _stage.SpawnHeroes(
            new[] { HeroFirstSkillId("GuanYu"), HeroFirstSkillId("ZhaoYun"), HeroFirstSkillId("LiuBei"), HeroFirstSkillId("ZhangFei") },
            new[] { "GuanYu", "ZhaoYun", "LiuBei", "ZhangFei" }, heroValues);

        // bind the hero prefabs
        for (int i = 0; i < heroes.Count && i < HeroPrefabs.Length; i++)
        {
            var reference = new AssetRef<PrefabAsset>(HeroPrefabs[i].Prefab);
            reference.EnsureLoaded();
            if (reference.Res?.Instantiate() is not GameObject instance)
            {
                Debug.LogWarning($"[SanguoBattle] hero {HeroPrefabs[i].Name} could not be instantiated.");
                continue;
            }
            Scene.Current!.Add(instance);
            instance.Transform.Position = new Float3(heroes[i].Position.X, 180f, heroes[i].Position.Y);
            BindPresenter(heroes[i], instance);
        }
        _leader = heroes.Count > 0 ? heroes[0] : null;
        _leaderIndex = 0;

        // TaoYuanBattle contains the authoring camera used by the scene capture workflow. Keep
        // that camera available in edit mode, but do not render it alongside the battle camera
        // during Play or every frame pays for a second full scene pipeline.
        foreach (GameObject sceneObject in Scene.Current!.AllObjects)
        {
            if (sceneObject.Name == "Scene Camera"
                && sceneObject.GetComponent<Camera>() is Camera authoringCamera)
                authoringCamera.Enabled = false;
        }

        // a battle camera above the spawn marker. FollowCamera supplies the exact aim vector and
        // keeps the display upright for the engine's +Z battle-forward convention.
        GameObject cameraGo = new("Battle Camera");
        Scene.Current!.Add(cameraGo);
        cameraGo.Transform.Position = new Float3(100f, 204f, 642f);
        cameraGo.Transform.LocalEulerAngles = new Float3(26f, 0f, 180f);
        _battleCamera = cameraGo.AddComponent<Camera>();
        _battleCamera.FieldOfView = 50f;
        _battleCamera.ClearFlags = CameraClearFlags.Skybox;
        _battleCamera.HDR = true;
        _battleCamera.FarClipPlane = 4000f;
        // Editor Play prioritizes a responsive battle view. The full桃园 scene already carries
        // baked/static shadow data, while rendering every dynamic caster into the cascades costs
        // more than the editor budget allows. Platform quality profiles can re-enable this later.
        UniversalAdditionalCameraData cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
        cameraData.RenderShadows = false;
        cameraData.RenderPostProcessing = true;

        // spawn every wave now: the stage queues them and the AI engages as they walk in
        int spawned = 0;
        while (_stage.SpawnNextGroup() != null) spawned++;
        Debug.Log("[SanguoBattle] stage ready: heroes " + heroes.Count + ", spawn groups " + spawned
            + ", enemies " + _stage.Enemies.Count);
    }

    private int HeroFirstSkillId(string hero)
    {
        foreach (SanguoSkill skill in _skills.Values)
            if (skill.KeyPosition == 1 && skill.SkillResource.Contains($"Hero_{hero}/", StringComparison.Ordinal))
                return skill.ID;
        return 0;
    }

    private static T Load<T>(Guid guid) where T : EngineObject
    {
        var reference = new AssetRef<T>(guid);
        reference.EnsureLoaded();
        return reference.Res ?? throw new InvalidOperationException($"data asset {guid} ({typeof(T).Name}) failed to load.");
    }

    /// <summary>Spawns the scene view of a kernel actor. Heroes and enemies use imported model
    /// prefabs, with the builtin cube retained only when an enemy model is unavailable.</summary>
    private void SpawnActorView(SanguoFieldActor actor, Float2 position, Float2 forward)
    {
        GameObject go;
        if (actor.Camp == SanguoBattleStage.PlayerCamp)
            return;                                     // hero views bind through SpawnHeroes instead

        if (_enemyViewCount >= MaxEnemyViewCount)
            return;

        if (_enemyViewPrefab?.Instantiate() is GameObject enemyInstance)
        {
            Scene.Current!.Add(enemyInstance);
            enemyInstance.Name = actor.Name;
            enemyInstance.Transform.Position = new Float3(position.X, 180f, position.Y);
            enemyInstance.Transform.LocalScale = new Float3(1.1f, 1.1f, 1.1f);
            BindPresenter(actor, enemyInstance);
            _enemyViewCount++;
            return;
        }

        go = new GameObject(actor.Name);
        Scene.Current!.Add(go);
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        // the monsters' real model prefabs are M7 scope; the battle cube asset (the scene
        // builder's builtin) stands in, tinted by camp
        var placeholder = new AssetRef<Mesh>(new Guid("0331af3b-03ea-d9e2-7e45-ca749920713c"));
        placeholder.EnsureLoaded();
        renderer.Mesh = placeholder;
        Material material = new(Shader.LoadDefault(DefaultShader.Unlit));
        material.SetColor("_MainColor", actor.MonsterType == 51 ? new Color(0.9f, 0.75f, 0.2f, 1f) : new Color(0.8f, 0.2f, 0.15f, 1f));
        renderer.Material = new AssetRef<Material>(material);
        go.Transform.Position = new Float3(position.X, 181.1f, position.Y);
        go.Transform.LocalScale = new Float3(1.2f, 2.2f, 1.2f);   // humanoid-ish pillar stand-in
        BindPresenter(actor, go);
    }

    private void BindPresenter(SanguoFieldActor actor, GameObject instance)
    {
        SanguoActorPresenter presenter = instance.GetComponent<SanguoActorPresenter>()
            ?? instance.AddComponent<SanguoActorPresenter>();
        presenter.Kernel = actor;
        _presenters[actor] = presenter;
        actor.Health.Died += () =>
        {
            var presenter2 = _presenters.GetValueOrDefault(actor);
            _ = presenter2 != null && presenter2.PlaySkillClip("die");
            Debug.Log("[SanguoBattle] died: " + actor.Name);
        };
    }

    private bool _updateLogged;
    private float _updateCount;
    private float _lastHeartbeatMs;

    public override void Update()
    {
        if (!_updateLogged)
        {
            _updateLogged = true;
            Debug.Log("[SanguoBattle] first Update tick, playing=" + Application.IsPlaying
                + ", stage=" + (_stage != null) + ", enemies=" + (_stage?.Enemies.Count ?? -1)
                + ", heroes=" + (_stage?.Heroes.Count ?? -1));
        }
        _updateCount += 1;
        if (_stage == null || _over) return;
        _fixedAccumulator += Math.Clamp(Time.DeltaTime, 0f, 0.25f);
        int steps = 0;
        while (_fixedAccumulator >= FixedStep && steps++ < 4)
        {
            SimulateFixedStep(FixedStep);
            _fixedAccumulator -= FixedStep;
        }
    }

    private void SimulateFixedStep(float deltaTime)
    {
        _battleTimeMs += deltaTime * 1000f;
        HandleInput(deltaTime);
        MonsterBrains(deltaTime);
        TickCasts(deltaTime);
        _stage!.Tick(deltaTime);
        FollowCamera(deltaTime);

        if (_stage.Won || _stage.Lost)
        {
            _over = true;
            Debug.Log("[SanguoBattle] " + (_stage.Won ? "WIN" : "LOSE") + " at " + (_battleTimeMs / 1000f) + "s");
            return;
        }

        if (_battleTimeMs - _lastHeartbeatMs < 2000f) return;
        _lastHeartbeatMs = _battleTimeMs;
        SanguoFieldActor? probeEnemy = _stage.Enemies.Count > 0 ? _stage.Enemies[0] : null;
        SanguoFieldActor? probeHero = _stage.Heroes.Count > 0 ? _stage.Heroes[0] : null;
        int chasing = 0, attacking = 0;
        for (int i = 0; i < _stage.Enemies.Count; i++)
        {
            SanguoFieldActor enemy = _stage.Enemies[i];
            if (enemy.StateMachine.State == SanguoActorState.Run) chasing++;
            if (enemy.StateMachine.State == SanguoActorState.Attack) attacking++;
        }
        Debug.Log("[SanguoBattle] t=" + (_battleTimeMs / 1000f) + "s enemy0="
            + (probeEnemy != null ? probeEnemy.Position.X + "," + probeEnemy.Position.Y + " state=" + probeEnemy.StateMachine.State : "none")
            + " hero0=" + (probeHero != null ? probeHero.Position.X + "," + probeHero.Position.Y + " hp=" + probeHero.Health.Hp : "none")
            + " chasing=" + chasing + " attacking=" + attacking);
    }

    // ------------------------------------------------------------ input + control

    private void HandleInput(float deltaTime)
    {
        if (_leader == null) return;
        // 1–4 switch the controlled hero
        for (int i = 0; i < HeroPrefabs.Length; i++)
        {
            if (Input.Current.GetKeyDown(KeyCode.Number1 + i) && i < _stage.Heroes.Count)
            {
                _leaderIndex = i;
                _leader = _stage.Heroes[i];
                Debug.Log("[SanguoBattle] leader -> " + _leader.Name);
            }
        }
        SanguoFieldActor leader = _leader;

        // camera-relative WASD: the battle camera looks -Z by default, so screen-up is +Z here
        float moveX = 0f, moveY = 0f;
        if (Input.Current.GetKey(KeyCode.W)) moveY += 1f;
        if (Input.Current.GetKey(KeyCode.S)) moveY -= 1f;
        if (Input.Current.GetKey(KeyCode.A)) moveX -= 1f;
        if (Input.Current.GetKey(KeyCode.D)) moveX += 1f;
        var move = new Float2(moveX, moveY);
        if (Float2.Length(move) > 0.01f && leader.StateMachine.CanMove)
        {
            Float2 direction = Float2.Normalize(move);
            leader.Motor.MoveTo(leader.Position + direction * 3f);
            leader.StateMachine.TryRun();
            _casts.Remove(leader);                          // moving cancels a cast window
        }
        else
        {
            leader.Motor.Stop();
            if (leader.StateMachine.State == SanguoActorState.Run) leader.StateMachine.Reset();
        }

        if (Input.Current.GetKeyDown(KeyCode.J)) StartLeaderAttack();
        if (Input.Current.GetKeyDown(KeyCode.K)) leader.StateMachine.TryDodge();
    }

    /// <summary>Starts the leader's next combo skill: the currently-keyed chain successor, else the
    /// KeyPosition-1 opener of the hero's own set.</summary>
    private void StartLeaderAttack()
    {
        SanguoFieldActor leader = _leader!;
        if (!leader.StateMachine.CanAttack) return;

        int skillId;
        if (_casts.TryGetValue(leader, out SanguoCastState? open))
        {
            skillId = SanguoSkillRules.ChainNext(open.Skill, SanguoSkillRules.AttackChainKey, open.ElapsedMs);
            if (skillId == 0 && open.ElapsedMs < open.DurationMs) return;   // swing still busy, no window
        }
        else
        {
            skillId = 0;
        }
        if (skillId == 0) skillId = HeroFirstSkillId(leader.Name);
        if (skillId == 0 || !_skills.TryGetValue(skillId, out SanguoSkill? skill)) return;

        // snap onto the nearest enemy and face it
        SanguoFieldActor? target = FindNearestEnemy(leader);
        if (target != null)
        {
            leader.Motor.FaceTowards(target.Position);
            if (Float2.Length(target.Position - leader.Position) > 4.5f)
            {
                leader.Motor.MoveTo(target.Position);
                leader.StateMachine.TryRun();
                return;                                     // walk into range first
            }
            leader.Motor.Stop();
        }
        if (!leader.StateMachine.TryStartAttack(MathF.Max(0.3f, skill.SwitchAllTime / 1000f))) return;
        _casts[leader] = new SanguoCastState(skill);
        var presenter = _presenters.GetValueOrDefault(leader);
        _ = presenter != null && presenter.PlaySkillClip(skill.AnimName);
    }

    // ------------------------------------------------------------ AI

    private void MonsterBrains(float deltaTime)
    {
        foreach (SanguoFieldActor enemy in _stage.Enemies)
        {
            if (enemy.Dead) continue;
            float cooldown = _aiCooldowns.TryGetValue(enemy, out float c) ? c : 0f;
            _aiCooldowns[enemy] = MathF.Max(0f, cooldown - deltaTime);
            if (enemy.StateMachine.Busy) continue;

            SanguoFieldActor? target = FindNearestHero(enemy);
            if (target == null) return;
            float distance = Float2.Length(target.Position - enemy.Position);
            if (distance > 2.2f)
            {
                enemy.Motor.MoveTo(target.Position);
                enemy.StateMachine.TryRun();
            }
            else if (cooldown <= 0f)
            {
                enemy.Motor.FaceTowards(target.Position);
                enemy.Motor.Stop();
                enemy.StateMachine.TryStartAttack(0.6f);
                _aiCooldowns[enemy] = 1.5f;
                // a plain melee hit: circle 2.2 m in front
                SanguoMissile swing = new() { ShapeType = 1, ShapeParam1 = 2.2f, DamageStartFrame = 1, DamageEndFrame = 1, DamageIntervalFrame = 1 };
                int attackerAtk = MonsterAttackOf(enemy);
                SanguoSkillRules.ResolveHits(swing, enemy.Position, enemy.Facing,
                    _stage.Heroes, SanguoBattleStage.EnemyCamp, attackerAtk, 1f, _hitScratch);
                for (int hitIndex = 0; hitIndex < _hitScratch.Count; hitIndex++)
                {
                    SanguoHit hit = _hitScratch[hitIndex];
                    OnHeroHit(hit.Target, hit.Damage);
                }
            }
            else
            {
                enemy.Motor.Stop();
                enemy.StateMachine.Reset();
            }
        }
    }

    private int MonsterAttackOf(SanguoFieldActor enemy)
        => enemy.Health.MaxHp > 0 ? 20 : 20;                // demo constant; MonsterValue atk drives M7

    private SanguoFieldActor? FindNearestHero(SanguoFieldActor from)
    {
        SanguoFieldActor? best = null;
        float bestSqr = float.MaxValue;
        foreach (SanguoFieldActor hero in _stage.Heroes)
        {
            if (hero.Dead) continue;
            float sqr = Float2.LengthSquared(hero.Position - from.Position);
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = hero;
            }
        }
        return best;
    }

    private SanguoFieldActor? FindNearestEnemy(SanguoFieldActor from)
    {
        SanguoFieldActor? best = null;
        float bestSqr = float.MaxValue;
        foreach (SanguoFieldActor enemy in _stage.Enemies)
        {
            if (enemy.Dead) continue;
            float sqr = Float2.LengthSquared(enemy.Position - from.Position);
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = enemy;
            }
        }
        return best;
    }

    // ------------------------------------------------------------ casts + bullets

    private void TickCasts(float deltaTime)
    {
        float deltaMs = deltaTime * 1000f;
        _finishedCasts.Clear();
        foreach ((SanguoFieldActor actor, SanguoCastState cast) in _casts)
        {
            if (actor.Dead)
            {
                _finishedCasts.Add(actor);
                continue;
            }
            foreach (SanguoSkillEvent ev in cast.Advance(deltaMs))
                FireEvent(actor, cast, ev);
            if (cast.Finished) _finishedCasts.Add(actor);
        }
        for (int i = 0; i < _finishedCasts.Count; i++) _casts.Remove(_finishedCasts[i]);

        // live bullets fly for their missile's total time and damage on their landing frames
        for (int i = _liveBullets.Count - 1; i >= 0; i--)
        {
            var entry = _liveBullets[i];
            float elapsed = entry.elapsedMs + deltaMs;
            int frame = (int)(elapsed / 1000f * 30f);
            SanguoMissile missile = entry.missile;
            if (missile.TotalTime > 0 && elapsed > missile.TotalTime)
            {
                _liveBullets.RemoveAt(i);
                continue;
            }
            if (!SanguoSkillRules.MissileLandsThisFrame(missile, frame)) continue;
            SanguoFieldActor owner = entry.owner;
            if (owner.Dead || _stage == null) continue;
            var alreadyHit = entry.hit;
            foreach (SanguoFieldActor candidate in _stage.Enemies)
            {
                if (candidate.Dead || alreadyHit.Contains(candidate.ID)) continue;
                if (Float2.Length(candidate.Position - owner.Position) > 5f) continue;   // reach
                var hitSet = alreadyHit;
                SanguoMissile shape = missile;
                bool inShape = shape.ShapeType switch
                {
                    2 => SanguoHitShapes.InCone(owner.Position, owner.Facing, candidate.Position,
                        shape.ShapeParam1, shape.ShapeParam2 * MathF.PI / 180f),
                    3 => SanguoHitShapes.InBox(owner.Position, owner.Facing, candidate.Position,
                        shape.ShapeParam1, shape.ShapeParam2),
                    _ => SanguoHitShapes.InCircle(owner.Position, candidate.Position,
                        shape.ShapeParam1 > 0f ? shape.ShapeParam1 : 3f),
                };
                if (!inShape) continue;
                hitSet.Add(candidate.ID);
                int damage = candidate.Health.ApplyHit(120, 1f);
                var targetPresenter = _presenters.GetValueOrDefault(candidate);
                targetPresenter?.PlayHurt();
                Debug.Log("[SanguoBattle] hit " + candidate.Name + " -" + damage);
            }
            if (missile.DamageEndFrame > 0 && frame >= missile.DamageEndFrame) _liveBullets.RemoveAt(i);
        }
    }

    private void FireEvent(SanguoFieldActor actor, SanguoCastState cast, SanguoSkillEvent ev)
    {
        if (ev.EventType != 1) return;                      // 5 = sound (M6), others later
        if (!_callBullets.TryGetValue(ev.EventIntParam, out SanguoCallBullet bullet)) return;
        int missileId = bullet.BulletID;
        if (missileId == 0 || !_missiles.TryGetValue(missileId, out SanguoMissile missile)) return;
        _liveBullets.Add((bullet, missile, actor, 0f, new HashSet<int>()));
    }

    // ------------------------------------------------------------ camera + hits

    private void FollowCamera(float deltaTime)
    {
        if (_battleCamera == null || _leader == null) return;
        Float3 target = new(_leader.Position.X, 192f, _leader.Position.Y - 12f);
        Float3 position = _battleCamera.Transform.Position;
        float blend = Math.Clamp(deltaTime * 6f, 0f, 1f);
        _battleCamera.Transform.Position = position + (target - position) * blend;
        // Keep the authored battle pitch and screen roll stable while the position follows the
        // leader. Rebuilding a LookRotation every fixed step feeds the previous roll back into
        // the transform and can flip GameView vertically at 60 Hz.
        _battleCamera.Transform.LocalEulerAngles = new Float3(26f, 0f, 180f);
    }

    private void OnHeroHit(SanguoFieldActor hero, int damage)
    {
        // the kernel already applied the damage (ResolveHits); this only drives the feedback
        Debug.Log("[SanguoBattle] hero hit " + hero.Name + " -" + damage + " (hp " + hero.Health.Hp + ")");
        if (hero.Health.Dead) return;
        hero.StateMachine.TakeHurt();
        var presenter = _presenters.GetValueOrDefault(hero);
        presenter?.PlayHurt();
    }
}
