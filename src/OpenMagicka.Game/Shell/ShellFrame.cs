using OpenMagicka.Engine.Rendering;
using OpenMagicka.Game.Play;

namespace OpenMagicka.Game.Shell;

/// <summary>What the game shows: the Tome's menu, a level loading, or a level at play.</summary>
public enum ShellMode : byte { Menu, Loading, Play }

/// <summary>The screen the settings ask for: its size (none: the default), windowed or not, its sync.</summary>
public readonly record struct ScreenMode(int? Width, int? Height, bool Fullscreen, bool VSync);

/// <summary>
/// What the game draws of a tick (OM-53): its mode; the level's frame and the serial of the level it is of (none:
/// 0), so that the render thread starts and ends its picture of a level as they come and go; the menu's picture (the
/// Tome, the pause menu, the loading screen, an ending's word); and the screen and language the settings ask for.
/// </summary>
public sealed class ShellFrame
{
    public ShellMode Mode { get; set; }

    public int Level { get; set; }

    public EffectChannel? Channel { get; set; }

    public PlayFrame Play { get; } = new();

    public MenuPicture Menu { get; } = new();

    /// <summary>The level's frame is to be kept once drawn (a chapter's end, for the book's end page).</summary>
    public bool CaptureShot { get; set; }

    public ScreenMode Screen { get; set; }

    public string Language { get; set; } = Content.Localization.LanguagePack.Fallback;
}

/// <summary>What the render thread tells the logic thread: the serial of the level it shows (so the level waits for it).</summary>
public sealed class ShellSignals
{
    private int shown;
    private volatile bool book;
    private int captured;

    /// <summary>The serial of the level whose last frame the render thread kept.</summary>
    public int Captured
    {
        get => Volatile.Read(ref captured);
        set => Volatile.Write(ref captured, value);
    }

    /// <summary>The book's pass is made (OM-83): the menu's page may lie in it.</summary>
    public bool Book
    {
        get => book;
        set => book = value;
    }

    public int Shown
    {
        get => Volatile.Read(ref shown);
        set => Volatile.Write(ref shown, value);
    }
}
