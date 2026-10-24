using System.Numerics;
using System.Runtime.CompilerServices;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>Grease: a half-second sweep of grease ahead that greases who it touches and leaves six slick fields (MAGICKS.md §5.7).</summary>
public sealed class Grease() : Magick(MagickType.Grease, SweepClip)
{
    public const string SpraySound = "magick_grease", SprayEffect = "magick_grease_spray";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity)
        {
            return false;
        }
        var spray = new GreaseSpray(caster);
        manager.Add(spray);
        caster.RaiseCue(new SoundCue(SpraySound, MagickEffects.SpellsBank, caster.Position, caster.Body.Facing));
        caster.RaiseCue(new ItemEffectCue(SprayEffect, caster.CastSource, caster.Body.Facing, Follow: true, spray));
        return true;
    }
}

/// <summary>
/// The sweep: over 0.5 s its way turns from 1.4·π/4 to one side to π/4 to the other, 6 long but cut by
/// shields and walls. Characters it touches (within 10° and 5 high) are given a grease status of no
/// strength, which as the original does not take; as it passes each sixth of its time, a field lands
/// 1.5 short of its end. A simulation object.
/// </summary>
public sealed class GreaseSpray : Entity, IMagickEffect
{
    public const float Lifetime = 0.5f, Reach = 6, Arc = 0.17453292f, Height = 5, Short = 1.5f;

    public const int Fields = 6;

    private float ttl = Lifetime;
    private EntityManager? level;

    internal GreaseSpray(Character caster) => Caster = caster;

    public Character Caster { get; }

    public override Vector3 Position => Caster.Position;

    public override float Radius => Reach;

    public override bool IsDead => ttl <= 0 || Caster.Manager is null;

    public override bool IsRemovable => IsDead;

    protected internal override void OnAdded() => level = Manager;

    protected internal override void OnRemoved() => level?.RaiseCue(this, new ItemEffectEndCue(this));

    /// <summary>Nullify ends it.</summary>
    public void End() => ttl = 0;

    public override void Update(float dt)
    {
        if (IsDead || Manager is not { } manager || Caster.World.IsDisposed)
        {
            ttl = 0;
            return;
        }
        var before = ttl;
        ttl -= dt;
        var way = Turned(ttl / Lifetime * Fields - 2.5f);
        var reach = Reach;
        var path = new Segment(Caster.Position, way * reach);
        foreach (var shield in manager.Shields.OfType<Entity>())
        {
            if (shield.Entry(path) is { } entry)
            {
                (reach, path) = (reach * entry, new Segment(path.Origin, path.Delta * entry));
            }
        }
        if (Caster.World.CastSegment(path.Origin, path.Origin + path.Delta, levelOnly: true) is { } wall)
        {
            reach *= wall.Fraction;
        }
        foreach (var entity in manager.Query(Caster.Position, reach, ignoreProtected: true))
        {
            if (entity is Character character && !ReferenceEquals(character, Caster) && !character.Statuses.Has(StatusEffects.Greased)
                && character.ArcIntersect(Caster.Position, way, reach, Arc, Height, out _))
            {
                character.AddStatus(StatusEffect.Create(StatusEffects.Greased, 0, 1));
            }
        }
        var (was, now) = (MathF.Floor(before / Lifetime * Fields), MathF.Floor(ttl / Lifetime * Fields));
        reach -= Short;
        if (now < was && now >= 0 && reach > 0)
        {
            var over = Caster.Position + Turned(now - 2.5f) * reach + Vector3.UnitY * 2;
            if (Caster.World.CastSegment(over, over - Vector3.UnitY * 4, levelOnly: true) is { } floor && manager.Entities.Count < EntityManager.Capacity)
            {
                manager.Add(new GreaseField(Caster, floor.Position));
            }
        }
    }

    /// <summary>The caster's facing turned by <c>−s/2.5 · π/4</c>.</summary>
    private Vector3 Turned(float step) =>
        Vector3.Transform(Caster.Body.Facing, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -step / 2.5f * MathF.PI / 4));
}

/// <summary>
/// A field of grease on the floor: characters on it slide, fire sets it alight and water or cold put it
/// out (MAGICKS.md §5.7). It lasts 60 s, three times faster while burning; burning, it sets fire to
/// everything within 1.5 (once per 0.25 s for all fields together). A simulation object.
/// </summary>
public sealed class GreaseField : Entity
{
    public const float Lifetime = 60, BurnRate = 3, FieldRadius = 1.5f, Warming = 0.5f;

