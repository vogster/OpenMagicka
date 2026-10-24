using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Characters;

public sealed partial class Character
{
    /// <summary>How often an NPC's struggle against vines counts.</summary>
    public const float BreakFreeInterval = 0.5f;

    private float panic;
    private bool burnt;
    private float breakFreeTimer;

    /// <summary>
    /// A struggle (an NPC's mind, every so often): it tears at its vines as an Earth blow of 0.5. A
    /// grip's struggle comes with grips (OM-33).
    /// </summary>
    public void BreakFree()
    {
        if (breakFreeTimer > 0)
        {
            return;
        }
        DecreaseEntanglement(0.5f, Spells.Elements.Earth);
        breakFreeTimer = BreakFreeInterval;
    }

    /// <summary>Lets go of an attack or dash it means to make (its mind gives up the attack).</summary>
    public void StopAttacking() => (Attacking, Dashing, NextAttackAnimation) = (false, false, null);

    /// <summary>
    /// Running about in a panic (ENTITIES_AI.md §5): the panic is how far a burn or a steaming goes past
    /// the template's limit, and it panics while that again passes the limit, as the original reckons.
    /// A script's panic order sets it to 10.
    /// </summary>
    public bool IsPanicking => panic - Template.MaxPanic > float.Epsilon && !Dead;

    /// <summary>Carried by the riding grip, which comes with grips (OM-33).</summary>
    public bool IsStumbling => false;

    /// <summary>A script's panic: into the panic state at once, as long as nothing burns it out.</summary>
    public void OrderPanic()
    {
        ChangeState(States.CharacterStates.Panic);
        panic = 10;
    }

    /// <summary>
    /// After the statuses tick: steam, else a burn, sets the panic; once the burn is over it is 0 again.
    /// As the original, the end of steam alone leaves it.
    /// </summary>
    private void UpdatePanic(float dt)
    {
        breakFreeTimer -= dt;
        if (Statuses.Has(StatusEffects.Steamed))
        {
            panic = MathF.Max(Statuses[StatusEffects.Steamed].Magnitude - Template.MaxPanic, 0);
        }
        else if (Statuses.Has(StatusEffects.Burning))
        {
            panic = MathF.Max(Statuses[StatusEffects.Burning].Magnitude - Template.MaxPanic, 0);
            burnt = true;
        }
        else if (burnt)
        {
            (panic, burnt) = (0, false);
        }
    }
}
