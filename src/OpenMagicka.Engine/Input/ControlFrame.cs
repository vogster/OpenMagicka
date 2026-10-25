using Microsoft.Xna.Framework.Input;

namespace OpenMagicka.Engine.Input;

/// <summary>
/// The keyboard and mouse at this tick and the last, read through the bindings: a control is down, was
/// pressed (down now, up before) or released. A wheel binding is pressed when the wheel turned its way
/// since the last tick, and is never down or released, as the original.
/// </summary>
public readonly record struct ControlFrame(KeyboardState Keyboard, MouseState Mouse, KeyboardState LastKeyboard, MouseState LastMouse)
{
    public bool IsDown(ControlBinding binding) => binding.IsMouse ? Down(Mouse, binding.Mouse) : binding.Key != Keys.None && Keyboard.IsKeyDown(binding.Key);

    public bool WasDown(ControlBinding binding) => binding.IsMouse ? Down(LastMouse, binding.Mouse) : binding.Key != Keys.None && LastKeyboard.IsKeyDown(binding.Key);

    public bool IsPressed(ControlBinding binding) => binding.Mouse switch
    {
        MouseButton.ScrollUp => Mouse.ScrollWheelValue > LastMouse.ScrollWheelValue,
        MouseButton.ScrollDown => Mouse.ScrollWheelValue < LastMouse.ScrollWheelValue,
        _ => IsDown(binding) && !WasDown(binding),
    };

    public bool IsReleased(ControlBinding binding) => !IsDown(binding) && WasDown(binding);

    private static bool Down(MouseState mouse, MouseButton button) => button switch
    {
        MouseButton.Left => mouse.LeftButton == ButtonState.Pressed,
        MouseButton.Middle => mouse.MiddleButton == ButtonState.Pressed,
        MouseButton.Right => mouse.RightButton == ButtonState.Pressed,
        MouseButton.X1 => mouse.XButton1 == ButtonState.Pressed,
        MouseButton.X2 => mouse.XButton2 == ButtonState.Pressed,
        _ => false,
    };
}
