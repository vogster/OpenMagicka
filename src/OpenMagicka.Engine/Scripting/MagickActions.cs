using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Magicks;

namespace OpenMagicka.Engine.Scripting;

/// <summary>A magick was unlocked by a script: the HUD shows its name (the "magick learned" text, OM-49).</summary>
public sealed record MagickUnlockedRequest(MagickType Magick) : ScriptRequest;

/// <summary>
/// <c>magick magickType= id=</c>: the named character's magick takes effect at once (no cast clip). The
/// original's magick at a <c>position</c> with no caster is never used; ours casts none.
/// </summary>
public sealed class MagickAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public MagickType MagickType { get; set; }
    public string Id { get; set; } = "";
    public Vector3 Position { get; set; }

    protected override void Execute()
    {
        if (Id.Length > 0 && Scene.World.Entities.FindByUniqueId(Hash(Id)) is Gameplay.Characters.Character caster && MagickEffects.For(MagickType) is { } magick)
        {
            magick.Execute(caster);
        }
    }
}

/// <summary>
/// <c>unlockMagick magickType=</c>: the magick is every player's (in Versus, every playing one's), and the
/// HUD says it was learned.
/// </summary>
public sealed class UnlockMagickAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public MagickType MagickType { get; set; }

    protected override void Execute()
    {
        Scene.Stage.Request(new MagickUnlockedRequest(MagickType));
        if (Scene.World.Session is not { } session)
        {
            return;
        }
        if (session.Type == GameType.Versus)
        {
            foreach (var player in session.Players.Where(p => p.Playing))
            {
                player.Unlock(MagickType);
            }
        }
        else
        {
            session.UnlockForAll(MagickType);
        }
    }
}

/// <summary>
/// <c>spawnMagick magick= area= immovable= onPickup= id= timeOut=</c>: a book of the magick lies at the
/// place (it stays put unless movable); its pick-up sets off <c>onPickup</c>. A network client spawns
/// none (the host's message does, OM-59). Not replayed: a checkpoint keeps what the players unlocked.
/// </summary>
public sealed class SpawnMagickAction : ScriptAction
{
    public string Area { get; set; } = "";
    public bool Immovable { get; set; } = true;
    public MagickType Magick { get; set; }
    public string OnPickup { get; set; } = "";
    public string Id { get; set; } = "";
    public float TimeOut { get; set; }

    public override void Initialize()
    {
        if (Magick is <= MagickType.None or >= MagickType.NrOfMagicks || !float.IsFinite(TimeOut) || TimeOut < 0)
        {
            throw new InvalidDataException($"<spawnMagick magick=\"{Magick}\">: not a player magick, or a bad time-out");
        }
    }

    protected override void Execute()
    {
        var manager = Scene.World.Entities;
        if (!Scene.IsHost || manager.Entities.Count >= EntityManager.Capacity || Scene.World.Physics.IsStepping)
        {
            return;
        }
        var at = Scene.Places.Placement(Hash(Area), Scene.Random).Translation;
        var book = new BookOfMagick(Scene.World.Physics, Magick, at, Immovable, timeout: TimeOut)
        {
            OnPickupTrigger = OnPickup.Length > 0 ? Hash(OnPickup) : 0,
        };
        manager.Add(book);
        if (Id.Length > 0)
        {
            book.UniqueId = Hash(Id);
        }
    }
}

/// <summary>
/// <c>spawnElemental area= nr= proximity= snapToNavMesh= id=</c>: <c>nr</c> elemental eggs at the place (on
/// the nav mesh's nearest point when snapped), turned its way; a wizard within <c>proximity</c> may hatch
/// one. A network client spawns none. Each run's replay lays <c>nr</c> again, so as many as were ever laid
/// (hatched or not), with their proximity (the original's replay leaves it out).
/// </summary>
public sealed class SpawnElementalAction : ScriptAction
{
    public string Id { get; set; } = "";
    public string Area { get; set; } = "";
    public bool SnapToNavMesh { get; set; }
    public int Nr { get; set; }
    public float Proximity { get; set; }

    public override void Initialize()
    {
        if (!float.IsFinite(Proximity))
        {
            throw new InvalidDataException("<spawnElemental>: proximity must be finite");
        }
    }

    public override void Replay() => Execute();

    protected override void Execute()
    {
        var manager = Scene.World.Entities;
        for (var made = 0; Scene.IsHost && made < Nr && manager.Entities.Count < EntityManager.Capacity && !Scene.World.Physics.IsStepping; made++)
        {
            var placement = Scene.Places.Placement(Hash(Area), Scene.Random);
            var ground = SnapToNavMesh ? manager.Walkable(placement.Translation) : placement.Translation;
            var egg = new ElementalEgg(Scene.World.Physics, ground, ScriptPlaces.Forward(placement), seed: Scene.Random.Next()) { Proximity = Proximity };
            manager.Add(egg);
            if (Id.Length > 0)
            {
                egg.UniqueId = Hash(Id);
            }
        }
    }
}
