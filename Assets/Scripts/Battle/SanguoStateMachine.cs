using System;
using System.Collections.Generic;

using XEngine.Vector;

namespace SanGuoXEngine.Battle;

/// <summary>The states a battle actor cycles through (jws2 StateSystem, demo subset).</summary>
public enum SanguoActorState
{
    Idle,
    Run,
    Attack,
    Dodge,
    Hurt,
    Dead,
}

/// <summary>
/// The battle actor's state machine: which states a transition may enter from, and the timed
/// recovery of the transient ones (attack/dodge/hurt lock the actor for their duration). Pure
/// logic — the owner plays animations and drives gameplay on the events.
/// </summary>
public sealed class SanguoStateMachine
{
    /// <summary>Seconds the transient states lock the actor for (skill cast times override).</summary>
    public float AttackLock;
    public float DodgeLock = 0.35f;
    public float HurtLock = 0.35f;

    /// <summary>Hurt interrupts attacks; death interrupts everything.</summary>
    public SanguoActorState State { get; private set; } = SanguoActorState.Idle;
    public float RemainingLock { get; private set; }

    /// <summary>Raised on every accepted transition (the owner plays the matching animation).</summary>
    public event Action<SanguoActorState, SanguoActorState>? Transitioned;

    public bool Busy => State is SanguoActorState.Attack or SanguoActorState.Dodge || RemainingLock > 0f;
    public bool Dead => State == SanguoActorState.Dead;

    public void Reset()
    {
        State = SanguoActorState.Idle;
        RemainingLock = 0f;
    }

    /// <summary>Advances the lock timers. Returns true while the actor is still locked.</summary>
    public bool Tick(float deltaTime)
    {
        if (Dead) return false;
        if (RemainingLock > 0f)
        {
            RemainingLock = MathF.Max(0f, RemainingLock - deltaTime);
            if (RemainingLock <= 0f && State is SanguoActorState.Attack or SanguoActorState.Dodge or SanguoActorState.Hurt)
                Enter(SanguoActorState.Idle);
        }
        return RemainingLock > 0f;
    }

    public bool CanAttack => !Dead && !Busy;
    public bool CanMove => !Dead && State is SanguoActorState.Idle or SanguoActorState.Run;

    /// <summary>Starts an attack for <paramref name="lockSeconds"/> (the cast time). False when busy.</summary>
    public bool TryStartAttack(float lockSeconds)
    {
        if (!CanAttack || lockSeconds <= 0f) return false;
        Enter(SanguoActorState.Attack);
        RemainingLock = lockSeconds;
        return true;
    }

    public bool TryDodge()
    {
        if (Dead || State == SanguoActorState.Attack) return false;   // dodge cancels run/idle, not a swing
        Enter(SanguoActorState.Dodge);
        RemainingLock = DodgeLock;
        return true;
    }

    /// <summary>Hurt interrupts anything except death.</summary>
    public void TakeHurt(float lockSeconds = 0f)
    {
        if (Dead) return;
        Enter(SanguoActorState.Hurt);
        RemainingLock = MathF.Max(lockSeconds, HurtLock);
    }

    /// <summary>Death is terminal; repeated kills are ignored.</summary>
    public void Kill()
    {
        if (Dead) return;
        Enter(SanguoActorState.Dead);
        RemainingLock = 0f;
    }

    /// <summary>Run is a free state: entering it from Idle is always legal, from anywhere else only via CanMove.</summary>
    public bool TryRun()
    {
        if (!CanMove) return false;
        Enter(SanguoActorState.Run);
        return true;
    }

    private void Enter(SanguoActorState next)
    {
        SanguoActorState previous = State;
        if (previous == next) return;
        State = next;
        Transitioned?.Invoke(previous, next);
    }
}

/// <summary>
/// Ground-plane movement: seek a destination at a speed, stop inside the arrive radius, and turn
/// the facing towards the movement direction (or an explicit target) with a bounded turn speed.
/// Pure XZ logic; the owner feeds <see cref="Tick"/> with delta time and applies the resulting
/// position/facing to its Transform.
/// </summary>
public sealed class SanguoMotor
{
    public Float2 Position;
    public Float2 Facing = new(0f, 1f);
    public float Speed = 4.5f;
    public float TurnSpeedRadians = 12f;
    public float ArriveRadius = 0.15f;

    public Float2? Destination { get; private set; }

    public void MoveTo(Float2 destination) => Destination = destination;
    public void Stop() => Destination = null;

