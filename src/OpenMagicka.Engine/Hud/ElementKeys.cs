using OpenMagicka.Engine.Input;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Hud;

/// <summary>An element key on the HUD: its element, its bound key's name, where it is (x of its icon's top left, and its row) and how it looks.</summary>
public readonly record struct ElementKey(Elements Element, string Label, float X, int Row, float Saturation, float Intensity);

/// <summary>
/// The eight element keys in the screen's bottom-left corner, as the original's keyboard HUD (docs/engine/ARCHITECTURE.md
/// §19): two rows in the keys' own layout (Water, Life, Shield, Cold over Lightning, Arcane, Earth, Fire). A conjured
/// element's key dims for 0.125 s, then flashes bright for 0.125 s and settles; a key the tutorial disables fades
/// to grey.
/// </summary>
public sealed class ElementKeys
{
    public const float Cooldown = 0.125f, FlashTime = 0.125f, FlashRise = 20, FlashCap = 20, FlashColour = 4, SettleSpeed = 10, GreySpeed = 4,
        DimSaturation = 0.5f;

    /// <summary>The keys in their drawing order (column by column, the top one first): element, binding, icon x, row.</summary>
    public static readonly (Elements Element, KeyboardBindings Binding, float X, int Row)[] Layout =
    [
        (Elements.Water, KeyboardBindings.Water, 40, 0), (Elements.Lightning, KeyboardBindings.Lightning, 65, 1),
        (Elements.Life, KeyboardBindings.Life, 90, 0), (Elements.Arcane, KeyboardBindings.Arcane, 115, 1),
        (Elements.Shield, KeyboardBindings.Shield, 140, 0), (Elements.Earth, KeyboardBindings.Earth, 165, 1),
        (Elements.Cold, KeyboardBindings.Cold, 190, 0), (Elements.Fire, KeyboardBindings.Fire, 215, 1),
    ];

    private readonly (float Saturation, float Intensity, float Cooldown, float Flash)[] keys = [.. Layout.Select(_ => (1f, 1f, 0f, 0f))];

    /// <summary>A key's look now.</summary>
    public (float Saturation, float Intensity) this[Elements element] =>
        Array.FindIndex(Layout, k => k.Element == element) is var i and >= 0 ? (keys[i].Saturation, keys[i].Intensity) : (1, 1);

    /// <summary>An element was conjured: its key dims, then flashes.</summary>
    public void Press(Elements element)
    {
        var i = Array.FindIndex(Layout, k => k.Element == element);
        if (i >= 0)
        {
            keys[i].Cooldown = Cooldown;
        }
    }

    public void Update(float dt, Elements disabled)
    {
        for (var i = 0; i < keys.Length; i++)
        {
            ref var key = ref keys[i];
            if ((disabled & Layout[i].Element) != 0)
            {
                key.Saturation = MathF.Max(0, key.Saturation - GreySpeed * dt);
                key.Intensity = MathF.Min(key.Intensity, 1);
                continue;
            }
            if (key.Cooldown > 0)
            {
                key.Saturation = MathF.Min(key.Saturation, DimSaturation);
                key.Cooldown -= dt;
                key.Flash = key.Cooldown <= 0 ? FlashTime : 0;
            }
            else if (key.Flash > 0)
            {
                key.Flash -= dt;
                key.Intensity = MathF.Min(FlashCap, key.Intensity + FlashRise * dt);
                key.Saturation = MathF.Min(1, key.Saturation + FlashColour * dt);
            }
            else
            {
                key.Saturation = MathF.Min(1, key.Saturation + SettleSpeed * dt);
                key.Intensity = MathF.Min(key.Intensity, 1);
            }
        }
    }

    /// <summary>The keys to draw, labelled with their bound keys.</summary>
    public void Write(ControlBindings bindings, List<ElementKey> into)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(into);
        for (var i = 0; i < Layout.Length; i++)
        {
            var (element, binding, x, row) = Layout[i];
            into.Add(new ElementKey(element, Label(bindings[binding]), x, row, keys[i].Saturation, keys[i].Intensity));
        }
    }

    /// <summary>A binding's name on its key plate: the key's letter or name, a mouse button's.</summary>
    public static string Label(ControlBinding binding) => binding.Mouse != MouseButton.Invalid ? binding.Mouse.ToString()
        : binding.Key.ToString() is { Length: 2 } digit && digit[0] == 'D' && char.IsDigit(digit[1]) ? digit[1..] : binding.Key.ToString();
}
