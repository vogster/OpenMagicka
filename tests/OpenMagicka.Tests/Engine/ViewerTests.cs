using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Content;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Threading;
using OpenMagicka.Game.Viewer;
using OpenMagicka.Tests.Levels;

namespace OpenMagicka.Tests.Engine;

public sealed class ViewerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "omview-" + Guid.NewGuid().ToString("N"));

    public ViewerTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Levels", "Swamp"));
        File.WriteAllText(Path.Combine(root, "Levels", "Swamp.lvl"),
            "<Level><Loading>l</Loading><Name>#swamp</Name><Description>d</Description>"
            + "<Scenes><Scene>Swamp/sw_s1.xml</Scene><Scene>Swamp/sw_s2.xml</Scene><Scene>Swamp/broken.xml</Scene></Scenes></Level>");
        File.WriteAllText(Path.Combine(root, "Levels", "Swamp", "sw_s1.xml"), SceneReaderTests.Scene("").Replace("swamp_s1", "sw_s1"));
        File.WriteAllText(Path.Combine(root, "Levels", "Swamp", "sw_s2.xml"), SceneReaderTests.Scene("").Replace("swamp_s1", "SW_S2"));
        File.WriteAllText(Path.Combine(root, "Levels", "Swamp", "broken.xml"), "<Scene><Sound/></Scene>");
        Directory.CreateDirectory(Path.Combine(root, "Models", "Characters"));
        File.WriteAllBytes(Path.Combine(root, "Models", "Characters", "wizard.xnb"), ModelFile("SkinnedModelReader"));
        File.WriteAllBytes(Path.Combine(root, "Models", "Characters", "Crate.xnb"), ModelFile("ModelReader"));
        File.WriteAllBytes(Path.Combine(root, "Models", "Characters", "skin.xnb"), new OpenMagicka.Tests.Content.XnbTestWriter().StringObject("texture").BuildBytes());
        File.WriteAllText(Path.Combine(root, "Models", "Characters", "broken.xnb"), "not xnb");
    }

    /// <summary>An XNB whose root reader is the named one (the catalog only looks at the reader table).</summary>
    private static byte[] ModelFile(string reader)
    {
        var w = new OpenMagicka.Tests.Content.XnbTestWriter().TypeId("Microsoft.Xna.Framework.Content." + reader + ", Microsoft.Xna.Framework");
        return w.BuildBytes();
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void TheCatalog_ListsEveryScenesModel_SkippingScenesThatDoNotLoad()
    {
        var catalog = SceneCatalog.Load(new ContentFileSystem(root));

        Assert.Equal([("Swamp", "sw_s1", "Levels/Swamp/sw_s1"), ("Swamp", "sw_s2", "Levels/Swamp/SW_S2")],
            catalog.Scenes.Select(s => (s.Level, s.Scene, s.Model)));
        Assert.Equal(2, catalog.Problems.Count);              // the broken scene and the broken .xnb
        Assert.Equal(1, catalog.Find("SW_S2"));
        Assert.Equal(-1, catalog.Find("nope"));
        Assert.Equal(0, catalog.Find(null));
    }

    [Fact]
    public void TheCatalog_ListsModelsAfterTheScenes_OnlyFilesThatAreModels()
    {
        var catalog = SceneCatalog.Load(new ContentFileSystem(root));

        Assert.Equal([("Models/Characters", "Crate", "Models/Characters/Crate"), ("Models/Characters", "wizard", "Models/Characters/wizard")],
            catalog.Models.Select(m => (m.Level, m.Scene, m.Model)));
        Assert.Equal(4, catalog.Items.Count);
        Assert.Equal(3, catalog.Find("wizard"));
        Assert.Equal(2, catalog.Find("models/characters/crate"));
        Assert.Equal([0, 2], catalog.SectionStarts);
    }

    [Fact]
    public void Tab_DoesNothingWithOneSection()
    {
        var host = new Host();
        var simulation = new ViewerSimulation(host, sceneCount: 5, startScene: 2, sectionStarts: [0]);
        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.Tab);
        simulation.Tick(0.1);

        Assert.Equal(2, simulation.Scene);
    }

    [Fact]
    public void Tab_JumpsBetweenScenesAndModels()
    {
        var host = new Host();
        var simulation = new ViewerSimulation(host, sceneCount: 5, startScene: 1, sectionStarts: [0, 3]);

        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.Tab);
        simulation.Tick(0.1);
        Assert.Equal(3, simulation.Scene);
        host.Input.Value = Keys();
        simulation.Tick(0.1);
        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.Tab);
        simulation.Tick(0.1);
        Assert.Equal(0, simulation.Scene);
    }

    internal sealed class Host : IGameHost
    {
        public LatestValue<InputState> Input { get; } = new(default);

        public bool ExitRequested { get; private set; }

        public void RequestExit() => ExitRequested = true;
    }

    internal static InputState Keys(params Keys[] keys) => new(new KeyboardState(keys), default, IsActive: true);

    [Fact]
    public void Keys_MoveTheCamera_AndPageKeysSwitchScenesOncePerPress()
    {
        var host = new Host();
        var simulation = new ViewerSimulation(host, sceneCount: 3, startScene: 0);
        var start = simulation.Camera.Position;

        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.W, Microsoft.Xna.Framework.Input.Keys.PageDown);
        simulation.Tick(0.1);
        simulation.Tick(0.1);                         // still held: no second switch
        host.Input.Value = Keys();
        simulation.Tick(0.1);
        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.PageUp);
        simulation.Tick(0.1);
        host.Input.Value = Keys();
        simulation.Tick(0.1);
        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.PageUp);
        simulation.Tick(0.1);

        Assert.True(Vector3.Dot(simulation.Camera.Position - start, -Vector3.UnitZ) > 0, "moved forward");
        Assert.Equal(2, simulation.Scene);            // 0 -> 1 -> 0 -> 2 (wraps around)
    }

    [Fact]
    public void NewLevelBounds_FrameTheCamera_AndEscapeExits()
    {
        var host = new Host();
        var simulation = new ViewerSimulation(host, sceneCount: 1, startScene: 0);
        var box = new BoundingBox(new Vector3(100, 0, 100), new Vector3(200, 20, 200));

        simulation.Loaded.Value = new LoadedScene(0, box);
        simulation.Tick(0.016);
        Assert.Equal(FreeCamera.Framing(box), simulation.Camera);

        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.Escape);
        simulation.Tick(0.016);
        Assert.True(host.ExitRequested);
    }

    [Fact]
    public void WithoutScenes_TheSceneStaysZero()
    {
        var host = new Host();
        var simulation = new ViewerSimulation(host, sceneCount: 0, startScene: 5);
        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.PageDown);
        simulation.Tick(0.1);

        Assert.Equal(0, simulation.Scene);
    }

    [Fact]
    public void FReframes_ButBoundsOfAnotherSceneAreIgnored()
    {
        var host = new Host();
        var simulation = new ViewerSimulation(host, sceneCount: 2, startScene: 0);
        var box = new BoundingBox(Vector3.Zero, new Vector3(10));
        simulation.Loaded.Value = new LoadedScene(1, box);
        simulation.Tick(0.1);
        Assert.NotEqual(FreeCamera.Framing(box), simulation.Camera);

        simulation.Loaded.Value = new LoadedScene(0, box);
        simulation.Tick(0.1);
        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.W);
        simulation.Tick(0.1);
        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.F);
        simulation.Tick(0.1);

        Assert.Equal(FreeCamera.Framing(box), simulation.Camera);
    }

    [Fact]
    public void TheRightMouseButton_LooksAround_OnlyInAnActiveWindow()
    {
        var host = new Host();
        var simulation = new ViewerSimulation(host, sceneCount: 1, startScene: 0);
        var yaw = simulation.Camera.Yaw;
        MouseState Mouse(int x) => new(x, 0, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);

        host.Input.Value = new InputState(default, Mouse(100), IsActive: false);
        simulation.Tick(0.1);
        host.Input.Value = new InputState(default, Mouse(200), IsActive: false);
        simulation.Tick(0.1);
        Assert.Equal(yaw, simulation.Camera.Yaw);

        host.Input.Value = new InputState(default, Mouse(300), IsActive: true);
        simulation.Tick(0.1);
        host.Input.Value = new InputState(default, Mouse(400), IsActive: true);
        simulation.Tick(0.1);
        Assert.Equal(yaw - 100 * ViewerSimulation.MouseRadiansPerPixel, simulation.Camera.Yaw, 5);
    }

    [Fact]
    public void Frames_CarryBothCamerasForInterpolation()
    {
        var host = new Host();
        var simulation = new ViewerSimulation(host, sceneCount: 1, startScene: 0);
        host.Input.Value = Keys(Microsoft.Xna.Framework.Input.Keys.D);
        simulation.Tick(0.1);
        var frame = new ViewerFrame();

        simulation.WriteFrame(frame, new FrameTiming(1, TimeSpan.Zero, 0.1));

        Assert.NotEqual(frame.Previous.Position, frame.Current.Position);
        Assert.Equal(simulation.Camera, frame.Current);
        Assert.Equal(0, frame.Scene);
    }
}
