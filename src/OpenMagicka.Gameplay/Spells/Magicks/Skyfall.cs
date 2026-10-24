using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells.Effects;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>
/// Meteor Shower: for 17.5 s, every 0.4 to 0.8 s one or two burning boulders fall within 20 of the
/// caster (MAGICKS.md §4.5). One shower at a time: casting it again only starts its time over. It
/// fails indoors.
/// </summary>
public sealed class MeteorShower() : Magick(MagickType.MeteorS, GlobalClip)
{
    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Indoors)
        {
            return Fail(caster);
        }
        if (manager.Entities.OfType<MeteorShowerEffect>().FirstOrDefault(s => !s.IsDead && ReferenceEquals(s.Caster.Manager, manager)) is { } running)
        {
            running.Restart();
            return true;
        }
        if (manager.Entities.Count >= EntityManager.Capacity)
        {
            return false;
        }
        manager.Add(new MeteorShowerEffect(caster));
        return true;
    }
}

/// <summary>The shower on its way; it follows its caster. The darkened light comes with the renderer. A simulation object.</summary>
public sealed class MeteorShowerEffect : Entity, IMagickEffect
{
    public const float Lifetime = 17.5f, Spread = 20, Height = 146, Splash = 4;

    public const string FallSound = "magick_meteor_preblast";

    /// <summary>Each meteor: Earth 5 and Fire 5, falling at 20.</summary>
    public static readonly Spell Meteor = new(earth: 5, fire: 5);

    public static readonly Vector3 FallVelocity = new(0.01f, -20, -0.01f);

    private float ttl = Lifetime;
    private float spawnTimer;

    internal MeteorShowerEffect(Character caster) => Caster = caster;

    public Character Caster { get; }

    public override Vector3 Position => Caster.Position;

    public override float Radius => Spread;

    public override bool IsDead => ttl <= 0;

    public override bool IsRemovable => IsDead;

    internal void Restart() => ttl = Lifetime;

    /// <summary>Nullify ends it.</summary>
    public void End() => ttl = 0;

    public override void Update(float dt)
    {
        ttl = Caster.Manager is null ? 0 : ttl - dt;                         // it ends with its caster
        spawnTimer -= dt;
        if (IsDead || spawnTimer > 0)
        {
            return;
        }
        spawnTimer += 0.4f + (float)Caster.NextRandom() * 0.4f;
        var count = (int)(Caster.NextRandom() * 2) + 1;
        for (var i = 0; i < count; i++)
        {
            var distance = MathF.Sqrt((float)Caster.NextRandom()) * Spread;
            var angle = (float)Caster.NextRandom() * MathF.Tau;
            var from = Caster.Position + new Vector3(MathF.Cos(angle) * distance, Height, MathF.Sin(angle) * distance);
            if (SpellMissiles.Launch(Caster, Meteor, from, FallVelocity, Splash, 1) is { } meteor)
            {
                Manager?.RaiseCue(meteor, new SoundCue(FallSound, MagickEffects.SpellsBank, from, Vector3.UnitX));
            }
        }
    }
}

/// <summary>
/// Napalm: a smoke marker 15 ahead; 5 s later a plane passes over it, and six fire bombs fall along
/// its line across the cast (MAGICKS.md §4.6). Until the plane comes, casting it again moves the
/// marker; while it passes, a cast fails. It fails indoors.
/// </summary>
public sealed class Napalm() : Magick(MagickType.Napalm, SweepClip)
{
    public const float Ahead = 15;

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Indoors)
        {
            return Fail(caster);
        }
        var target = caster.Position + caster.Body.Facing * Ahead;
        if (manager.Entities.OfType<NapalmStrike>().FirstOrDefault(s => !s.IsDead) is { } coming)
        {
            return coming.MoveTo(target) || Fail(caster);                   // the plane is passing
        }
        if (manager.Entities.Count >= EntityManager.Capacity)
        {
            return false;
        }
        manager.Add(new NapalmStrike(caster, target, caster.Body.Facing));
        return true;
    }
}

