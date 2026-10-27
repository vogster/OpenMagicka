using Microsoft.Xna.Framework;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Audio;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Game.Menu;

/// <summary>A loading screen's narration (the intro's last words): its text in the tip's place, and the cue the level waits for.</summary>
public sealed record Narration(string TextKey, ISoundCue? Cue);

/// <summary>
/// The pages played before a chapter (OM-84, docs/engine/ARCHITECTURE.md §20), as the original's: the intro before the
/// first, the world map's cutscene before any other. Each plays to its end or till skipped (a click, Enter, Space or A;
/// ours: Esc or B too), fades to black, and goes away for the chapter to load.
/// </summary>
public static class CutscenePages
{
    /// <summary>What plays before a chapter; none for one with no cutscene, which starts at once.</summary>
    public static MenuPage? For(TomeMenu menu, int slot, int chapter, bool resume)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return chapter == 0 ? new IntroPage(menu, slot, resume)
            : menu.Host.Cutscenes.GetValueOrDefault(chapter) is { } cutscene ? new MapCutscenePage(menu, slot, chapter, resume, cutscene)
            : null;
    }

    /// <summary>The chapter is begun: after its cutscene, or at once without one.</summary>
    public static void Begin(TomeMenu menu, int slot, int chapter, bool resume)
    {
        if (For(menu, slot, chapter, resume) is { } page)
        {
            menu.Push(page);
        }
        else
        {
            menu.Host.Play(slot, chapter, resume);
        }
    }

    public const string SubtitleFont = "Maiandra14";

    /// <summary>The subtitles over the screen's bottom, as the original's: wrapped, centred, 32 px above the bottom, white over a shadow.</summary>
    internal static void Subtitles(MenuPicture picture, string text, Point screen, int wrap)
    {
        if (text.Length > 0)
        {
            picture.ScreenTexts.Add(new MenuText(SubtitleFont, text, new Vector2(screen.X / 2f, screen.Y - 32), TextAlignment.Center, Vector4.One,
                Wrap: Math.Max(1, wrap), Shadow: true, FromBottom: true));
        }
    }

    /// <summary>
    /// The chapter's name and title on the page: a map's title wrapped to 800 px and left out when longer than 64 letters,
    /// the intro's as it is, as the original's.
    /// </summary>
    internal static void ChapterTitle(MenuPicture picture, TomeMenu menu, int chapter, bool introRule = false)
    {
        picture.PageTexts.Add(new MenuText(MenuPage.TitleFont, menu.Text(Chapters.NameKey(chapter)), new Vector2(512, 360), TextAlignment.Center, MenuPage.Ink));
        var title = menu.Text(Chapters.TitleKey(chapter));
        if (introRule || title.Length <= 64)
        {
            picture.PageTexts.Add(new MenuText(MenuPage.TitleFont, title, new Vector2(512, 480), TextAlignment.Center, MenuPage.Ink, Wrap: introRule ? 0 : 800));
        }
    }

    /// <summary>A string of the game's when it has the key; none (empty) when it lacks it.</summary>
    internal static string Known(TomeMenu menu, string key) => menu.Host.Strings.TryGetRaw(StringTable.Id(key), out _) ? menu.Text(key) : "";
}

/// <summary>
/// A page that plays and then starts its chapter: it ends (by itself or skipped) with a fade to black, then leaves
/// without a sound and the chapter loads. It takes no skip while the book turns to it (the original's riffle), so a
/// button let go as a level ends skips nothing.
/// </summary>
public abstract class CutscenePage(TomeMenu menu, int slot, int chapter, bool resume, float fadeTime, float turning) : MenuPage(menu)
{
    private float fade = -1, shown;
    private bool gone;

    protected int Chapter => chapter;

    /// <summary>It is ending: fading to black.</summary>
    public bool IsEnding => fade >= 0;

    public sealed override void Update(MenuInput input)
    {
        if (!IsEnding && shown >= turning && !Menu.IsTurning && (input.Accept || input.Click || input.Back))
        {
            Skip();
        }
    }

    public sealed override void Tick(float dt)
    {
        if (gone)
        {
            return;
        }
        shown += dt;
        if (!IsEnding)
        {
            Play(dt);
            return;
        }
        fade += dt / fadeTime;
        if (fade >= 1)
        {
            gone = true;
            Menu.PopSilently();
            Menu.Host.Play(slot, chapter, resume, StartNarration());
        }
    }

