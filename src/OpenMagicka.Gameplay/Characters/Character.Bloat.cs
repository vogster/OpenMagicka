using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>Bloating: a beam's kill swells the character until it bursts (docs/gameplay/SPELLS.md §5.4).</summary>
public sealed partial class Character
{
    /// <summary>How long a character swells before it bursts.</summary>
    public const float BloatTime = 0.333f;

    /// <summary>The burst reaches this far beyond the character's radius.</summary>
    public const float BloatReach = 2.5f;

    private float bloatTimer;
    private Elements bloatElement;
    private Entity? bloatKiller;

    public bool IsBloating { get; private set; }

    /// <summary>The burst is over: it was overkilled.</summary>
    public bool IsBloatKilled { get; private set; }

    /// <summary>
    /// A beam's kill: a character with gibs swells with no hit points and bursts 0.333 s later. As the
    /// original, one without gibs that is already at no hit points simply dies as it would; one that is
    /// not bursts at once and is killed.
    /// </summary>
    public void BloatKill(Elements element, Entity? killer)
    {
        if (IsBloating || Dead || (!Template.HasGibs && HitPoints <= 0))
        {
            return;
        }
        RemoveInvisibility();
        (bloatElement, bloatKiller) = (element, killer);
        if (Template.HasGibs)
        {
            (IsBloating, bloatTimer, HitPoints) = (true, 0, 0);
        }
        else
        {
            Burst();
        }
    }

    private void UpdateBloat(float dt)
    {
        if (!IsBloating)
        {
            return;
        }
        bloatTimer += dt;
        if (bloatTimer >= BloatTime)
        {
            Burst();
        }
    }

    /// <summary>The burst: a blast of the beam's element around the character, as its killer, then the end.</summary>
    private void Burst()
    {
        if (bloatElement != Elements.None && Manager is { } manager && !world.IsDisposed)
        {
            var amount = bloatElement == Elements.Life ? SpellDamage.Life : SpellDamage.Arcane;
            var burst = new DamageEntry(AttackProperties.Damage, bloatElement, amount, 1 + MaxHitPoints / 5000);
            Blasts.Full(manager, world, bloatKiller, null, Radius + BloatReach, Position, [burst], source: this);
        }
        IsBloating = false;
        if (Template.HasGibs)
        {
            IsBloatKilled = true;
            OverKill();
        }
        else
        {
            Kill();
        }
    }
}
