using System.Numerics;

namespace OpenMagicka.Tests.Gameplay;

public class ArcIntersectTests
{
    private static readonly Thing Target = new(new Vector3(0, 0, -4), radius: 1);

    [Fact]
    public void ATargetAhead_IsHit_OnItsNearSide()
    {
        Assert.True(Target.ArcIntersect(Vector3.Zero, -Vector3.UnitZ, range: 4, angle: 0.1f, heightTolerance: 1, out var hit));

        Assert.Equal(new Vector3(0, 0, -3), hit);
    }

    [Theory]
    [InlineData(2.9f, 0.5f, 1f, false)]                // out of reach: 4 − 1 > 2.9
    [InlineData(3f, 0.5f, 1f, true)]
    [InlineData(4f, 0.5f, 0.1f, true)]                 // level with the origin: any tolerance will do
    public void TheReach_CountsTheTargetsRadius(float range, float angle, float tolerance, bool hits)
    {
        Assert.Equal(hits, Target.ArcIntersect(Vector3.Zero, -Vector3.UnitZ, range, angle, tolerance, out _));
    }

    [Fact]
    public void TheArc_IsWidenedByTheTargetsHalfWidth()
    {
        var sideways = Vector3.Normalize(new Vector3(1, 0, -1));                // 45° off the target
        var halfWidth = MathF.Acos(1 - 1f / (2 * 16));                         // r = 1 seen from 4 m

        Assert.True(Target.ArcIntersect(Vector3.Zero, sideways, 10, MathF.PI / 4 - halfWidth + 0.01f, 1, out _));
        Assert.False(Target.ArcIntersect(Vector3.Zero, sideways, 10, MathF.PI / 4 - halfWidth - 0.01f, 1, out _));
    }

    [Fact]
    public void PointBlank_AndBehind_AreMisses()
    {
        Assert.False(Target.ArcIntersect(new Vector3(0, 0, -3.6f), -Vector3.UnitZ, 10, 3, 1, out _));      // 0.4 from the centre: under r/2
        Assert.False(Target.ArcIntersect(new Vector3(0, 0, -4), -Vector3.UnitZ, 10, 3, 1, out _));
        Assert.False(Target.ArcIntersect(Vector3.Zero, Vector3.UnitZ, 10, 0.5f, 1, out _));                // facing away
    }

    [Fact]
    public void ATargetTooHighOrTooLow_IsMissed()
    {
        Assert.False(Target.ArcIntersect(new Vector3(0, 2, 0), -Vector3.UnitZ, 10, 1, heightTolerance: 1.5f, out _));
        Assert.True(Target.ArcIntersect(new Vector3(0, 1, 0), -Vector3.UnitZ, 10, 1, heightTolerance: 1.5f, out _));
    }
}
