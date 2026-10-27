using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Physics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Liquids;
using Elements = OpenMagicka.Gameplay.Spells.Elements;
using OpenMagicka.Tests.Content;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Water, ice and lava (OM-88 phase 1): freezing, melting, drifting back, and what a segment meets.</summary>
public class LiquidTests
{
    /// <summary>A 2 × 2 sheet at height 0 of two triangles facing up (corners 0..3: −x−z, +x−z, +x+z, −x+z).</summary>
    private static Liquid Sheet(LiquidKind kind = LiquidKind.Water, bool freezable = true, bool autoFreeze = false) =>
        new(kind, [new(-1, 0, -1), new(1, 0, -1), new(1, 0, 1), new(-1, 0, 1)], [0, 2, 1, 0, 3, 2], true, freezable, autoFreeze);

    private static DamageEntry Entry(Elements element, float magnitude) => new(default, element, 0, magnitude);

    [Theory]
    [InlineData(1, 1.734f)]
    [InlineData(2, 2.466f)]
    [InlineData(3, 2.7747f)]
    [InlineData(4, 2.905f)]
    public void TheAmount_SaturatesAtThree(float cold, float expected) =>
        Assert.Equal(expected, Liquid.Amount(LiquidKind.Water, [Entry(Elements.Cold, cold)], 1), 2);

    [Fact]
    public void FireMelts_ColdAndFireCancel_AndWaterCoolsOnlyLava()
    {
        Assert.Equal(-1.734f, Liquid.Amount(LiquidKind.Water, [Entry(Elements.Fire, 1)], 1), 2);
        Assert.Equal(0, Liquid.Amount(LiquidKind.Water, [Entry(Elements.Cold, 1), Entry(Elements.Fire, 1)], 1));
        Assert.Equal(0, Liquid.Amount(LiquidKind.Water, [Entry(Elements.Water, 2)], 1));
        Assert.Equal(2.466f, Liquid.Amount(LiquidKind.Lava, [Entry(Elements.Water, 2)], 1), 2);
        Assert.Equal(-27.7f, Liquid.Amount(LiquidKind.Water, [Entry(Elements.Fire, 3)], 10), 1);   // a napalm bomb
        Assert.Equal(2 * 2.466f, Liquid.Amount(LiquidKind.Water, [Entry(Elements.Cold | Elements.Water, 2)], 2), 2);   // an entry counts for each element
    }

    [Fact]
    public void AFrozenCorner_MakesItsTrianglesIce_AndItDriftsBackAtATenthASecond()
    {
        var sheet = Sheet();
        Assert.False(sheet.IsIce(0));
        sheet.Freeze(new Vector3(1, 5, 0.8f), new Vector3(0, 0, 0.5f), MathF.PI / 8, 2);   // high above: heights do not count

        Assert.Equal([0f, 0f, 2f, 0f], sheet.FreezeValues.ToArray());
        Assert.True(sheet.IsIce(0) && sheet.IsIce(1));                       // both triangles share the corner
        sheet.Step(10);
        Assert.Equal(1, sheet.FreezeValues[2], 4);
        sheet.Step(100);
        Assert.Equal(0, sheet.FreezeValues[2]);                              // never below liquid
    }

    [Fact]
    public void ALiquidFrozenByItself_StartsSolid_AndFreezesBack_WithinMinusOneToOne()
    {
        var sheet = Sheet(autoFreeze: true);
        Assert.True(sheet.IsIce(0));
        sheet.FreezeAll(-27.7f);
        sheet.Step(0.001f);
        Assert.Equal(-1, sheet.FreezeValues[0], 3);
        sheet.Step(15);
        Assert.True(sheet.FreezeValues[0] >= Liquid.Frozen);                 // solid again in 15 s
        sheet.Step(100);
        Assert.Equal(1, sheet.FreezeValues[0]);
    }

    [Fact]
    public void ALiquidThatDoesNotFreeze_OnlyDrifts()
    {
        var sheet = Sheet(freezable: false);
        sheet.Freeze(Vector3.Zero, new Vector3(0, 0, 5), 2 * MathF.PI, 2);
        sheet.FreezeAll(2);

        Assert.All(sheet.FreezeValues.ToArray(), v => Assert.Equal(0, v));
    }

    /// <summary>A ring of corners, one every 2°, 1 from the middle.</summary>
    private static Liquid Ring(LiquidKind kind)
    {
        var corners = Enumerable.Range(0, 180).Select(i => i * 2 * MathF.PI / 180).Select(a => new Vector3(MathF.Sin(a), 0, MathF.Cos(a))).ToArray();
        return new Liquid(kind, corners, [0, 1, 2], true, true, false);
    }

