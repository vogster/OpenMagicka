using System.Numerics;
using Jitter2;
using Jitter2.Collision;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using J = Jitter2.Collision.Shapes;

namespace OpenMagicka.Physics;

/// <summary>
/// The physics simulation behind our own facade (docs/adr/0003-physics.md): static meshes
/// tagged with a material, moving bodies and segment queries. Not thread-safe.
/// </summary>
public sealed class PhysicsWorld : IDisposable
{
    private readonly World world;
    private readonly SegmentQuery segments = new();
    private readonly HashSet<PhysicsBody> bodies = [];
    private bool disposed;

    public PhysicsWorld(PhysicsSettings? settings = null)
    {
        Settings = settings ?? PhysicsSettings.Default;
        Settings.Validate();
        world = new World
        {
            Gravity = Settings.Gravity.ToJitter(),
            SubstepCount = Settings.SubSteps,
            SolverIterations = (Settings.SolverIterations, Settings.RelaxationIterations),
            AllowDeactivation = true,
        };
        world.NarrowPhaseFilter = new ContactFilter(world, world.NarrowPhaseFilter ?? new TriangleEdgeCollisionFilter());
        world.PreSubStep += _ => MixFriction();
    }

    public PhysicsSettings Settings { get; }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public bool IsDisposed => disposed;

    /// <summary>True while <see cref="Step"/> runs, when contact listeners are heard.</summary>
    public bool IsStepping { get; private set; }

    /// <summary>Bodies and meshes cannot change while the engine walks them.</summary>
    internal void ThrowIfStepping()
    {
        ThrowIfDisposed();
        if (IsStepping)
        {
            throw new InvalidOperationException("the world cannot change during a step (from a contact listener)");
        }
    }

