using System.Numerics;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Input;

/// <summary>Where the cursor points this tick: its ray from the eye into the level, and where that crosses the avatar's height.</summary>
public readonly record struct CursorPick(Segment Ray, Vector3 Ground);

/// <summary>
/// A keyboard-and-mouse player's controls on its avatar each tick (docs/engine/ARCHITECTURE.md §9,
/// ENTITIES_AI.md §6.1), as the original's:
/// <list type="bullet">
/// <item>The avatar faces the cursor on the ground at its height, and walks there while Move (the left
/// button) is held. A press on something it can use (an item or a book to pick up, a character to talk
/// to, a point of interest) uses it when in reach, or walks to it and uses it once there.</item>
/// <item>Cast (the right button) casts Force, or Area with Shift; Move with Shift attacks (a Weapon cast
/// with elements queued, else a melee swing; a held gun or charged weapon keeps attacking). CastSelf
/// (the middle button) casts on oneself, or with no elements uses the staff's special ability.</item>
/// <item>Boost casts the magick or boosts; Block blocks while held; the wheel picks the previous or
/// next magick the players may cast; Inventory waits for the HUD (OM-54).</item>
/// <item>An element key gives its element (its pair of directions through the player's spell tree)
/// once its cooldown is over, and not while the tutorial forbids it.</item>
/// <item>While the stage holds input (a cutscene, a script) the avatar only stops; the tutorial's cast
/// types and push locks hold.</item>
/// </list>
/// The original warps the cursor to stay on a target pressed; ours keeps aiming at the target.
/// </summary>
public sealed class KeyboardMouseControl(ControlBindings bindings)
{
    private readonly ControlBindings bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
    private readonly ElementCooldowns cooldowns = new();
    private object? pressedTarget, lockedTarget;
    private bool interactLock, walking;

    /// <summary>What a press marked is forgotten, the walk stops (a scene change, a checkpoint's restart, a hold).</summary>
    public void Reset() => (interactLock, walking, pressedTarget, lockedTarget) = (false, false, null, null);

    /// <summary>The player's directions toward an element (its spell tree).</summary>
    public SpellCombos Combos { get; } = new();

    /// <summary>What the cursor points at (a character to talk to, an item, a point of interest), as of the last update.</summary>
    public object? Hovered { get; private set; }

