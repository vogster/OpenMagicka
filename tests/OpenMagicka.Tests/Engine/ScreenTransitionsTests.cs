using System.Numerics;
using OpenMagicka.Engine.Screen;

namespace OpenMagicka.Tests.Engine;

/// <summary>The screen's one transition (OM-67), by the original's rules.</summary>
public class ScreenTransitionsTests
{
    private static readonly Vector3 Black = Vector3.Zero, White = Vector3.One;

    [Fact]
    public void ABegin_CoversTheScreen_EndsOnce_AndStaysCovering()
    {
        var screen = new ScreenTransitions();
        screen.Begin(TransitionKind.Fade, Black, 1);

        Assert.Null(screen.Step(0.5f));
        Assert.Equal(0.5f, screen.Picture.Amount, 4);
        Assert.Equal(TransitionDirection.Out, screen.Step(0.5f));               // ended, in its direction
        Assert.Null(screen.Step(0.5f));                                        // and not again
        Assert.True(screen.IsActive);                                           // the screen stays covered
        Assert.Equal((TransitionKind.Fade, 1f), (screen.Picture.Kind, screen.Picture.Amount));
    }

    [Fact]
    public void AnEnd_UncoversFromTheColour_ThenGoes()
    {
        var screen = new ScreenTransitions();
        screen.End(TransitionKind.CrossFade, White, 0.25f);

        Assert.Equal(1, screen.Picture.Amount);                                 // from fully covered, even on a clear screen: a flash
        Assert.Null(screen.Step(0.125f));
        Assert.Equal(TransitionDirection.In, screen.Step(0.125f));
        Assert.False(screen.IsActive);
        Assert.Equal(TransitionKind.None, screen.Picture.Kind);
    }

    [Fact]
    public void AStart_AlwaysResetsTheAmount_AndReplacesTheOneRunning()
    {
        var screen = new ScreenTransitions();
        screen.Begin(TransitionKind.Fade, Black, 1);
        screen.Step(2);
        screen.Begin(TransitionKind.Fade, White, 1);

        Assert.Equal((0f, White), (screen.Picture.Amount, screen.Picture.Colour));   // a begin on a covered screen jumps clear
        screen.Begin(TransitionKind.None, Black, 1);
        Assert.False(screen.IsActive);                                          // none drops it, with no end
    }

    [Fact]
    public void ATimeOfNothing_EndsOnTheFirstStep_AndTheColourIsKeptAsBytes()
    {
        var screen = new ScreenTransitions();
        screen.Begin(TransitionKind.Fade, new Vector3(2, 0.5f, -1), 0);

        Assert.Equal(new Vector3(1, 128 / 255f, 0), screen.Picture.Colour);
        Assert.Equal(TransitionDirection.Out, screen.Step(0));
        Assert.Equal(1, screen.Picture.Amount);
    }

    [Fact]
    public void AStepThatIsNotANumber_StepsNothing()
    {
        var screen = new ScreenTransitions();
        screen.Begin(TransitionKind.Fade, Black, 1);
        screen.Step(float.NaN);

        Assert.Equal(0, screen.Picture.Amount);
    }

    [Fact]
    public void Clear_DropsIt_WithNoEnd()
    {
        var screen = new ScreenTransitions();
        screen.End(TransitionKind.Fade, Black, 1);
        screen.Clear();

        Assert.False(screen.IsActive);
        Assert.Null(screen.Step(1));
    }
}
