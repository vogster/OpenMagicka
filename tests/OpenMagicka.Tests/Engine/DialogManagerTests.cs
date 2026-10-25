using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Dialogs;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;

namespace OpenMagicka.Tests.Engine;

/// <summary>The dialogs at play (OM-49): taking interacts, advancing by presses and time, ending, locking, checkpoints.</summary>
public class DialogManagerTests
{
    private static int Key(string id) => LegacyStringHash.Lower(id);

    private static DialogLine Line(string key, float ttl = -1, string? sound = null) => new(key, [], default, sound, Animation.None, ttl);

    private static DialogMessage Message(string? speaker, params DialogLine[] lines) => new(speaker, default, true, true, 600, lines);

    private static DialogInteract Interact(bool locks, int count, params DialogMessage[] messages) => new(locks, false, count, InteractType.Talk, messages);

    /// <summary>A world, its stage, a player with a wizard, an old man named "hermit", and the manager over the dialogs.</summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture(params DialogDefinition[] dialogs)
        {
            World = new GameWorld(session: new GameSession());
            World.Physics.AddStaticMesh(2, [new(-50, 0, -50), new(50, 0, -50), new(50, 0, 50), new(-50, 0, 50)],
                [new OpenMagicka.Physics.MeshTriangle(0, 2, 1), new OpenMagicka.Physics.MeshTriangle(0, 3, 2)]);
            Player = World.Session!.AddPlayer();
            Player.Avatar = Spawn(World, at: new Numerics.Vector3(0, 0.9f, 0));
            Player.Avatar.IsPlayer = true;
            Hermit = Spawn(World, at: new Numerics.Vector3(2, 0.9f, 0));
            Hermit.UniqueId = Key("hermit");
            var strings = StringTable.Create([new LocEntry("#hi", "Hello there"), new LocEntry("#bye", "Farewell"), new LocEntry("hermit", "Old Hermit")]);
            Dialogs = new DialogManager(DialogCollection.Of(dialogs), strings, BitmapTextTests.Font(), World, Stage, () => InCutscene);
        }

        public GameWorld World { get; }
        public ScriptStage Stage { get; } = new();
        public Player Player { get; }
        public Character Hermit { get; }
        public DialogManager Dialogs { get; }
        public bool InCutscene { get; set; }

        public void Step(float seconds)
        {
            for (var t = 0f; t < seconds - 1e-4f; t += Tick)
            {
                Dialogs.Update(Tick);
            }
        }

        /// <summary>Presses until the box is typed, then once more.</summary>
        public void Press()
        {
            Step(DialogManager.Holdoff + 0.01f);
            Dialogs.Advance(Player);
            Step(DialogManager.Holdoff + 0.01f);
            if (Dialogs.Boxes.Any(b => b.IsAnimating))
            {
                Dialogs.Advance(Player);
            }
        }

