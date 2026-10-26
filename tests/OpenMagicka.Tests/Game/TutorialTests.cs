using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Hud;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Tests.Content;

namespace OpenMagicka.Tests.Game;

/// <summary>The tutorial's hints (OM-53): the dialog hint that gives the input back when closed, the hints' fades.</summary>
public class TutorialTests
{
    private const float Tick = 1 / 60f;

    private static ControlFrame Press(params Keys[] pressed) => new(new KeyboardState(pressed), default, default, default);

    private static ControlFrame Click(bool right = false)
    {
        var down = new MouseState(0, 0, 0, right ? ButtonState.Released : ButtonState.Pressed, ButtonState.Released,
            right ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released, ButtonState.Released);
        return new ControlFrame(default, down, default, default);
    }

    [Fact]
    public void ADialogHint_HoldsOffASecond_ThenClosesOnce_GivingItsTrigger()
    {
        var tutorial = new ScriptTutorial();
        tutorial.Show(new TutorialDialogHint(Elements.Life, MagickType.None, "#UI_Text_element_life", 42, null, null));
        Assert.True(tutorial.IsDark);

        tutorial.Step(0.5f);
        Assert.Null(tutorial.Close());                                        // the input is dropped
        tutorial.Step(0.5f);
        Assert.Equal(42, tutorial.Close());
        Assert.Null(tutorial.DialogHint);
        Assert.False(tutorial.IsDark);
        Assert.Null(tutorial.Close());
    }

    [Fact]
    public void ClicksKeysAndFaceButtons_Close_ButNotEscape_NorKeysInACutscene()
    {
        Assert.True(LevelPlay.Closes(Click(), null, inCutscene: true));
        Assert.True(LevelPlay.Closes(Click(right: true), null, inCutscene: false));
        Assert.True(LevelPlay.Closes(Press(Keys.W), null, inCutscene: false));
        Assert.False(LevelPlay.Closes(Press(Keys.W), null, inCutscene: true));
        Assert.False(LevelPlay.Closes(Press(Keys.Escape), null, inCutscene: false));
        Assert.False(LevelPlay.Closes(new ControlFrame(new KeyboardState(Keys.W), default, new KeyboardState(Keys.W), default), null, inCutscene: false));   // held, not pressed
        var a = new GamePadState(default, default, new GamePadButtons(Buttons.A), default);
        Assert.True(LevelPlay.Closes(null, new PadFrame(a, default, new PadBindings()), inCutscene: true));
    }

    [Fact]
    public void AHint_FadesIn_AnotherWaitsForItToFadeOut()
    {
        var tutorial = new ScriptTutorial { Hint = new TutorialHint("#a", false, HintAnimations.None, HintPosition.BottomRight) };
        var hints = new TutorialHints();
        hints.Step(0.1f, tutorial);
        Assert.Equal(0.4f, hints.HintAlpha, 3);
        hints.Step(0.2f, tutorial);
        Assert.Equal(1, hints.HintAlpha);

        tutorial.Hint = new TutorialHint("#b", true, HintAnimations.None, HintPosition.Top);
        hints.Step(0.1f, tutorial);
        Assert.Equal(("#a", 0.6f), (hints.Shown!.Key, hints.HintAlpha));     // fading out
        hints.Step(0.2f, tutorial);
        Assert.Equal(("#a", 0f), (hints.Shown!.Key, hints.HintAlpha));
        hints.Step(0, tutorial);
        Assert.Equal("#b", hints.Shown!.Key);
        Assert.Equal("#b_pad", TutorialHints.HintKey(hints.Shown, pad: true));
        hints.Step(0.25f, tutorial);
        Assert.Equal(1, hints.HintAlpha);
    }

    [Fact]
    public void ADialogHintsPicture_HasItsElementsNameOverItsText()
    {
        var tutorial = new ScriptTutorial();
        tutorial.Show(new TutorialDialogHint(Elements.Fire, MagickType.None, "#UI_Text_element_fire", 0, null, null));
        var hints = new TutorialHints();
        hints.Step(Tick, tutorial);

        var picture = hints.Picture(key => key.ToUpperInvariant(), pad: false)!.Value;

        Assert.Equal("#ELEMENT_FIRE\n\n#UI_TEXT_ELEMENT_FIRE", picture.Dialog);
        Assert.Equal(Elements.Fire, picture.Element);
        Assert.True(picture.Dark > 0);
        Assert.Equal(new Rectangle(150, 156, 50, 50), GuiPass.IconCell(Elements.Fire, GuiPass.HintIconSize));
    }

