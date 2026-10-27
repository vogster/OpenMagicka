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
/// for both, so no stale contact points keep pushing. A liquid's liquid triangle (OM-88) never pushes: it is
/// registered with the solver off, and only a body that feels liquids hears it (its point on the liquid's surface).
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
        if (IsLiquid(shapeA, ownerA) || IsLiquid(shapeB, ownerB))
        {
            var meshIsA = ownerA is StaticMesh;
            var (mesh, feeler) = meshIsA ? (ownerA as StaticMesh, ownerB as PhysicsBody) : (ownerB as StaticMesh, ownerA as PhysicsBody);
            if (feeler is { FeelsLiquids: true })
            {
                var away = meshIsA ? normal.ToNumerics() : -normal.ToNumerics();
                var surface = (meshIsA ? pointA : pointB).ToNumerics();
                Touch(mesh, feeler, away, surface, penetration);                  // a part's water carries who wades in it
                Ask(feeler, mesh, away, surface, penetration, liquid: true);
            }
            world.RegisterContact(shapeA.ShapeId, shapeB.ShapeId, shapeA.RigidBody, shapeB.RigidBody, in pointA, in pointB, in normal, ContactData.SolveMode.Full);
            return false;
        }
        if ((ownerA is StaticMesh { Passable: true } && ownerB is PhysicsBody { PassesPassableMeshes: true })
            || (ownerB is StaticMesh { Passable: true } && ownerA is PhysicsBody { PassesPassableMeshes: true }))
        {
            world.RegisterContact(shapeA.ShapeId, shapeB.ShapeId, shapeA.RigidBody, shapeB.RigidBody, in pointA, in pointB, in normal, ContactData.SolveMode.Full);
            return false;                                                     // a shield going down lets characters through
        }
        var push = normal.ToNumerics();
        Touch(ownerA as StaticMesh, ownerB as PhysicsBody, push, pointB.ToNumerics(), penetration);
        Touch(ownerB as StaticMesh, ownerA as PhysicsBody, -push, pointA.ToNumerics(), penetration);
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

    /// <summary>A liquid triangle (not frozen) of a mesh with a surface.</summary>
    private static bool IsLiquid(J.RigidBodyShape shape, object? owner) =>
        owner is StaticMesh { Surface: not null } mesh && shape is J.TriangleShape triangle && !mesh.IsSolid(triangle);

    /// <summary>A mesh that listens hears the body touching it.</summary>
    private static void Touch(StaticMesh? mesh, PhysicsBody? body, Vector3 push, Vector3 point, float penetration)
    {
        if (mesh?.Touched is { } touched && body is not null)
        {
            var length = push.Length();
            touched(body, new Contact(length > 0 ? push / length : Vector3.UnitY, point, penetration, null, mesh));
        }
    }

    /// <param name="push">The way the contact pushes <paramref name="body"/>.</param>
    private static ContactResponse Ask(PhysicsBody? body, object? other, Vector3 push, Vector3 point, float penetration, bool liquid = false)
    {
        if (body?.Listener is not { } listener)
        {
            return ContactResponse.Collide;
        }
        var length = push.Length();
        var unit = length > 0 ? push / length : Vector3.UnitY;
        return listener.OnContact(body, new Contact(unit, point, penetration, other as PhysicsBody, other as StaticMesh, liquid));
    }
}
