using Microsoft.Xna.Framework.Input;
using OpenMagicka.Content.Levels;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game.Play;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>A cutscene's bars and skip, and the interact prompt (OM-49).</summary>
public class CutscenePresentationTests
{
    private static ControlFrame Keyboard(Keys[] now, Keys[]? before = null) => new(new KeyboardState(now), default, new KeyboardState(before ?? []), default);

    private static PadFrame Pad(params Buttons[] now) => new(new GamePadState(default, default, 0, 0, now), new GamePadState(), new PadBindings());

    [Fact]
    public void ASkip_IsAFreshKeyThatSharesNoBitWithEscape_OrAPadsActionButton()
    {
        Assert.True(LevelPlay.SkipsCutscene(Keyboard([Keys.Space]), null));
        Assert.True(LevelPlay.SkipsCutscene(Keyboard([Keys.D]), null));
        Assert.False(LevelPlay.SkipsCutscene(Keyboard([Keys.Escape]), null));
        Assert.False(LevelPlay.SkipsCutscene(Keyboard([Keys.Enter]), null));                           // 13 shares a bit with Escape's 27
        Assert.False(LevelPlay.SkipsCutscene(Keyboard([Keys.Space], [Keys.Space]), null));             // held, not pressed
        Assert.False(LevelPlay.SkipsCutscene(new ControlFrame(default,
            new MouseState(0, 0, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), default, default), null));

        Assert.True(LevelPlay.SkipsCutscene(null, Pad(Buttons.A)));                                    // attack
        Assert.True(LevelPlay.SkipsCutscene(null, Pad(Buttons.X)));                                    // interact
        Assert.False(LevelPlay.SkipsCutscene(null, Pad(Buttons.Start)));
    }

    [Fact]
    public void TheBars_SlideInAndOutInASecond_AndTheSkipTipFadesWithThem()
    {
        var (bars, game, stage) = (new CutsceneBars(), new ScriptGame(), new ScriptStage());
        game.InCutscene = true;
        stage.CutsceneSkipTrigger = 1;
        stage.ShowsSkipTip = true;

        bars.Update(0.5f, game, stage);
        Assert.Equal((0.5f, 1f), (bars.Amount, bars.Prompt), new ToleranceComparer());
        Assert.True(bars.IsInCutscene(game));
        bars.Update(0.6f, game, stage);
        Assert.Equal(1, bars.Amount);

        game.InCutscene = false;
        Assert.True(bars.IsInCutscene(game));                                                          // its bars are still all in
        bars.Update(0.25f, game, stage);
        Assert.False(bars.IsInCutscene(game));
        bars.Update(1, game, stage);
        bars.Update(0.1f, game, stage);
        Assert.Equal((0f, 0f, false), (bars.Amount, bars.Prompt, stage.ShowsSkipTip));
    }

    [Fact]
    public void ThePrompt_SaysItsInteraction_AndItsStripStretchesInTheMiddle()
    {
        Assert.Equal("#interact_08", LevelPlay.InteractKey(InteractType.Talk));
        Assert.Equal("#interact_07", LevelPlay.InteractKey(InteractType.Examine));

        var strip = GuiPass.Strip();
        Assert.Equal(8, strip.Length);
        Assert.Equal([0f, 0f, 32f, 32f, 48f, 48f, 64f, 64f], strip.Select(v => v.Position.X));
        Assert.Equal([0f, 32f, 0f, 32f, 0f, 32f, 0f, 32f], strip.Select(v => v.Position.Y));
        Assert.Equal([0f, 0f, 0.5f, 0.5f, 0.75f, 0.75f, 1f, 1f], strip.Select(v => v.TextureCoordinate.X));
        Assert.Equal(new Xna.Vector2(58, 120), GuiPass.PromptCorner(new Xna.Vector2(100, 100)));
        Assert.Equal((40f, 6f), (GuiPass.PromptWidth(40, pad: true), GuiPass.PromptWidth(40, pad: false)));
    }

    [Fact]
    public void TheBreakFreePrompt_SaysItsWord_ItsGlyphDimmedWhileBoostsWait_ComingBackInAboutASecond()
    {
        Assert.Equal("#interact_12", LevelPlay.InteractKey(InteractType.BreakFree));
        Assert.Equal((char)0x32A, GuiPass.SpaceBarGlyph);
        Assert.Equal(0.554f, LevelPlay.ButtonGlow(LevelPlay.PromptButtonDimmed, 1f / 60), 3);       // dimmed each frame, it shows a bit over half
        Assert.Equal(0.9995f, LevelPlay.ButtonGlow(LevelPlay.PromptButtonDimmed, 1), 4);
        Assert.Equal(1, new PromptPicture("x", null, null, 1).ButtonIntensity);
        Assert.Null(new PromptPicture("x", null, null, 1).Button);                                  // the keyboard's strip, without a glyph
    }

    private sealed class ToleranceComparer : IEqualityComparer<(float, float)>
    {
        public bool Equals((float, float) a, (float, float) b) => MathF.Abs(a.Item1 - b.Item1) < 1e-4f && MathF.Abs(a.Item2 - b.Item2) < 1e-4f;

        public int GetHashCode((float, float) obj) => 0;
    }
}
