using System.Numerics;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using OpenMagicka.Gameplay.Spells.Magicks;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>A character's magicks, its boost and the timed effects magicks leave on it (docs/gameplay/MAGICKS.md).</summary>
public sealed partial class Character
{
    /// <summary>A boost waits this long for the next press before the boost state ends.</summary>
    public const float BoostCooldownTime = 0.25f;

    /// <summary>Each boost heals a bubble by this much, or mends a shield by this much.</summary>
    public const float BubbleBoost = 50, ShieldBoost = 100;

    /// <summary>A shield to mend is looked for this far ahead, within this angle of the facing.</summary>
    public const float BoostReach = 20, BoostAngle = MathF.PI / 4;

    private float hasteTime;
    private float enchantTime;
    private bool enchanted;
    private float levitateTime;
    private float levitationFall;
    private bool discarded;

    /// <summary>The player whose wizard this is; null for everyone else.</summary>
    public Player? Player { get; set; }

    /// <summary>Boosts pressed since the boost state last spent them.</summary>
    public int Boosts { get; internal set; }

    public float BoostCooldown { get; private set; }

    public bool IsHasted => hasteTime > 0;

    /// <summary>What the enchantment's following effect is keyed to.</summary>
    internal object EnchantmentLook { get; } = new();

    /// <summary>Under Performance Enchantment (its boost or its withdrawal).</summary>
    public bool IsEnchanted => enchantTime > 0;

    public bool IsLevitating => levitateTime > 0 && !Dead;

    /// <summary>
    /// The magick button (an avatar's boost; MAGICKS.md §2). A queue that spells out an allowed magick
    /// becomes it, and a queue holding a magick casts it: an instant one at once when the state could
    /// not get to it. Otherwise the press tears at vines, or boosts a bubble or a shield ahead.
    /// </summary>
    public void Boost()
    {
        if (Dead || State == CharacterStates.Charge)
        {
            return;
        }
        var idle = CurrentSpell is null && CastType is CastType.None or CastType.Magick;   // a cast under way keeps its spell
        var magick = idle ? MagickBook.Recognize(SpellQueue.Entries) : MagickType.None;
        if (magick != MagickType.None && MayCast(magick))
        {
            SpellQueue = SpellQueue.WithMagick(magick);
        }
        if (idle && SpellQueue.Count > 0 && SpellQueue.Entries[0].Magick != MagickType.None)
        {
            CastType = CastType.Magick;
            if (MagickEffects.IsInstant(Spell.Magick) && (Statuses.Has(StatusEffects.Frozen) || State == CharacterStates.Flying || State == CharacterStates.KnockDown))
            {
                CastSpell(fromStaff: true);
            }
            return;
        }
        if (IsEntangled)
        {
            DecreaseEntanglement(0.5f, Elements.Earth);
            if (State != CharacterStates.Cast)
            {
                GoToAnimation("spec_entangled_attack", 0.1f);
            }
            return;
        }
        if (ShieldToBoost() is null && SelfShield is not { IsBubble: true })
        {
            return;
        }
        BoostAgain();
    }

    /// <summary>Whether this character may cast the magick in its game (an NPC, or a game without players, always may).</summary>
    public bool MayCast(MagickType magick) => Manager?.Session is not { } session || session.IsMagickAllowed(Player, magick);

