using System.Text;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Particles;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Animation.Actions;
using OpenMagicka.Gameplay.Combat;
using AttackProperties = OpenMagicka.Gameplay.Combat.AttackProperties;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Props;
using OpenMagicka.Tests.Content;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The level's effects passed to the renderer (OM-75): the scene's named ones, the gameplay's cues, the debris and the shake.</summary>
public class EffectDirectorTests
{
    private sealed class Content(string scene) : ILevelContent
    {
        public SceneDefinition Scene(string path) => SceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(scene)), path);

        public LevelModelContent? Model(string asset) => new(null, [], [], [new VisualEffectPlacement("Torch", new Xna.Vector3(3, 1, 0), Xna.Vector3.Forward, 0, "fx_torch")],
            [], [], [], [new CollisionMeshContent([new(-50, 0, -50), new(50, 0, -50), new(50, 0, 50), new(-50, 0, 50)], [new CollisionTriangle(0, 2, 1), new CollisionTriangle(0, 3, 2)])],
            null, [], [new LocatorContent("start0", Xna.Matrix.CreateTranslation(2, 0, 0), 1), new LocatorContent("spot", Xna.Matrix.CreateTranslation(-5, 0, 0), 1)],
            new NavMeshContent([], []));
    }

    private static LevelPlay Play(string scripts = "") =>
        new(new LevelDefinition("#C", "#D", "#D", "loading", new LevelStart("t_s1", "start", true, false, null, true), null, ["Levels/T/t_s1.xml"],
            LevelDefinition.DefaultAvatar, [], []), new Content($"<Scene><Model>m</Model>{scripts}</Scene>"), new LevelServices(), (Goblin, null),
            gibShapes: model => model == "models/gib" ? (Numerics.Vector3.Zero, 0.3f) : null);

    private static List<EffectCommand> Drain(LevelPlay play)
    {
        var taken = new List<EffectCommand>();
        while (play.Channel.Commands.TryDequeue(out var command))
        {
            taken.Add(command);
        }
        return taken;
    }

    [Fact]
    public void TheScenesEffects_Start_Follow_Stop_AndThoseThatPlayedOut_AreToldToTheScene()
    {
        using var play = Play("""
            <Trigger id="off" autorun="false"><Then><stopEffect name="torch"/></Then></Trigger>
            <Trigger id="spark" autorun="false"><Then><spawnEffect effect="fx_spark" name="Spark" area="spot"/></Then></Trigger>
            """);
        play.Start();
        var started = Drain(play);
        var torch = Assert.Single(started, c => c.Kind == EffectCommandKind.Start);
        Assert.Equal("fx_torch", torch.Effect);
        Assert.Equal(new Numerics.Vector3(3, 1, 0), torch.World.Translation);

        play.Tick(Tick);
        Assert.Contains(Drain(play), c => c is { Kind: EffectCommandKind.Move } && c.Id == torch.Id);
        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("off"), null);
        play.Tick(Tick);
        Assert.Contains(Drain(play), c => c is { Kind: EffectCommandKind.Stop } && c.Id == torch.Id);

        play.Scene.Scripts.ExecuteTrigger(ScriptScene.Hash("spark"), null);
        play.Tick(Tick);
        var spark = Assert.Single(Drain(play), c => c.Kind == EffectCommandKind.Start);
        play.Channel.Ended.Enqueue(spark.Id);                                                       // it played out
        play.Tick(Tick);
        Assert.False(play.Scene.Scripts.Effects.Find(ScriptScene.Hash("spark"))!.IsActive);
    }

    [Fact]
    public void ABrokenProp_ShattersIntoDebris_ThatTrailItsEffect_AndLiveTenToTwentySeconds()
    {
        using var play = Play();
        play.Start();
        var crate = new PropTemplate("crate", false, false, true, 100, 50, ResistanceTable.Neutral,
            new PropBox(new Numerics.Vector3(-0.5f, 0, -0.5f), Numerics.Vector3.One, Numerics.Quaternion.Identity),
            [new PropGib("models/gib", 2, 1), new PropGib("models/gib", 3, 0.5f), new PropGib("models/none", 1, 1)], "fx_smoke", [], []);
        var prop = new PhysicsProp(play.World.Physics, crate, Numerics.Matrix4x4.CreateTranslation(0, 0, 5));
        play.World.Entities.Add(prop);
        Drain(play);

        prop.Damage(new DamageEntry(AttackProperties.Damage, OpenMagicka.Gameplay.Spells.Elements.Earth, 500, 1), null, prop.Position);
        play.Tick(Tick);

        var gibs = play.World.Entities.Entities.OfType<Gib>().ToArray();
        Assert.Equal(2, gibs.Length);                                                            // the one with no model is left out
        Assert.All(gibs, g => Assert.InRange(g.Life, 9.9f, 20));
        Assert.All(gibs, g => Assert.True(g.Body.Velocity.Y >= 0));
        Assert.Equal(2, Drain(play).Count(c => c is { Kind: EffectCommandKind.Start, Effect: "fx_smoke" }));
        Assert.Equal(2, play.Effects.Running - 1);                                               // the torch and the two trails
    }

    private static void Raise(LevelPlay play, Entity source, AnimationCue cue) =>
        typeof(EntityManager).GetMethod("RaiseCue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(play.World.Entities, [source, cue]);

    [Fact]
    public void ACameraShakeCue_ShakesTheCamera()
    {
        using var play = Play();
        play.Start();

        Raise(play, play.Player.Avatar!, new CameraShakeCue(Numerics.Vector3.Zero, 3, 0.5f));
        play.Tick(Tick);

        Assert.Equal(3, play.Camera.ShakeMagnitude);
    }

    [Fact]
    public void AnAttachedEffect_FollowsItsCharacter_UntilItsEndCue_AndAOneThatPlayedOutIsForgotten()
    {
        using var play = Play();
        play.Start();
        var avatar = play.Player.Avatar!;
        var (aura, flash) = (new PlayEffect(0, 1, "fx_aura", "root", attach: true), new PlayEffect(0, 1, "fx_flash", "root", attach: true));
        var before = play.Effects.Running;

        Raise(play, avatar, new EffectCue("fx_aura", "root", Attached: true, aura));
        Raise(play, avatar, new EffectCue("fx_flash", "root", Attached: true, flash));
        play.Tick(Tick);
        var started = Drain(play).Where(c => c.Kind == EffectCommandKind.Start).ToDictionary(c => c.Effect!, c => c.Id);
        Assert.Equal(before + 2, play.Effects.Running);
        avatar.Steer(new Numerics.Vector3(1, 0, 0), walk: true);
        play.Tick(Tick);
        Assert.Contains(Drain(play), c => c.Kind == EffectCommandKind.Move && c.Id == started["fx_aura"]);

        Raise(play, avatar, new EffectEndCue(aura));
        play.Channel.Ended.Enqueue(started["fx_flash"]);                                           // the flash played out
        play.Tick(Tick);

        var last = Drain(play);
        Assert.Contains(last, c => c.Kind == EffectCommandKind.Stop && c.Id == started["fx_aura"]);
        Assert.DoesNotContain(last, c => c.Id == started["fx_flash"]);
        Assert.Equal(before, play.Effects.Running);
    }

    [Fact]
    public void ARestart_UncoversTheScreen()
    {
        using var play = Play("""
            <Trigger id="save"><Then><triggerCheckpoint spawnPoint="start"/></Then></Trigger>
            <Trigger id="dark" autorun="false"><Then><fade fadeIn="true" time="0.1"/></Then></Trigger>
            """);
        play.Start();
        play.Tick(Tick);
        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("dark"), null);
        for (var i = 0; i < 20; i++)
        {
            play.Tick(Tick);
        }
        Assert.Equal(1, play.Cover.Amount, 3);

        Assert.True(play.Level.RestartAtCheckpoint());
        play.Tick(Tick);

        Assert.Equal(0, play.Cover.Amount, 3);
    }

    [Fact]
    public void TheStage_PlaysTheCommands_AndTellsWhatPlayedOut()
    {
        var root = Path.Combine(Path.GetTempPath(), "omfx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Effects"));
        try
        {
            File.WriteAllText(Path.Combine(root, "Effects", "puff.xml"),
                Particles.VisualEffectReaderTests.Effect("", "type=\"Single\" duration=\"0.1\" keyFramesPerSecond=\"10\" version=\"2\""));
            File.WriteAllText(Path.Combine(root, "Effects", "glow.xml"), Particles.VisualEffectReaderTests.Effect(""));
            var channel = new EffectChannel();
            var stage = new EffectStage(Task.FromResult(EffectLibrary.Load(new ContentFileSystem(root))), channel);
            channel.Commands.Enqueue(new EffectCommand(EffectCommandKind.Start, 1, "puff", Numerics.Matrix4x4.Identity));
            channel.Commands.Enqueue(new EffectCommand(EffectCommandKind.Start, 2, "glow", Numerics.Matrix4x4.Identity));
            channel.Commands.Enqueue(new EffectCommand(EffectCommandKind.Start, 3, "nothing", Numerics.Matrix4x4.Identity));

            stage.Step(0.05f);
            Assert.Equal(2, stage.Running);
            stage.Step(0.1f);
            channel.Commands.Enqueue(new EffectCommand(EffectCommandKind.Stop, 2, null, default));
            stage.Step(0.05f);

            Assert.Equal(0, stage.Running);
            Assert.Equal([3L, 1L], channel.Ended.ToArray());                                     // the missing at once, the puff once it played out
            Assert.Contains("nothing", stage.Missing);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [GameContentFact]
    public void AGameLevel_PassesItsEffects_AndTheStagePlaysThem()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var loader = new ContentLoader(files, XnbReaderRegistry.Magicka);
        var path = PlayGame.FindLevel(files, "battlefield")!;
        using var play = LevelPlay.Open(LevelReader.Read(new MemoryStream(files.ReadAllBytes(path)), path), files, loader);
        var stage = new EffectStage(Task.FromResult(EffectLibrary.Load(files)), play.Channel);

        play.Start();
        for (var i = 0; i < 60; i++)
        {
            play.Tick(Tick);
            stage.Step(Tick);
        }

        Assert.True(stage.Running > 0);
        Assert.True(stage.Pool.LastActive >= 0);                                                 // particles are flying
    }
}
