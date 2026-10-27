using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// The page between a chapter and the next one's map (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"), as the
/// original's: the level's last frame on the right page (800 × 640, its middle cut to 1.25 : 1), lit by the candle.
/// It is put on at once over the lobby as the camera pulls back from the page; it takes no input; once the camera is
/// still and the book open it riffles on to the next chapter's map (20 pages), or loads that chapter when it has none,
/// or (no next chapter) goes back.
/// </summary>
/// <param name="next">The chapter to go on to; none: back to the lobby.</param>
public sealed class EndPage(TomeMenu menu, int slot, int? next) : MenuPage(menu)
{
    /// <summary>Where the shot lies on the page (its shape the cut's, <see cref="MenuPass.ShotCut"/>).</summary>
    public static readonly Rectangle Area = new(112, 192, 800, 640);

    private bool gone;

    /// <summary>The count the original keeps for it (the book riffles back by it).</summary>
    public override int Pages => 9;

    public override void Update(MenuInput input)
    {
        // it shows the level: no choice, no going back
    }

    public override void Tick(float dt)
    {
        if (gone || Menu.Book is { IsOpen: false } or { IsCameraMoving: true })
        {
            return;
        }
        gone = true;
        if (next is not { } chapter)
        {
            Menu.Pop();
        }
        else if (CutscenePages.For(Menu, slot, chapter, resume: true) is { } map)
        {
            Menu.Push(map);                                                   // this page rides the sheets away
        }
        else
        {
            Menu.PopSilently();
            Menu.Host.Play(slot, chapter, resume: true);
        }
    }

    public override void Draw(MenuPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        picture.PageSprites.Add(new MenuSprite(MenuAtlas.Shot, Rectangle.Empty, new Vector2(Area.X, Area.Y), new Vector2(Area.Width, Area.Height),
            Vector4.One));                                                     // the cut is the shot's own (its size the render thread's)
    }
}
