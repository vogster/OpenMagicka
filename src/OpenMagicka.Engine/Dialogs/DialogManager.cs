using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Engine.Text;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Engine.Dialogs;

/// <summary>
/// The level's dialogs at play, as the original's dialog manager (docs/engine/ARCHITECTURE.md §18, LEVELS_TRIGGERS.md
/// §11): up to 8 shown at once, each in a slot with its speech box; one started in a cutscene plays in the cutscene's
/// box at the bottom instead. A start takes the dialog's next interact and shows its first line; a press of the player
/// who drives it (or a line's time running out) goes on; the dialog is finished when an interact plays to its end.
/// A <c>lock</c> freezes the world's physics and holds every player's input till its interact ends. Logic thread.
/// </summary>
public sealed partial class DialogManager : IScriptDialogs
{
    public const int Slots = 8;

    /// <summary>After a start or a press that ends an interact, presses wait this long.</summary>
    public const float Holdoff = 0.2f;

    private readonly DialogCollection dialogs;
    private readonly StringTable strings;
    private readonly BitmapFont wrapping;
    private readonly GameWorld world;
    private readonly ScriptStage stage;
    private readonly Func<bool> inCutscene;
    private readonly Dictionary<int, DialogRun> runs = [];
    private readonly InteractRun?[] slots = new InteractRun?[Slots];
    private readonly SpeechBox[] boxes = [.. Enumerable.Range(0, Slots).Select(_ => new SpeechBox())];
    private int cutsceneSlot = -1;
    private float holdoff;

    /// <param name="wrapping">The font lines are wrapped and boxes sized with (the original's Maiandra16).</param>
    /// <param name="inCutscene">Whether the level is in a cutscene now (its bars in, or coming in).</param>
    public DialogManager(DialogCollection dialogs, StringTable strings, BitmapFont wrapping, GameWorld world, ScriptStage stage, Func<bool> inCutscene)
    {
        (this.dialogs, this.strings, this.wrapping, this.world, this.stage, this.inCutscene) = (dialogs ?? throw new ArgumentNullException(nameof(dialogs)),
            strings ?? throw new ArgumentNullException(nameof(strings)), wrapping ?? throw new ArgumentNullException(nameof(wrapping)),
            world ?? throw new ArgumentNullException(nameof(world)), stage ?? throw new ArgumentNullException(nameof(stage)),
            inCutscene ?? throw new ArgumentNullException(nameof(inCutscene)));
    }

    /// <summary>The slots' speech boxes.</summary>
    public IReadOnlyList<SpeechBox> Boxes => boxes;

    /// <summary>The cutscene's box at the bottom of the screen.</summary>
    public SpeechBox CutsceneBox { get; } = new();

    /// <summary>Whether the cutscene's box is drawn (a dialog was started in the cutscene and plays on).</summary>
    public bool ShowsCutsceneBox { get; private set; }

    /// <summary>Menus' message boxes: none here.</summary>
    public bool MessageBoxActive => false;

    /// <summary>Presses wait (just after a start or an ending press).</summary>
    public bool IsAwaitingInput => holdoff > 0;

    public bool IsDialogActive => slots.Any(s => s is not null);

    /// <summary>Whether the world's physics stands still for a locking interact.</summary>
    public bool IsFrozen { get; private set; }

    public bool IsActive(int dialog) => slots.Any(s => s?.Dialog.Definition.Key == dialog);

    /// <summary>Finished, and more than that many interacts taken (−1: finished at all); an unknown dialog is not.</summary>
    public bool IsDone(int dialog, int interactIndex) => runs.TryGetValue(dialog, out var run) && run.IsFinished && run.Interacted > interactIndex;

    /// <summary>The player drives one of the shown interacts (its presses go to the dialog).</summary>
    public bool CanAdvance(Player player) => slots.Any(s => s is not null && ReferenceEquals(s.Interactor, player));

    /// <summary>The player's own input is held: an interact they drive is shown.</summary>
    public bool Holds(Player player) => CanAdvance(player);

    /// <summary>A dialog's state, made on first use; none for one the level lacks.</summary>
    public DialogRun? Run(int dialog)
    {
        if (!runs.TryGetValue(dialog, out var run) && dialogs.TryGet(dialog, out var definition))
        {
            runs[dialog] = run = new DialogRun(definition);
        }
        return run;
    }

    /// <summary>
    /// Starts a dialog at a place, driven by a player or by time alone: ignored while it is shown, or with no interact
    /// left. It takes a free slot (a box still shutting is not free); with all 8 taken the first is ended for it (ours:
    /// the original overwrites it as it stands). Started in a cutscene, it takes the cutscene's box: one playing there
    /// is played through for it (ours: the original leaves it in its slot, where it never ends).
    /// </summary>
    public void Start(int dialog, DialogAnchor where, Player? interactor = null)
    {
        if (IsActive(dialog) || Run(dialog)?.Pop() is not { } interact)
        {
            return;
        }
        if (inCutscene() && ShowsCutsceneBox && cutsceneSlot >= 0 && slots[cutsceneSlot] is { } playing)
        {
            End(playing, CutsceneBox);
            Complete(cutsceneSlot, playing);
        }
        var slot = Enumerable.Range(0, Slots).FirstOrDefault(i => slots[i] is null && !boxes[i].IsVisible, -1);
        if (slot < 0)
        {
            slot = Math.Max(0, Array.FindIndex(slots, s => s is not null));
            Drop(slot, finished: false);
            boxes[slot].Snap();
        }
        slots[slot] = interact;
        Face(interact, where, interactor);
        if (inCutscene())
        {
            (cutsceneSlot, ShowsCutsceneBox) = (slot, true);
        }
        else
        {
            ShowsCutsceneBox = false;
        }
        Begin(interact, BoxOf(slot), where, interactor);
        holdoff = Holdoff;
        if (interact.Definition.Lock)
        {
            IsFrozen = true;
            stage.LimitInput(this);
        }
    }

