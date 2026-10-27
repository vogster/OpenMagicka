using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Liquids;
using OpenMagicka.Physics;

namespace OpenMagicka.Engine.Physics;

/// <summary>
/// A moving level part's collision and what rides on it (docs/formats/LEVELS_TRIGGERS.md §9.7), as the
/// original's: its mesh (a part may have none), given in the part's own space, is placed where the part is
/// each frame. A body touching it from above (the contact pushing it up past 0.7) while the part plays is
/// carried for the next 0.25 s, each touch renewing that; one attached (a script's item) is carried until
/// it leaves the world. Each frame what it carries moves (and turns) as the part moved, a character's
/// wanted way turned along, a lying item kept where it now is. What only lies on a part at rest stays.
/// </summary>
public sealed class PartCollision
{
    /// <summary>A touch carries a body this long.</summary>
    public const float CarryTime = 0.25f;

    /// <summary>A touch carries only what lies on top: the contact's push up at least this.</summary>
    public const float OnTop = 0.7f;

    private readonly PhysicsWorld world;
    private readonly Dictionary<PhysicsBody, float> carried = new(ReferenceEqualityComparer.Instance);
    private Matrix4x4 last;
    private bool playing;

    private readonly IReadOnlyList<(Liquid Liquid, StaticMesh? Mesh)> liquids;

    /// <param name="mesh">Its mesh in the world, or none for a part without one (it still carries what is attached).</param>
    /// <param name="liquids">Its water and lava, each with its mesh (none without collision): they ride it (OM-88).</param>
    public PartCollision(PhysicsWorld world, StaticMesh? mesh, Matrix4x4 at, IReadOnlyList<(Liquid, StaticMesh?)>? liquids = null)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        Mesh = mesh;
        last = at;
        this.liquids = liquids ?? [];
        if (mesh is not null)
        {
            world.PlaceStaticMesh(mesh, at);
            mesh.Touched = Touched;
        }
        foreach (var (liquid, liquidMesh) in this.liquids)
        {
            liquid.World = at;
            if (liquidMesh is not null)
            {
                world.PlaceStaticMesh(liquidMesh, at);
                liquidMesh.Touched = Touched;                                 // what stands on its ice, or wades in it, rides it too
            }
        }
    }

    public StaticMesh? Mesh { get; }

    /// <summary>The bodies it carries now.</summary>
    public IReadOnlyCollection<PhysicsBody> Carried => carried.Keys;

    /// <summary>The bodies attached to it that are still in the world (kept on the part while the scene is left).</summary>
    public IReadOnlyList<PhysicsBody> Attached =>
        [.. carried.Where(p => float.IsPositiveInfinity(p.Value) && !p.Key.IsRemoved).Select(p => p.Key)];

    /// <summary>Whether it carries a body now (the original's touching test for a part).</summary>
    public bool Touches(PhysicsBody body) => carried.TryGetValue(body, out var left) && left > 0;

    /// <summary>Carries a body until it leaves the world, whether it touches the part or not (a script's attachTo).</summary>
    public void Attach(PhysicsBody body) => carried[body ?? throw new ArgumentNullException(nameof(body))] = float.PositiveInfinity;

    /// <summary>Heard during the physics step: a body on top of a playing part is carried a while longer.</summary>
    private void Touched(PhysicsBody body, Contact contact)
    {
        if (playing && contact.Normal.Y > OnTop)
        {
            carried[body] = MathF.Max(CarryTime, carried.GetValueOrDefault(body));
        }
    }

    /// <summary>
    /// A frame, once the part took its place: its mesh follows it, and what it carries moves and turns as it
    /// did since the last frame (one whose time ran out goes, after this last move, as the original's).
    /// </summary>
    public void Update(float dt, Matrix4x4 at, bool isPlaying)
    {
        playing = isPlaying;
        if (Mesh is { IsRemoved: true } || world.IsDisposed)
        {
            carried.Clear();
            return;
        }
        var moved = at != last;
        if (!moved && carried.Count == 0)
        {
            return;
        }
        if (moved && Matrix4x4.Decompose(at, out _, out _, out _))
        {
            if (Mesh is not null)
            {
                world.PlaceStaticMesh(Mesh, at);                              // a transform it cannot take (sheared) leaves it where it was
            }
            foreach (var (liquid, liquidMesh) in liquids)
            {
                liquid.World = at;
                if (liquidMesh is not null)
                {
                    world.PlaceStaticMesh(liquidMesh, at);
                }
            }
        }
        var delta = moved && Matrix4x4.Invert(last, out var inverse) ? inverse * at : Matrix4x4.Identity;
        foreach (var body in carried.Keys.ToList())
        {
            var left = carried[body] - dt;
            if (left <= 0 || body.IsRemoved)
            {
                carried.Remove(body);
            }
            else
            {
                carried[body] = left;
            }
            if (moved && !body.IsRemoved)
            {
                Carry(body, delta);
            }
        }
        last = at;
    }

    private static void Carry(PhysicsBody body, Matrix4x4 delta)
    {
        if (body.Tag is WorldItem item)
        {
            item.Carry(delta);
            return;
        }
        body.Position = Vector3.Transform(body.Position, delta);
        if (!Matrix4x4.Decompose(delta, out _, out var turn, out _))
        {
            return;
        }
        if (body.Tag is Character character)
        {
            var wanted = Vector3.TransformNormal(character.Body.WantedDirection, delta) with { Y = 0 };
            if (wanted.LengthSquared() > 1e-6f)
            {
                character.Body.WantedDirection = Vector3.Normalize(wanted);
            }
        }
        else if (!body.IsStatic)
        {
            body.Orientation = Quaternion.Normalize(Quaternion.Concatenate(body.Orientation, turn));
        }
    }
}
