using System;
using System.Collections.Generic;

namespace SanGuoXEngine.Battle;

/// <summary>Fixed-cell broad phase for target and missile queries in editor Play.</summary>
public sealed class SanguoSpatialHash
{
    private const float CellSize = 8f;
    private readonly Dictionary<Cell, List<SanguoFieldActor>> _cells = new();

    private readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X;
        public readonly int Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public bool Equals(Cell other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
    }

    public void Rebuild(IReadOnlyList<SanguoFieldActor> heroes,
        IReadOnlyList<SanguoFieldActor> enemies)
    {
        foreach (List<SanguoFieldActor> bucket in _cells.Values) bucket.Clear();
        AddLiving(heroes);
        AddLiving(enemies);
    }

    public SanguoFieldActor? FindNearest(SanguoFieldActor from, int camp, float maxDistance)
    {
        float radius = MathF.Max(0f, maxDistance);
        float bestSqr = radius * radius;
        int minX = ToCell(from.Position.X - radius), maxX = ToCell(from.Position.X + radius);
        int minY = ToCell(from.Position.Y - radius), maxY = ToCell(from.Position.Y + radius);
        SanguoFieldActor? best = null;
        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        {
            if (!_cells.TryGetValue(new Cell(x, y), out List<SanguoFieldActor>? bucket)) continue;
            for (int i = 0; i < bucket.Count; i++)
            {
                SanguoFieldActor actor = bucket[i];
                if (actor.Camp != camp || actor.Dead || ReferenceEquals(actor, from)) continue;
                float sqr = Float2.LengthSquared(actor.Position - from.Position);
                if (sqr < bestSqr) { bestSqr = sqr; best = actor; }
            }
        }
        // Preserve the original battle semantics when a target is outside the usual query
        // radius: a distant actor is still a valid chase target. This fallback is reached only
        // when no nearby bucket contained a candidate, so normal combat stays broad-phase only.
        if (best != null) return best;
        foreach (List<SanguoFieldActor> bucket in _cells.Values)
        for (int i = 0; i < bucket.Count; i++)
        {
            SanguoFieldActor actor = bucket[i];
            if (actor.Camp != camp || actor.Dead || ReferenceEquals(actor, from)) continue;
            float sqr = Float2.LengthSquared(actor.Position - from.Position);
            if (sqr < bestSqr) { bestSqr = sqr; best = actor; }
        }
        return best;
    }

    public void CollectNearby(Float2 centre, float radius, int camp,
        List<SanguoFieldActor> output)
    {
        output.Clear();
        float safeRadius = MathF.Max(0f, radius);
        float radiusSqr = safeRadius * safeRadius;
        int minX = ToCell(centre.X - safeRadius), maxX = ToCell(centre.X + safeRadius);
        int minY = ToCell(centre.Y - safeRadius), maxY = ToCell(centre.Y + safeRadius);
        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        {
            if (!_cells.TryGetValue(new Cell(x, y), out List<SanguoFieldActor>? bucket)) continue;
            for (int i = 0; i < bucket.Count; i++)
            {
                SanguoFieldActor actor = bucket[i];
                if (actor.Camp == camp && !actor.Dead
                    && Float2.LengthSquared(actor.Position - centre) <= radiusSqr)
                    output.Add(actor);
            }
        }
    }

    private void AddLiving(IReadOnlyList<SanguoFieldActor> actors)
    {
        for (int i = 0; i < actors.Count; i++)
        {
            SanguoFieldActor actor = actors[i];
            if (actor.Dead) continue;
            Cell cell = new(ToCell(actor.Position.X), ToCell(actor.Position.Y));
            if (!_cells.TryGetValue(cell, out List<SanguoFieldActor>? bucket))
                _cells.Add(cell, bucket = new List<SanguoFieldActor>(8));
            bucket.Add(actor);
        }
    }

    private static int ToCell(float value) => (int)MathF.Floor(value / CellSize);
}