    public void Update(ControlFrame input, Player player, ScriptScene scene, CursorPick cursor, float dt)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(scene);
        var tutorial = scene.Game.Tutorial;
        if (!scene.Game.InCutscene)
        {
            cooldowns.Update(dt);
        }
        if (player.Avatar is not { IsDead: false } avatar)
        {
            return;
        }
        if (scene.Stage.IsInputLimited || scene.Game.InCutscene)
        {
            ControlTargets.LetGo(avatar);                                     // held: it stands, its hands off
            Reset();
            Hovered = null;
            return;
        }
        var shift = input.IsDown(bindings[KeyboardBindings.Shift]);
        walking &= !shift;                                                    // an attack stands
        var target = shift ? null : lockedTarget ?? ControlTargets.UnderCursor(avatar, scene, cursor);
        Hovered = shift ? null : ControlTargets.UnderCursor(avatar, scene, cursor);
        var inReach = target is not null && ControlTargets.InReach(avatar, scene, target);
        var aim = (lockedTarget is null ? cursor.Ground : ControlTargets.Where(scene, lockedTarget)) - avatar.Position;   // a target that moves is followed
        avatar.Steer(aim, walking);
        var move = bindings[KeyboardBindings.Move];
        if (!input.IsDown(move))
        {
            (interactLock, walking, pressedTarget, lockedTarget) = (false, false, null, null);
        }
        if (!scene.Game.InCutscene)
        {
            if (shift)
            {
                Casts(avatar, input, tutorial, area: true);
                if (input.IsPressed(move) || (input.IsDown(move) && (avatar.IsWieldingGun || avatar.ItemIn(0)?.IsSpellCharged == true)))
                {
                    avatar.Attack(!tutorial.DisabledCastTypes.Contains(CastType.Weapon));
                }
                else if (input.IsReleased(move))
                {
                    avatar.ReleaseAttack();
                }
            }
            else
            {
                Casts(avatar, input, tutorial, area: false);
                if (avatar.Attacking)
                {
                    avatar.ReleaseAttack();
                }
                Walk(avatar, scene, input, target, inReach);
            }
        }
        var self = bindings[KeyboardBindings.CastSelf];
        if (input.IsPressed(self) && (avatar.SpellQueue.Count == 0 || !tutorial.DisabledCastTypes.Contains(CastType.Self)))
        {
            avatar.PressCast(CastType.Self);
        }
        else if (input.IsReleased(self))
        {
            avatar.ReleaseCast(CastType.Self);
        }
        PickMagick(player, avatar, input);
        if (input.IsPressed(bindings[KeyboardBindings.Boost]))
        {
            avatar.Boost();
        }
        avatar.IsBlocking = input.IsDown(bindings[KeyboardBindings.Block]);
        ConjureKeys(avatar, input, tutorial.DisabledElements);
    }

    /// <summary>The cast button: Area with Shift (Force let go), else Force (Area let go); each as the tutorial allows.</summary>
    private void Casts(Character avatar, ControlFrame input, ScriptTutorial tutorial, bool area)
    {
        var (held, other) = area ? (CastType.Area, CastType.Force) : (CastType.Force, CastType.Area);
        avatar.ReleaseCast(other);
        if (input.IsDown(bindings[KeyboardBindings.Cast]))
        {
            if (!tutorial.DisabledCastTypes.Contains(held) && (avatar.SpellQueue.Count > 0 || !tutorial.IsPushDisabled))
            {
                avatar.PressCast(held);
            }
        }
        else
        {
            avatar.ReleaseCast(held);
        }
    }

    /// <summary>Move held: a press uses what is under the cursor when in reach, else marks it; held, it walks, and uses the mark once there.</summary>
    private void Walk(Character avatar, ScriptScene scene, ControlFrame input, object? target, bool inReach)
    {
        var move = bindings[KeyboardBindings.Move];
        if (input.IsPressed(move))
        {
            if (inReach)
            {
                ControlTargets.Use(avatar, scene, target!);
                interactLock = true;
            }
            else
            {
                (pressedTarget, lockedTarget) = (target, target);
            }
        }
        else if (input.IsDown(move) && !interactLock)
        {
            walking = true;
            if (pressedTarget is not null && inReach)
            {
                ControlTargets.Use(avatar, scene, pressedTarget);
                (pressedTarget, lockedTarget) = (null, null);
            }
        }
    }

    /// <summary>The wheel: the previous or next magick the players may cast (round past none), as the original's book.</summary>
    private void PickMagick(Player player, Character avatar, ControlFrame input)
    {
        var step = input.IsPressed(bindings[KeyboardBindings.PrevMagick]) ? -1 : input.IsPressed(bindings[KeyboardBindings.NextMagick]) ? 1 : 0;
        if (step != 0)
        {
            player.SelectedMagick = GamePadControl.NextMagick(player.SelectedMagick, step, avatar.MayCast);
        }
    }

    /// <summary>The element keys: a pressed one whose cooldown is over gives its pair of directions, then cools down.</summary>
    private void ConjureKeys(Character avatar, ControlFrame input, Gameplay.Spells.Elements disabled)
    {
        foreach (var (element, function) in ElementKeys)
        {
            if (!input.IsPressed(bindings[function]) || !cooldowns.CanCast(element, disabled))
            {
                continue;
            }
            var (first, second) = SpellCombos.Pairs[element];
            foreach (var direction in new[] { first, second })
            {
                if (!avatar.IsInEvent && avatar.State != Gameplay.Characters.States.CharacterStates.Charge
                    && Combos.Give(direction, e => (e & disabled) == 0) is var given and not Gameplay.Spells.Elements.None)
                {
                    avatar.Conjure(given);
                }
            }
            cooldowns.Start(element);
        }
    }

    private static readonly (Gameplay.Spells.Elements Element, KeyboardBindings Function)[] ElementKeys =
    [
        (Gameplay.Spells.Elements.Water, KeyboardBindings.Water), (Gameplay.Spells.Elements.Lightning, KeyboardBindings.Lightning),
        (Gameplay.Spells.Elements.Life, KeyboardBindings.Life), (Gameplay.Spells.Elements.Arcane, KeyboardBindings.Arcane),
        (Gameplay.Spells.Elements.Shield, KeyboardBindings.Shield), (Gameplay.Spells.Elements.Earth, KeyboardBindings.Earth),
        (Gameplay.Spells.Elements.Cold, KeyboardBindings.Cold), (Gameplay.Spells.Elements.Fire, KeyboardBindings.Fire),
    ];
}