    [Fact]
    public void AQuarterTurn_CoversOnlyFiftyFourDegreesEitherSide_AsTheOriginals()
    {
        var ring = Ring(LiquidKind.Water);
        ring.Freeze(Vector3.Zero, new Vector3(0, 0, 2), MathF.PI / 2, 1);

        var reached = Enumerable.Range(0, 180).Where(i => ring.FreezeValues[i] > 0).Select(i => i <= 90 ? i * 2 : i * 2 - 360).ToArray();
        Assert.Equal(-54, reached.Min());
        Assert.Equal(54, reached.Max());
    }

    [Fact]
    public void AWholeCircle_CountsWatersCornersOnce_ButLavasTwiceWhereItsSlicesMeet()
    {
        var water = Ring(LiquidKind.Water);
        var lava = Ring(LiquidKind.Lava);
        water.Freeze(Vector3.Zero, new Vector3(0, 0, 2), 2 * MathF.PI, 1);
        lava.Freeze(Vector3.Zero, new Vector3(0, 0, 2), 2 * MathF.PI, 1);

        Assert.True(water.FreezeValues.ToArray().Count(v => v > 1) <= 17);  // the slices' first corners only (the original's slip)
        Assert.True(lava.FreezeValues.ToArray().Count(v => v > 1) > 17);
    }

    [Fact]
    public void ASlice_StopsWhereItsReachEnds()
    {
        var ring = Ring(LiquidKind.Water);
        ring.Freeze(Vector3.Zero, new Vector3(0, 0, 2), MathF.PI / 8, 1, reach: (_, _) => 0.5f);   // a shield in the way

        Assert.All(ring.FreezeValues.ToArray(), v => Assert.Equal(0, v));
    }

    [Fact]
    public void ASegment_MeetsLiquidOrIce_AsAsked_FromAbove_AndOnAMovedSheet()
    {
        var sheet = Sheet();
        Assert.True(sheet.Meets(new Vector3(0.5f, 1, 0.2f), new Vector3(0.5f, -1, 0.2f), false, false, out var point, out var normal));
        Assert.Equal(new Vector3(0.5f, 0, 0.2f), point);
        Assert.True(normal.Y > 0.99f);                                        // its triangles face up
        Assert.True(sheet.Meets(new Vector3(0.5f, 1, 0.2f), new Vector3(0.5f, -1, 0.2f), false, true, out _, out _));   // liquid: met unless skipped
        Assert.False(sheet.Meets(new Vector3(0.5f, 1, 0.2f), new Vector3(0.5f, -1, 0.2f), true, false, out _, out _));

        sheet.World = Matrix4x4.CreateTranslation(0, 3, 0);
        Assert.True(sheet.Meets(new Vector3(0, 4, 0), new Vector3(0, 2, 0), false, false, out point, out _));
        Assert.Equal(3, point.Y, 4);
    }

    [GameContentFact]
    public void EveryLiquidOfTheGame_Builds_ItsTrianglesFacingUp()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var (sheets, up, all) = (0, 0, 0);
        foreach (var path in loader.Files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.LoadUncached(path[..^4]).Root is not LevelModelContent level)
            {
                continue;
            }
            foreach (var content in level.Liquids)
            {
                if (LiquidSheets.From(content) is not { } sheet)
                {
                    continue;
                }
                sheets++;
                for (var t = 0; t < sheet.TriangleCount; t++)
                {
                    var (a, b, c) = (sheet.Vertices[sheet.Triangles[t * 3]], sheet.Vertices[sheet.Triangles[t * 3 + 1]], sheet.Vertices[sheet.Triangles[t * 3 + 2]]);
                    var normal = Vector3.Cross(b - a, c - a);
                    all++;
                    up += normal.Y > 0 ? 1 : 0;
                }
            }
        }

        Assert.True(sheets >= 80);
        Assert.True(up > all * 0.95, $"{up} of {all} triangles face up");
    }

    [Fact]
    public void TheLiquidsMaterials_AreTheLevelsWaterAndLava() =>
        Assert.Equal(((int)CollisionMaterial.Water, (int)CollisionMaterial.Lava), (Liquid.WaterMaterial, Liquid.LavaMaterial));

    [Fact]
    public void ADriftThatChangesNothing_IsNotAChange()
    {
        var sheet = Sheet();
        var before = sheet.Version;
        sheet.Step(1);
        Assert.Equal(before, sheet.Version);                                  // liquid already: nothing to upload again
        sheet.FreezeAll(1);
        sheet.Step(1);
        Assert.Equal(before + 2, sheet.Version);
    }
}