    /// <summary>A tick of its own while it plays.</summary>
    protected abstract void Play(float dt);

    /// <summary>It is skipped: what it plays stops (as authored), and the fade begins.</summary>
    protected virtual void Skip() => End();

    /// <summary>The fade to black begins.</summary>
    protected void End() => fade = Math.Max(fade, 0);

    /// <summary>What the loading screen narrates, its cue started (none but the intro's, played through).</summary>
    protected virtual Narration? StartNarration() => null;

    public override void Draw(MenuPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        picture.Cover = Math.Clamp(fade, 0, 1);
    }
}

/// <summary>
/// The world map's cutscene before a chapter (the original's 20-page turn to it): for 3 s the chapter's name and title,
/// then the map painted into the paper, the camera moving over it as the cutscene's keys say, the narration and its
/// subtitles; it ends when the keys are over and the narration done, with a fade of half a second. The menu's music
/// plays on.
/// </summary>
public sealed class MapCutscenePage : CutscenePage
{
    public const float TitleTime = 3, FadeTime = 0.5f, Turning = 1.06f;

    /// <summary>The map's size in texels.</summary>
    public const int MapSize = 2048;

    private readonly MapCutscene cutscene;
    private readonly ISoundCue? voice;
    private float time = -TitleTime;

    public MapCutscenePage(TomeMenu menu, int slot, int chapter, bool resume, MapCutscene cutscene)
        : base(menu, slot, chapter, resume, FadeTime, Turning)
    {
        this.cutscene = cutscene ?? throw new ArgumentNullException(nameof(cutscene));
        voice = cutscene.Cue is { } cue ? menu.Host.Voice(cutscene.Bank, cue) : null;   // prepared, played with the map
    }

    public override int Pages => 20;

    /// <summary>The map is shown (past the chapter's title).</summary>
    public bool OnMap => time >= 0;

    protected override void Play(float dt)
    {
        var wasOnMap = OnMap;
        time += dt;
        if (OnMap && !wasOnMap)
        {
            Menu.Riffle(1, picture => CutscenePages.ChapterTitle(picture, Menu, Chapter));   // the title's page turns away, the map under it
            voice?.Play();
        }
        if (OnMap && time > cutscene.Duration && voice is null or { IsStopping: true } or { IsStopped: true })
        {
            End();
        }
    }

    protected override void Skip()
    {
        voice?.Stop(immediate: !OnMap);                                       // not played yet: no tail to ring out
        End();
    }

    public override void Draw(MenuPicture picture)
    {
        base.Draw(picture);
        if (!OnMap)
        {
            CutscenePages.ChapterTitle(picture, Menu, Chapter);
            return;
        }
        var (at, zoom) = cutscene.Camera(time);
        var map = new MenuSprite(MenuAtlas.CampaignMap, new Rectangle(0, 0, MapSize, MapSize), new Vector2(512 - at.X * zoom, 512 - at.Y * zoom), new Vector2(MapSize * zoom), Vector4.One);
        picture.PagePaintings.Add(new MenuPainting(MenuAtlas.MapMask, new Rectangle(0, 0, MenuPicture.PageSize, MenuPicture.PageSize), map));
        if (!IsEnding && cutscene.Subtitles is { } subtitles)
        {
            CutscenePages.Subtitles(picture, CutscenePages.Known(Menu, subtitles), Menu.Screen, Menu.Screen.X - 128);
        }
    }
}

/// <summary>
/// The intro before the first chapter, as the original's: the room dark and <c>music_intro</c> for 8 s, the narration
/// starting at 3 s under the title page (its words as subtitles), the title page till 25 s, then six pictures painted
/// into the paper, each with its words and its narration, the third to fifth moving. After the last a fade of 2 s, and
/// the loading screen narrates the last words, the level waiting for them; skipped, it fades and loads plainly.
/// </summary>
public sealed class IntroPage : CutscenePage
{
    public const float FadeTime = 2, LeadIn = 8, TitleTime = 17, NarrationAt = 3, Turning = 0.94f;

    /// <summary>The pictures' times, and where each lies on the page (page pixels).</summary>
    public static readonly float[] SlideTimes = [6, 20, 11, 17, 13, 9];

    public static readonly Rectangle PictureArea = new(86, 90, 852, 652);

    public static readonly Vector4 WordsColour = new(0.1f, 0.1f, 0.1f, 0.9f);