    /// <summary>At most this many fields lie in a level; a new one takes the place of the oldest (the original's pool).</summary>
    public const int MostFields = 72;

    public const string Decal = "grease", BurnEffect = "grease_burning";

    /// <summary>What a burning field sets on fire: Fire, a status of 4 × 60.</summary>
    public static readonly DamageEntry Burn = new(AttackProperties.Status, Elements.Fire, SpellDamage.Fire * 4, 1);

    private static readonly ConditionalWeakTable<EntityManager, Hits> Shared = [];

    private readonly Vector3 position;
    private EntityManager? level;
    private float ttl = Lifetime;
    private float temperature;

    public GreaseField(Character? owner, Vector3 floor) => (Owner, position) = (owner, floor);

    public Character? Owner { get; }

    public bool IsBurning { get; private set; }

    public override Vector3 Position => position;

    public override float Radius => FieldRadius;

    public override bool IsDamageable => true;

    public override bool IsDead => ttl < 0;

    public override bool IsRemovable => IsDead;

    /// <summary>Fire warms it by its magnitude (alight at 1); water or cold cool it (out at 0). It takes no harm.</summary>
    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        if ((damage.Element & Elements.Fire) == Elements.Fire)
        {
            temperature = MathF.Min(1, temperature + damage.Magnitude);
            Light();
        }
        else if (ElementRelations.InclusiveOpposites(damage.Element, Elements.Fire))
        {
            temperature = MathF.Max(0, temperature - damage.Magnitude);
            if (IsBurning && temperature <= 0)
            {
                IsBurning = false;
                Manager?.RaiseCue(this, new ItemEffectEndCue(this));
            }
        }
        return DamageResult.None;
    }

    /// <summary>Set alight with a heat (napalm's 3); as the original, a field set alight keeps that heat.</summary>
    public void Ignite(float heat)
    {
        temperature = MathF.Min(1, temperature + heat);
        if (!IsBurning && temperature >= 1)
        {
            temperature = heat;
            Light();
        }
    }

    public void Kill() => ttl = -1;

    protected internal override void OnAdded()
    {
        if (Manager is not { } manager)
        {
            return;
        }
        level = manager;
        var fields = manager.Entities.OfType<GreaseField>().Where(f => !ReferenceEquals(f, this) && !f.IsDead).ToList();
        if (fields.Count >= MostFields)
        {
            fields[0].Kill();
        }
        manager.RaiseCue(this, new DecalCue(Decal, position, Vector3.UnitY, Vector3.UnitX, 1));
    }

    protected internal override void OnRemoved()
    {
        if (IsBurning)
        {
            level?.RaiseCue(this, new ItemEffectEndCue(this));
        }
    }

    public override void Update(float dt)
    {
        if (Manager is not { } manager)
        {
            return;
        }
        var hits = Shared.GetValue(manager, _ => new Hits());
        hits.Tick(this, dt);
        if (IsBurning)
        {
            temperature = MathF.Min(1, temperature + dt * Warming);
            ttl -= dt * BurnRate;
            foreach (var entity in manager.Query(position, FieldRadius))
            {
                if (!ReferenceEquals(entity, this) && entity.IsDamageable && hits.Add(entity))
                {
                    entity.Damage(Burn, Owner, position);
                }
            }
        }
        else
        {
            ttl -= dt;
        }
        foreach (var character in manager.Query(position, FieldRadius).OfType<Character>())
        {
            if (character.Body.IsTouchingGround && !character.Template.Nonslippery)
            {
                character.Body.Conditions = character.Body.Conditions with { IsOnGrease = true };
            }
        }
    }

    private void Light()
    {
        if (!IsBurning && temperature >= 1)
        {
            IsBurning = true;
            Manager?.RaiseCue(this, new ItemEffectCue(BurnEffect, position, Vector3.UnitY, Follow: false, this));
        }
    }

    /// <summary>All fields' memory of whom they set alight, timed by the first field to tick it each frame.</summary>
    private sealed class Hits
    {
        private readonly Dictionary<Entity, float> times = new(ReferenceEqualityComparer.Instance);
        private GreaseField? keeper;

        public bool Add(Entity entity) => times.TryAdd(entity, 0.25f);

        public void Tick(GreaseField field, float dt)
        {
            if (keeper is null || keeper.Manager is null)
            {
                keeper = field;
            }
            if (!ReferenceEquals(keeper, field))
            {
                return;
            }
            foreach (var entity in times.Keys.ToList())
            {
                if ((times[entity] -= dt) <= 0)
                {
                    times.Remove(entity);
                }
            }
        }
    }
}
