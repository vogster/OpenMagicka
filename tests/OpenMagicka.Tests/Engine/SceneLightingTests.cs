using Microsoft.Xna.Framework;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Rendering;
using Light = OpenMagicka.Content.Magicka.LevelLightContent;

namespace OpenMagicka.Tests.Engine;

public class SceneLightingTests
{
    private static Light Make(LevelLightType type, Vector3 direction, Vector3 diffuse) => new(
        "sun", Vector3.Zero, direction, type, OpenMagicka.Content.Magicka.LightVariationType.None, 10, false, 0, 0, diffuse,
        new Vector3(0.1f), 0.5f, 0, 0, 512, false);

    [Fact]
    public void TheSun_IsTheLevelsFirstDirectionalLight()
    {
        var lights = new[] { Make(LevelLightType.Point, Vector3.Down, Vector3.One), Make(LevelLightType.Directional, new Vector3(0, -2, 0), new Vector3(1, 0.5f, 0)) };

        var sun = SceneLighting.Sun(lights);

        Assert.Equal((Vector3.Down, new Vector3(1, 0.5f, 0), new Vector3(0.1f), 0.5f), (sun.Direction, sun.Diffuse, sun.Ambient, sun.Specular));
    }

    [Fact]
    public void WithoutADirectionalLight_ASoftDefaultIsUsed()
    {
        var sun = SceneLighting.Sun([]);

        Assert.True(sun.Direction.Y < 0, "from above");
        Assert.Equal(1, sun.Direction.Length(), 4);
    }

    [Fact]
    public void Fog_ComesFromTheSceneSettings_WhenTheSceneHasIt()
    {
        var fogged = SceneSettings.Default with { Fog = new FogSettings(10, 90, new Vector4(0.2f, 0.3f, 0.4f, 1)) };

        Assert.Equal(new SceneFog(true, 10, 90, new Vector3(0.2f, 0.3f, 0.4f)), SceneLighting.Fog(fogged));
        Assert.False(SceneLighting.Fog(SceneSettings.Default).Enabled);
        Assert.False(SceneLighting.Fog(null).Enabled);
        Assert.False(SceneLighting.Fog(fogged with { Fog = new FogSettings(50, 50, Vector4.One) }).Enabled);   // no range
    }
}
