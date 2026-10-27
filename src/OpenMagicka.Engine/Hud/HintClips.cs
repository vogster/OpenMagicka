using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Hud;

/// <summary>A picture of a hint's clip: its cell of <c>UI/HUD/Tutorial</c>, its centre's offset from the clip's anchor, and the letter on it (none: '\0').</summary>
public readonly record struct ClipPiece(Rectangle Source, Vector2 Offset, char Letter = '\0');

/// <summary>
/// The clips beside a tutorial hint's text (OM-86, docs/engine/ARCHITECTURE.md §21), as the original's: pictures of
/// <c>UI/HUD/Tutorial</c> (512 × 256) switched on timers, a keyboard set and a pad set. The clip's flags play one
/// after the other in their bit order (not the script's), the whole looping; each picture is centred on the clip's
/// anchor (the middle of a 128 px strip right of the text) plus its offset.
/// </summary>
public static class HintClips
{
    /// <summary>The strip the clip takes right of the hint's text.</summary>
    public const int Strip = 128;

    /// <summary>The clips' pictures.</summary>
    public const string Atlas = "UI/HUD/Tutorial";

    /// <summary>The stick's travel, R = 18 px.</summary>
    public const float StickReach = 18;

    /// <summary>The elements whose keys' letters a clip shows, in the order a picture's letters are given.</summary>
    public static readonly IReadOnlyList<Elements> LetterOrder =
        [Elements.Water, Elements.Life, Elements.Shield, Elements.Cold, Elements.Lightning, Elements.Arcane, Elements.Earth, Elements.Fire];

    private static readonly Rectangle Mouse = new(432, 0, 80, 64), MouseLeft = new(432, 64, 80, 64), MouseMiddle = new(432, 128, 80, 64),
        MouseRight = new(432, 192, 80, 64), MouseWheel = new(352, 192, 80, 64), Cap = new(384, 96, 48, 48), CapLit = new(336, 96, 48, 48),
        Shift = new(368, 0, 64, 48), ShiftLit = new(368, 48, 64, 48), Plus = new(192, 0, 32, 32), Space = new(240, 0, 128, 48), SpaceLit = new(240, 48, 128, 48),
        BlockKey = new(368, 144, 64, 48), BlockKeyLit = new(304, 144, 64, 48),                // ours: 64 wide, not the original's 72 that takes a sliver of the mouse
        Face = new(0, 64, 64, 64), FaceA = new(0, 128, 64, 64), FaceB = new(64, 128, 64, 64), FaceX = new(0, 192, 64, 64), FaceY = new(64, 192, 64, 64),
        Pad = new(64, 64, 64, 64), PadLit = new(128, 64, 64, 64), Stick = new(64, 0, 64, 64), Well = new(128, 0, 64, 64),
        LeftTrigger = new(128, 128, 64, 36), LeftTriggerLit = new(128, 192, 64, 36), LeftBumper = new(128, 164, 64, 28),
        RightTrigger = new(192, 128, 64, 36), RightTriggerLit = new(192, 192, 64, 36), RightBumper = new(192, 164, 64, 28), RightBumperLit = new(192, 228, 64, 28);

    private const HintAnimations Conjures = HintAnimations.ConjureWater | HintAnimations.ConjureLife | HintAnimations.ConjureShield | HintAnimations.ConjureCold
        | HintAnimations.ConjureLightning | HintAnimations.ConjureArcane | HintAnimations.ConjureEarth | HintAnimations.ConjureFire;

    /// <summary>A flag's time in its set.</summary>
    private static float Time(HintAnimations flag, bool pad) => flag switch
    {
        HintAnimations.Interact or HintAnimations.Block => 1,
        HintAnimations.Attack => pad ? 1 : 1.5f,
        HintAnimations.Conjure => pad ? 2 : 1.5f,
        _ when (flag & Conjures) != 0 => pad ? 5 : 2,
        HintAnimations.None => 0,
        _ => 1.5f,
    };

    /// <summary>The clip's whole loop: its flags' times together.</summary>
    public static float Duration(HintAnimations clip, bool pad)
    {
        var sum = 0f;
        for (var bit = 1; bit <= (int)HintAnimations.Attack; bit <<= 1)
        {
            sum += ((int)clip & bit) != 0 ? Time((HintAnimations)bit, pad) : 0;
        }
        return sum;
    }

