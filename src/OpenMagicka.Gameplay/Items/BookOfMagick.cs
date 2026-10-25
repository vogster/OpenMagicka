using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Items;

/// <summary>
/// A book of magick lying in the world (a scene's <c>spawnMagick</c>, a Versus wizard's dropped
/// magicks): picking it up unlocks its magick (docs/gameplay/MAGICKS.md §1.2). An immovable book
/// stays where it is put; another falls and tumbles. A simulation object.
/// </summary>
public sealed class BookOfMagick : Entity
{
    public const float Mass = 10, Size = 0.5f;

    private readonly PhysicsWorld world;
    private readonly PhysicsBody? body;

    protected internal override IEnumerable<PhysicsBody> Bodies => body is null ? [] : [body];
    private Vector3 lastPosition;
    private float timeout;
    private bool taken;

    /// <param name="timeout">It vanishes after this long; 0 for never.</param>
    public BookOfMagick(PhysicsWorld world, MagickType magick, Vector3 position, bool immovable, Vector3 velocity = default, float timeout = 0)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        if (magick is <= MagickType.None or >= MagickType.NrOfMagicks)
        {
            throw new ArgumentOutOfRangeException(nameof(magick), magick, "not a player magick");
        }
        if (!float.IsFinite(timeout) || timeout < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "must be finite and not negative");
        }
        (Magick, lastPosition, this.timeout) = (magick, position, timeout);
        if (!immovable)
        {
            body = world.AddBody(new BoxShape(new Vector3(Size)), position, Mass);
            (body.Tag, body.Velocity) = (this, velocity);
            body.Listener = new Contacts();
        }
    }

    public MagickType Magick { get; }

    public override Vector3 Position => body is { IsRemoved: false } && !world.IsDisposed ? body.Position : lastPosition;

    public override float Radius => Size;

    public override bool IsDead => taken;

    public override bool IsRemovable => taken;

    /// <summary>
    /// Picked up: in Versus its magick is the picker's; elsewhere every player's (as the scene's
    /// unlock action). It goes at the end of the frame. False when it is already gone.
    /// </summary>
    public bool Unlock(Player? picker)
    {
        if (taken || Manager?.Session is not { } session || (session.Type == GameType.Versus && picker is null))
        {
            return false;                                                     // a book serves a game's players
        }
        taken = true;
        if (session.Type == GameType.Versus)
        {
            picker!.Unlock(Magick);
        }
        else
        {
            session.UnlockForAll(Magick);
        }
        return true;
    }

    public override void Update(float dt)
    {
        if (timeout > 0)
        {
            timeout -= dt;
            taken |= timeout <= 0;
        }
        if (body is { IsRemoved: false } && !world.IsDisposed)
        {
            lastPosition = body.Position;
            taken |= lastPosition.Y <= Missile.LowestY;                       // fell out of the level
        }
    }

    protected internal override void OnRemoved()
    {
        if (body is { IsRemoved: false } && !world.IsDisposed)
        {
            lastPosition = body.Position;
            world.Remove(body);
        }
    }

    /// <summary>Characters pass through a book, as through a lying item.</summary>
    private sealed class Contacts : IContactListener
    {
        public ContactResponse OnContact(PhysicsBody body, in Contact contact) =>
            contact.OtherBody?.Tag is Character ? ContactResponse.Ignore : ContactResponse.Collide;
    }
}