    /// <summary>
    /// Adds an immovable triangle mesh whose hits report <paramref name="material"/>. A triangle is
    /// solid, and hit by segments, only from its front: the side its right-hand normal
    /// (B − A) × (C − A) points to. Degenerate triangles are dropped.
    /// </summary>
    /// <param name="friction">The level's own surfaces use 1 (docs/engine/ARCHITECTURE.md §7.3).</param>
    /// <param name="isLevel">False for a solid thing that is not the level (level-only tests pass it); it can then be removed.</param>
    public StaticMesh AddStaticMesh(int material, IReadOnlyList<Vector3> vertices, IReadOnlyList<MeshTriangle> triangles, float friction = 1, bool isLevel = true)
    {
        ThrowIfStepping();
        NonNegative(friction, nameof(friction));
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangles);
        var points = vertices.Select(v => Vectors.ThrowIfNotFinite(v, nameof(vertices)).ToJitter()).ToArray();
        var indices = new int[triangles.Count * 3];
        for (var i = 0; i < triangles.Count; i++)
        {
            var t = triangles[i];
            foreach (var index in (ReadOnlySpan<int>)[t.A, t.B, t.C])
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(triangles));
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, points.Length, nameof(triangles));
            }
            (indices[3 * i], indices[3 * i + 1], indices[3 * i + 2]) = (t.A, t.B, t.C);
        }
        var shapes = NonDegenerate(points, indices);
        var mesh = new StaticMesh(material, shapes.Count, friction, isLevel);
        if (shapes.Count > 0)
        {
            var body = world.CreateRigidBody();
            body.MotionType = MotionType.Static;
            body.Friction = friction;
            body.Tag = mesh;
            body.AddShapes(shapes, MassInertiaUpdateMode.Preserve);
            mesh.Body = body;
        }
        return mesh;
    }

    /// <summary>Takes a mesh that is not the level out of the world (a shield gone).</summary>
    public void RemoveStaticMesh(StaticMesh mesh)
    {
        ThrowIfStepping();
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.IsLevel)
        {
            throw new InvalidOperationException("the level stays");
        }
        if (mesh.IsRemoved)
        {
            return;
        }
        if (mesh.Body is { } body)
        {
            world.Remove(body);
        }
        (mesh.Body, mesh.IsRemoved) = (null, true);
    }

    private static List<J.TriangleShape> NonDegenerate(JVector[] points, int[] indices)
    {
        if (indices.Length == 0)
        {
            return [];
        }
        try
        {
            return [.. J.TriangleShape.CreateAllShapes(new J.TriangleMesh(points, indices, ignoreDegenerated: true))];
        }
        catch (J.TriangleMesh.DegenerateTriangleException)
        {
            return [];                                       // nothing but degenerate triangles
        }
    }

    public PhysicsBody AddBody(BodyShape shape, Vector3 position, float mass, BodyOptions options = BodyOptions.None)
    {
        ThrowIfStepping();
        ArgumentNullException.ThrowIfNull(shape);
        Vectors.ThrowIfNotFinite(position, nameof(position));
        if (!(mass > 0) || !float.IsFinite(mass))
        {
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "mass must be positive");
        }
        var collider = ToJitter(shape);
        var body = world.CreateRigidBody();
        body.AddShape(collider, MassInertiaUpdateMode.Update);
        body.SetMassInertia(mass);
        body.Position = position.ToJitter();
        body.AllowedMotion = options.HasFlag(BodyOptions.LockRotation) ? MotionAxes.Linear : MotionAxes.All;
        body.EnableCcd = options.HasFlag(BodyOptions.Fast);
        if (options.HasFlag(BodyOptions.NeverSleeps))
        {
            body.DeactivationTime = NeverDeactivate;
        }
        if (options.HasFlag(BodyOptions.Undamped))
        {
            body.Damping = (0, 0);
        }
        var wrapper = new PhysicsBody(this, body, shape);
        body.Tag = wrapper;
        bodies.Add(wrapper);
        return wrapper;
    }

    /// <summary>Long enough to never happen in a game session.</summary>
    private static readonly TimeSpan NeverDeactivate = TimeSpan.FromDays(365);

    /// <summary>The engine takes the larger friction of a pair; ours is the geometric mean, so 0 slides on anything.</summary>
    private void MixFriction()
    {
        foreach (var body in bodies)
        {
            foreach (var arbiter in body.Identity.Contacts)
            {
                ref var data = ref arbiter.Handle.Data;
                data.Friction = MathF.Sqrt(arbiter.Body1.Friction * arbiter.Body2.Friction);
            }
        }
    }

    internal static J.RigidBodyShape ToJitter(BodyShape shape) => shape switch
    {
        SphereShape s => new J.SphereShape(Positive(s.Radius, nameof(s.Radius))),
        BoxShape b => new J.BoxShape(Positive(b.Size.X, "Size.X"), Positive(b.Size.Y, "Size.Y"), Positive(b.Size.Z, "Size.Z")),
        CapsuleShape c => new J.CapsuleShape(Positive(c.Radius, nameof(c.Radius)), NonNegative(c.Length, nameof(c.Length))),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "unknown shape"),
    };

    private static float Positive(float value, string name) =>
        value > 0 && float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, "must be positive");

    private static float NonNegative(float value, string name) =>
        value >= 0 && float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, "must not be negative");

    public void Remove(PhysicsBody body)
    {
        ThrowIfStepping();
        ArgumentNullException.ThrowIfNull(body);
        if (body.IsRemoved || !body.BelongsTo(this))
        {
            throw new InvalidOperationException("the body is not in this world");
        }
        world.Remove(body.Body);
        body.IsRemoved = true;
        bodies.Remove(body);
    }

    /// <summary>Advances the simulation by one tick.</summary>
    public void Step(float dt)
    {
        ThrowIfStepping();
        if (!(dt > 0) || !float.IsFinite(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "the time step must be positive");
        }
        IsStepping = true;
        try
        {
            world.Step(dt, multiThread: false);
        }
        finally
        {
            IsStepping = false;
        }
    }

    /// <summary>
    /// The nearest hit from <paramref name="start"/> towards <paramref name="end"/> (excluded), or
    /// null. Triangles are hit from their front only, as they collide. A segment of no length hits
    /// nothing.
    /// </summary>
    /// <param name="ignore">A body the segment passes through, such as the caster.</param>
    /// <param name="levelOnly">Only the level's static meshes count.</param>
    public SegmentHit? CastSegment(Vector3 start, Vector3 end, PhysicsBody? ignore = null, bool levelOnly = false)
    {
        ThrowIfDisposed();
        Vectors.ThrowIfNotFinite(start, nameof(start));
        Vectors.ThrowIfNotFinite(end, nameof(end));
        if (start == end)
        {
            return null;
        }
        var shapeHit = segments.Run(world.DynamicTree, start, end, ignore?.Identity, levelOnly, out var proxy, out var normal, out var lambda);
        if (segments.Triangle is { } triangle && (!shapeHit || segments.TriangleLambda <= lambda))
        {
            triangle.GetWorldVertices(out var a, out var b, out var c);
            var face = Vector3.Cross(b.ToNumerics() - a.ToNumerics(), c.ToNumerics() - a.ToNumerics());
            return Hit(segments.TriangleLambda, face, triangle.RigidBody.Tag);
        }
        return shapeHit ? Hit(lambda, normal.ToNumerics(), (proxy as J.RigidBodyShape)?.RigidBody.Tag) : null;

        SegmentHit Hit(float fraction, Vector3 surface, object? owner) => new(
            Vector3.Lerp(start, end, fraction),
            surface == Vector3.Zero ? Vector3.Normalize(start - end) : Vector3.Normalize(surface),   // zero: started inside
            fraction,
            (owner as StaticMesh)?.Material,
            owner as PhysicsBody);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            world.Dispose();
        }
    }
}