    /// <summary>A press of a player: an animating box of theirs finishes; else their interact goes on, and ends when played through.</summary>
    public void Advance(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (inCutscene() || holdoff > 0)
        {
            return;
        }
        for (var slot = 0; slot < Slots; slot++)
        {
            if (slots[slot] is not { } interact || !ReferenceEquals(interact.Interactor, player))
            {
                continue;
            }
            var box = BoxOf(slot);
            if (box.IsAnimating)
            {
                box.Finish();
            }
            else if (Step(interact, box))
            {
                Complete(slot, interact);
                holdoff = Holdoff;
            }
        }
    }

    /// <summary>Every dialog ends (a cutscene's start or end, a checkpoint): its boxes shut, the world thaws, the input frees. None is finished.</summary>
    public void EndAll()
    {
        for (var slot = 0; slot < Slots; slot++)
        {
            if (slots[slot] is { } interact)
            {
                End(interact, BoxOf(slot));
                slots[slot] = null;
            }
            boxes[slot].Snap();
            boxes[slot].Hide();
        }
        CutsceneBox.Hide();
        CutsceneBox.Snap();
        (ShowsCutsceneBox, cutsceneSlot, IsFrozen) = (false, -1, false);
        stage.UnlimitInput(this);
    }

    /// <summary>A dialog ends where it is shown (its speaker died); finished when <paramref name="finished"/>.</summary>
    public void End(int dialog, bool finished = false)
    {
        for (var slot = 0; slot < Slots; slot++)
        {
            if (slots[slot]?.Dialog.Definition.Key == dialog)
            {
                Drop(slot, finished);
            }
        }
    }

    /// <summary>
    /// A step: presses wait out their holdoff; each box opens, types and counts its line's time (one run out goes on by
    /// itself); a box on a dead speaker shuts, its dialog ended (not finished); the cutscene box steps in a cutscene.
    /// </summary>
    public void Update(float dt)
    {
        holdoff = MathF.Max(0, holdoff - dt);
        for (var slot = 0; slot < Slots; slot++)
        {
            var inScene = slot == cutsceneSlot && ShowsCutsceneBox;
            var box = BoxOf(slot);
            if (!inScene && box.Update(dt) && slots[slot] is { } timed && Step(timed, box))
            {
                Complete(slot, timed);
            }
            if (box.IsVisible && slots[slot] is { } shown && box.Anchor.Owner is { } owner && (owner.IsDead || owner.Manager is null))
            {
                Drop(slot, owner is Character { Dialog: var own } && own == shown.Dialog.Definition.Key);   // its owner died talking: done
            }
        }
        if (ShowsCutsceneBox && CutsceneBox.Update(dt) && cutsceneSlot >= 0 && slots[cutsceneSlot] is { } scene && Step(scene, CutsceneBox))
        {
            Complete(cutsceneSlot, scene);
            (ShowsCutsceneBox, cutsceneSlot) = (false, -1);
        }
    }

    /// <summary>A slot's interact ends where it stands: its box shut, its lock lifted (ours: the original leaves a lock on), finished when said.</summary>
    private void Drop(int slot, bool finished)
    {
        if (slots[slot] is not { } interact)
        {
            return;
        }
        var box = BoxOf(slot);
        End(interact, box);
        box.Hide();
        slots[slot] = null;
        interact.Dialog.IsFinished |= finished;
        Unlock(interact);
        if (slot == cutsceneSlot)
        {
            (ShowsCutsceneBox, cutsceneSlot) = (false, -1);
        }
    }

    /// <summary>
    /// A player talks to a character: it turns to them when the line it starts with says so (only on this path, as the
    /// original's), and the talk starts, the player driving it. Ignored while presses wait.
    /// </summary>
    public void Talk(Character speaker, Player player)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(player);
        if (!IsAwaitingInput && speaker is { IsDead: false, Dialog: not 0 })
        {
            Start(speaker.Dialog, new DialogAnchor(speaker), player);
        }
    }

    /// <summary>A character a player started talking to turns to them when its message says so.</summary>
    private static void Face(InteractRun interact, DialogAnchor where, Player? interactor)
    {
        if (interactor?.Avatar is not { } avatar || where.Owner is not Character { IsDead: false } speaker || interact.Definition.Messages.Count == 0
            || !interact.Definition.Messages[Math.Min(interact.Current, interact.Definition.Messages.Count - 1)].Facing)
        {
            return;
        }
        var way = (avatar.Position - speaker.Position) with { Y = 0 };
        if (way.LengthSquared() > 1e-6f)
        {
            speaker.Body.WantedDirection = System.Numerics.Vector3.Normalize(way);
        }
    }

    private SpeechBox BoxOf(int slot) => slot == cutsceneSlot && ShowsCutsceneBox ? CutsceneBox : boxes[slot];

    /// <summary>An interact played through: its dialog finished, its lock lifted, its slot emptied.</summary>
    private void Complete(int slot, InteractRun interact)
    {
        interact.Dialog.IsFinished = true;
        Unlock(interact);
        slots[slot] = null;
    }

    private void Unlock(InteractRun interact)
    {
        if (interact.Definition.Lock && !slots.Any(s => s is not null && !ReferenceEquals(s, interact) && s.Definition.Lock))
        {
            IsFrozen = false;
            stage.UnlimitInput(this);
        }
    }
}
