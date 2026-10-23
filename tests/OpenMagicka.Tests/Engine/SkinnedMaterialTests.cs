using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content.Xna;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Tests.Engine;

public class SkinnedMaterialTests
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

    private static ContentRef<Texture2DContent> Ref(string path) => ContentRef<Texture2DContent>.FromExternal(new ExternalReference(path));

    private static SkinnedModelBasicEffectContent Crusader(bool baseMap = true) => new(
        0, 0.25f, new Vector3(0.5f), 0.75f, 20, 0.6f, true, true, baseMap, true, false, false, false,
        Ref("details"), Ref("base"), Ref("spec"), default, default, default);

    [Fact]
    public void Apply_SetsTheMaterial_AsTheOriginalDrawsACharacter()
    {
        var parameters = new Recorder();

        SkinnedDeferredMaterial.Apply(parameters, Crusader(), _ => null);

        Assert.Equal(Vector3.One, parameters.Set["DiffuseColor"]);   // characters are drawn with a white diffuse colour
        Assert.Equal((0.25f, 0.75f, 20f, 0.6f, true), (parameters.Set["EmissiveAmount"], parameters.Set["SpecularAmount"],
            parameters.Set["SpecularPower"], parameters.Set["OverlayAlpha"], parameters.Set["UseSoftLightBlend"]));
        Assert.Equal((1f, 0f, 4f), (parameters.Set["Alpha"], parameters.Set["Damage"], parameters.Set["FresnelPower"]));
        Assert.Equal(Vector4.Zero, parameters.Set["Colorize"]);
        Assert.Equal(false, parameters.Set["DiffuseMap0Enabled"]);   // nothing was bound (lookup gave null)
        Assert.Equal(false, parameters.Set["DamageMap0Enabled"]);
    }

    [Fact]
    public void Apply_BindsTheDiffuseMaps_AndTheSpecularMapAsTheMaterialMap()
    {
        var asked = new List<string?>();

        SkinnedDeferredMaterial.Apply(new Recorder(), Crusader(), reference =>
        {
            asked.Add(reference?.Path);
            return null;
        });

        Assert.Equal(["base", "details", "spec"], asked.OfType<string>().Order(StringComparer.Ordinal));
        Assert.Equal(["base", "details", "spec"], SkinnedDeferredMaterial.References(Crusader()).Select(r => r.Path).Order(StringComparer.Ordinal));
        Assert.Equal(["details", "spec"], SkinnedDeferredMaterial.References(Crusader(baseMap: false)).Select(r => r.Path).Order(StringComparer.Ordinal));
    }
}