    /// <summary>Face a point immediately this frame (attacks snap the actor onto its target).</summary>
    public void FaceTowards(Float2 point)
    {
        Float2 direction = point - Position;
        if (Float2.Length(direction) > 1e-4f) Facing = Float2.Normalize(direction);
    }

    /// <summary>Advances movement/facing. Returns true while the actor is moving towards a destination.</summary>
    public bool Tick(float deltaTime)
    {
        bool moving = false;
        if (Destination is Float2 destination)
        {
            Float2 offset = destination - Position;
            float distance = Float2.Length(offset);
            if (distance <= ArriveRadius)
            {
                Destination = null;
            }
            else
            {
                Float2 direction = offset / distance;
                float step = MathF.Min(Speed * deltaTime, distance);
                Position += direction * step;
                TurnTowards(direction, deltaTime);
                moving = true;
            }
        }
        return moving;
    }

    private void TurnTowards(Float2 direction, float deltaTime)
    {
        Float2 current = Float2.Normalize(Facing);
        Float2 target = Float2.Normalize(direction);
        float cross = current.X * target.Y - current.Y * target.X;
        float dot = Math.Clamp(Float2.Dot(current, target), -1f, 1f);
        float angle = MathF.Atan2(MathF.Abs(cross), dot);          // 0..π, no acos degeneracy
        if (angle < 1e-3f)
        {
            Facing = target;
            return;
        }
        float step = MathF.Min(TurnSpeedRadians * deltaTime, angle);
        float sign = cross >= 0f ? 1f : -1f;                       // at 180° pick the positive arc
        float cos = MathF.Cos(step);
        float sin = MathF.Sin(step) * sign;
        // rotate the current facing by ±step around the plane — length-preserving (a linear
        // blend degenerates to zero exactly at the 180° turn)
        Facing = new Float2(current.X * cos - current.Y * sin, current.X * sin + current.Y * cos);
    }
}

/// <summary>
/// Registry of the actors in the field, per camp: target selection for the AI and the player's
/// auto-target (jws2 TargetFindAI). Pure lists — the stage registers/unregisters actors.
/// </summary>
public sealed class SanguoActorRegistry
{
    private readonly Dictionary<int, List<SanguoFieldActor>> _byCamp = new();

    public void Register(SanguoFieldActor actor)
    {
        if (!_byCamp.TryGetValue(actor.Camp, out List<SanguoFieldActor>? list))
            _byCamp[actor.Camp] = list = [];
        if (!list.Contains(actor)) list.Add(actor);
    }

    public void Unregister(SanguoFieldActor actor)
    {
        if (_byCamp.TryGetValue(actor.Camp, out List<SanguoFieldActor>? list)) list.Remove(actor);
    }

    public IReadOnlyList<SanguoFieldActor> OfCamp(int camp)
        => _byCamp.TryGetValue(camp, out List<SanguoFieldActor>? list) ? list : (IReadOnlyList<SanguoFieldActor>)Array.Empty<SanguoFieldActor>();

    /// <summary>The nearest living actor of <paramref name="enemyCamp"/> to <paramref name="from"/>.</summary>
    public SanguoFieldActor? FindNearest(Float2 from, int enemyCamp, float maxDistance = 40f)
    {
        SanguoFieldActor? best = null;
        float bestSqr = maxDistance * maxDistance;
        foreach (SanguoFieldActor actor in OfCamp(enemyCamp))
        {
            if (actor.Dead) continue;
            float sqr = Float2.LengthSquared(actor.Position - from);
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = actor;
            }
        }
        return best;
    }
}

/// <summary>
/// The battle actor as the kernel sees it: identity, vitals and the motor/state pair. Pure state
/// (no scene types) so gameplay rules are unit-testable; the scene-side presenter component binds
/// it to a GameObject, plays animations and writes the motor result into the Transform.
/// </summary>
public sealed class SanguoFieldActor
{
    public int ID;
    public string Name = "";
    public int Camp;
    public int MonsterType;
    public SanguoHealth Health;
    public SanguoStateMachine StateMachine = new();
    public SanguoMotor Motor = new();
    public Float2 Position
    {
        get => Motor.Position;
        set => Motor.Position = value;
    }
    public Float2 Facing => Motor.Facing;
    public float ShoveRadius = 0.5f;
    public bool Dead => Health.Dead || StateMachine.Dead;

    /// <summary>The current attack's hit landing time (skill timeline in M4-3); 0 while idle.</summary>
    public float AttackLockRemaining => StateMachine.RemainingLock;
}