        public void Dispose() => World.Dispose();
    }

    [Fact]
    public void Popping_ShowsAnInteractItsCount_ThenGoesOn_AndWrapsToRepeatFrom()
    {
        var dialog = new DialogDefinition("talk", Key("talk"), InteractType.Talk, 1,
            [Interact(false, 2, Message("owner", Line("#hi"))), Interact(false, 1, Message("owner", Line("#bye")))]);
        var run = new DialogRun(dialog);

        Assert.Same(run.Interacts[0], run.Pop());
        Assert.Equal((0, 0), (run.Next, run.Interacted));                                              // shown twice: still next
        run.Interacts[0].RepeatCounter = 1;
        Assert.Same(run.Interacts[0], run.Pop());
        Assert.Equal((1, 1), (run.Next, run.Interacted));
        Assert.Same(run.Interacts[1], run.Pop());
        Assert.Equal((1, 2), (run.Next, run.Interacted));                                              // past the end: back to repeatFrom
        Assert.Equal(0, run.Interacts[0].RepeatCounter);                                               // every interact afresh

        var once = new DialogRun(dialog with { RepeatFrom = -1 });
        once.Pop();
        once.Interacts[0].RepeatCounter = 1;
        once.Pop();
        once.Pop();
        Assert.Null(once.Pop());                                                                        // a dialog that does not repeat ends
    }

    [Fact]
    public void ADialogDrivenByAPlayer_TypesItsLines_GoesOnByPresses_AndIsFinished()
    {
        using var fixture = new Fixture(new DialogDefinition("hermit_talk", Key("hermit_talk"), InteractType.Talk, 0,
            [Interact(false, 1, Message("owner", Line("#hi"), Line("#bye")), Message("hermit", Line("#bye")))]));
        var dialogs = fixture.Dialogs;

        dialogs.Start(Key("hermit_talk"), new DialogAnchor(fixture.Hermit), fixture.Player);
        Assert.True(dialogs.IsActive(Key("hermit_talk")));
        Assert.True(dialogs.Holds(fixture.Player));
        var box = Assert.Single(dialogs.Boxes, b => b.IsVisible);
        Assert.Equal("Hello there", box.Text.Text);
        Assert.Equal("Old Hermit", box.Name);                                                           // the speaker's id is a loc key
        Assert.Same(fixture.Hermit, box.Anchor.Owner);

        dialogs.Advance(fixture.Player);
        Assert.Equal("Hello there", box.Text.Text);                                                    // presses wait out the holdoff
        fixture.Step(0.3f);
        dialogs.Advance(fixture.Player);
        Assert.True(box.Text.IsFinished);                                                              // the first press finishes the typing
        fixture.Press();
        Assert.Equal("Farewell", box.Text.Text);
        fixture.Press();
        fixture.Press();
        Assert.False(dialogs.IsActive(Key("hermit_talk")));
        Assert.True(dialogs.IsDone(Key("hermit_talk"), 0));
        Assert.False(dialogs.IsDone(Key("hermit_talk"), 1));
        Assert.False(dialogs.Holds(fixture.Player));
    }

    [Fact]
    public void ALineWithATime_GoesOnByItself_AndALockingInteract_FreezesTheWorldTillItEnds()
    {
        using var fixture = new Fixture(new DialogDefinition("notice", Key("notice"), InteractType.Talk, -1,
            [Interact(true, 1, Message("owner", Line("#hi", ttl: 0.5f)))]));

        fixture.Dialogs.Start(Key("notice"), new DialogAnchor(fixture.Hermit));
        Assert.True(fixture.Dialogs.IsFrozen);
        Assert.True(fixture.Stage.IsInputLimited);
        fixture.Step(1.5f);                                                                             // opens, types, waits its half second

        Assert.False(fixture.Dialogs.IsActive(Key("notice")));
        Assert.True(fixture.Dialogs.IsDone(Key("notice"), -1));
        Assert.False(fixture.Dialogs.IsFrozen);
    }

    [Fact]
    public void ASpeakerGone_SkipsTheirMessage_AndADyingOwner_EndsItsDialogFinished()
    {
        using var fixture = new Fixture(
            new DialogDefinition("ghost", Key("ghost"), InteractType.Talk, -1, [Interact(false, 1, Message("nobody", Line("#hi")), Message("owner", Line("#bye")))]),
            new DialogDefinition("last", Key("last"), InteractType.Talk, -1, [Interact(false, 1, Message("owner", Line("#hi")))]));
        fixture.Hermit.Dialog = Key("last");

        fixture.Dialogs.Start(Key("ghost"), new DialogAnchor(fixture.Hermit), fixture.Player);
        Assert.Equal("Farewell", fixture.Dialogs.Boxes.Single(b => b.IsVisible).Text.Text);           // nobody's line skipped

        fixture.Dialogs.EndAll();
        fixture.Step(0.3f);
        fixture.Dialogs.Start(Key("last"), new DialogAnchor(fixture.Hermit));
        fixture.Hermit.Kill();
        fixture.Step(Tick);
        Assert.False(fixture.Dialogs.IsActive(Key("last")));
        Assert.True(fixture.Dialogs.IsDone(Key("last"), -1));
    }

    [Fact]
    public void AFocusingInteract_DrawsTheCameraToTheSpeaker_AndALinesSound_IsAskedForAndStopped()
    {
        var dialog = new DialogDefinition("show", Key("show"), InteractType.Talk, -1,
            [new DialogInteract(false, true, 1, InteractType.Talk, [Message("owner", Line("#hi", sound: "hermit_hello"))])]);
        using var fixture = new Fixture(dialog);

        fixture.Dialogs.Start(Key("show"), new DialogAnchor(fixture.Hermit), fixture.Player);

        Assert.Equal(CameraBehaviour.MoveToTarget, fixture.Stage.Camera.Behaviour);
        Assert.Equal(DialogManager.FocusMagnification, fixture.Stage.Camera.TargetMagnification);
        var sound = Assert.IsType<PlaySoundRequest>(Assert.Single(fixture.Stage.TakeRequests()));
        Assert.Equal("hermit_hello", sound.Cue);
        Assert.Equal(fixture.Hermit.Position, sound.At);
        fixture.Dialogs.EndAll();
        Assert.Equal(sound.Id, Assert.IsType<StopSoundRequest>(Assert.Single(fixture.Stage.TakeRequests())).Id);
    }

    [Fact]
    public void ACheckpoint_KeepsWhatWasSaidAndFinished_AndTheDialogsComeBackToIt()
    {
        var dialog = new DialogDefinition("story", Key("story"), InteractType.Talk, -1,
            [Interact(false, 1, Message("owner", Line("#hi", ttl: 0.1f))), Interact(false, 1, Message("owner", Line("#bye", ttl: 0.1f)))]);
        using var fixture = new Fixture(dialog);
        fixture.Dialogs.Start(Key("story"), new DialogAnchor(fixture.Hermit));
        fixture.Step(1);
        var saved = fixture.Dialogs.Save();

        fixture.Dialogs.Start(Key("story"), new DialogAnchor(fixture.Hermit));
        fixture.Step(1);
        Assert.Equal(2, fixture.Dialogs.Run(Key("story"))!.Next);
        fixture.Dialogs.Restore(saved);

        var run = fixture.Dialogs.Run(Key("story"))!;
        Assert.Equal((1, 1, true), (run.Next, run.Interacted, run.IsFinished));
        fixture.Dialogs.Start(Key("story"), new DialogAnchor(fixture.Hermit));
        Assert.Equal("Farewell", fixture.Dialogs.Boxes.Single(b => b.IsVisible).Text.Text);           // the second interact again
    }

    [Fact]
    public void InACutscene_ADialogPlaysInTheBottomBox_AndPressesDoNothing()
    {
        using var fixture = new Fixture(new DialogDefinition("scene", Key("scene"), InteractType.Talk, -1,
            [Interact(false, 1, Message("owner", Line("#hi", ttl: 0.5f)))]));
        fixture.InCutscene = true;

        fixture.Dialogs.Start(Key("scene"), new DialogAnchor(fixture.Hermit), fixture.Player);
        fixture.Step(0.3f);
        fixture.Dialogs.Advance(fixture.Player);

        Assert.True(fixture.Dialogs.ShowsCutsceneBox);
        Assert.Equal("Hello there", fixture.Dialogs.CutsceneBox.Text.Text);
        Assert.DoesNotContain(fixture.Dialogs.Boxes, b => b.IsVisible);
        fixture.Step(1.5f);
        Assert.False(fixture.Dialogs.ShowsCutsceneBox);                                                 // its time ran out
        Assert.True(fixture.Dialogs.IsDone(Key("scene"), -1));
    }

    [Fact]
    public void APlayerTalking_TurnsTheSpeakerToThem_AndDrivesItsDialog_ButNotADeadOne()
    {
        using var fixture = new Fixture(new DialogDefinition("hermit_talk", Key("hermit_talk"), InteractType.Talk, -1,
            [Interact(false, 1, Message("owner", Line("#hi")))]));
        fixture.Hermit.Dialog = Key("hermit_talk");

        fixture.Dialogs.Talk(fixture.Hermit, fixture.Player);

        Assert.True(fixture.Dialogs.IsActive(Key("hermit_talk")));
        Assert.True(fixture.Dialogs.CanAdvance(fixture.Player));
        var facing = fixture.Hermit.Body.WantedDirection;
        Assert.True(facing.X < -0.99f, $"faces {facing}");                                             // toward the wizard at the origin

        using var other = new Fixture(new DialogDefinition("hermit_talk", Key("hermit_talk"), InteractType.Talk, -1,
            [Interact(false, 1, Message("owner", Line("#hi")))]));
        other.Hermit.Dialog = Key("hermit_talk");
        other.Hermit.Kill();
        other.Dialogs.Talk(other.Hermit, other.Player);
        Assert.False(other.Dialogs.IsActive(Key("hermit_talk")));
    }

    [Fact]
    public void ALockingDialogsOwnerDying_LiftsTheLock()
    {
        using var fixture = new Fixture(new DialogDefinition("held", Key("held"), InteractType.Talk, -1,
            [Interact(true, 1, Message("owner", Line("#hi")))]));
        fixture.Dialogs.Start(Key("held"), new DialogAnchor(fixture.Hermit), fixture.Player);
        Assert.True(fixture.Dialogs.IsFrozen);
        Assert.True(fixture.Stage.IsInputLimited);

        fixture.Hermit.Kill();
        fixture.Step(0.1f);

        Assert.False(fixture.Dialogs.IsActive(Key("held")));
        Assert.False(fixture.Dialogs.IsFrozen);
        fixture.Stage.Update(1);                                                                        // past the stage's unlock delay
        Assert.False(fixture.Stage.IsInputLimited);
    }

    [Fact]
    public void ANinthDialog_PushesOutTheFirst()
    {
        var names = Enumerable.Range(0, DialogManager.Slots + 1).Select(i => $"talk{i}").ToArray();
        using var fixture = new Fixture([.. names.Select(n => new DialogDefinition(n, Key(n), InteractType.Talk, -1,
            [Interact(false, 1, Message("owner", Line("#hi")))]))]);

        foreach (var name in names)
        {
            fixture.Dialogs.Start(Key(name), new DialogAnchor(fixture.Hermit));
        }

        Assert.False(fixture.Dialogs.IsActive(Key(names[0])));
        Assert.All(names.Skip(1), n => Assert.True(fixture.Dialogs.IsActive(Key(n))));
    }

    [Fact]
    public void AMessageOfAMissingSpeaker_IsSkipped_ForTheNextOne()
    {
        using var fixture = new Fixture(new DialogDefinition("chat", Key("chat"), InteractType.Talk, -1,
            [Interact(false, 1, Message("owner", Line("#hi")), Message("nobody", Line("#hi")), Message("owner", Line("#bye")))]));
        fixture.Dialogs.Start(Key("chat"), new DialogAnchor(fixture.Hermit), fixture.Player);
        fixture.Step(DialogManager.Holdoff + 0.01f);
        fixture.Dialogs.Boxes.Single(b => b.IsVisible).Finish();

        fixture.Dialogs.Advance(fixture.Player);

        Assert.Equal("Farewell", fixture.Dialogs.Boxes.Single(b => b.IsVisible).Text.Text);
    }

    [Fact]
    public void ASecondDialogInACutscene_TakesItsBox_AndTheFirstPlaysThrough()
    {
        using var fixture = new Fixture(
            new DialogDefinition("first", Key("first"), InteractType.Talk, -1, [Interact(false, 1, Message("owner", Line("#hi", ttl: 5)))]),
            new DialogDefinition("second", Key("second"), InteractType.Talk, -1, [Interact(false, 1, Message("owner", Line("#bye", ttl: 5)))]));
        fixture.InCutscene = true;

        fixture.Dialogs.Start(Key("first"), new DialogAnchor(fixture.Hermit));
        fixture.Step(0.5f);
        fixture.Dialogs.Start(Key("second"), new DialogAnchor(fixture.Hermit));

        Assert.True(fixture.Dialogs.IsDone(Key("first"), -1));
        Assert.True(fixture.Dialogs.IsActive(Key("second")));
        Assert.Equal("Farewell", fixture.Dialogs.CutsceneBox.Text.Text);
    }
}
