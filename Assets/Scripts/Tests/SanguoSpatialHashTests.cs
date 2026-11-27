using System.Collections.Generic;

using SanGuoXEngine.Battle;

using XFloat2 = SanGuoXEngine.Battle.Float2;

using Xunit;

namespace SanGuoXEngine.Tests;

public sealed class SanguoSpatialHashTests
{
    [Fact]
    public void FindNearestUsesCampAndDistance()
    {
        SanguoFieldActor source = Actor(2, 0f, 0f);
        SanguoFieldActor nearEnemy = Actor(1, 3f, 0f);
        SanguoFieldActor farEnemy = Actor(1, 30f, 0f);
        SanguoFieldActor friendly = Actor(2, 1f, 0f);
        SanguoSpatialHash hash = new();
        hash.Rebuild(new[] { source, friendly }, new[] { nearEnemy, farEnemy });

        Assert.Same(nearEnemy, hash.FindNearest(source, 1, 10f));
        Assert.Null(hash.FindNearest(source, 1, 2f));
    }

    [Fact]
    public void CollectNearbyReturnsOnlyLivingActorsInsideRadius()
    {
        SanguoFieldActor inside = Actor(1, 1f, 1f);
        SanguoFieldActor outside = Actor(1, 12f, 0f);
        SanguoFieldActor dead = new()
        {
            Camp = 1,
            Health = new SanguoHealth(1, 1, 0),
            Position = new XFloat2(2f, 0f),
        };
        dead.Health.ApplyHit(10, 1f);
        SanguoSpatialHash hash = new();
        hash.Rebuild(new[] { Actor(2, 0f, 0f) }, new[] { inside, outside, dead });
        List<SanguoFieldActor> result = new();

        hash.CollectNearby(new XFloat2(0f, 0f), 4f, 1, result);

        Assert.Single(result);
        Assert.Same(inside, result[0]);
    }

    [Fact]
    public void RebuildRemovesActorsThatMovedOutOfRange()
    {
        SanguoFieldActor source = Actor(2, 0f, 0f);
        SanguoFieldActor enemy = Actor(1, 2f, 0f);
        SanguoSpatialHash hash = new();
        hash.Rebuild(new[] { source }, new[] { enemy });
        enemy.Position = new XFloat2(100f, 0f);
        hash.Rebuild(new[] { source }, new[] { enemy });

        Assert.Null(hash.FindNearest(source, 1, 10f));
    }

    private static SanguoFieldActor Actor(int camp, float x, float y)
        => new() { Camp = camp, Health = new SanguoHealth(100, 10, 0), Position = new XFloat2(x, y) };
}
