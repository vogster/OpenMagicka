using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Particles;

/// <summary>A particle as an emitter makes it.</summary>
public readonly record struct ParticleSpawn
{
    public Vector3 Position { get; init; }
    public Vector3 Velocity { get; init; }
    public float Gravity { get; init; }
    public float Drag { get; init; }
    public float TTL { get; init; }
    public float StartSize { get; init; }
    public float EndSize { get; init; }
    public float Rotation { get; init; }
    public float RotationVelocity { get; init; }
    public Vector4 Color { get; init; }
    public byte Sprite { get; init; }
    public bool AlphaBlended { get; init; }
    public bool Hsv { get; init; }
    public bool Colorize { get; init; }
    public bool RotationAligned { get; init; }
}

/// <summary>
/// A particle as the shader reads it, one instance of the quad: 56 bytes, laid out as the
/// original's instance stream (position, rotation, radius, normalized lifetime, colour, sprite,
/// alpha-blended, HSV × 50 + colorize × 5, rotation-aligned, velocity). Mutable: the pool
/// updates these in place every step and uploads them as they are.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ParticleInstance
{
    public const int SizeInBytes = 56;

    public Vector3 Position;
    public float Rotation;
    public float Radius;
    public float NormalizedLifetime;
    public Vector4 Color;
    public byte Sprite;
    public byte AlphaBlended;
    public byte HsvColorize;
    public byte RotationAligned;
    public Vector3 Velocity;
}

/// <summary>
/// The particles of the world, as the original keeps them (docs/engine/ARCHITECTURE.md §5.1): a
/// fixed pool (32768) whose free slots are taken lowest first; each step every live particle ages,
/// falls, slows and turns, and its radius goes from its start size to its end size over its life
/// (past it, extrapolated, in the step it dies). A particle whose time ran out frees its slot; below
/// a live one it is still drawn that step and hidden (radius 0) on the next, while dead slots at the
/// top leave the drawn range (<see cref="LastActive"/>) at once. As in the original, a particle
/// spawned with a negative lifetime keeps its slot until <see cref="Clear"/>, and a NaN one never dies.
/// </summary>
public sealed class ParticlePool
{
    public const int DefaultCapacity = 32768;

    private readonly ParticleInstance[] instances;
    private readonly Logic[] logic;
    private readonly PriorityQueue<int, int> free = new();

    public ParticlePool(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        instances = new ParticleInstance[capacity];
        logic = new Logic[capacity];
        Clear();
    }

    private struct Logic
    {
        public float RotationSpeed;
        public float Gravity;
        public float Drag;
        public float TTL;
        public float InvMaxTTL;
        public float StartSize;
        public float EndSize;
    }

    /// <summary>The highest slot in use, -1 when none is.</summary>
    public int LastActive { get; private set; } = -1;

    /// <summary>Every slot; the ones up to <see cref="LastActive"/> are drawn.</summary>
    public ReadOnlySpan<ParticleInstance> Instances => instances;

    public void Clear()
    {
        LastActive = -1;
        free.Clear();
        free.EnqueueRange(Enumerable.Range(0, instances.Length).Select(i => (i, i)));
        Array.Fill(logic, new Logic { TTL = -1 });
    }

    public void Spawn(in ParticleSpawn particle)
    {
        if (!free.TryDequeue(out var slot, out _))
        {
            return;
        }
        instances[slot] = new ParticleInstance
        {
            Position = particle.Position,
            Rotation = particle.Rotation,
            Radius = particle.StartSize,
            NormalizedLifetime = 0,
            Color = particle.Color,
            Sprite = particle.Sprite,
            AlphaBlended = (byte)(particle.AlphaBlended ? 1 : 0),
            HsvColorize = (byte)((particle.Hsv ? 50 : 0) + (particle.Colorize ? 5 : 0)),
            RotationAligned = (byte)(particle.RotationAligned ? 1 : 0),
            Velocity = particle.Velocity,
        };
        logic[slot] = new Logic
        {
            RotationSpeed = particle.RotationVelocity,
            Gravity = particle.Gravity,
            Drag = particle.Drag,
            TTL = particle.TTL,
            InvMaxTTL = 1 / particle.TTL,
            StartSize = particle.StartSize,
            EndSize = particle.EndSize,
        };
        LastActive = Math.Max(LastActive, slot);
    }

    public void Update(float dt)
    {
        for (var i = 0; i <= LastActive; i++)
        {
            ref var p = ref instances[i];
            ref var l = ref logic[i];
            if (l.TTL < 0)
            {
                p.Radius = 0;
                continue;
            }
            l.TTL -= dt;
            var left = l.TTL * l.InvMaxTTL;
            p.NormalizedLifetime = 1 - left;
            p.Velocity.Y += l.Gravity * dt;
            p.Velocity *= MathF.Pow(l.Drag, dt);
            p.Position += p.Velocity * dt;
            p.Rotation += l.RotationSpeed * dt;
            p.Radius = l.StartSize * left + l.EndSize * (1 - left);
            if (l.TTL < 0)
            {
                free.Enqueue(i, i);
            }
        }
        while (LastActive >= 0 && logic[LastActive].TTL < 0)
        {
            LastActive--;
        }
    }
}
