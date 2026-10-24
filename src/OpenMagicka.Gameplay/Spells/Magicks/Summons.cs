using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Physics;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>Making summoned characters from the level's bestiary (MAGICKS.md §7).</summary>
public static class Summoning
{
    /// <summary>
    /// A character of the asset standing on the ground point, facing a way, summoned by its master
    /// (when there is one); null when the bestiary lacks it or the level is full. What it then does
    /// comes with the AI (OM-40).
    /// </summary>
    public static Character? Call(EntityManager manager, PhysicsWorld world, Character? master, string asset, Vector3 ground, Vector3 facing)
    {
        if (manager.Bestiary?.Find(asset) is not { } found || manager.Entities.Count >= EntityManager.Capacity || world.IsDisposed || world.IsStepping)
        {
            return null;
        }
        var (template, animations) = found;
        var players = Math.Max(1, manager.Session?.Players.Count(p => p.Playing) ?? 1);
        var seed = (int)((master?.NextRandom() ?? 0.5) * int.MaxValue);
        var summon = new Character(template, world, ground + Vector3.UnitY * (template.Length / 2 + template.Radius), players,
            random: new Random(seed), animations: animations);
        try
        {
            manager.Add(summon);
        }
        catch
        {
            world.Remove(summon.Body.Body);
            throw;
        }
        summon.Body.Face(facing);
        if (master is not null)
        {
            summon.Summoned(master);
        }
        return summon;
    }

    /// <summary>The floor under a point, looked for along a probe from <paramref name="above"/> it, <paramref name="down"/> long; or the point.</summary>
    internal static Vector3 Floor(PhysicsWorld world, Vector3 point, float above, float down)
    {
        var from = point + Vector3.UnitY * above;
        return world.CastSegment(from, from - Vector3.UnitY * down, levelOnly: true)?.Position ?? point;
    }
}

/// <summary>Raise Dead: for 8.1 s the ground 4 ahead gives up a zombie every 2 s, five in all (MAGICKS.md §7.1).</summary>
public sealed class SummonUndead() : Magick(MagickType.SUndead, DirectClip)
{
    public const float Ahead = 4;

    public const string Cry = "magick_raise_dead";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager)
        {
            return false;
        }
        if (manager.Entities.Count >= EntityManager.Capacity)
        {
            return true;                                                      // as the original: the cast counts, nothing rises
        }
        var spot = Summoning.Floor(caster.World, manager.Walkable(caster.Position + caster.Body.Facing * Ahead), 0, 3);
        manager.Add(new UndeadRising(caster, spot));
        Sound(caster, Cry);
        return true;
    }
}

/// <summary>The ground giving up its dead. A simulation object.</summary>
public sealed class UndeadRising : Entity, IMagickEffect
{
    public const float Lifetime = 8.1f, Interval = 2, Spread = 3;

    public const string Asset = "data/characters/zombie", GroundEffect = "magick_summonundead_ground";

    private readonly Character caster;
    private readonly Vector3 spot;
    private float ttl = Lifetime;
    private float spawnTimer;
    private EntityManager? level;

    internal UndeadRising(Character caster, Vector3 spot) => (this.caster, this.spot) = (caster, spot);

    public override Vector3 Position => spot;

    public override float Radius => Spread;

    public override bool IsDead => ttl <= 0;

    public override bool IsRemovable => IsDead;

    /// <summary>The ground effect (four times its size, renderer) lasts as long as the rising.</summary>
    protected internal override void OnAdded()
    {
        level = Manager;
        level?.RaiseCue(this, new ItemEffectCue(GroundEffect, spot, Vector3.UnitX, Follow: false, this));
    }

    protected internal override void OnRemoved() => level?.RaiseCue(this, new ItemEffectEndCue(this));

    /// <summary>Nullify ends it.</summary>
    public void End() => ttl = 0;

    public override void Update(float dt)
    {
        ttl -= dt;
        spawnTimer -= dt;
        if (IsDead || spawnTimer > 0 || Manager is not { } manager)
        {
            return;
        }
        spawnTimer += Interval;
        var off = new Vector3(((float)caster.NextRandom() - 0.5f) * Spread, 0, ((float)caster.NextRandom() - 0.5f) * Spread);
        var ground = Summoning.Floor(caster.World, manager.Walkable(spot + off), 0, 10);
        var angle = (float)caster.NextRandom() * MathF.Tau;
        if (Summoning.Call(manager, caster.World, caster, Asset, ground, new Vector3(MathF.Sin(angle), 0, -MathF.Cos(angle))) is { } zombie)
        {
            zombie.ForceAnimation("spawn");                                   // as the original: only the clip, no rising state
        }
    }
}

