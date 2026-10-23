using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine;

/// <summary>
/// FNA game whose simulation runs on a <see cref="FixedStepClock"/> while frames are drawn as
/// fast as the display allows, interpolated between ticks (ADR-0001). FNA's own fixed time
/// step is disabled: it would tie rendering to the tick rate.
/// </summary>
public abstract class FixedStepGame : Microsoft.Xna.Framework.Game
{
    protected FixedStepGame(double ticksPerSecond = FixedStepClock.DefaultTicksPerSecond)
    {
        Clock = new FixedStepClock(ticksPerSecond);
        Graphics = new GraphicsDeviceManager(this)
        {
            SynchronizeWithVerticalRetrace = true,
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected GraphicsDeviceManager Graphics { get; }

    protected FixedStepClock Clock { get; }

    /// <summary>Advances the simulation by exactly one tick of <c>dt</c> seconds.</summary>
    protected abstract void Simulate(double dt);

    /// <summary>Draws a frame; <c>alpha</c> in [0, 1) is the position between the last two ticks.</summary>
    protected abstract void Render(double alpha);

    protected sealed override void Update(GameTime gameTime)
    {
        var steps = Clock.Advance(gameTime.ElapsedGameTime);
        for (var i = 0; i < steps; i++)
        {
            Simulate(Clock.StepSeconds);
        }
        base.Update(gameTime);
    }

    protected sealed override void Draw(GameTime gameTime)
    {
        Render(Clock.Alpha);
        base.Draw(gameTime);
    }
}
