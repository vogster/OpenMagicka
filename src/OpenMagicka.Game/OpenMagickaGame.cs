using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine;

namespace OpenMagicka.Game;

/// <summary>Top-level game. For now an empty window on the fixed-tick loop.</summary>
public sealed class OpenMagickaGame : FixedStepGame
{
    private static readonly Color Background = new(24, 20, 36);

    public OpenMagickaGame()
    {
        Window.Title = "OpenMagicka";
    }

    protected override void Simulate(double dt)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
        {
            Exit();
        }
    }

    protected override void Render(double alpha)
    {
        GraphicsDevice.Clear(Background);
    }
}
