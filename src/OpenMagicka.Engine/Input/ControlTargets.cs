using System.Numerics;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Engine.Input;

/// <summary>
/// What a player's avatar can use (ENTITIES_AI.md §6.1): a character to talk to (one with a dialog, on its
/// side), an item or a book of magick to pick up, a point of interest; found under the cursor, or (a
/// pad's) ahead of the avatar; and using it.
/// </summary>
public static class ControlTargets
{
    /// <summary>
    /// The player's hands come off the controls (input held, a cutscene, the window's focus lost, the other
    /// device taking over): every cast button and the block let go, and it stands (ours: the original
    /// leaves a cast held through them).
    /// </summary>
    public static void LetGo(Character avatar)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        foreach (var cast in new[] { Gameplay.Spells.CastType.Force, Gameplay.Spells.CastType.Area, Gameplay.Spells.CastType.Self, Gameplay.Spells.CastType.Weapon })
        {
            avatar.ReleaseCast(cast);
        }
        avatar.IsBlocking = false;
        if (!avatar.IsInEvent)
        {
            avatar.Body.Movement = Vector3.Zero;
        }
    }

    /// <summary>How near the cursor's ray the original looks for a character or an item to use.</summary>
    public const float PickReach = 6;

    /// <summary>How near a character must be to talk to it.</summary>
    public const float TalkReach = 3;

    /// <summary>How squarely ahead a pad's avatar must face what it uses (the cosine of the angle).</summary>
    public const float AheadCosine = 0.7f;

    /// <summary>What the cursor's ray points at: a character to talk to, else an item or a book to pick up, else a point of interest.</summary>
    public static object? UnderCursor(Character avatar, ScriptScene scene, CursorPick cursor)
    {
        var near = avatar.Manager?.Query(cursor.Ground, PickReach) ?? [];
        var talker = near.OfType<Character>().FirstOrDefault(c => !ReferenceEquals(c, avatar) && !c.IsPlayer && !c.IsDead && c.Dialog != 0
            && (c.Factions & avatar.Factions) != 0 && c.SegmentIntersect(cursor.Ray, 0));
        if (talker is not null)
        {
            return talker;
        }
        var item = near.FirstOrDefault(e => e is WorldItem { IsPickable: true, IsDead: false } or BookOfMagick { IsDead: false } && e.SegmentIntersect(cursor.Ray, 0));
        if (item is not null)
        {
            return item;
        }
        return scene.Triggers.OfType<ScriptInteractable>().Where(p => p.Enabled && p.Id is not null)
            .Select(p => (Point: p, At: scene.Places.LocatorPosition(LegacyHash(p.Id!)), Radius: scene.Places.LocatorRadius(LegacyHash(p.Id!)) ?? 1))
            .Where(p => p.At is { } at && Segment.DistanceSquared(cursor.Ray, new Segment(at, Vector3.Zero), out _, out _) <= p.Radius * p.Radius)
            .Select(p => p.Point).FirstOrDefault();
    }

    /// <summary>Whether the avatar can use it now: a talker within 3, an item or a book within its pick-up reach, a point within its radius.</summary>
    public static bool InReach(Character avatar, ScriptScene scene, object target) => target switch
    {
        Character talker => Flat(talker.Position - avatar.Position) <= TalkReach * TalkReach && !talker.IsDead,
        Entity { IsDead: false } thing => Vector3.Distance(thing.Position, avatar.Position) <= Character.PickUpReach + thing.Radius,
        ScriptInteractable point => point.Enabled && scene.Places.LocatorPosition(LegacyHash(point.Id!)) is { } at
            && Flat(at - avatar.Position) <= MathF.Pow(MathF.Max(scene.Places.LocatorRadius(LegacyHash(point.Id!)) ?? 1, avatar.Template.Radius), 2),
        _ => false,
    };

    public static Vector3 Where(ScriptScene scene, object target) => target switch
    {
        Entity entity => entity.Position,
        ScriptInteractable point => scene.Places.LocatorPosition(LegacyHash(point.Id!)) ?? default,
        _ => default,
    };

    /// <summary>Using it: an item or a book is picked up, a character's dialog starts over it, a point of interest is interacted with.</summary>
    public static void Use(Character avatar, ScriptScene scene, object target)
    {
        switch (target)
        {
            case WorldItem lying:
                avatar.PickUp(lying);
                break;
            case BookOfMagick book:
                avatar.PickUp(book);
                break;
            case Character talker:
                scene.Stage.Request(new StartDialogRequest(talker.Dialog, talker, null, null, ForceOnScreen: false));
                break;
            case ScriptInteractable point:
                point.Interact(avatar);
                break;
        }
    }

    /// <summary>
    /// What a pad's avatar uses (its action button): a character to talk to ahead within 3, else an item or a
    /// book in reach ahead, else a point of interest in reach ahead (or one it stands on).
    /// </summary>
    public static object? Ahead(Character avatar, ScriptScene scene)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        ArgumentNullException.ThrowIfNull(scene);
        var facing = avatar.Body.Facing with { Y = 0 };
        bool Faces(Vector3 at)
        {
            var way = (at - avatar.Position) with { Y = 0 };
            return way.LengthSquared() > 1e-6f && Vector3.Dot(Vector3.Normalize(way), Vector3.Normalize(facing)) >= AheadCosine;
        }
        var near = avatar.Manager?.Query(avatar.Position, TalkReach) ?? [];
        var talker = near.OfType<Character>().FirstOrDefault(c => !ReferenceEquals(c, avatar) && !c.IsPlayer && !c.IsDead && c.Dialog != 0
            && (c.Factions & avatar.Factions) != 0 && Faces(c.Position));
        if (talker is not null)
        {
            return talker;
        }
        var item = (avatar.Manager?.Query(avatar.Position, Character.PickUpReach + 2) ?? [])
            .FirstOrDefault(e => e is WorldItem { IsPickable: true, IsDead: false } or BookOfMagick { IsDead: false } && InReach(avatar, scene, e) && Faces(e.Position));
        if (item is not null)
        {
            return item;
        }
        return scene.Triggers.OfType<ScriptInteractable>().FirstOrDefault(p => p.Enabled && p.Id is not null && InReach(avatar, scene, p)
            && (Flat(Where(scene, p) - avatar.Position) <= avatar.Template.Radius * avatar.Template.Radius || Faces(Where(scene, p))));
    }

    private static float Flat(Vector3 v) => v.X * v.X + v.Z * v.Z;

    private static int LegacyHash(string name) => Content.LegacyStringHash.Compute(name);
}
