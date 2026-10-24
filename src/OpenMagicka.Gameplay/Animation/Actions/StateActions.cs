using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Animation.Actions;

/// <summary>Turns ethereal or solid and fades (a speed of 0 only sets the transparency, at once).</summary>
public sealed class Ethereal(float start, float end, bool ethereal, float alpha, float speed) : AnimationAction(start, end)
{
    public bool IsEthereal { get; } = ethereal;

    public float Alpha { get; } = Finite(alpha, nameof(alpha));

    public float Speed { get; } = Finite(speed, nameof(speed)) >= 0 ? speed : throw new ArgumentOutOfRangeException(nameof(speed), speed, "must not be negative");

    public override void Run(Character owner, bool first)
    {
        if (!first)
        {
            return;
        }
        if (Speed == 0)
        {
            owner.EtherealAlpha = Alpha;
        }
        else
        {
            owner.Ethereal(IsEthereal, Alpha, Speed);
        }
    }
}

/// <summary>Invisible (or only not drawn) for the window.</summary>
public sealed class Invisible(float start, float end, bool noEffect) : AnimationAction(start, end)
{
    public bool NoEffect { get; } = noEffect;

    public override void Run(Character owner, bool first)
    {
        if (!first)
        {
            return;
        }
        if (NoEffect)
        {
            owner.IsHidden = true;
        }
        else
        {
            owner.SetInvisible(float.MaxValue);
        }
    }

    public override void Stop(Character owner)
    {
        if (NoEffect)
        {
            owner.IsHidden = false;
        }
        else
        {
            owner.SetInvisible(0);
        }
    }
}

/// <summary>Immune for the window (3 s at a time), passing through others too unless it collides.</summary>
public sealed class Immortal(float start, float end, bool collide) : AnimationAction(start, end)
{
    public const float Time = 3;

    public const float PassThroughTime = 0.15f;

    public bool Collide { get; } = collide;

    public override void Run(Character owner, bool first)
    {
        owner.SetImmortalTime(Time);
        if (!Collide)
        {
            owner.IgnoreCollisions(PassThroughTime);
        }
    }

    public override void Stop(Character owner) => owner.SetImmortalTime(0);
}

/// <summary>Ends a status.</summary>
public sealed class RemoveStatus(float start, float end, StatusEffects status) : AnimationAction(start, end)
{
    public StatusEffects Status { get; } = status;

    public override void Run(Character owner, bool first)
    {
        if (first)
        {
            owner.StopStatuses(Status);
        }
    }
}

/// <summary>Dies on the spot (bombers, clones): shattered, or killed with no death clip to wait for.</summary>
public sealed class Suicide(float start, float end, bool overkill) : AnimationAction(start, end)
{
    public bool Overkill { get; } = overkill;

    public override void Run(Character owner, bool first)
    {
        if (Overkill)
        {
            owner.OverKill();
            return;
        }
        owner.Kill();
        owner.Die();
    }
}

/// <summary>The item in a slot blocks for the window.</summary>
public sealed class Block(float start, float end, int slot) : AnimationAction(start, end)
{
    public int Slot { get; } = slot;

    public override void Run(Character owner, bool first) => owner.BlockSlot = Slot;

    public override void Stop(Character owner) => owner.BlockSlot = -1;
}

/// <summary>Tears at the vines (breaking a grip comes with grips).</summary>
public sealed class BreakFree(float start, float end, float magnitude) : AnimationAction(start, end)
{
    public float Magnitude { get; } = Finite(magnitude, nameof(magnitude));

    public override void Run(Character owner, bool first)
    {
        if (first && owner.IsEntangled)
        {
            owner.DecreaseEntanglement(Magnitude, Elements.Earth);
        }
    }
}
