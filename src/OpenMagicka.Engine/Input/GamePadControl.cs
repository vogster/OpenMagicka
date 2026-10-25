using System.Numerics;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Engine.Input;

/// <summary>
/// A pad player's controls on its avatar each tick (docs/engine/ARCHITECTURE.md §9, ENTITIES_AI.md §6.1),
/// as the original's:
/// <list type="bullet">
/// <item>The left stick walks the avatar its way, as fast as it is pushed (under a quarter: it stands).</item>
/// <item>The right stick gestures the elements: pushed past 0.6 toward one of the four ways (within 0.5 of
/// it) it starts a gesture; turned on to another way it gives the pair (an element through the spell
/// tree) and holds until the stick comes back under 0.3. Between 0.3 and 0.6 it only shows a way on the
/// wheel.</item>
/// <item>The triggers cast Force (right) and Area (left); A attacks (a held gun or a charged weapon keeps
/// attacking), Y is the special (a cast on oneself, or the staff's ability), B boosts, X uses what is
/// ahead (talks, picks up, interacts), the right shoulder blocks, the pad's down and up pick the magick.</item>
/// <item>While the stage holds input the avatar only stops; the tutorial's locks hold.</item>
/// </list>
/// The spell wheel's picture, the inventory and the pause wait for the HUD and the menus (OM-53, OM-54).
/// </summary>
public sealed class GamePadControl
{
    public const float DeadZone = 0.25f, GestureStart = 0.6f, GestureRest = 0.3f, GestureReach = 0.5f;

    private ControllerDirection current, shown;
    private bool locked;

    /// <summary>A gesture under way is forgotten (a hold, a scene change): the stick must rest before the next.</summary>
    public void Reset() => (locked, WantsWheel, current, shown) = (false, false, ControllerDirection.Center, ControllerDirection.Center);

    /// <summary>The player's directions toward an element (its spell tree).</summary>
    public SpellCombos Combos { get; } = new();

    /// <summary>The way the right stick shows on the wheel now (Center: none).</summary>
    public ControllerDirection Shown => shown != ControllerDirection.Center ? shown : current;

    /// <summary>The spell wheel's help is asked for (its button held).</summary>
    public bool WantsWheel { get; private set; }