/// <summary>Summon Elemental: an egg 3 ahead that hatches into an elemental of the element it is struck with (MAGICKS.md §7.2).</summary>
public sealed class SummonElemental() : Magick(MagickType.SElemental, DirectClip)
{
    public const float Ahead = 3;

    public const string Cry = "magick_summon_elemental", Effect = "magick_summonelemental";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager || manager.Entities.Count >= EntityManager.Capacity)
        {
            return true;
        }
        var spot = Summoning.Floor(caster.World, manager.Walkable(caster.Position + caster.Body.Facing * Ahead), 1, 10);
        manager.Add(new ElementalEgg(caster.World, spot, caster.Body.Facing, caster, (int)(caster.NextRandom() * int.MaxValue)));
        manager.RaiseCue(caster, new WorldEffectCue(Effect, spot, caster.Body.Facing));
        Sound(caster, Cry);
        return true;
    }
}

/// <summary>
/// An elemental's egg: it remembers the last blow of each element (the status elements, Lightning,
/// Arcane) and hatches into the elemental of the one whose blow was strongest. A level's egg may
/// also hatch at random when a wizard comes near (MAGICKS.md §7.2). A simulation object.
/// </summary>
public sealed class ElementalEgg : Entity
{
    public const float EggRadius = 1;

    public const Elements Hatching = ElementSets.StatusEffects | Elements.Lightning | Elements.Arcane;

    /// <summary>What a wizard coming near hatches, at random.</summary>
    public static readonly IReadOnlyList<Elements> ProximityHatches =
        [Elements.Arcane, Elements.Cold, Elements.Poison, Elements.Fire, Elements.Life, Elements.Water, Elements.Steam, Elements.Lightning];

    private readonly Dictionary<Elements, float> struck = [];
    private readonly Random random;
    private float proximityTimer;
    private readonly PhysicsWorld world;
    private readonly Vector3 ground;
    private Elements hatch;
    private EntityManager? level;

    public ElementalEgg(PhysicsWorld world, Vector3 ground, Vector3 facing, Character? summoner = null, int seed = 0) =>
        (this.world, this.ground, Facing, Summoner, random) = (world, ground, facing, summoner, new Random(seed));

    public Character? Summoner { get; }

    public Vector3 Facing { get; }

    /// <summary>The element it hatches into; None while it waits.</summary>
    public Elements Hatch => hatch;

    /// <summary>How near a wizard hatches it (checked each second); 0, never. A level's eggs set it.</summary>
    public float Proximity { get; set; }

    public override Vector3 Position => ground + Vector3.UnitY * EggRadius;

    public override float Radius => EggRadius;

    public override bool IsDamageable => true;

    public override bool IsDead => hatch != Elements.None;

    public override bool IsRemovable => IsDead;

    /// <summary>A blow of a hatching element is remembered, as amount × magnitude per element; it is struck.</summary>
    public override DamageResult Damage(DamageEntry damage, Entity? attacker, Vector3 attackPosition)
    {
        if ((damage.Element & Hatching) == 0)
        {
            return DamageResult.None;
        }
        foreach (var element in Enumerable.Range(0, ElementIndex.Count).Select(i => (Elements)(1 << i)).Where(e => (damage.Element & e) != 0 && (Hatching & e) != 0))
        {
            struck[element] = damage.Amount * damage.Magnitude;
        }
        return DamageResult.Hit;
    }

    /// <summary>
    /// Killed outright it would be a Shield elemental, as the original; but its next update picks
    /// again from its blows, and a Shield egg removed first hatches nothing.
    /// </summary>
    public void Kill()
    {
        if (hatch == Elements.None)
        {
            hatch = Elements.Shield;
        }
    }

    protected internal override void OnAdded() => level = Manager;

    public override void Update(float dt)
    {
        hatch = Elements.None;
        var best = float.MinValue;
        foreach (var (element, amount) in struck)
        {
            if (amount > best)
            {
                (hatch, best) = (element, amount);
            }
        }
        proximityTimer += dt;
        if (Proximity <= 0 || proximityTimer <= 1 || Manager is not { } manager)
        {
            return;
        }
        proximityTimer -= 1;
        if (manager.Query(Position, Proximity).OfType<Character>().Any(c => c.IsPlayer && Vector3.DistanceSquared(c.Position, Position) <= Proximity * Proximity))
        {
            hatch = ProximityHatches[random.Next(ProximityHatches.Count)];
        }
    }

