using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Items;

/// <summary>The bounds of an item's model, in its own frame.</summary>
public readonly record struct ItemBounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;

    public Vector3 Centre => (Min + Max) / 2;
}

/// <summary>
/// An item lying in the world: a box of its model's bounds that characters pass through
/// (docs/engine/ENTITIES_AI.md §8.1.5). A thin box can fall through the level in one step, so its
/// centre never crosses the level: crossing puts it back where it was, still. A simulation object:
/// it changes in place.
/// </summary>
public sealed class WorldItem : Entity
{
    public const float Mass = 50;

    /// <summary>How long an item no one can pick up lies before it goes.</summary>
    public const float Lifetime = 15;

    /// <summary>The smallest side a box may have (a flat model still needs a body).</summary>
    private const float Thinnest = 0.01f;

    private readonly PhysicsWorld world;
    private Vector3 lastPosition;
    private float timeLeft = Lifetime;
    private bool taken;

    /// <param name="from">A point on the level's open side (its holder); a box whose centre lies past the level from it starts at the level.</param>
    /// <param name="transform">Where the item is: the box goes where its bounds are, turned with it (scale left out), but upright itself.</param>
    public WorldItem(PhysicsWorld world, Item item, Matrix4x4 transform, Vector3 velocity = default, Vector3? from = null)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        Item = item ?? throw new ArgumentNullException(nameof(item));
        var bounds = item.Template.Bounds ?? throw new ArgumentException($"{item.Template.Id} has no model to lie in the world", nameof(item));
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "must be finite");
        }
        var size = Vector3.Max(bounds.Size, new Vector3(Thinnest));
        Radius = size.Length() / 2;
        var centre = transform.Translation + Ranged.Turn(bounds.Centre, transform);
        if (from is { } open && world.CastSegment(open, centre, levelOnly: true) is { } wall)
        {
            centre = wall.Position + wall.Normal * (Vector3.Dot(Vector3.Abs(wall.Normal), size) / 2);   // the box's whole depth along the wall's way
        }
        lastPosition = centre;
        Body = world.AddBody(new BoxShape(size), centre, Mass);
        (Body.Friction, Body.Restitution, Body.Tag, Body.Velocity) = (1, 0, this, velocity);
        Body.Listener = new Contacts();
    }

    public Item Item { get; }

    protected internal override IEnumerable<PhysicsBody> Bodies => [Body];

    /// <summary>The script trigger picking it up sets off, the picker as the sender (0 for none).</summary>
    public int OnPickupTrigger { get; set; }

    /// <summary>A tractor pull (a magick that draws items) leaves it be.</summary>
    public bool IgnoresTractorPull { get; set; }

    /// <summary>It stays where it was put (a script's item with no physics): no gravity, no drift.</summary>
    public bool IsPinned
    {
        get => pinned is not null;
        set
        {
            pinned = value ? Body.Position : null;
            Body.AffectedByGravity = !value;
        }
    }

    private Vector3? pinned;

    /// <summary>Puts it elsewhere, as it would be put down there anew (its box where its bounds are, still).</summary>
    public void MoveTo(Matrix4x4 transform)
    {
        var centre = transform.Translation + Ranged.Turn(Item.Template.Bounds!.Value.Centre, transform);
        if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y) || !float.IsFinite(centre.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(transform), transform, "must be finite");
        }
        (Body.Position, Body.Velocity, lastPosition) = (centre, Vector3.Zero, centre);
        if (pinned is not null)
        {
            pinned = centre;
        }
    }

    public PhysicsBody Body { get; }

    /// <summary>Whether an avatar can pick it up.</summary>
    public bool IsPickable => Item.Template.Pickable;

    public override Vector3 Position => Body.IsRemoved || world.IsDisposed ? lastPosition : Body.Position;

    public override Vector3 Velocity => Body.IsRemoved || world.IsDisposed ? Vector3.Zero : Body.Velocity;

    public override float Radius { get; }

    /// <summary>Where its model's origin is now (its box's centre and turn, the model's centre undone): what the renderer draws at.</summary>
    public Matrix4x4 Transform
    {
        get
        {
            var turn = Body.IsRemoved || world.IsDisposed ? Quaternion.Identity : Body.Orientation;
            var at = Matrix4x4.CreateFromQuaternion(turn);
            at.Translation = Position - Vector3.Transform(Item.Template.Bounds?.Centre ?? Vector3.Zero, turn);
            return at;
        }
    }

    public override bool IsDead => taken;

    public override bool IsRemovable => taken;

    /// <summary>It leaves the world (picked up); it goes at the end of the frame.</summary>
    internal void Take() => taken = true;

    /// <summary>An item no one can pick up goes after its lifetime; a pickable one lies on.</summary>
    public override void Update(float dt)
    {
        if (!IsPickable && !taken)
        {
            timeLeft -= dt;
            taken = timeLeft < 0;
        }
        if (!taken && Item.TickSpecial(dt))                                   // a lying item recharges too, as the original; a taken one is its holder's
        {
            Manager?.RaiseCue(this, new ItemEffectCue(SpecialAbilities.ReadyEffect, Position, Vector3.UnitY, Follow: false, Item.PassiveSource));
            Manager?.RaiseCue(this, new SoundCue(SpecialAbilities.RechargeSound, SpecialAbilities.WeaponsBank, Position, Vector3.UnitY));
        }
        if (Body.IsRemoved || world.IsDisposed)
        {
            return;
        }
        if (pinned is { } spot)
        {
            (Body.Position, Body.Velocity) = (spot, Vector3.Zero);
        }
        if (Body.Position.Y <= Missile.LowestY)
        {
            taken = true;                                                     // fell out of the level
            return;
        }
        if (world.CastSegment(lastPosition, Body.Position, Body, levelOnly: true) is not null)
        {
            (Body.Position, Body.Velocity) = (lastPosition, Vector3.Zero);   // it went through the level: back, still
        }
        lastPosition = Body.Position;
    }

    protected internal override void OnAdded()
    {
        if (Body.IsRemoved)
        {
            throw new InvalidOperationException("a removed item cannot lie in the world again; drop a new one");
        }
    }

    protected internal override void OnRemoved()
    {
        if (!world.IsDisposed && !Body.IsRemoved)
        {
            lastPosition = Body.Position;
            world.Remove(Body);
        }
    }

    /// <summary>Characters pass through a lying item, as the original.</summary>
    private sealed class Contacts : IContactListener
    {
        public ContactResponse OnContact(PhysicsBody body, in Contact contact) =>
            contact.OtherBody?.Tag is Character ? ContactResponse.Ignore : ContactResponse.Collide;
    }
}
