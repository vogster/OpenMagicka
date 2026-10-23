using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Tests.Engine;

public class MaterialTests
{
    private sealed class Recorder : IEffectParameters
    {
        public Dictionary<string, object?> Set { get; } = [];

        public bool Contains(string name) => true;

        public void SetValue(string name, Matrix value) => Set[name] = value;

        public void SetValue(string name, Vector3 value) => Set[name] = value;

        public void SetValue(string name, float value) => Set[name] = value;

        public void SetValue(string name, bool value) => Set[name] = value;

        public void SetValue(string name, Vector2 value) => Set[name] = value;

        public void SetValue(string name, Vector4 value) => Set[name] = value;

        public void SetValue(string name, Texture? value) => Set[name] = value;
    }

    private static RenderDeferredLayer Layer(string? diffuse, Vector3 color, string? normal = null) =>
        new(true, false, color, 0.5f, 16, 0.25f, 1.5f, 0.1f, diffuse is null ? null : new ExternalReference(diffuse), null,
            normal is null ? null : new ExternalReference(normal));

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 3)]
    public void TheTechnique_FollowsReflectionAndTheSecondLayer(bool reflection, bool secondLayer, int expected)
    {
        var material = new RenderDeferredEffectContent(1, 0, false, false, null, Layer("a", Vector3.One),
            secondLayer ? Layer("b", Vector3.One) : null);

        Assert.Equal(expected, RenderDeferredMaterial.Technique(material, hasReflectionMap: reflection));
    }

    [Fact]
    public void Apply_SetsEveryMaterialParameter_AndTheTextureFlagsFromWhatIsBound()
    {
        var material = new RenderDeferredEffectContent(0.75f, 2, true, false, null, Layer("rock", new Vector3(1, 0, 0), normal: "rock_n"), null);
        var parameters = new Recorder();

        RenderDeferredMaterial.Apply(parameters, Matrix.CreateTranslation(1, 2, 3), material, _ => null);

        Assert.Equal(Matrix.CreateTranslation(1, 2, 3), parameters.Set["World"]);
        Assert.Equal((0.75f, 2f, true), (parameters.Set["Alpha"], parameters.Set["Sharpness"], parameters.Set["VertexColorEnabled"]));
        Assert.Equal((new Vector3(1, 0, 0), 0.5f, 16f, 0.25f, 1.5f, 0.1f), (parameters.Set["DiffuseColor0"], parameters.Set["SpecAmount0"],
            parameters.Set["SpecPower0"], parameters.Set["EmissiveAmount0"], parameters.Set["NormalPower0"], parameters.Set["Reflectiveness0"]));
        Assert.Equal(true, parameters.Set["DiffuseTexture0AlphaDisabled"]);
        Assert.Equal(false, parameters.Set["DiffuseTexture0Enabled"]);    // nothing was bound (lookup gave null)
        Assert.Equal(false, parameters.Set["NormalMap0Enabled"]);
        Assert.Equal(Vector3.Zero, parameters.Set["DiffuseColor1"]);      // no second layer: zeros, like the original's empty material
        Assert.Equal(false, parameters.Set["DiffuseTexture1Enabled"]);
    }

    [Fact]
    public void Apply_AsksForEveryTextureTheMaterialReferences()
    {
        var material = new RenderDeferredEffectContent(1, 0, false, false, new ExternalReference("sky_cube"),
            Layer("rock", Vector3.One, normal: "rock_n"), Layer("moss", Vector3.One));
        var asked = new List<string?>();

        RenderDeferredMaterial.Apply(new Recorder(), Matrix.Identity, material, reference =>
        {
            asked.Add(reference?.Path);
            return null;
        });

        Assert.Equal(["moss", "rock", "rock_n", "sky_cube"], asked.OfType<string>().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AStandIn_KeepsTheBatchsDiffuse_AndTheSkinnedMaterialsSpecularAndEmissive()
    {
        var none = default(ContentRef<Texture2DContent>);
        var skinned = new SkinnedModelBasicEffectContent(0, 0.3f, new Vector3(0.5f), 0.4f, 20, 1, false, true, false, false, false, false, false,
            none, none, none, none, none, none);

        var material = RenderDeferredMaterial.StandIn(skinned, new ExternalReference("crusader"), new Vector3(0.5f));

        Assert.Equal("crusader", material.Layer0.DiffuseTexture?.Path);
        Assert.Equal((new Vector3(0.5f), 0.4f, 20f, 0.3f), (material.Layer0.DiffuseColor, material.Layer0.SpecAmount,
            material.Layer0.SpecPower, material.Layer0.EmissiveAmount));
        Assert.Equal((1f, (RenderDeferredLayer?)null, (ExternalReference?)null), (material.Alpha, material.Layer1, material.ReflectionMap));
        Assert.Equal(RenderDeferredMaterial.SingleLayer, RenderDeferredMaterial.Technique(material, hasReflectionMap: false));
    }

    [Fact]
    public void AStandIn_ForAnUnknownMaterial_IsJustItsDiffuse()
    {
        var material = RenderDeferredMaterial.StandIn(null, null, new Vector3(0.2f, 0.4f, 0.6f));

        Assert.Equal((new Vector3(0.2f, 0.4f, 0.6f), 0f, 0f, (ExternalReference?)null), (material.Layer0.DiffuseColor,
            material.Layer0.SpecAmount, material.Layer0.EmissiveAmount, material.Layer0.DiffuseTexture));
    }
}