    /// <summary>
    /// The pictures of a clip at a time (from its hint's setting), for a set; <paramref name="letters"/> the keys'
    /// letters in <see cref="LetterOrder"/>.
    /// </summary>
    public static void Pieces(HintAnimations clip, float time, bool pad, string letters, List<ClipPiece> into)
    {
        ArgumentNullException.ThrowIfNull(letters);
        ArgumentNullException.ThrowIfNull(into);
        var loop = Duration(clip, pad);
        if (!(loop > 0) || !float.IsFinite(time))
        {
            return;
        }
        var u = time % loop;
        for (var bit = 1; bit <= (int)HintAnimations.Attack; bit <<= 1)
        {
            var flag = (HintAnimations)bit;
            if ((clip & flag) == 0)
            {
                continue;
            }
            var length = Time(flag, pad);
            if (u < length)
            {
                if (pad)
                {
                    PadPieces(flag, u, into);
                }
                else
                {
                    KeyPieces(flag, u, letters, into);
                }
                return;
            }
            u -= length;
        }
    }

    private static char Letter(string letters, Elements element)
    {
        for (var i = 0; i < LetterOrder.Count && i < letters.Length; i++)
        {
            if (LetterOrder[i] == element)
            {
                return letters[i];
            }
        }
        return ' ';
    }

    private static void KeyPieces(HintAnimations flag, float t, string letters, List<ClipPiece> into)
    {
        switch (flag)
        {
            case HintAnimations.Interact:
                into.Add(new ClipPiece(t < 0.5f ? Mouse : MouseLeft, Vector2.Zero));
                break;
            case HintAnimations.NavMagick:
                into.Add(new ClipPiece(MouseWheel, Vector2.Zero));
                break;
            case HintAnimations.Conjure:                                      // the Haste combo's keys
                into.Add(new ClipPiece(Cap, new Vector2(-32, 0), Letter(letters, Elements.Lightning)));
                into.Add(new ClipPiece(Cap, Vector2.Zero, Letter(letters, Elements.Arcane)));
                into.Add(new ClipPiece(Cap, new Vector2(32, 0), Letter(letters, Elements.Fire)));
                break;
            case HintAnimations.CastForce:
                into.Add(new ClipPiece(t < 1 ? Mouse : MouseRight, Vector2.Zero));
                break;
            case HintAnimations.CastSelf:
                into.Add(new ClipPiece(t < 1 ? Mouse : MouseMiddle, Vector2.Zero));
                break;
            case HintAnimations.CastArea or HintAnimations.Attack:
                into.Add(new ClipPiece(t < 0.5f ? Shift : ShiftLit, new Vector2(-48, 0)));
                into.Add(new ClipPiece(Plus, new Vector2(-12, 0)));
                into.Add(new ClipPiece(t < 1 ? Mouse : flag == HintAnimations.Attack ? MouseLeft : MouseRight, new Vector2(32, 0)));
                break;
            case HintAnimations.CastMagick:
                into.Add(new ClipPiece(t < 1 ? Space : SpaceLit, Vector2.Zero));
                break;
            case HintAnimations.Block:
                into.Add(new ClipPiece(t < 0.5f ? BlockKey : BlockKeyLit, Vector2.Zero));
                break;
            default:                                                          // an element's key, lit at the third of its time
                into.Add(new ClipPiece(t < 4 / 3f ? Cap : CapLit, Vector2.Zero, Letter(letters, ElementOf(flag))));
                break;
        }
    }

    private static void PadPieces(HintAnimations flag, float t, List<ClipPiece> into)
    {
        switch (flag)
        {
            case HintAnimations.Interact:
                into.Add(new ClipPiece(t < 0.5f ? Face : FaceX, Vector2.Zero));
                break;
            case HintAnimations.NavMagick:
                into.Add(new ClipPiece(t < 0.5f ? Pad : PadLit, Vector2.Zero));
                break;
            case HintAnimations.Conjure:
                into.Add(new ClipPiece(Stick, Vector2.Zero));
                break;
            case HintAnimations.CastForce:
                into.Add(new ClipPiece(t < 0.5f ? RightTrigger : RightTriggerLit, new Vector2(0, -16)));
                into.Add(new ClipPiece(RightBumper, new Vector2(0, 16)));
                break;
            case HintAnimations.CastArea:
                into.Add(new ClipPiece(t < 0.5f ? LeftTrigger : LeftTriggerLit, new Vector2(0, -16)));
                into.Add(new ClipPiece(LeftBumper, new Vector2(0, 16)));
                break;
            case HintAnimations.CastSelf:
                into.Add(new ClipPiece(t < 0.5f ? Face : FaceY, Vector2.Zero));
                break;
            case HintAnimations.CastMagick:
                into.Add(new ClipPiece(t < 0.5f ? Face : FaceB, Vector2.Zero));
                break;
            case HintAnimations.Attack:
                into.Add(new ClipPiece(t < 0.5f ? Face : FaceA, Vector2.Zero));
                break;
            case HintAnimations.Block:
                into.Add(new ClipPiece(t < 0.5f ? RightBumper : RightBumperLit, new Vector2(0, 16)));
                into.Add(new ClipPiece(RightTrigger, new Vector2(0, -16)));
                break;
            default:
                into.Add(new ClipPiece(Well, Vector2.Zero));
                into.Add(new ClipPiece(Stick, StickAt(ElementOf(flag), t)));
                break;
        }
    }