    public void Update(PadFrame pad, Player player, ScriptScene scene)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(scene);
        if (player.Avatar is not { IsDead: false } avatar)
        {
            return;
        }
        if (scene.Stage.IsInputLimited || scene.Game.InCutscene)
        {
            ControlTargets.LetGo(avatar);                                     // held: it stands, its hands off
            Reset();
            return;
        }
        var tutorial = scene.Game.Tutorial;
        if (pad.IsPressed(ControllerFunction.Attack) || (pad.IsDown(ControllerFunction.Attack) && (avatar.IsWieldingGun || avatar.ItemIn(0)?.IsSpellCharged == true)))
        {
            avatar.Attack(!tutorial.DisabledCastTypes.Contains(CastType.Weapon));
        }
        else if (pad.IsReleased(ControllerFunction.Attack))
        {
            avatar.ReleaseAttack();
        }
        if (pad.IsPressed(ControllerFunction.Boost))
        {
            avatar.Boost();
        }
        if (pad.IsPressed(ControllerFunction.Interact) && ControlTargets.Ahead(avatar, scene) is { } target)
        {
            ControlTargets.Use(avatar, scene, target);
        }
        if (pad.IsPressed(ControllerFunction.Special) && (avatar.SpellQueue.Count == 0 || !tutorial.DisabledCastTypes.Contains(CastType.Self)))
        {
            avatar.PressCast(CastType.Self);
        }
        else if (pad.IsReleased(ControllerFunction.Special))
        {
            avatar.ReleaseCast(CastType.Self);
        }
        WantsWheel = pad.IsDown(ControllerFunction.Spell_Wheel);
        avatar.IsBlocking = pad.IsDown(ControllerFunction.Block);
        Gesture(avatar, new Vector2(pad.Value(ControllerFunction.Spell_Right) - pad.Value(ControllerFunction.Spell_Left),
            pad.Value(ControllerFunction.Spell_Up) - pad.Value(ControllerFunction.Spell_Down)), tutorial.DisabledElements);
        var walk = new Vector2(pad.Value(ControllerFunction.Move_Right) - pad.Value(ControllerFunction.Move_Left),
            pad.Value(ControllerFunction.Move_Up) - pad.Value(ControllerFunction.Move_Down));
        walk = walk.Length() > 1 ? Vector2.Normalize(walk) : walk.Length() < DeadZone ? Vector2.Zero : walk;
        avatar.Drive(new Vector3(walk.X, 0, -walk.Y));
        Cast(avatar, pad.IsDown(ControllerFunction.Cast_Area), CastType.Area, tutorial);
        Cast(avatar, pad.IsDown(ControllerFunction.Cast_Force), CastType.Force, tutorial);
        var step = pad.IsPressed(ControllerFunction.Magick_Next) ? 1 : pad.IsPressed(ControllerFunction.Magick_Prev) ? -1 : 0;
        if (step != 0)
        {
            player.SelectedMagick = NextMagick(player.SelectedMagick, step, avatar.MayCast);
        }
    }

    private static void Cast(Character avatar, bool held, CastType cast, ScriptTutorial tutorial)
    {
        if (!held)
        {
            avatar.ReleaseCast(cast);
        }
        else if (!tutorial.DisabledCastTypes.Contains(cast) && (avatar.SpellQueue.Count > 0 || !tutorial.IsPushDisabled))
        {
            avatar.PressCast(cast);
        }
    }

    /// <summary>The right stick's gesture, as the original's.</summary>
    private void Gesture(Character avatar, Vector2 stick, Elements disabled)
    {
        var length = stick.Length();
        if (length < GestureRest)
        {
            (locked, shown, current) = (false, ControllerDirection.Center, ControllerDirection.Center);
            return;
        }
        if (locked)
        {
            return;
        }
        if (length < GestureStart)
        {
            shown = MathF.Abs(stick.X) >= MathF.Abs(stick.Y) ? stick.X >= 0 ? ControllerDirection.Right : ControllerDirection.Left
                : stick.Y >= 0 ? ControllerDirection.Up : ControllerDirection.Down;
            return;
        }
        shown = ControllerDirection.Center;                                   // a gesture shows its own way
        var way = current == ControllerDirection.Center ? Nearest(stick) : Turned(current, stick);
        if (way != current && current != ControllerDirection.Center)
        {
            foreach (var direction in new[] { current, way })
            {
                if (!avatar.IsInEvent && avatar.State != CharacterStates.Charge && Combos.Give(direction, e => (e & disabled) == 0) is var element
                    and not Gameplay.Spells.Elements.None)
                {
                    avatar.Conjure(element);
                }
            }
            locked = true;
        }
        else
        {
            current = way;
        }
    }

    /// <summary>The way the stick lies within 0.5 of (the last such of right, left, up, down), else none.</summary>
    private static ControllerDirection Nearest(Vector2 stick)
    {
        var found = ControllerDirection.Center;
        foreach (var (way, at) in new[] { (ControllerDirection.Right, Vector2.UnitX), (ControllerDirection.Left, -Vector2.UnitX),
            (ControllerDirection.Up, Vector2.UnitY), (ControllerDirection.Down, -Vector2.UnitY) })
        {
            if (Vector2.Distance(at, stick) < GestureReach)
            {
                found = way;
            }
        }
        return found;
    }

    /// <summary>A gesture under way turns across: from up or down to the side it leans more to, from a side to up or down.</summary>
    private static ControllerDirection Turned(ControllerDirection from, Vector2 stick) => from switch
    {
        ControllerDirection.Up or ControllerDirection.Down when MathF.Abs(stick.X) > MathF.Abs(stick.Y) =>
            stick.X > 0 ? ControllerDirection.Right : ControllerDirection.Left,
        ControllerDirection.Right or ControllerDirection.Left when MathF.Abs(stick.Y) > MathF.Abs(stick.X) =>
            stick.Y > 0 ? ControllerDirection.Up : ControllerDirection.Down,
        _ => from,
    };

    /// <summary>The previous or next magick the players may cast, round past none, as the original's book.</summary>
    public static MagickType NextMagick(MagickType from, int step, Func<MagickType, bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        var count = (int)MagickType.NrOfMagicks;
        var magick = (int)from;
        for (var tries = 0; tries < count; tries++)
        {
            magick = ((magick + step) % count + count) % count;
            if (magick == 0 || allowed((MagickType)magick))
            {
                break;
            }
        }
        return (MagickType)magick;
    }
}