    private static readonly MenuAtlas[] Pictures = [MenuAtlas.Intro1, MenuAtlas.Intro2, MenuAtlas.Intro3, MenuAtlas.Intro4, MenuAtlas.Intro5, MenuAtlas.Intro6];

    private int slide = -1;
    private float left = LeadIn;
    private ISoundCue? voice;
    private bool spoken, said, skipped;

    public IntroPage(TomeMenu menu, int slot, bool resume)
        : base(menu, slot, 0, resume, FadeTime, Turning)
    {
    }

    public override int Pages => 8;

    /// <summary>The picture shown (1…6), 0 the title page, −1 the lead-in.</summary>
    public int Slide => slide;

    public override void Enter() => Menu.Host.PlayMusic("music_intro");

    protected override void Play(float dt)
    {
        left -= dt;
        if (slide < 0 && left <= LeadIn - NarrationAt && !spoken)
        {
            spoken = true;
            voice = Say("cuts_int0");
        }
        if (slide >= 1 && !said && left <= SlideTimes[slide - 1] - 0.5f)
        {
            said = true;
            voice = Say($"cuts_int{slide}");                                  // as the page has turned
        }
        if (left > 0)
        {
            return;
        }
        if (slide < 0)
        {
            (slide, left) = (0, TitleTime);
        }
        else if (slide < SlideTimes.Length)
        {
            var gone = slide++;
            (left, said) = (SlideTimes[slide - 1], false);
            Menu.Riffle(1, picture => DrawSlide(picture, gone, 1));          // the old picture, as it ended, turns away
        }
        else
        {
            End();
        }
    }

    private ISoundCue? Say(string name)
    {
        var cue = Menu.Host.Voice(SoundBanks.WaveBank, name);
        cue?.Play();
        return cue;
    }

    protected override void Skip()
    {
        skipped = true;
        voice?.Stop(immediate: false);
        End();
    }

    protected override Narration? StartNarration() => skipped ? null : new Narration("#cutscene_intro7", Say("cuts_int7"));

    /// <summary>A picture's progress: still for its first and last tenth, between them eased twice.</summary>
    public static float Progress(float left, float time)
    {
        var x = (1 - left / time) / 0.8f - 0.125f;
        return MapCutscene.SmoothStep(0, 1, MapCutscene.SmoothStep(0, 1, x));
    }

    /// <summary>
    /// The window of a picture's texels shown at a progress (x, y, width, height; between texels, so a slow pan glides):
    /// the third pans, the fourth zooms out to all of it, the fifth drifts.
    /// </summary>
    public static Vector4 Window(int slide, float p)
    {
        var q = 1 - p;
        return slide switch
        {
            3 => new Vector4(100 + 600 * p, 0, 852, 652),
            4 => new Vector4(426 * q, 64 * q, 852 * (1 + p), 652 * (1 + p)),
            5 => new Vector4(280 * p, 220 * p, 852 * (1 + 0.1f * p), 652 * (1 + 0.1f * p)),
            _ => new Vector4(0, 0, 852, 652),
        };
    }

    public override void Draw(MenuPicture picture)
    {
        base.Draw(picture);
        DrawSlide(picture, Math.Max(slide, 0), slide > 0 ? Progress(left, SlideTimes[slide - 1]) : 0);
        if (slide <= 0 && spoken && !IsEnding)
        {
            CutscenePages.Subtitles(picture, CutscenePages.Known(Menu, "#cutscene_intro0"), Menu.Screen, Menu.Screen.X * 8 / 10);
        }
    }

    /// <summary>A slide on the page (0: the title page) at a progress of its motion.</summary>
    private void DrawSlide(MenuPicture picture, int shown, float progress)
    {
        if (shown == 0)
        {
            CutscenePages.ChapterTitle(picture, Menu, 0, introRule: true);
            return;
        }
        var art = new MenuSprite(Pictures[shown - 1], PictureArea, new Vector2(PictureArea.X, PictureArea.Y), new Vector2(PictureArea.Width, PictureArea.Height),
            Vector4.One);
        picture.PagePaintings.Add(new MenuPainting(MenuAtlas.IntroMask, PictureArea, art, Window(shown, progress)));
        picture.PageTexts.Add(new MenuText(BoxFont, Menu.Text($"#cutscene_intro{shown}"), new Vector2(512, 768), TextAlignment.Center, WordsColour, Wrap: 896));
    }
}
