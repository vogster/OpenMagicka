using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells.Effects;
using Barrier = OpenMagicka.Gameplay.Spells.Effects.Barrier;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>Vortex: a black hole 10 ahead that pulls things in and swallows what reaches its core, growing on them (MAGICKS.md §5.9).</summary>
public sealed class Vortex() : Magick(MagickType.Vortex, GlobalClip)
{
    public const float Ahead = 10;

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity)
        {
            return false;
        }
        manager.Add(new VortexHole(caster, caster.Position + caster.Body.Facing * Ahead));
        return true;
    }
}

/// <summary>
/// The hole: its mass m eases toward a target that wanes by 0.05 a second; it pulls within 10·m and
/// swallows what touches its core (radius 0.5), each swallowed mass raising the target. It is gone
/// when m is. A simulation object.
/// </summary>
public sealed class VortexHole : Entity
{
    public const float StartMass = 0.25f, Reach = 10, Core = 0.5f, Pull = 15, Wane = 0.05f, Ease = 0.1f, Growth = 0.75f;

    public const string Sound = "magick_vortex", Effect = "magick_vortex";

    private readonly Vector3 centre;
    private readonly HashSet<Entity> swallowed = new(ReferenceEqualityComparer.Instance);
    private float mass = StartMass;

    public override float Danger => mass * 20;
    private float target = StartMass;

    internal VortexHole(Character? owner, Vector3 centre) => (Owner, this.centre) = (owner, centre);

    public Character? Owner { get; }

    public float Mass => mass;

    public override Vector3 Position => centre;

    public override float Radius => mass * Reach;

    public override bool IsDead => mass < float.Epsilon;

    public override bool IsRemovable => IsDead;

    /// <summary>Closes it at once.</summary>
    public void Kill() => (mass, target) = (0, 0);

    protected internal override void OnAdded()
    {
        Manager?.RaiseCue(this, new SoundCue(Sound, MagickEffects.SpellsBank, centre, Vector3.UnitX));
        Manager?.RaiseCue(this, new WorldEffectCue(Effect, centre, Vector3.UnitY));
    }

    public override void Update(float dt)
    {
        mass = target + (mass - target) * MathF.Pow(Ease, dt);
        if (mass < 1e-6f || Manager is not { } manager)
        {
            mass = MathF.Max(0, mass);
            return;
        }
        var radius = mass * Reach;
        foreach (var entity in manager.Query(centre, radius, ignoreProtected: true))
        {
            if (entity is VortexHole || entity.IsDead || entity is Character { IsEthereal: true })
            {
                continue;
            }
            if (Vector3.Distance(entity.Position, centre) < Core + entity.Radius)
            {
                Swallow(entity);
                if (swallowed.Add(entity))
                {
                    target = 1 - (1 - target) * MathF.Pow(Growth, BodyMass(entity));   // its mass counts once, even if it dies over a clip
                }
                continue;
            }
            var distance = Vector3.Distance(centre, entity.Position);
            var strength = Pull * (1 - distance / radius);
            if (distance <= 1e-6f || strength <= 0)
            {
                continue;
            }
            var force = (centre - entity.Position) / distance * strength;
            switch (entity)
            {
                case Character character:
                    character.Body.AddExtraForce(force);
                    break;
                case Missile missile when !missile.Body.IsRemoved:
                    missile.Body.Velocity += force;
                    break;
                case WorldItem item when !item.Body.IsRemoved:
                    item.Body.Velocity += force;
                    break;
            }
        }
        target -= dt * Wane;
    }

    /// <summary>A character without gibs vanishes, one with them is overkilled; anything else simply ends.</summary>
    private static void Swallow(Entity entity)
    {
        switch (entity)
        {
            case Character character when !character.Template.HasGibs:
                character.Terminate();
                break;
            case Character character:
                character.OverKill();
                break;
            case Missile missile:
                missile.Kill();
                break;
            case WorldItem item:
                item.Take();
                break;
            case Barrier barrier:
                barrier.Kill();
                break;
            case SpellShield shield:
                shield.Kill();
                break;
            case SpellMine mine:
                mine.Detonate();
                break;
            case GreaseField field:
                field.Kill();
                break;
        }
    }

    private static float BodyMass(Entity entity) => entity switch
    {
        Character c => c.Body.Body.Mass,
        Missile => Missile.Mass,
        WorldItem => WorldItem.Mass,
        _ => 1,
    };
}