    [Fact]
    public void Hints_StandWhereTheOriginalsDo()
    {
        Assert.Equal(new Vector2(1216 - 50, 684 - 20), GuiPass.HintCentre(HintPosition.BottomRight, new Point(100, 40), 1280, 720));
        Assert.Equal(new Vector2(640, 72), GuiPass.HintCentre(HintPosition.Top, new Point(100, 40), 1280, 720));
        Assert.Equal(new Point(250, 50), GuiPass.DialogHintSize(new Point(200, 30), new Point(50, 50), 1, null));
        Assert.Equal(new Point(700, 250), GuiPass.DialogHintSize(new Point(300, 120), new Point(400, 250), 1, null));
        Assert.Equal((false, new Rectangle(1600, 1250, 400, 250)), GuiPass.MagickCell((MagickType)29));
        Assert.Equal((true, new Rectangle(0, 0, 400, 250)), GuiPass.MagickCell((MagickType)37));
        Assert.Null(GuiPass.MagickCell(MagickType.None));
    }

    /// <summary>The castle's level in its cellar (the tutorial), its first ticks run.</summary>
    private static LevelPlay Cellar()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var loader = new ContentLoader(files, XnbReaderRegistry.Magicka);
        var path = PlayGame.FindLevel(files, "Wizard_Castle")!;
        var play = LevelPlay.Open(LevelReader.Read(new MemoryStream(files.ReadAllBytes(path)), path), files, loader);
        play.Start();
        play.Level.ChangeScene(new SpawnPoint(LegacyStringHash.Lower("wc_s3"), [LegacyStringHash.Compute("start")], true),
            ScriptTransition.None, 0, keep: false);
        return play;
    }

    private static readonly Point Screen = new(1280, 720);

    private static void Wait(LevelPlay play, float seconds)
    {
        for (var tick = 0; tick < seconds / Tick; tick++)
        {
            play.Tick(Tick, Press(), Screen);
        }
    }

    /// <summary>The bug of the beta: in the castle's cellar the input never came back, as the dialog hint was never closed.</summary>
    [GameContentFact]
    public void InTheCastlesCellar_ClosingTheFirstDialogHint_GivesTheInputBack()
    {
        using var play = Cellar();
        var tutorial = play.Level.Game.Tutorial;
        for (var tick = 0; tick < 60 * 120 && tutorial.DialogHint is null; tick++)
        {
            play.Tick(Tick, tick % 30 == 0 ? Press(Keys.Space) : Press(), Screen);   // the cellar's talk, advanced
        }
        Assert.Equal("wc_s3", play.Scene!.Definition.Name);
        Assert.NotNull(tutorial.DialogHint);
        Assert.True(play.Level.Level.Stage.IsInputLimited);

        Wait(play, 1.5f);
        play.Tick(Tick, Click(), Screen);
        Wait(play, 0.5f);

        Assert.Null(tutorial.DialogHint);
        Assert.False(play.Level.Level.Stage.IsInputLimited);
    }

    /// <summary>
    /// A room's checkpoint is taken before its dialog hint shows (its actions still waiting): the restart runs them
    /// again, and the hint must survive the restart's scene change, or the input never comes back.
    /// </summary>
    [GameContentFact]
    public void ARestartAtARoomsCheckpoint_ShowsItsDialogHintAgain_WhichGivesTheInputBack()
    {
        using var play = Cellar();
        var tutorial = play.Level.Game.Tutorial;
        Wait(play, 0.1f);
        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("Room1"), null, ignoreConditions: true);
        Wait(play, 2);
        play.Tick(Tick, Click(), Screen);
        Wait(play, 1);
        Assert.Null(tutorial.DialogHint);

        Assert.True(play.Level.RestartAtCheckpoint());
        Wait(play, 2);
        Assert.NotNull(tutorial.DialogHint);
        Assert.True(play.Level.Level.Stage.IsInputLimited);
        play.Tick(Tick, Click(), Screen);
        Wait(play, 1);
        Assert.False(play.Level.Level.Stage.IsInputLimited);
    }
}
