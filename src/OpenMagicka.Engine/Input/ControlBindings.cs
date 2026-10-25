using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Input;

namespace OpenMagicka.Engine.Input;

/// <summary>What a keyboard-and-mouse player's controls do (the original's 17 functions, in its order).</summary>
public enum KeyboardBindings
{
    Water, Life, Shield, Cold, Lightning, Arcane, Earth, Fire, Cast, CastSelf, Boost, Move, Block, Shift, Inventory, PrevMagick, NextMagick,
}

/// <summary>A mouse button, or a turn of the wheel, as a control.</summary>
public enum MouseButton : byte { Invalid, Left, Middle, Right, X1, X2, ScrollUp, ScrollDown }

/// <summary>A control's key or mouse button; none bound is the default.</summary>
public readonly record struct ControlBinding(Keys Key, MouseButton Mouse)
{
    public static ControlBinding Of(Keys key) => new(key, MouseButton.Invalid);

    public static ControlBinding Of(MouseButton button) => new(Keys.None, button);

    [JsonIgnore]
    public bool IsMouse => Mouse != MouseButton.Invalid;

    [JsonIgnore]
    public bool IsBound => IsMouse || Key != Keys.None;
}

/// <summary>
/// The keyboard-and-mouse bindings (docs/engine/ARCHITECTURE.md §9): the original's defaults (QWER
/// ASDF for the elements, the right button to cast, the middle one to cast on oneself, the left one to
/// move, Space to boost, Left Ctrl to block, Left Shift for the area and the weapon, Tab for the
/// inventory, the wheel for the magicks). A key or button bound anew leaves the function that had it
/// unbound; Escape, the Windows keys and the menu key cannot be bound. Kept in our own file (JSON);
/// importing the original's settings is OM-53.
/// </summary>
public sealed class ControlBindings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private readonly ControlBinding[] bindings = new ControlBinding[Enum.GetValues<KeyboardBindings>().Length];

    public ControlBindings()
    {
        foreach (var (function, binding) in Defaults)
        {
            bindings[(int)function] = binding;
        }
    }

    public static IReadOnlyDictionary<KeyboardBindings, ControlBinding> Defaults { get; } = new Dictionary<KeyboardBindings, ControlBinding>
    {
        [KeyboardBindings.Water] = ControlBinding.Of(Keys.Q),
        [KeyboardBindings.Life] = ControlBinding.Of(Keys.W),
        [KeyboardBindings.Shield] = ControlBinding.Of(Keys.E),
        [KeyboardBindings.Cold] = ControlBinding.Of(Keys.R),
        [KeyboardBindings.Lightning] = ControlBinding.Of(Keys.A),
        [KeyboardBindings.Arcane] = ControlBinding.Of(Keys.S),
        [KeyboardBindings.Earth] = ControlBinding.Of(Keys.D),
        [KeyboardBindings.Fire] = ControlBinding.Of(Keys.F),
        [KeyboardBindings.Cast] = ControlBinding.Of(MouseButton.Right),
        [KeyboardBindings.CastSelf] = ControlBinding.Of(MouseButton.Middle),
        [KeyboardBindings.Boost] = ControlBinding.Of(Keys.Space),
        [KeyboardBindings.Move] = ControlBinding.Of(MouseButton.Left),
        [KeyboardBindings.Block] = ControlBinding.Of(Keys.LeftControl),
        [KeyboardBindings.Shift] = ControlBinding.Of(Keys.LeftShift),
        [KeyboardBindings.Inventory] = ControlBinding.Of(Keys.Tab),
        [KeyboardBindings.PrevMagick] = ControlBinding.Of(MouseButton.ScrollUp),
        [KeyboardBindings.NextMagick] = ControlBinding.Of(MouseButton.ScrollDown),
    };

    public ControlBinding this[KeyboardBindings function] => bindings[Index(function)];

    /// <summary>Binds a key or button to a function; false (nothing changed) for one that cannot be bound.</summary>
    public bool Set(KeyboardBindings function, ControlBinding binding)
    {
        var index = Index(function);
        if (!binding.IsBound || binding.Key is Keys.Escape or Keys.LeftWindows or Keys.RightWindows or Keys.Apps)
        {
            return false;
        }
        for (var i = 0; i < bindings.Length; i++)
        {
            if (bindings[i] == binding)
            {
                bindings[i] = default;                                        // taken from the function that had it
            }
        }
        bindings[index] = binding;
        return true;
    }

    public string Save() => JsonSerializer.Serialize(Enum.GetValues<KeyboardBindings>().ToDictionary(f => f, f => bindings[(int)f]), Json);

    /// <summary>
    /// Bindings read from our file, each bound as anew (one that cannot be bound, or none, keeps the default);
    /// the defaults where it says nothing; an unreadable file, or one naming no control, fails.
    /// </summary>
    public static ControlBindings Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var loaded = new ControlBindings();
        Dictionary<KeyboardBindings, ControlBinding>? read;
        try
        {
            read = JsonSerializer.Deserialize<Dictionary<KeyboardBindings, ControlBinding>>(json, Json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"the controls file does not read: {e.Message}", e);
        }
        foreach (var (function, binding) in read ?? [])
        {
            if (!Enum.IsDefined(function) || !Enum.IsDefined(binding.Mouse))
            {
                throw new InvalidDataException($"the controls file names no control \"{function}\" or no mouse button \"{binding.Mouse}\"");
            }
            loaded.Set(function, binding);                                    // as a binding anew: one function per key, none on Escape
        }
        return loaded;
    }

    private static int Index(KeyboardBindings function) =>
        Enum.IsDefined(function) ? (int)function : throw new ArgumentOutOfRangeException(nameof(function), function, "not a control");
}
