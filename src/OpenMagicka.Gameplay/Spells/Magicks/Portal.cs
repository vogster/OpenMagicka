using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>
/// Portal: a gate 2 ahead of the caster; with two gates up and near enough, what touches one comes
/// out of the other (MAGICKS.md §7.5). A level has two gates, orange and blue; each cast moves the
/// older one.
/// </summary>
public sealed class Portal() : Magick(MagickType.Portal, "cast_area_fireworks")
{
    public const float Ahead = 2, Lift = 1.5f, Reach = 5;

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity)
        {
            return Fail(caster);
        }
        if (Spot(manager, caster, caster.Position + caster.Body.Facing * Ahead) is not { } spot)
        {
            return Fail(caster);
        }
        var gates = PortalGate.Of(manager);
        var link = gates.Count > 0 ? gates[0].Link : new PortalLink();
        var colour = gates.Count switch
        {
            0 => PortalColour.Orange,
            1 => gates[0].Colour == PortalColour.Orange ? PortalColour.Blue : PortalColour.Orange,
            _ => gates[0].Colour,
        };
        if (gates.Count > 1)
        {
            gates[0].Kill();                                                  // the older gate moves
        }
        manager.Add(new PortalGate(link, colour, spot));
        return true;
    }

    /// <summary>
    /// The nav mesh's nearest point (water and moving parts allowed) within 5, then 1.5 above the
    /// floor under it (looked for from 2 above, 4 down), or the point itself with no floor. Without
    /// a nav mesh, the floor under the spot itself is needed.
    /// </summary>
    private static Vector3? Spot(EntityManager manager, Character caster, Vector3 at)
    {
        if (manager.NavMesh is { } nav)
        {
            if (nav.Nearest(at, Navigation.NavMovement.Water | Navigation.NavMovement.Dynamic) is not { } near || near.DistanceSquared > Reach * Reach)
            {
                return null;
            }
            at = near.Point;
        }
        return caster.World.CastSegment(at + Vector3.UnitY * 2, at - Vector3.UnitY * 2, levelOnly: true) is { } floor
            ? floor.Position + Vector3.UnitY * Lift
            : manager.NavMesh is null ? null : at;
    }
}

public enum PortalColour { Blue, Orange }

/// <summary>What a level's two gates share: whether they are connected, and who has just gone through.</summary>
public sealed class PortalLink
{
    public const float MaxDistance = 30, Ignored = 0.1f;

    private readonly Dictionary<Entity, float> ignored = [];
    private long order;

    public bool Connected { get; private set; }

    internal long Next() => ++order;

    /// <summary>A touch: true when the entity is not just through; either way it is ignored for 0.1 s more.</summary>
    internal bool Touch(Entity entity)
    {
        var fresh = !ignored.ContainsKey(entity);
        ignored[entity] = Ignored;
        return fresh;
    }

    /// <summary>Once a frame (the newer gate): the memory counts down, and the gates connect when both stand within 30.</summary>
    internal void Tick(IReadOnlyList<PortalGate> gates, float dt)
    {
        foreach (var (entity, left) in ignored.ToList())
        {
            if (left - dt <= 0)
            {
                ignored.Remove(entity);
            }
            else
            {
                ignored[entity] = left - dt;
            }
        }
        Connect(gates);
    }

    /// <summary>Whether the gates (both up) are within 30; each gate asks afresh, so a moved gate counts at once.</summary>
    internal bool Connect(IReadOnlyList<PortalGate> gates) =>
        Connected = gates.Count == 2 && Vector3.DistanceSquared(gates[0].Position, gates[1].Position) <= MaxDistance * MaxDistance;
}

/// <summary>
/// One gate: an upright capsule (radius 0.5, 2 tall). While connected, a character or missile that
/// touches it is put at the other gate, its offset from this one shrunk to 0.8. A simulation object.
/// </summary>
public sealed class PortalGate : Entity
{
    public const float GateRadius = 0.5f, HalfHeight = 1, Offset = 0.8f;

    public const string BlueEffect = "magick_portal_blue", OrangeEffect = "magick_portal_orange",
        SpawnSound = "magick_teleportb", TeleportSound = "magick_teleporta";

    private readonly Vector3 position;
    private bool dead, shown;
    private EntityManager? level;

    internal PortalGate(PortalLink link, PortalColour colour, Vector3 position) =>
        (Link, Colour, this.position, Order) = (link, colour, position, link.Next());

    public PortalLink Link { get; }

    public PortalColour Colour { get; }

    /// <summary>When it was opened; the larger, the newer.</summary>
    public long Order { get; }

    public override Vector3 Position => position;

    public override float Radius => GateRadius + HalfHeight;

    public override bool IsDead => dead;

    public override bool IsRemovable => dead;

    /// <summary>The level's live gates, the older first.</summary>
    public static IReadOnlyList<PortalGate> Of(EntityManager manager) =>
        [.. manager.Entities.OfType<PortalGate>().Where(g => !g.IsDead).OrderBy(g => g.Order)];

    /// <summary>It closes (Nullify closes both).</summary>
    public void Kill() => dead = true;

    protected internal override void OnAdded()
    {
        level = Manager;
        level?.RaiseCue(this, new SoundCue(SpawnSound, MagickEffects.SpellsBank, position, -Vector3.UnitZ));
    }

    protected internal override void OnRemoved() => Show(false);

    public override void Update(float dt)
    {
        if (dead || Manager is not { } manager)
        {
            return;
        }
        var gates = Of(manager);
        if (gates[^1] == this)
        {
            Link.Tick(gates, dt);
        }
        else
        {
            Link.Connect(gates);
        }
        Show(Link.Connected);
        if (!Link.Connected || gates.FirstOrDefault(g => g != this) is not { } other)
        {
            return;
        }
        foreach (var entity in manager.Query(position, Radius + 2).Where(Touches).ToList())
        {
            if (Link.Touch(entity))
            {
                Send(manager, entity, other);
            }
        }
    }

    /// <summary>Puts the entity at the other gate, as far off it as it was off this one, times 0.8.</summary>
    private void Send(EntityManager manager, Entity entity, PortalGate other)
    {
        var to = other.Position + (entity.Position - position) * Offset;
        switch (entity)
        {
            case Character character when !character.World.IsStepping:
                character.Body.Body.Position = to;
                break;
            case Missile missile when !missile.IsDead:
                missile.Body.Position = to;
                break;
            default:
                return;
        }
        manager.RaiseCue(this, new SoundCue(TeleportSound, MagickEffects.SpellsBank, position, -Vector3.UnitZ));
    }

    /// <summary>What goes through: characters and missiles that touch the capsule (the rest of ours have no body to move).</summary>
    private bool Touches(Entity entity)
    {
        if (entity is not (Character or Missile) || entity.IsDead)
        {
            return false;
        }
        var off = entity.Position - position;
        var along = Math.Clamp(off.Y, -HalfHeight, HalfHeight);
        var reach = GateRadius + entity.Radius;
        return (off - Vector3.UnitY * along).LengthSquared() <= reach * reach;
    }

    /// <summary>The gate's look is on while connected (the pair's hum, by distance, comes with the audio).</summary>
    private void Show(bool on)
    {
        if (on == shown || level is null)
        {
            return;
        }
        shown = on;
        if (on)
        {
            level.RaiseCue(this, new ItemEffectCue(Colour == PortalColour.Blue ? BlueEffect : OrangeEffect, position, -Vector3.UnitZ, Follow: false, this));
        }
        else
        {
            level.RaiseCue(this, new ItemEffectEndCue(this));
        }
    }
}
