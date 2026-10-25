using OpenMagicka.Gameplay.AI;
using OpenMagicka.Gameplay.Characters.States;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>
/// What a level's script gave a character (LEVELS_TRIGGERS.md §9): triggers its death and damage set
/// off, its dialog, a clip to rise with, and for a player, scripted events.
/// </summary>
public sealed partial class Character
{
    private Agent? scriptedMind;

    /// <summary>The script trigger its death sets off (0 for none).</summary>
    public int OnDeathTrigger { get; set; }

    /// <summary>The script trigger each hit that hurts it sets off (0 for none).</summary>
    public int OnDamageTrigger { get; set; }

    /// <summary>The dialog talking to it starts (by its script name; 0 for none).</summary>
    public int Dialog { get; set; }

    /// <summary>Its hit points set to a share of the most (a spawn's health); not clamped, as the original.</summary>
    public void SetHealth(float share) =>
        HitPoints = MaxHitPoints * (float.IsFinite(share) ? share : throw new ArgumentOutOfRangeException(nameof(share), share, "must be finite"));

    /// <summary>Rises with a clip (a spawn's own): the revival state plays it, then the character goes on.</summary>
    public void RiseWith(string clip)
    {
        ArgumentException.ThrowIfNullOrEmpty(clip);
        SpawnAnimation = clip;
        if (State is not null)
        {
            ChangeState(CharacterStates.Ressurection);
        }
    }

    /// <summary>
    /// A player's scripted events (an avatarMove): its avatar walks, plays, turns and dies as they say,
    /// as a mind's idle order would. None ends them: it stands idle again.
    /// </summary>
    public void PlayEvents(IReadOnlyList<AiEvent>? events)
    {
        if (events is not { Count: > 0 })
        {
            scriptedMind = null;
            Body.Movement = System.Numerics.Vector3.Zero;
            if (State is not null)
            {
                ChangeState(CharacterStates.Idle);
            }
            GoToAnimation("idle", 0.05f);
            return;
        }
        scriptedMind ??= new Agent(this, AiProfile.None);
        scriptedMind.DropToIdle();
        scriptedMind.SetOrder(Order.Idle, events: events);
    }

    /// <summary>Its scripted events still run (input waits for them); death ends them.</summary>
    public bool IsInEvent => !IsDead && (scriptedMind?.HasPendingEvents ?? false);

    private void RunScriptedEvents(float dt)
    {
        if (IsDead)
        {
            scriptedMind = null;
        }
        else if (scriptedMind is { HasPendingEvents: true } mind)
        {
            mind.RunScript(dt);
        }
    }

    protected internal override IEnumerable<Physics.PhysicsBody> Bodies => [Body.Body];

    /// <summary>Its spell ends as it waits outside (the spell's entities went with the scene).</summary>
    protected internal override void OnWithdrawn() => StopSpell();

    private void RaiseScriptTrigger(int trigger)
    {
        if (trigger != 0)
        {
            Manager?.RaiseScriptTrigger(this, trigger);
        }
    }
}
