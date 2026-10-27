using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// What a menu gets of the player's input in a tick (OM-53): the mouse on the page (page pixels) and on the screen,
/// whether it moved or clicked (a left press released), and the moves and choices of the keys and a pad (arrows or
/// the d-pad and left stick, repeating every 0.25 s while held; Enter, Space or A to choose; Esc, Backspace or B to go
/// back). Enter and Space choose too, though the original's book takes only the mouse and A. A pad's Start is told
/// apart (it closes the pause menu it opened).
/// </summary>
public readonly record struct MenuInput(Vector2? Page, Point Screen, bool Moved, bool Click, int Vertical, int Horizontal, bool Accept, bool Back,
    bool Delete, int Wheel, bool Start = false)
{
    public static MenuInput None => default;
}

/// <summary>Turns two ticks' input into a menu's, repeating held moves.</summary>
public sealed class MenuInputReader
{
    public const float Repeat = 0.25f, StickDead = 0.5f;

    /// <summary>The mouse onto the page from the screen's size (none: the flat page's square); the book's when it is shown.</summary>
    public Func<Point, Point, Vector2?>? Picking { get; set; }

    private InputState last;
    private (int Vertical, int Horizontal) held;
    private float wait;

    public MenuInput Read(InputState input, float dt)
    {
        var before = last;
        last = input;
        if (!input.IsActive || !before.IsActive)
        {
            held = (0, 0);
            return MenuInput.None;
        }
        var (keys, was) = (input.Keyboard, before.Keyboard);
        bool Pressed(Keys key) => keys.IsKeyDown(key) && !was.IsKeyDown(key);
        bool Pad(Buttons button) => input.Pad.IsButtonDown(button) && !before.Pad.IsButtonDown(button);
        var stick = input.Pad.ThumbSticks.Left;
        var vertical = keys.IsKeyDown(Keys.Down) || input.Pad.IsButtonDown(Buttons.DPadDown) || stick.Y < -StickDead ? 1
            : keys.IsKeyDown(Keys.Up) || input.Pad.IsButtonDown(Buttons.DPadUp) || stick.Y > StickDead ? -1 : 0;
        var horizontal = keys.IsKeyDown(Keys.Right) || input.Pad.IsButtonDown(Buttons.DPadRight) || stick.X > StickDead ? 1
            : keys.IsKeyDown(Keys.Left) || input.Pad.IsButtonDown(Buttons.DPadLeft) || stick.X < -StickDead ? -1 : 0;
        var (moveV, moveH) = (0, 0);
        if ((vertical, horizontal) != held)
        {
            (held, wait, moveV, moveH) = ((vertical, horizontal), Repeat, vertical, horizontal);
        }
        else if (vertical != 0 || horizontal != 0)
        {
            wait -= dt;
            if (wait <= 0)
            {
                (wait, moveV, moveH) = (Repeat, vertical, horizontal);
            }
        }
        var mouse = new Point(input.Mouse.X, input.Mouse.Y);
        var page = input.Screen.X <= 0 || input.Screen.Y <= 0 ? null
            : Picking is { } pick ? pick(mouse, input.Screen) : MenuPicture.ToPage(mouse, input.Screen.X, input.Screen.Y);
        return new MenuInput(page, mouse, mouse != new Point(before.Mouse.X, before.Mouse.Y),
            input.Mouse.LeftButton == ButtonState.Released && before.Mouse.LeftButton == ButtonState.Pressed, moveV, moveH,
            Pressed(Keys.Enter) || Pressed(Keys.Space) || Pad(Buttons.A), Pressed(Keys.Escape) || Pressed(Keys.Back) || Pad(Buttons.B), Pressed(Keys.Delete),
            Math.Sign(input.Mouse.ScrollWheelValue - before.Mouse.ScrollWheelValue), Pad(Buttons.Start));
    }
}
