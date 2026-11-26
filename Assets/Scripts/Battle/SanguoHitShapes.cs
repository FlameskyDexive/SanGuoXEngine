using System;
using System.Collections.Generic;

using XEngine.Vector;

namespace SanGuoXEngine.Battle;

/// <summary>
/// Pure hit-shape geometry for the battle kernel (jws2's 扇形 / 圆形 / 盒形 judgement, plan §2).
/// Everything is XZ-planar: the battle moves on the ground plane, y is ignored. Shapes are
/// expressed in the attacker's local frame (forward = +Z of <paramref name="forward"/>).
/// </summary>
public static class SanguoHitShapes
{
    /// <summary>Hit when the target is within <paramref name="radius"/> of the centre.</summary>
    public static bool InCircle(Float2 centre, Float2 targetXZ, float radius)
        => Float2.Length(targetXZ - centre) <= radius;

    /// <summary>
    /// 扇形 hit: within <paramref name="radius"/> of the origin AND inside the half-angle
    /// <paramref name="halfAngleRadians"/> measured from <paramref name="forward"/> (MissileData's
    /// ShapeType 2 carries radius + half angle in ShapeParam1/2).
    /// </summary>
    public static bool InCone(Float2 origin, Float2 forward, Float2 targetXZ, float radius, float halfAngleRadians)
    {
        Float2 offset = targetXZ - origin;
        float distance = Float2.Length(offset);
        if (distance > radius) return false;
        if (distance < 1e-4f) return true;              // on the origin: inside every cone
        float forwardLength = Float2.Length(forward);
        if (forwardLength < 1e-4f) return true;         // no facing: treat as a circle
        float cosine = Float2.Dot(offset / distance, forward / forwardLength);
        return cosine >= MathF.Cos(halfAngleRadians);
    }

    /// <summary>
    /// 盒形 hit: the target in the oriented box centred at <paramref name="centre"/> with half
    /// extents (<paramref name="halfForward"/> along <paramref name="forward"/>,
    /// <paramref name="halfSide"/> across).
    /// </summary>
    public static bool InBox(Float2 centre, Float2 forward, Float2 targetXZ, float halfForward, float halfSide)
    {
        Float2 offset = targetXZ - centre;
        float forwardLength = Float2.Length(forward);
        if (forwardLength < 1e-4f) return Float2.Length(offset) <= MathF.Max(halfForward, halfSide);
        Float2 along = forward / forwardLength;
        Float2 across = new(-along.Y, along.X);
        float alongOffset = Float2.Dot(offset, along);
        // forward-only: the box spans [0, halfForward] ahead of the attacker (an attack box, not
        // a symmetric collider) and [-halfSide, halfSide] across.
        if (alongOffset < 0f || alongOffset > halfForward) return false;
        return MathF.Abs(Float2.Dot(offset, across)) <= halfSide;
    }
}

/// <summary>A 2D vector mirroring the jws2 battle plane; thin wrapper over explicit X/Z floats so
/// the pure kernel never depends on scene types.</summary>
public readonly struct Float2
{
    public readonly float X;
    public readonly float Y;

    public Float2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static Float2 operator +(Float2 a, Float2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Float2 operator -(Float2 a, Float2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Float2 operator *(Float2 a, float s) => new(a.X * s, a.Y * s);
    public static Float2 operator /(Float2 a, float s) => new(a.X / s, a.Y / s);

    public static float Dot(Float2 a, Float2 b) => a.X * b.X + a.Y * b.Y;

    public static float Length(Float2 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    public static float LengthSquared(Float2 v) => v.X * v.X + v.Y * v.Y;

    public static Float2 Normalize(Float2 v)
    {
        float length = Length(v);
        return length < 1e-6f ? default : new Float2(v.X / length, v.Y / length);
    }
}
