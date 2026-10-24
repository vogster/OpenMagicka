using System.Numerics;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Tests.Gameplay;

public class SegmentTests
{
    [Fact]
    public void CrossingSegments_AreZeroApart_WhereTheyCross()
    {
        var a = Segment.Between(new Vector3(-1, 0, 0), new Vector3(1, 0, 0));
        var b = Segment.Between(new Vector3(0, -1, 0), new Vector3(0, 3, 0));

        var d = Segment.DistanceSquared(a, b, out var s, out var t);

        Assert.Equal(0, d, 5);
        Assert.Equal((0.5f, 0.25f), (s, t));
    }

    [Fact]
    public void SkewSegments_MeetAtTheirCommonNormal()
    {
        var a = Segment.Between(new Vector3(-1, 0, 0), new Vector3(1, 0, 0));
        var b = Segment.Between(new Vector3(0, -1, 2), new Vector3(0, 1, 2));

        Assert.Equal(4, Segment.DistanceSquared(a, b, out _, out _), 5);
    }

    [Fact]
    public void BeyondAnEnd_TheEndIsNearest()
    {
        var a = Segment.Between(Vector3.Zero, new Vector3(1, 0, 0));
        var b = Segment.Between(new Vector3(3, 1, 0), new Vector3(3, -1, 0));

        Assert.Equal(4, Segment.DistanceSquared(a, b, out var s, out var t), 5);
        Assert.Equal((1f, 0.5f), (s, t));
    }

    [Fact]
    public void ParallelAndPointSegments_StillMeasure()
    {
        var a = Segment.Between(Vector3.Zero, new Vector3(2, 0, 0));
        var b = Segment.Between(new Vector3(1, 1, 0), new Vector3(3, 1, 0));
        var point = new Segment(new Vector3(1, 3, 0), Vector3.Zero);

        Assert.Equal(1, Segment.DistanceSquared(a, b, out _, out _), 5);
        Assert.Equal(9, Segment.DistanceSquared(a, point, out _, out _), 5);
        Assert.Equal(9, Segment.DistanceSquared(point, a, out _, out _), 5);
        Assert.Equal(0, Segment.DistanceSquared(point, point, out _, out _));
    }
}
