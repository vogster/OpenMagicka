using System.Numerics;
using Jitter2;
using Jitter2.Collision;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using J = Jitter2.Collision.Shapes;

namespace OpenMagicka.Physics;

/// <summary>
/// Hands every contact to the listeners of both bodies after the engine's own filter (which
/// smooths triangle edges). A contact one side passes through is registered by hand with the
/// solver turned off for that side; one either side ignores is registered with the solver off
/// for both, so no stale contact points keep pushing.
/// </summary>
internal sealed class ContactFilter(World world, INarrowPhaseFilter inner) : INarrowPhaseFilter
{
    public bool Filter(J.RigidBodyShape shapeA, J.RigidBodyShape shapeB, ref JVector pointA, ref JVector pointB, ref JVector normal, ref float penetration)
    {
        if (!inner.Filter(shapeA, shapeB, ref pointA, ref pointB, ref normal, ref penetration))
        {
            return false;
        }
        var (ownerA, ownerB) = (shapeA.RigidBody.Tag, shapeB.RigidBody.Tag);
        var push = normal.ToNumerics();
        var a = Ask(ownerA as PhysicsBody, ownerB, -push, pointA.ToNumerics(), penetration);
        var b = Ask(ownerB as PhysicsBody, ownerA, push, pointB.ToNumerics(), penetration);
        if (a == ContactResponse.Collide && b == ContactResponse.Collide)
        {
            return true;
        }
        var off = a == ContactResponse.Ignore || b == ContactResponse.Ignore
            ? ContactData.SolveMode.Full
            : (a == ContactResponse.PassThrough ? ContactData.SolveMode.FullBody1 : ContactData.SolveMode.None)
              | (b == ContactResponse.PassThrough ? ContactData.SolveMode.FullBody2 : ContactData.SolveMode.None);
        world.RegisterContact(shapeA.ShapeId, shapeB.ShapeId, shapeA.RigidBody, shapeB.RigidBody, in pointA, in pointB, in normal, off);
        return false;
    }

    /// <param name="push">The way the contact pushes <paramref name="body"/>.</param>
    private static ContactResponse Ask(PhysicsBody? body, object? other, Vector3 push, Vector3 point, float penetration)
    {
        if (body?.Listener is not { } listener)
        {
            return ContactResponse.Collide;
        }
        var length = push.Length();
        var unit = length > 0 ? push / length : Vector3.UnitY;
        return listener.OnContact(body, new Contact(unit, point, penetration, other as PhysicsBody, other as StaticMesh));
    }
}
