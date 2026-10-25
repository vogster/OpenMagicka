using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Engine.Dialogs;

/// <summary>
/// The interacts and messages at play, as the original's (docs/engine/ARCHITECTURE.md §18): each step shows a message's
/// next line, or, past its last, ends it and shows the next message's first. The speaker is found anew for each line;
/// one dead or gone skips its message.
/// </summary>
public sealed partial class DialogManager
{
    /// <summary>A focused speaker draws the camera over this long, this close.</summary>
    public const float FocusTime = 2, FocusMagnification = 1.5f;

    /// <summary>A speaker's clips blend in this long.</summary>
    public const float AnimationBlend = 0.3f;

    private readonly Dictionary<(DialogMessage, DialogLine), string> texts = [];
    private readonly Dictionary<DialogMessage, (int, int)> minSizes = [];
    private int nextCue;

    /// <summary>An interact begins at a place (its first line shown), driven by a player if one started it.</summary>
    private void Begin(InteractRun interact, SpeechBox box, DialogAnchor where, Player? interactor)
    {
        interact.Anchor = where;
        if (interact.Interactor is null && interactor is not null)
        {
            interact.Interactor = interactor;
        }
        Step(interact, box);
    }

    /// <summary>The interact goes on; true when it has played through (its counter up, its interactor let go).</summary>
    private bool Step(InteractRun interact, SpeechBox box)
    {
        var messages = interact.Definition.Messages;
        if ((interact.Definition.Count > 0 && interact.RepeatCounter >= interact.Definition.Count) || messages.Count == 0)
        {
            return true;
        }
        while (ShowNext(interact, interact.Current, box))                     // a message ended (or was skipped): the next one's first line
        {
            interact.Current++;
            if (interact.Current >= messages.Count)
            {
                (interact.RepeatCounter, interact.Current, interact.Interactor) = (interact.RepeatCounter + 1, 0, null);
                return true;
            }
        }
        return false;
    }

    /// <summary>A message's next line shown; true when it had none left (it ended: its box shut, its speaker idle, the camera let go).</summary>
    private bool ShowNext(InteractRun interact, int message, SpeechBox box)
    {
        var definition = interact.Definition.Messages[message];
        var speaker = Speaker(interact, definition);
        if (interact.Anchor.Owner is not null && speaker is null or { IsDead: true })
        {
            interact.Lines[message] = 0;                                      // ours: the original leaves the line where it was
            return true;                                                      // a speaker dead or gone skips their message
        }
        StopCue(interact, message);
        var lines = definition.Lines;
        if (interact.Lines[message] >= lines.Count)
        {
            interact.Lines[message] = 0;
            box.Hide();
            if (interact.Definition.FocusOwner && speaker is not null)
            {
                stage.Camera = stage.Camera.Release(FocusTime);
            }
            if (lines.Count > 0 && lines[^1].Animation != Content.Magicka.Animation.None && speaker is Character { IsDead: false } talker)
            {
                talker.GoToAnimation(talker.SpecialIdleAnimation ?? "idle", AnimationBlend);
            }
            return true;
        }
        var line = lines[interact.Lines[message]++];
        if (speaker is Character { IsDead: false } character && line.Animation != Content.Magicka.Animation.None)
        {
            character.GoToAnimation(line.Animation.ToString(), AnimationBlend);
        }
        if (line.Sound is { Length: > 0 } cue)
        {
            interact.Cues[message] = ++nextCue;
            stage.Request(new PlaySoundRequest(-nextCue, line.Bank, cue, null, speaker?.Position, 0, speaker is not null));
        }
        if (interact.Definition.FocusOwner && speaker is not null)
        {
            var at = speaker.Position;
            stage.Camera = stage.Camera.MoveTo(new Microsoft.Xna.Framework.Vector3(at.X, at.Y, at.Z), FocusTime) with { TargetMagnification = FocusMagnification };
        }
        var place = speaker is not null && interact.Anchor.Owner is null ? new DialogAnchor(speaker) : interact.Anchor;
        var nameId = place.Owner is { UniqueId: not 0 } named ? named.UniqueId : Name(definition);
        box.Fill(Text(definition, line), strings.TryGetRaw(nameId, out var name) ? name : null, MinSize(definition), definition.ForceOnScreen, place, line.Ttl);
        return false;
    }

    /// <summary>The message's speaker: the owner for "owner", else the one of that name (by a point: a character only).</summary>
    private Entity? Speaker(InteractRun interact, DialogMessage message)
    {
        if (interact.Anchor.OnScreen is not null)
        {
            return null;                                                      // on the screen: no speaker is moved, the cue plays flat
        }
        if (message.Character is null or "owner")
        {
            return message.Character is null ? null : interact.Anchor.Owner;
        }
        var found = world.Entities.FindByUniqueId(Name(message));
        return interact.Anchor.Owner is null && found is not Character ? null : found;
    }

    private static int Name(DialogMessage message) => message.Character is { } name ? Content.LegacyStringHash.Lower(name) : 0;

    /// <summary>A message's sound stops (as authored: its tail plays).</summary>
    private void StopCue(InteractRun interact, int message)
    {
        if (interact.Cues[message] != 0)
        {
            stage.Request(new StopSoundRequest(-interact.Cues[message], Instant: false));
            interact.Cues[message] = 0;
        }
    }

    /// <summary>An interact ends where it stands (its message's sound cut, its player let go); its place in its messages is kept.</summary>
    private void End(InteractRun interact, SpeechBox box)
    {
        for (var message = 0; message < interact.Cues.Length; message++)
        {
            if (interact.Cues[message] != 0)
            {
                stage.Request(new StopSoundRequest(-interact.Cues[message], Instant: true));
                interact.Cues[message] = 0;
            }
        }
        interact.Interactor = null;
    }

    /// <summary>A line's text: resolved, then wrapped to its message's width (both once).</summary>
    public string Text(DialogMessage message, DialogLine line)
    {
        if (!texts.TryGetValue((message, line), out var text))
        {
            texts[(message, line)] = text = wrapping.Wrap(DialogText.Resolve(line, strings), message.Width);
        }
        return text;
    }

    /// <summary>The smallest box all of a message's lines fit.</summary>
    public (int Width, int Height) MinSize(DialogMessage message)
    {
        if (!minSizes.TryGetValue(message, out var size))
        {
            minSizes[message] = size = message.Lines.Select(l => wrapping.Measure(Text(message, l)))
                .Aggregate((0, 0), (most, s) => (Math.Max(most.Item1, s.Width), Math.Max(most.Item2, s.Height)));
        }
        return size;
    }
}
