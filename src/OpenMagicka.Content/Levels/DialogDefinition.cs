using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Content.Levels;

/// <summary>The kind of a dialog message; the game has one.</summary>
public enum MessageType
{
    Say,
}

/// <summary>
/// One line of a message: a localization key (lowered) with optional arguments, keys that fill
/// its <c>#1;</c>, <c>#2;</c> placeholders; a sound cue (lowered, null for none) from a bank; the
/// speaker's animation; and how long it stays (-1: until the player continues).
/// </summary>
public sealed record DialogLine(string Key, IReadOnlyList<string> Arguments, SoundBanks Bank, string? Sound, Animation Animation, float Ttl)
{
    private readonly IReadOnlyList<string> arguments = ReadOnly.List(Arguments);

    public IReadOnlyList<string> Arguments { get => arguments; init => arguments = ReadOnly.List(value); }
}

/// <summary>A text window: who speaks (lowered id, "owner" for the dialog's owner) and its lines.</summary>
public sealed record DialogMessage(string? Character, MessageType Type, bool Facing, bool ForceOnScreen, int Width, IReadOnlyList<DialogLine> Lines)
{
    private readonly IReadOnlyList<DialogLine> lines = ReadOnly.List(Lines);

    public IReadOnlyList<DialogLine> Lines { get => lines; init => lines = ReadOnly.List(value); }
}

/// <summary>
/// What one start of the dialog shows: its messages in turn. Count is how many starts show it
/// before the next interact (≤ 0: forever); IconText is the dialog's.
/// </summary>
public sealed record DialogInteract(bool Lock, bool FocusOwner, int Count, InteractType IconText, IReadOnlyList<DialogMessage> Messages)
{
    private readonly IReadOnlyList<DialogMessage> messages = ReadOnly.List(Messages);

    public IReadOnlyList<DialogMessage> Messages { get => messages; init => messages = ReadOnly.List(value); }
}

/// <summary>
/// A dialog (docs/formats/LEVELS_TRIGGERS.md §11). Id is lowered (null when absent) and Key is its
/// hash (0 without an id). After the last interact the dialog goes back to RepeatFrom; a negative
/// RepeatFrom ends it.
/// </summary>
public sealed record DialogDefinition(string? Id, int Key, InteractType IconText, int RepeatFrom, IReadOnlyList<DialogInteract> Interacts)
{
    private readonly IReadOnlyList<DialogInteract> interacts = ReadOnly.List(Interacts);

    public IReadOnlyList<DialogInteract> Interacts { get => interacts; init => interacts = ReadOnly.List(value); }
}