    /// <summary>
    /// The nearest standing shield the character faces: met by a segment 20 along its flat facing, the
    /// meeting point nearer than 20 and within π/4 of the facing.
    /// </summary>
    public SpellShield? ShieldToBoost()
    {
        if (Manager is not { } manager)
        {
            return null;
        }
        var facing = Body.Facing with { Y = 0 };
        facing = facing.LengthSquared() > 1e-12f ? Vector3.Normalize(facing) : -Vector3.UnitZ;
        var flat = Position with { Y = 0 };
        var path = new Segment(Position, facing * BoostReach);
        SpellShield? best = null;
        var bestDistance = BoostReach * BoostReach;
        foreach (var shield in manager.Shields.OfType<SpellShield>())
        {
            if (shield.HitPoints <= 0 || !shield.SegmentIntersect(path, 0, out var hit))
            {
                continue;
            }
            var to = (hit with { Y = 0 }) - flat;
            var distance = to.LengthSquared();
            if (distance < bestDistance && (distance <= float.Epsilon || MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(to), facing), -1, 1)) < BoostAngle))
            {
                (best, bestDistance) = (shield, distance);
            }
        }
        return best;
    }

    /// <summary>One more boost: a first press counts one; while boosting, a press counts one more (two with ShieldBoost); boost auras and buffs multiply.</summary>
    private void BoostAgain()
    {
        var boosts = Boosts + 1 + (HasPassiveAbility(PassiveAbility.ShieldBoost) ? 1 : 0);
        if (State != CharacterStates.Boost)
        {
            boosts = 1;
        }
        (Boosts, BoostCooldown) = (boosts * BoostFactor, BoostCooldownTime);
    }

    /// <summary>The boost state's work: a bubble heals, else a shield ahead is mended; false with neither (the state ends).</summary>
    internal bool SpendBoosts()
    {
        if (SelfShield is SelfShield { IsBubble: true } bubble)
        {
            bubble.AbsorbOverTime(-BubbleBoost * Boosts, Elements.Shield);
            Boosts = 0;
            return true;
        }
        if (ShieldToBoost() is not { } shield)
        {
            return false;
        }
        shield.Repair(ShieldBoost * Boosts);
        Boosts = 0;
        return true;
    }

    /// <summary>Haste for a time (a recast starts it over): speed ×(2 − 0.5^t) while t runs down.</summary>
    public void Haste(float duration) => hasteTime = float.IsFinite(duration) ? duration : throw new ArgumentOutOfRangeException(nameof(duration));

    /// <summary>Performance Enchantment for a time: a boost while more than 4 s are left, then the withdrawal.</summary>
    public void Enchant(float duration) => (enchantTime, enchanted) = (float.IsFinite(duration) ? duration : throw new ArgumentOutOfRangeException(nameof(duration)), true);

    /// <summary>Takes on a status directly (a magick's grease), through its defences.</summary>
    internal void AddStatus(StatusEffect effect) => Statuses = Statuses.Add(effect, Defences).Set;

    /// <summary>Stunned for a time (a magick's hold).</summary>
    public void Stun(float seconds) => stunTimer = float.IsFinite(seconds) && seconds >= 0 ? seconds : throw new ArgumentOutOfRangeException(nameof(seconds));

    public void Unstun() => stunTimer = 0;

    /// <summary>Levitation for a time: no gravity, hovering over the ground (MAGICKS.md §3.3); 0 ends it.</summary>
    public void SetLevitate(float duration)
    {
        if (!(duration > 0))
        {
            StopLevitate();
            return;
        }
        levitateTime = duration;
        Body.Body.AffectedByGravity = false;
    }

    public void StopLevitate()
    {
        levitateTime = 0;
        Body.Body.AffectedByGravity = !Template.Flies;
    }

    /// <summary>
    /// Casts the magick the spell holds (the <c>CastSpell</c> action, or at once for an instant one).
    /// In the cast state the cast type stays until the clip ends, so no other cast starts under it.
    /// </summary>
    private void CastMagick()
    {
        var magick = Spell.Magick;
        Spell = default;
        if (State != CharacterStates.Cast)
        {
            CastType = CastType.None;
        }
        MagickEffects.For(magick)?.Execute(this);
    }

    /// <summary>The boost's wait, haste's speed and levitation's hovering, after the self-shield's speed.</summary>
    private void UpdateMagicks(float dt)
    {
        BoostCooldown = MathF.Max(0, BoostCooldown - dt);
        if (hasteTime > 0)
        {
            hasteTime -= dt;
            if (Dead)
            {
                hasteTime = 0;
            }
            else
            {
                Body.SpeedMultiplier *= 2 - MathF.Pow(0.5f, hasteTime);
            }
        }
        UpdateEnchantment(dt);
        if (levitateTime > 0 && Dead)
        {
            StopLevitate();                                                   // the dead do not levitate
        }
        if (levitateTime > 0)
        {
            levitateTime -= dt;
            Hover(dt);
            if (levitateTime <= 0)
            {
                StopLevitate();
            }
        }
    }

    /// <summary>
    /// The enchantment's speed: ×(2 − 0.5^(t − 4)) while boosted, ×(1 − 0.75^t) in the last 4 s.
    /// As the original, its auras stay as they were for their 10 s, and its end clears the self-cast auras.
    /// </summary>
    private void UpdateEnchantment(float dt)
    {
        if (enchantTime <= 0)
        {
            return;
        }
        enchantTime -= dt;
        if (enchantTime <= 0)
        {
            ClearSelfCastAuras();
            return;
        }
        if (enchantTime < PerformanceEnchantment.Withdrawal)
        {
            if (enchanted)
            {
                enchanted = false;
                RaiseCue(new Animation.SoundCue(PerformanceEnchantment.NerfSound, MagickEffects.AdditionalBank, Position, Body.Facing));
                RaiseCue(new Animation.ItemEffectEndCue(EnchantmentLook));   // the withdrawal ends the glow
            }
            if (!Dead)
            {
                Body.SpeedMultiplier *= 1 - MathF.Pow(0.75f, enchantTime);
            }
        }
        else if (!Dead)
        {
            Body.SpeedMultiplier *= 2 - MathF.Pow(0.5f, enchantTime - PerformanceEnchantment.Withdrawal);
        }
    }

    /// <summary>
    /// Holds the levitating body over the ground: a probe from halfway down the body to 0.75 under
    /// its feet lifts it by what it is short of; with no ground it sinks, faster and faster.
    /// </summary>
    private void Hover(float dt)
    {
        if (world.IsDisposed || world.IsStepping)
        {
            return;
        }
        var half = Body.HeightOffset * 0.5f;
        var from = Position + Vector3.UnitY * half;
        var to = from + Vector3.UnitY * (-0.75f + half);
        var velocity = Body.Body.Velocity;
        if (world.CastSegment(from, to, levelOnly: true) is { } ground)
        {
            velocity.Y += (1 - ground.Fraction) * 0.75f;
            levitationFall = 0;
        }
        else
        {
            velocity.Y -= dt * (0.5f + 1.5f * levitationFall);
            levitationFall = MathF.Min(levitationFall + dt * 0.5f, 1);
        }
        Body.Body.Velocity = velocity;
    }

    /// <summary>Rises from a revival with a share of its hit points, playing its revive clip.</summary>
    internal void Rise(float share)
    {
        (Dead, HitPoints) = (false, MaxHitPoints * share);
        SpawnAnimation = HasAnimation("revive") ? "revive" : "hit";
        ChangeState(CharacterStates.Ressurection);
    }

    /// <summary>The first book of magick lying within pick-up reach.</summary>
    public BookOfMagick? FindBook() => Manager?.Query(Position, PickUpReach).OfType<BookOfMagick>().FirstOrDefault();

    /// <summary>Picks up a book of magick within reach, unlocking its magick (MAGICKS.md §1.2).</summary>
    public bool PickUp(BookOfMagick book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return !Dead && Manager is not null && ReferenceEquals(book.Manager, Manager) && Vector3.Distance(book.Position, Position) <= PickUpReach + book.Radius && book.Unlock(Player);
    }

    /// <summary>Only a script's explicit kill ends it: Crash to Desktop spares it.</summary>
    public bool CannotDieWithoutExplicitKill { get; set; }

    /// <summary>Vanishes on the spot (Crash to Desktop): dead at once, its spell and auras gone, no corpse left.</summary>
    public void Terminate()
    {
        StopSpell();
        auras.Clear();
        buffs.Clear();
        RemoveSelfShield();
        StopLevitate();
        (HitPoints, Dead, discarded, undieTimer) = (0, true, true, float.NaN);
    }

    /// <summary>The corpse goes on the next removal (its wizard rose anew elsewhere).</summary>
    internal void RemoveCorpse() => discarded = true;
}
