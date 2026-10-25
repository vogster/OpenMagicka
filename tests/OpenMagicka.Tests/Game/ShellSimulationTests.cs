using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Threading;
using OpenMagicka.Game.Install;
using OpenMagicka.Game.Menu;
using OpenMagicka.Game.Shell;
using OpenMagicka.Tests.Content;

namespace OpenMagicka.Tests.Game;

/// <summary>The game's logic from the menu into a chapter and back (OM-53), on the game's files, without a window.</summary>
public sealed class ShellSimulationTests : IDisposable
{
    private const double Step = 1 / 60.0;
    private static readonly Point Screen = new(1280, 720);

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("om53");
    private readonly Host host = new();
    private readonly ShellSignals signals = new();
    private ShellContent? content;

    private sealed class Host : IGameHost
    {
        public LatestValue<InputState> Input { get; } = new(default);

        public bool Exited { get; private set; }

        public void RequestExit() => Exited = true;
    }

    private ShellSimulation Start()
    {
        content = ShellContent.Open(GameContent.ContentDirectory!, new GameSettings(), Path.Combine(folder.FullName, "settings.json"),
            Path.Combine(folder.FullName, "campaign.json"), sound: false);
        Press();
        return new ShellSimulation(host, content, signals, [Screen], Screen);
    }

    private void Press(params Keys[] keys) => host.Input.Value = new InputState(new KeyboardState(keys), default, true, Screen);

    /// <summary>Ticks until the condition holds (the level loads in the background); false after half a minute.</summary>
    private static bool TickUntil(ShellSimulation shell, Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
            {
                return false;
            }
            shell.Tick(Step);
            Thread.Sleep(1);
        }
        return true;
    }

    /// <summary>A frame written, and the render thread's word that it shows the level the frame is of.</summary>
    private ShellFrame Show(ShellSimulation shell)
    {
        var frame = new ShellFrame();
        shell.WriteFrame(frame, new FrameTiming(0, TimeSpan.Zero, Step));
        signals.Shown = frame.Level;
        return frame;
    }

    [GameContentFact]
    public void AnEscapeHeldThroughTheLoading_DoesNotPauseTheStart_ThenItPausesAndResumes()
    {
        var shell = Start();
        shell.Saves.Set(0, new CampaignSlot(0, 0));
        shell.Play(0, 0);
        Press(Keys.Escape);
        Assert.True(TickUntil(shell, () => shell.Mode == ShellMode.Play));
        shell.Tick(Step);                                                     // the level starts
        Assert.NotNull(shell.Level!.Scene);
        Assert.Equal(ShellMode.Loading, Show(shell).Mode);                    // still behind the loading screen
        shell.Tick(Step);
        Assert.Equal(ShellMode.Play, Show(shell).Mode);
        shell.Tick(Step);
        Assert.False(shell.Paused);                                           // the Escape was held, not pressed

        Press();
        shell.Tick(Step);
        Press(Keys.Escape);
        shell.Tick(Step);
        Assert.True(shell.Paused);
        Press();
        shell.Tick(Step);
        Press(Keys.Escape);
        shell.Tick(Step);
        Assert.False(shell.Paused);
    }

    [GameContentFact]
    public void QuittingFromThePause_KeepsTheTimePlayed_AndGoesBackToTheMenu()
    {
        var shell = Start();
        shell.Saves.Set(0, new CampaignSlot(0, 0));
        shell.Play(0, 0);
        Assert.True(TickUntil(shell, () => shell.Mode == ShellMode.Play));
        shell.Tick(Step);
        Show(shell);
        for (var i = 0; i < 120; i++)
        {
            shell.Tick(Step);
        }
        Press(Keys.Escape);
        shell.Tick(Step);
        Assert.True(shell.Paused);

        foreach (var keys in new[] { Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Enter, Keys.Up, Keys.Enter })
        {
            Press();
            shell.Tick(Step);
            Press(keys);
            shell.Tick(Step);
        }

        Assert.Equal(ShellMode.Menu, shell.Mode);
        Assert.Null(shell.Level);
        Assert.InRange(shell.Saves[0]!.Seconds, 1.9, 2.1);                   // two seconds played; the pause did not count
        Assert.InRange(CampaignSaves.Load(Path.Combine(folder.FullName, "campaign.json"))[0]!.Seconds, 1.9, 2.1);
    }

    public void Dispose()
    {
        content?.Dispose();
        folder.Delete(recursive: true);
    }
}