/// <summary>The strike on its way: the wait, then the pass. The plane and its shadow come with the renderer. A simulation object.</summary>
public sealed class NapalmStrike : Entity, IMagickEffect
{
    public const float Wait = 5, Pass = 2, Run = 120, Bombs = 6, BlastRadius = 5;

    public const string MarkerEffect = "magick_smoke_target", BombEffect = "magick_napalm", BombSound = "magick_napalm", PlaneSound = "magick_plane_passby";

    public static readonly IReadOnlyList<DamageEntry> Blast =
    [
        new(AttackProperties.Damage, Elements.Fire, 2000, 1),
        new(AttackProperties.Knockback, Elements.Earth, 500, 2),
        new(AttackProperties.Status, Elements.Fire, 300, 3),
    ];

    private readonly Vector3 line;
    private Vector3 target;
    private float timer = Wait;
    private bool passing;
    private int bombs;

    internal NapalmStrike(Character caster, Vector3 target, Vector3 facing)
    {
        (Caster, this.target) = (caster, target);
        line = Vector3.TransformNormal(facing with { Y = 0 }, Matrix4x4.CreateRotationY(MathF.PI / 2));
        line = line.LengthSquared() > 1e-12f ? Vector3.Normalize(line) : Vector3.UnitX;
    }

    public Character Caster { get; }

    /// <summary>The marker's spot.</summary>
    public override Vector3 Position => target;

    public override float Radius => BlastRadius;

    public bool IsPassing => passing;

    public override bool IsDead => passing && timer <= 0;

    public override bool IsRemovable => IsDead;

    /// <summary>Moves the marker while the plane has not come; false once it passes.</summary>
    internal bool MoveTo(Vector3 spot)
    {
        if (passing)
        {
            return false;
        }
        target = spot;
        Manager?.RaiseCue(this, new WorldEffectCue(MarkerEffect, target, line));
        return true;
    }

    protected internal override void OnAdded() => Manager?.RaiseCue(this, new WorldEffectCue(MarkerEffect, target, line));

    /// <summary>Nullify ends it.</summary>
    public void End() => (passing, timer) = (true, 0);

    public override void Update(float dt)
    {
        timer -= dt;
        if (!passing)
        {
            if (timer <= 0)
            {
                (passing, timer) = (true, Pass);
                Manager?.RaiseCue(this, new CameraShakeCue(target, 0.5f, 2));
                Manager?.RaiseCue(this, new SoundCue(PlaneSound, MagickEffects.AdditionalBank, target, line));
            }
            return;
        }
        var flown = 1 - timer / Pass;
        if (bombs < Bombs && flown >= 0.705f + bombs * 0.015f)
        {
            Bomb(Vector3.Lerp(target + line * Run, target - line * Run, flown - 0.25f));
            bombs++;
        }
    }

    /// <summary>A bomb where the floor is under the point: a napalm blast, and a field of grease set alight (heat 3).</summary>
    private void Bomb(Vector3 over)
    {
        if (Manager is not { } manager || Caster.World.IsDisposed
            || Caster.World.CastSegment(over + Vector3.UnitY * 10, over - Vector3.UnitY * 10, levelOnly: true) is not { } floor)
        {
            return;
        }
        manager.RaiseCue(this, new WorldEffectCue(BombEffect, floor.Position, Vector3.UnitY));
        manager.RaiseCue(this, new SoundCue(BombSound, MagickEffects.AdditionalBank, floor.Position, Vector3.UnitY));
        manager.RaiseCue(this, new CameraShakeCue(floor.Position, 0.25f, 0.5f));
        if (manager.Entities.Count < EntityManager.Capacity)
        {
            var field = new GreaseField(Caster, floor.Position);
            manager.Add(field);
            field.Ignite(3);
        }
        Explode(manager, Caster, floor.Position);
    }

    /// <summary>Everything damageable within 5 that a shield does not hide, but the ethereal, takes the napalm, its caster too.</summary>
    public static void Explode(EntityManager manager, Entity? owner, Vector3 at)
    {
        foreach (var entity in manager.Query(at, BlastRadius, ignoreProtected: true))
        {
            if (entity.IsDamageable && entity is not Character { IsEthereal: true })
            {
                entity.Damage(Blast, owner, at);
            }
        }
    }
}
