using System.Numerics;
using OpenMagicka.Engine.Input;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Hud;

/// <summary>An element of the pad's wheel to draw: its element, its place from the wizard's centre (pixels), its alpha.</summary>
public readonly record struct WheelIcon(Elements Element, Vector2 Offset, float Alpha);

/// <summary>
/// The pad's element wheel around a wizard (docs/engine/ARCHITECTURE.md §19): eight elements 64 px out, two to each way
/// of the stick (right Cold and Fire, up Arcane and Life, left Lightning and Water, down Earth and Shield). A way the
/// stick points shows its pair, sliding out from the way's point over 0.2 s; it fades over 0.2 s once let go; the
/// wheel button shows all four. Ours draws the pair whole (the original cuts the second icon's crescent out of the
/// first).
/// </summary>
public sealed class SpellWheel
{
    public const float Radius = 64, FadeTime = 0.2f, SlideTime = 0.2f;

    /// <summary>The ways and their pairs, at their angles (degrees, counter-clockwise from the right).</summary>
    public static readonly (ControllerDirection Way, float Angle, (Elements Element, float Angle) First, (Elements Element, float Angle) Second)[] Ways =
    [
        (ControllerDirection.Right, 0, (Elements.Cold, 22.5f), (Elements.Fire, 337.5f)),
        (ControllerDirection.Up, 90, (Elements.Arcane, 67.5f), (Elements.Life, 112.5f)),
        (ControllerDirection.Left, 180, (Elements.Lightning, 202.5f), (Elements.Water, 157.5f)),
        (ControllerDirection.Down, 270, (Elements.Earth, 247.5f), (Elements.Shield, 292.5f)),
    ];

    private readonly float[] fade = new float[Ways.Length];
    private readonly float[] moving = new float[Ways.Length];

    public bool IsShown => fade.Any(f => f > 0);

    public void Update(float dt, ControllerDirection way, bool help)
    {
        for (var i = 0; i < Ways.Length; i++)
        {
            if (help || Ways[i].Way == way)
            {
                moving[i] = fade[i] <= 0 ? 0 : moving[i] + dt;                // a way coming up slides out again
                fade[i] = FadeTime;
            }
            else
            {
                fade[i] = MathF.Max(0, fade[i] - dt);
                moving[i] += dt;
            }
        }
    }

    private static WheelIcon Icon((Elements Element, float Angle) icon, float way, float slide, float alpha)
    {
        var at = MathF.PI / 180 * (way + (icon.Angle - way) * slide);
        return new WheelIcon(icon.Element, new Vector2(MathF.Cos(at), -MathF.Sin(at)) * Radius, alpha);
    }

    public void Write(List<WheelIcon> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        for (var i = 0; i < Ways.Length; i++)
        {
            if (fade[i] <= 0)
            {
                continue;
            }
            var (_, angle, first, second) = Ways[i];
            var slide = Math.Clamp(moving[i] / SlideTime, 0, 1);
            var alpha = Math.Clamp(fade[i] / FadeTime, 0, 1);
            into.Add(Icon(first, angle, slide, alpha));
            into.Add(Icon(second, angle, slide, alpha));
        }
    }
}
