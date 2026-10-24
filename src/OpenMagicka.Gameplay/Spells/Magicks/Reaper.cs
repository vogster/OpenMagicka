using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>
/// Summon Death: Death comes for the weakest living thing near (MAGICKS.md §7.4) while time slows
/// to a quarter. One at a time.
/// </summary>
public sealed class SummonDeath() : Magick(MagickType.SDeath, DirectClip)
{
    public const float SearchRadius = 40, Distance = 7;

    public const string Cry = "magick_summon_death";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity
            || manager.Entities.OfType<Reaper>().Any(r => !r.IsDead))
        {
            return Fail(caster);
        }
        var target = Victim(manager, caster) ?? caster;
        var angle = ((float)caster.NextRandom() - 0.5f) * 2 * MathF.PI;
        var from = target.Position + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * Distance;
        Sound(caster, Cry);
        manager.Add(new Reaper(caster.World, target, from));
        return true;
    }

    /// <summary>
    /// The living character within 40 of the caster (standing in for the original's view centre), not
    /// undead, with the smallest share of its hit points; the scan starts at random, and a later equal
    /// one wins. The caster may be it.
    /// </summary>
    public static Character? Victim(EntityManager manager, Character caster)
    {
        var near = manager.Query(caster.Position, SearchRadius).ToList();
        if (near.Count == 0)
        {
            return null;
        }
        var start = (int)(caster.NextRandom() * near.Count) % near.Count;
        Character? victim = null;
        var least = 1f;
        for (var i = 0; i < near.Count; i++)
        {
            if (near[(i + start) % near.Count] is Character { IsDead: false } character && (character.Factions & Factions.Undead) == 0)
            {
                var share = character.HitPoints / character.MaxHitPoints;
                if (share > 0 && share <= least)
                {
                    (least, victim) = (share, character);
                }
            }
        }
        return victim;
    }
}

/// <summary>
/// Death itself: ethereal, it glides to its victim and swings its scythe, which takes all the hit
/// points of whoever it cuts. Its time runs on real time while it slows the world. Its looks (and the
/// half-grey screen while it is about) come with the renderer. A simulation object.
/// </summary>
public sealed class Reaper : Entity, IMagickEffect
{
    public const float Speed = 8, Slowed = 0.25f, HoverHeight = 1.2f, Rise = 0.833f, Fall = 1.667f, Vanish = 1.042f;

    /// <summary>The share of the fall in which the blade cuts, and how long one it cut is spared.</summary>
    public const float CutFrom = 0.1f, CutTo = 0.8f, Spared = 1.5f;

    /// <summary>The blade's reach from Death (its bones come with the renderer; this is our stand-in).</summary>
    public const float Reach = 2;

    public const int CharactersBank = 0x20, WeaponsBank = 0x80;

    public const string Pursuit = "boss_death_proximity", Swing = "wep_death_scythe_swing", Cut = "wep_death_scythe",
        Gone = "boss_death_death", SpawnEffect = "death_ethereal_spawn", VanishEffect = "death_ethereal_despawn";

    public enum Phase { Glide, Rising, Falling, Vanishing, Done }

    private readonly Physics.PhysicsWorld world;
    private readonly Character target;
    private readonly float rangeSquared;
    private readonly Dictionary<Entity, float> spared = [];
    private Vector3 position;
    private Vector3 facing;
    private float speed = Speed;
    private float spawnDelay = 1 / Slowed * 0.5f;
    private float clip;
    private float slowTo = Slowed;
    private bool swung, struck;
    private EntityManager? level;

    internal Reaper(Physics.PhysicsWorld world, Character target, Vector3 from)
    {
        (this.world, this.target) = (world, target);
        rangeSquared = 1.2f * (0.396f + target.Radius * target.Radius);
        var ground = world.CastSegment(from + Vector3.UnitY, from - Vector3.UnitY * 5, levelOnly: true)?.Position ?? from;
        position = ground + Vector3.UnitY * 1.3f;
        facing = Unit((target.Position - position) with { Y = 0 }, -Vector3.UnitZ);
    }

    public Character Target => target;

    public Phase Now { get; private set; }

    /// <summary>Ethereal until it raises its scythe.</summary>
    public bool IsEthereal => Now == Phase.Glide;

    public Vector3 Facing => facing;

    public override Vector3 Position => position;

    public override float Radius => 0.6f;

    public override bool IsDamageable => !IsEthereal;

    public override bool IsDead => Now == Phase.Done;

    public override bool IsRemovable => IsDead;

