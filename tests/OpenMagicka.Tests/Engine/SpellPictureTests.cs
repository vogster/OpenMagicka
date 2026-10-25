using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Tests.Content;
using Microsoft.Xna.Framework;
using Numerics = System.Numerics;

namespace OpenMagicka.Tests.Engine;

/// <summary>The spells' own pictures (OM-76): their colours, the beams' strips and helices, the capsule lights.</summary>
public class SpellPictureTests
{
    [Fact]
    public void ASpellsColour_AveragesItsElements_LightningAndFireByTheirMagnitude()
    {
        Assert.Equal(SpellColours.Arcane, SpellColours.Of(Spell.Of(Elements.Arcane)));
        var bolt = SpellColours.Of(new Spell(lightning: 2, arcane: 1));
        Assert.True(Numerics.Vector3.Distance((SpellColours.Lightning * 2 + SpellColours.Arcane) / 3, bolt) < 1e-5f, bolt.ToString());
        Assert.Equal(SpellColours.Water, SpellColours.Of(new Spell(water: 3)));                     // counted once, whatever its magnitude
        Assert.Equal(Numerics.Vector3.Zero, SpellColours.Of(new Spell(steam: 1, poison: 1)));      // they have no colour
    }

    [Fact]
    public void TheBeamsStrip_CrowdsNearTheCaster_TwoSidesAPair_OnTheArcaneRow()
    {
        var strip = BeamDrawing.Strip();

        Assert.Equal(BeamDrawing.StripVertices * 4, strip.Length);
        Assert.Equal([0, 1, 0, 0.5f], strip[0..4]);
        Assert.Equal([0, -1, 0, 0.625f], strip[4..8]);
        Assert.Equal(1, strip[^4], 5);                                                              // the last pair at the end
        Assert.Equal(MathF.Pow(1 / 255f, 2), strip[8], 6);
    }

    [Fact]
    public void ABeamsHelices_WidenAndSlow_TurningInTurn_ABranchsStayingNarrow()
    {
        var root = new BeamPicture(Vector3.Zero, Vector3.Forward, 10, 6, 4, false, Vector3.One, BeamRows.None);

        var rings = BeamDrawing.Rings(root).ToList();
        var branch = BeamDrawing.Rings(root with { Rings = 2, Branch = true }).ToList();

        Assert.Equal(4, rings.Count);
        Assert.Equal([true, false, true, false], rings.Select(r => r.Clockwise));
        Assert.Equal(1.25f * MathF.Sqrt(0.5f), rings[0].MaxRadius, 5);
        Assert.Equal((6 + 10 * 1.25f) * MathF.Sqrt(1.25f), rings[3].Time, 4);
        Assert.Equal(0.1f / MathF.Sqrt(0.75f), rings[1].RayRadius, 5);
        Assert.Equal(2, branch.Count);
        Assert.All(branch, r => Assert.Equal(r.MinRadius, r.MaxRadius));
    }

    [Fact]
    public void ARowsQuad_RunsAlongTheBeam_AndFacesTheCamera()
    {
        var beam = new BeamPicture(new Vector3(1, 1, 0), Vector3.UnitX, 8, 0, 4, false, Vector3.One, BeamRows.Fire);

        var place = BeamDrawing.RowPlace(beam, new Vector3(4, 10, 0), Vector3.Down);

        Assert.True(Vector3.Distance(new Vector3(9, 1, 0), Vector3.Transform(new Vector3(0, 1, 0), place)) < 1e-4f);   // its far end at the beam's
        Assert.True(Vector3.Distance(new Vector3(1, 1, 0), Vector3.Transform(Vector3.Zero, place)) < 1e-4f);
        Assert.Equal(1, Math.Abs(Vector3.Dot(place.Backward, Vector3.UnitY)), 4);                   // its face up, to the camera above
        Assert.Equal(1, place.Right.Length(), 4);                                                    // 2 across, the quad's −1 to 1
    }

    [Fact]
    public void ACapsuleLight_IsPutHalfwayAlong_ItsMinusZTowardTheEnd()
    {
        var lamp = new CapsuleLighting(new Vector3(0, 1, 0), new Vector3(6, 1, 0), 5, Vector3.One, Vector3.Zero);
        var upright = lamp with { End = new Vector3(0, 7, 0) };

        Assert.True(Vector3.Distance(new Vector3(3, 1, 0), lamp.World.Translation) < 1e-4f);
        Assert.True(Vector3.Distance(Vector3.UnitX, Vector3.TransformNormal(-Vector3.UnitZ, lamp.World)) < 1e-4f);
        Assert.True(Vector3.Distance(Vector3.UnitY, Vector3.TransformNormal(-Vector3.UnitZ, upright.World)) < 1e-4f);
        var nearlyUpright = lamp with { End = new Vector3(1e-6f, 7, 0) };
        Assert.True(LevelParts.IsFinite(nearlyUpright.World));
        Assert.Equal(Matrix.CreateTranslation(lamp.Start), (lamp with { End = lamp.Start }).World);
    }

    [Fact]
    public void ABeamsPicture_CarriesItsRows_AndItsLight_ABranchLitRoundAbout()
    {
        Assert.Equal(BeamRows.Fire | BeamRows.Steam, SpellVisuals.Rows(Elements.Arcane | Elements.Fire | Elements.Steam | Elements.Shield));
        Assert.Equal(BeamRows.None, SpellVisuals.Rows(Elements.Life));
    }

    [GameContentFact]
    public void TheGamesCapsule_IsAUnitCapsule_OfPositionsNormalsAndTextureCoordinates()
    {
        var loader = new OpenMagicka.Content.ContentLoader(new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);

        var (vertices, count, indices, primitives) = CapsuleLights.Capsule((OpenMagicka.Content.Xna.ModelContent)loader.LoadUncached(CapsuleLights.Mesh).Root!);

        Assert.Equal((114, 100), (count, primitives));
        Assert.Equal(count * 32, vertices.Length);
        Assert.Equal(primitives * 3 * 2, indices.Length);
        var z = Enumerable.Range(0, count).Select(i => BitConverter.ToSingle(vertices, i * 32 + 8)).ToList();
        Assert.InRange(z.Max(), 2, 2.1f);                                                            // the round ends past ±1
    }
}
