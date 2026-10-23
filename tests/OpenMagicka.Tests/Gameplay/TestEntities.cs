using System.Numerics;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>A plain entity whose state the test sets.</summary>
internal class Thing(Vector3 position, float radius = 0.5f, bool damageable = true) : Entity
{
    public Vector3 At { get; set; } = position;

    public bool Gone { get; set; }

    public bool Removed { get; set; }

    public int Updates { get; private set; }

    public int Deinitialised { get; private set; }

    public NetId IdWhenRemoved { get; private set; }

    public Action<Thing>? OnUpdate { get; set; }

    public Action<Thing>? OnRemove { get; set; }

    public override Vector3 Position => At;

    public override float Radius => radius;

    public override bool IsDead => Gone;

    public override bool IsRemovable => Removed;

    public override bool IsDamageable => damageable;

    public override void Update(float dt)
    {
        Updates++;
        OnUpdate?.Invoke(this);
    }

    protected override void OnRemoved()
    {
        Deinitialised++;
        IdWhenRemoved = Id;
        OnRemove?.Invoke(this);
    }
}

/// <summary>A sphere shield that blocks every segment crossing its surface.</summary>
internal sealed class TestShield(Vector3 position, float radius, bool damageable = false) : Thing(position, radius, damageable), IShield
{
    public bool IsSphere => true;

    public bool Blocks(Vector3 from, Vector3 to, float tolerance, out Vector3 hit)
    {
        var crosses = (Vector3.Distance(from, Position) < Radius) != (Vector3.Distance(to, Position) < Radius);
        hit = crosses ? Position + Vector3.Normalize(from - Position) * Radius : default;   // where it crosses, towards the start
        return crosses;
    }
}
