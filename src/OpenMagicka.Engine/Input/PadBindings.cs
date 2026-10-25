using Microsoft.Xna.Framework.Input;

namespace OpenMagicka.Engine.Input;

/// <summary>What a pad's controls do (the original's 24 functions, in its order).</summary>
public enum ControllerFunction
{
    Move_Right, Move_Up, Move_Left, Move_Down, Menu_Select, Menu_Back, Spell_Right, Spell_Up, Spell_Left, Spell_Down, Magick_Next, Magick_Prev,
    Attack, Special, Block, Interact, Boost, Cast_Force, Cast_Area, Cast_Self, Cast_Magick, Pause, Inventory, Spell_Wheel,
}

/// <summary>How a pad control is read.</summary>
public enum PadInput : byte { None, Button, Trigger, PositiveStick, NegativeStick }

/// <summary>A pad control: a button (its flag), a trigger (0 left, 1 right) or a stick's axis one way (0, 1: the left's X and Y; 2, 3: the right's).</summary>
public readonly record struct PadBinding(PadInput Input, int Index)
{
    public static PadBinding Of(Buttons button) => new(PadInput.Button, (int)button);
}

/// <summary>
/// A pad's bindings (docs/engine/ARCHITECTURE.md §9), the original's defaults: the left stick moves, the
/// right one gestures the elements; A attacks, B boosts, X uses, Y is the special; the right shoulder
/// blocks; the right trigger casts Force, the left one Area; the pad's up and down pick the magick;
/// Start pauses, Back is the inventory, the right stick's click shows the spell wheel. The pads are
/// SDL's game controllers, through FNA.
/// </summary>
public sealed class PadBindings
{
    /// <summary>A trigger or a stick counts as held from half way.</summary>
    public const float Held = 0.5f;

    private readonly PadBinding[] bindings =
    [
        new(PadInput.PositiveStick, 0), new(PadInput.PositiveStick, 1), new(PadInput.NegativeStick, 0), new(PadInput.NegativeStick, 1),
        PadBinding.Of(Buttons.A), PadBinding.Of(Buttons.B),
        new(PadInput.PositiveStick, 2), new(PadInput.PositiveStick, 3), new(PadInput.NegativeStick, 2), new(PadInput.NegativeStick, 3),
        PadBinding.Of(Buttons.DPadDown), PadBinding.Of(Buttons.DPadUp),
        PadBinding.Of(Buttons.A), PadBinding.Of(Buttons.Y), PadBinding.Of(Buttons.RightShoulder), PadBinding.Of(Buttons.X), PadBinding.Of(Buttons.B),
        new(PadInput.Trigger, 1), new(PadInput.Trigger, 0), PadBinding.Of(Buttons.Y), PadBinding.Of(Buttons.B),
        PadBinding.Of(Buttons.Start), PadBinding.Of(Buttons.Back), PadBinding.Of(Buttons.RightStick),
    ];

    public PadBinding this[ControllerFunction function] =>
        Enum.IsDefined(function) ? bindings[(int)function] : throw new ArgumentOutOfRangeException(nameof(function), function, "not a pad control");

    public void Set(ControllerFunction function, PadBinding binding) =>
        bindings[Enum.IsDefined(function) ? (int)function : throw new ArgumentOutOfRangeException(nameof(function), function, "not a pad control")] = binding;

    /// <summary>How far a control is pushed, 0..1 (a button: 0 or 1).</summary>
    public static float Value(PadBinding binding, GamePadState pad) => binding.Input switch
    {
        PadInput.Button => pad.IsButtonDown((Buttons)binding.Index) ? 1 : 0,
        PadInput.Trigger => binding.Index == 0 ? pad.Triggers.Left : binding.Index == 1 ? pad.Triggers.Right : 0,
        PadInput.PositiveStick => MathF.Max(Axis(pad, binding.Index), 0),
        PadInput.NegativeStick => MathF.Max(-Axis(pad, binding.Index), 0),
        _ => 0,
    };

    public static bool IsDown(PadBinding binding, GamePadState pad) => binding.Input == PadInput.Button ? Value(binding, pad) > 0 : Value(binding, pad) >= Held;

    private static float Axis(GamePadState pad, int index) => index switch
    {
        0 => pad.ThumbSticks.Left.X,
        1 => pad.ThumbSticks.Left.Y,
        2 => pad.ThumbSticks.Right.X,
        3 => pad.ThumbSticks.Right.Y,
        _ => 0,
    };
}

/// <summary>A pad at this tick and the last, read through its bindings.</summary>
public readonly record struct PadFrame(GamePadState Pad, GamePadState Last, PadBindings Bindings)
{
    public float Value(ControllerFunction function) => PadBindings.Value(Bindings[function], Pad);

    public bool IsDown(ControllerFunction function) => PadBindings.IsDown(Bindings[function], Pad);

    public bool IsPressed(ControllerFunction function) => IsDown(function) && !PadBindings.IsDown(Bindings[function], Last);

    public bool IsReleased(ControllerFunction function) => !IsDown(function) && PadBindings.IsDown(Bindings[function], Last);
}