    /// <summary>It hatches as it goes: the elemental rises where it lay.</summary>
    protected internal override void OnRemoved()
    {
        if (level is null || (hatch & Hatching) == 0 || world.IsDisposed || world.IsStepping)
        {
            return;
        }
        if (Summoning.Call(level, world, Summoner, $"data/characters/elemental_{hatch.ToString().ToLowerInvariant()}", ground, Facing) is { } elemental)
        {
            elemental.SpawnAnimation = "special0";
            elemental.ChangeState(CharacterStates.Ressurection);
        }
    }
}

/// <summary>
/// Summon Phoenix: a phoenix descends near the caster, bursts into flame around it, and raises
/// every dead player (MAGICKS.md §7.3). Indoors it only cries. One at a time; a cast while one is
/// on its way does nothing, and counts.
/// </summary>
public sealed class SummonPhoenix() : Magick(MagickType.SPhoenix, DirectClip)
{
    public const string Cry = "magick_summon_phoenix", IndoorsSound = "magick_summon_phoenix_indoors";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager)
        {
            return true;
        }
        if (manager.Indoors)
        {
            Sound(caster, IndoorsSound);                                      // the original's indoor shake is never reached
            return true;
        }
        if (manager.Entities.Count >= EntityManager.Capacity || manager.Entities.OfType<PhoenixDescent>().Any(p => !p.IsDead))
        {
            return true;
        }
        var off = new Vector3(((float)caster.NextRandom() - 0.5f) * 4, 0, ((float)caster.NextRandom() - 0.5f) * 4);
        var spot = Summoning.Floor(caster.World, manager.Walkable(caster.Position + off), 1, 3);
        manager.Add(new PhoenixDescent(caster, spot));
        Sound(caster, Cry);
        return true;
    }
}

/// <summary>The phoenix on its way down (its descent clip, 2.083 s), then its burst. A simulation object.</summary>
public sealed class PhoenixDescent : Entity, IMagickEffect
{
    public const float Descent = 2.083f, BlastRadius = 6;

    public const string BlastEffect = "magick_phoenix_blast", BlastSound = "spell_fire_area";

    public static readonly IReadOnlyList<DamageEntry> Burst =
    [
        new(AttackProperties.Damage, Elements.Fire, 400, 1),
        new(AttackProperties.Status, Elements.Fire, 200, 5),
        new(AttackProperties.Knockdown, Elements.Fire, 400, 2),
    ];

    private readonly Character caster;
    private readonly Vector3 spot;
    private float timer = Descent;
    private bool done;

    internal PhoenixDescent(Character caster, Vector3 spot) => (this.caster, this.spot) = (caster, spot);

    public override Vector3 Position => spot;

    public override float Radius => BlastRadius;

    public override bool IsDead => done;

    public override bool IsRemovable => done;

    /// <summary>Nullify ends it before it lands.</summary>
    public void End() => done = true;

    public override void Update(float dt)
    {
        timer -= dt;
        if (done || timer > 0 || Manager is not { } manager)
        {
            return;
        }
        done = true;
        var at = spot + Vector3.UnitY * 0.5f;
        foreach (var entity in manager.Query(at, BlastRadius))
        {
            if (entity.IsDamageable)
            {
                entity.Damage(Burst, caster, at);
            }
        }
        manager.RaiseCue(this, new CameraShakeCue(at, 1, 1));
        manager.RaiseCue(this, new WorldEffectCue(BlastEffect, at, Vector3.UnitX));
        manager.RaiseCue(this, new SoundCue(BlastSound, MagickEffects.SpellsBank, at, Vector3.UnitX));
        Rebirth(manager);
    }

    /// <summary>Every player a revival may raise rises around the phoenix (a quarter turn apart), with all its hit points.</summary>
    private void Rebirth(EntityManager manager)
    {
        if (manager.Session is not { } session)
        {
            return;
        }
        foreach (var player in session.Players)
        {
            if (Revive.CanRise(player) && manager.Entities.Count + 2 <= EntityManager.Capacity)
            {
                var turn = Matrix4x4.CreateRotationY(player.Id * MathF.Tau * 0.25f);
                var place = Summoning.Floor(caster.World, manager.Walkable(spot + Vector3.TransformNormal(-Vector3.UnitZ, turn)), 1, 3);
                manager.Add(new ReviveEffect(player, caster.World, place, 1));
            }
        }
    }
}