    /// <summary>Only Life, once it is solid, strikes it; then it goes.</summary>
    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        if ((damage.Element & Elements.Life) != Elements.Life || IsEthereal)
        {
            return DamageResult.None;
        }
        struck |= damage.Attack.HasFlag(AttackProperties.Damage);
        return DamageResult.Hit;
    }

    /// <summary>It goes at once (time comes back).</summary>
    public void Kill() => Now = Phase.Done;

    /// <summary>Nullify ends it, as every running magick.</summary>
    public void End() => Kill();

    protected internal override void OnAdded()
    {
        level = Manager;
        level?.RaiseCue(this, new WorldEffectCue(SpawnEffect, position, facing));
        level?.RaiseCue(this, new SoundCue(Pursuit, CharactersBank, position, facing));
        level?.RaiseCue(this, new SoundCue(TimeWarp.WarpSound, MagickEffects.SpellsBank, position, facing));
    }

    protected internal override void OnRemoved()
    {
        if (level is null)
        {
            return;
        }
        level.TimeModifier = 1;
        level.RaiseCue(this, new SoundStopCue(Pursuit));
        level.RaiseCue(this, new SoundStopCue(TimeWarp.WarpSound));
    }

    /// <summary>The world's time eases toward a quarter (back to whole as it vanishes); Death's own runs on real time.</summary>
    public override void Update(float dt)
    {
        if (Manager is not { } manager || IsDead)
        {
            return;
        }
        var modifier = manager.TimeModifier;
        manager.TimeModifier = Math.Clamp(modifier + (slowTo - modifier) * (dt / slowTo), 0.01f, 1);
        dt /= manager.TimeModifier * manager.TimeMultiplier;                // as the original, by the modifier just set
        foreach (var (entity, left) in spared.ToList())
        {
            if (left - dt <= 0)
            {
                spared.Remove(entity);
            }
            else
            {
                spared[entity] = left - dt;
            }
        }
        Hover(dt);
        if (target.IsDead || target.Manager is null)
        {
            Now = Phase.Done;                                                 // as the original, in every phase: after a kill it does not vanish
            return;
        }
        switch (Now)
        {
            case Phase.Glide: Glide(manager, dt); break;
            case Phase.Rising: Raise(dt); break;
            case Phase.Falling: Swipe(manager, dt); break;
            case Phase.Vanishing: Go(dt); break;
        }
    }

    private void Hover(float dt)
    {
        if (Now == Phase.Glide && spawnDelay <= 0)
        {
            position += Unit((target.Position - position) with { Y = 0 }, Vector3.Zero) * speed * dt;
        }
        if (world.CastSegment(position + Vector3.UnitY, position - Vector3.UnitY * 3, levelOnly: true) is { } floor)
        {
            position.Y += (floor.Position.Y + HoverHeight - position.Y) * dt;   // with no floor under it, it keeps its height
        }
    }

    private void Glide(EntityManager manager, float dt)
    {
        var way = (target.Position - position) with { Y = 0 };
        Turn(way, 8 * dt);
        spawnDelay -= dt;
        if (way.LengthSquared() <= rangeSquared)
        {
            (Now, speed, clip) = (Phase.Rising, 0, Rise);
            manager.RaiseCue(this, new SoundCue(Swing, WeaponsBank, position, facing));
        }
    }

    private void Raise(float dt)
    {
        clip -= dt;
        if (clip <= 0)
        {
            (Now, clip) = (Phase.Falling, Fall);
        }
    }

    /// <summary>The scythe comes down: from 10% to 80% of the fall it cuts what is before Death, each once in 1.5 s.</summary>
    private void Swipe(EntityManager manager, float dt)
    {
        var way = (target.Position - position) with { Y = 0 };
        Turn(way, Math.Clamp(way.LengthSquared() - rangeSquared * 1.1f, 0, 1) * 4 * dt);
        clip -= dt;
        var done = 1 - clip / Fall;
        if (done >= CutFrom && done <= CutTo)
        {
            if (!swung)
            {
                swung = true;
                manager.RaiseCue(this, new SoundCue(Swing, WeaponsBank, position, facing));
            }
            foreach (var victim in manager.Query(position, Reach + 1).OfType<Character>().Where(InBlade).ToList())
            {
                manager.RaiseCue(this, new SoundCue(Cut, WeaponsBank, position, facing));
                victim.Hurt(victim.HitPoints, Elements.Arcane);
                spared[victim] = Spared;
            }
        }
        if (struck || clip <= 0)
        {
            (Now, clip, slowTo) = (Phase.Vanishing, Vanish, 1);
            manager.RaiseCue(this, new SoundCue(Gone, CharactersBank, position, facing));
            manager.RaiseCue(this, new WorldEffectCue(VanishEffect, position, facing));
        }
    }

    private bool InBlade(Character character)
    {
        if (spared.ContainsKey(character) || character.IsDead)
        {
            return false;
        }
        var off = character.Position - position;
        var flat = off with { Y = 0 };
        return MathF.Abs(off.Y) <= Reach && flat.Length() <= Reach + character.Radius && Vector3.Dot(flat, facing) >= 0;
    }

    private void Go(float dt)
    {
        slowTo = 1;
        clip -= dt;
        if (clip <= 0)
        {
            Now = Phase.Done;
        }
    }

    private void Turn(Vector3 way, float amount)
    {
        if (way.LengthSquared() > 1e-6f)
        {
            facing = Unit(Vector3.Lerp(facing, Vector3.Normalize(way), Math.Clamp(amount, 0, 1)), facing);
        }
    }

    private static Vector3 Unit(Vector3 way, Vector3 fallback) => way.LengthSquared() > 1e-6f ? Vector3.Normalize(way) : fallback;
}
