using System.Numerics;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Items;

/// <summary>Who a melee strike may hit besides the holder's target (docs/engine/ENTITIES_AI.md §8.1.2).</summary>
[Flags]
public enum DamageTargets : byte
{
    Target = 0,
    Friendly = 1,
    Enemy = 2,
    NonCharacters = 4,
    All = 255,
}

/// <summary>One item in play, a copy of its template's; it changes in place (shown or hidden, where it is, whom it hit).</summary>
public sealed class Item
{
    /// <summary>How long a blow that met a shield or the level keeps the item from striking again.</summary>
    public const float GlanceTime = 0.25f;

    private readonly List<Entity> hit = [];
    private bool placed;

    public Item(ItemTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.Validate();
        Template = template;
        MeleeEvents = new EventGroups(template.Melee ?? []);
        GunEvents = new EventGroups(template.Gun?.Events ?? []);
        Auras = [.. (template.Auras ?? []).Select(a => new Buffs.ActiveAura(a))];
    }

    /// <summary>This item's own auras at work (their time never ends them while held).</summary>
    public IReadOnlyList<Buffs.ActiveAura> Auras { get; }

    public ItemTemplate Template { get; }

    public WeaponClass WeaponClass => Template.WeaponClass;

    /// <summary>In its holder's hand; a released item stays in its slot, detached, and does nothing there (§8.1.5).</summary>
    public bool IsAttached { get; private set; } = true;

    /// <summary>Drawn on its bone; an animation can hide it.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Where the item is, as of the end of its holder's last frame.</summary>
    public Matrix4x4 Transform { get; private set; } = Matrix4x4.Identity;

    public Vector3 Position => Transform.Translation;

    /// <summary>The tip (<c>attach0</c>) in the world as of the holder's last frame, and the frame before.</summary>
    public Vector3 Tip { get; private set; }

    public Vector3 LastTip { get; private set; }

    /// <summary>This item's own copy of its melee groups.</summary>
    public EventGroups MeleeEvents { get; }

    /// <summary>This item's own copy of its gun groups.</summary>
    public EventGroups GunEvents { get; }

    /// <summary>The gun's accuracy for this burst (its own × the action's).</summary>
    public float GunAccuracy { get; internal set; }

    /// <summary>The NPC ranged ability its holder used it for, and at whom: its next throw aims there (§8.1.4).</summary>
    internal (AI.RangedAbility Ability, Entities.Entity Target)? AimedBy { get; set; }

    /// <summary>Rounds left in this burst.</summary>
    public int Rounds { get; internal set; }

    /// <summary>Time to the next shot; above 0 while a burst runs.</summary>
    public float ShotTimer { get; internal set; }

    public bool Firing { get; internal set; }

    /// <summary>Shots fired so far: every fifth draws a tracer.</summary>
    public int Shots { get; internal set; }

    /// <summary>Whom the current strike hit, in order.</summary>
    public IReadOnlyList<Entity> HitList => hit;

    /// <summary>Restarts with each strike; abilities read it.</summary>
    public float Cooldown { get; internal set; }

    /// <summary>The spell imbued into it (docs/gameplay/SPELLS.md §4, §4.1).</summary>
    public Spells.ElementQueue Imbued { get; internal set; } = Spells.ElementQueue.Empty;

    public bool IsSpellCharged => Imbued.Count > 0;

    /// <summary>Takes the imbued spell out, combined; the item is empty after.</summary>
    internal Spells.Spell RetrieveSpell()
    {
        var spell = Imbued.Combine();
        Imbued = Imbued.Cleared();
        return spell;
    }

    /// <summary>Time until the special ability is ready again (§8.1.7); at or below 0 it is.</summary>
    public float SpecialCooldown { get; internal set; }

    public bool IsSpecialReady => SpecialCooldown <= 0 && Template.Special is not null;

    /// <summary>The emissive light while the special ability recharges, <c>min(0.05^cooldown, 1)</c>; 1 otherwise.</summary>
    public float SpecialLight => SpecialCooldown > 0 ? MathF.Min(MathF.Pow(0.05f, SpecialCooldown), 1) : 1;

    /// <summary>The passive ability's pulse timer (§8.1.6).</summary>
    internal float PassiveTimer { get; set; }

    /// <summary>A Glow item's light, 0 to 1, for the renderer.</summary>
    public float Glow { get; internal set; }

    /// <summary>A Gungnir's spear in flight (§8.1.6).</summary>
    public Entities.Missile? Thrown { get; internal set; }

    /// <summary>Its passive effect is running (started once while held).</summary>
    internal bool PassiveEffect { get; set; }

    /// <summary>The source of its passive effect's cues, apart from its events' (a strike's end leaves it running).</summary>
    public object PassiveSource { get; } = new();

    internal int NextToDamage { get; set; }

    internal float ShieldGlance { get; set; }

    internal float LevelGlance { get; set; }

    /// <summary>A new strike: nobody hit yet.</summary>
    public void ClearHitList()
    {
        hit.Clear();
        NextToDamage = 0;
    }

    internal void AddHit(Entity entity) => hit.Add(entity);

    /// <summary>Moves the item (its holder's call, at the end of its frame); the first place after a new hold has no sweep.</summary>
    internal void Place(Matrix4x4 transform)
    {
        Transform = transform;
        var tip = ((Template.Attach0 ?? Matrix4x4.Identity) * transform).Translation;
        (LastTip, Tip, placed) = (placed ? Tip : tip, tip, true);
    }

    /// <summary>Taken into a hand: attached, and the next place starts afresh.</summary>
    internal void Held() => (placed, IsAttached) = (false, true);

    /// <summary>Let go: it no longer acts from its slot, and shows again, as the original's lying items.</summary>
    internal void Detach() => (IsAttached, PassiveEffect, IsVisible) = (false, false, true);

    /// <summary>The special cooldown runs down; true when it just came ready (crossing 0 from above).</summary>
    internal bool TickSpecial(float dt)
    {
        var cooling = SpecialCooldown > 0;
        SpecialCooldown -= dt;
        return cooling && SpecialCooldown <= 0;
    }

    /// <summary>The item's timers run down (its holder's call, each frame).</summary>
    internal void Tick(float dt)
    {
        Cooldown = MathF.Max(0, Cooldown - dt);
        ShieldGlance = MathF.Max(-1, ShieldGlance - dt);
        LevelGlance = MathF.Max(-1, LevelGlance - dt);
    }
}
