namespace OpenMagicka.Gameplay.Characters;

/// <summary>How a character dies and rises again (docs/engine/ENTITIES_AI.md §2.5, §5.4).</summary>
public sealed partial class Character
{
    /// <summary>Kills (a kill plane, a script): at once without a state machine, through the death clip with one.</summary>
    public void Kill()
    {
        RemoveInvisibility();
        HitPoints = MathF.Min(HitPoints, 0);
        DieUnlessDying();
    }

    /// <summary>Shatters: no corpse, no revival.</summary>
    public void OverKill()
    {
        RemoveInvisibility();
        if (!IsOverkilled)
        {
            IsOverkilled = true;
            HitPoints = -MaxHitPoints - 1000;
            corpseTimer = -100;
        }
        DieUnlessDying();
    }

    /// <summary>With a state machine, the dead state dies when the death clip ends; without, now.</summary>
    private void DieUnlessDying()
    {
        if (State is null)
        {
            Die();
        }
    }

    internal void Die()
    {
        if (Dead)
        {
            return;
        }
        undieTimer = undying && !IsOverkilled
            ? (Template.UndieTime < float.Epsilon ? 0.5f : Template.UndieTime)       // as the original: 0.5 s only when none is given
            : float.NaN;
        Dead = true;
        IsKnockedDown = false;
        ReleaseEntanglement();
    }

    private void Afterlife(float dt)
    {
        if (!float.IsNaN(undieTimer) && !IsOverkilled)
        {
            undieTimer -= dt;
            if (undieTimer <= 0)
            {
                (Dead, HitPoints) = (false, Template.UndieHitPoints);                // the timer stays spent: the dead state sees a revival
            }
        }
        else
        {
            corpseTimer -= dt;
        }
    }
}