    /// <summary>
    /// Where the pad's stick is for an element at a time of its 5 s, as the original's: centred for a second, out
    /// toward the element's first way over the next, held, round a quarter turn to its second, held. The ways are the
    /// spell wheel's (screen y down).
    /// </summary>
    public static Vector2 StickAt(Elements element, float t)
    {
        var (first, second) = Ways(element);
        return t switch
        {
            <= 1 => Vector2.Zero,
            < 2 => first * StickReach * (t - 1),
            < 3 => first * StickReach,
            < 4 => StickReach * (first * MathF.Cos((t - 3) * MathF.PI / 2) + second * MathF.Sin((t - 3) * MathF.PI / 2)),
            _ => second * StickReach,
        };
    }

    private static (Vector2 First, Vector2 Second) Ways(Elements element)
    {
        Vector2 up = new(0, -1), down = new(0, 1), left = new(-1, 0), right = new(1, 0);
        return element switch
        {
            Elements.Life => (up, left),
            Elements.Arcane => (up, right),
            Elements.Shield => (down, right),
            Elements.Earth => (down, left),
            Elements.Water => (left, up),
            Elements.Lightning => (left, down),
            Elements.Cold => (right, up),
            _ => (right, down),                                               // fire
        };
    }

    private static Elements ElementOf(HintAnimations flag) => flag switch
    {
        HintAnimations.ConjureWater => Elements.Water,
        HintAnimations.ConjureLife => Elements.Life,
        HintAnimations.ConjureShield => Elements.Shield,
        HintAnimations.ConjureCold => Elements.Cold,
        HintAnimations.ConjureLightning => Elements.Lightning,
        HintAnimations.ConjureArcane => Elements.Arcane,
        HintAnimations.ConjureEarth => Elements.Earth,
        _ => Elements.Fire,
    };

    /// <summary>The letters of the keys bound to the elements, in <see cref="LetterOrder"/>.</summary>
    public static string Letters(ControlBindings bindings, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return new string([.. LetterOrder.Select(e => Letter(bindings[Enum.Parse<KeyboardBindings>(e.ToString())], text))]);
    }

    /// <summary>
    /// A key's letter on a clip's key cap, as the original's: none bound a space; a mouse button its name's first letter;
    /// a letter, digit, number pad or punctuation key what it types (ours: on a US layout); any other key its name's
    /// first letter (Space "S"). A mouse button's name comes as the language writes it.
    /// </summary>
    public static char Letter(ControlBinding binding, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (binding.IsMouse)
        {
            var name = text(binding.Mouse switch
            {
                MouseButton.Left => "#mouse_left",
                MouseButton.Middle => "#mouse_middle",
                MouseButton.Right => "#mouse_right",
                MouseButton.X1 => "#mouse_4",
                MouseButton.X2 => "#mouse_5",
                MouseButton.ScrollUp => "#mouse_scroll_up",
                _ => "#mouse_scroll_down",
            });
            return name.Length > 0 ? name[0] : ' ';
        }
        var key = binding.Key;
        return key switch
        {
            Keys.None => ' ',
            >= Keys.A and <= Keys.Z => (char)('A' + (key - Keys.A)),
            >= Keys.D0 and <= Keys.D9 => (char)('0' + (key - Keys.D0)),
            >= Keys.NumPad0 and <= Keys.NumPad9 => (char)('0' + (key - Keys.NumPad0)),
            Keys.Multiply => '*',
            Keys.Add => '+',
            Keys.Separator => ',',
            Keys.Subtract => '-',
            Keys.Decimal => '.',
            Keys.Divide => '/',
            Keys.OemSemicolon => ';',
            Keys.OemPlus => '=',
            Keys.OemComma => ',',
            Keys.OemMinus => '-',
            Keys.OemPeriod => '.',
            Keys.OemQuestion => '/',
            Keys.OemTilde => '`',
            Keys.OemOpenBrackets => '[',
            Keys.OemPipe or Keys.OemBackslash => '\\',
            Keys.OemCloseBrackets => ']',
            Keys.OemQuotes => '\'',
            _ => key.ToString()[0],
        };
    }
}
