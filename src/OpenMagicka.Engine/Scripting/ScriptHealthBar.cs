namespace OpenMagicka.Engine.Scripting;

/// <summary>What the generic bar counts.</summary>
public enum GenericHealthBarTypes { HP, TimerIncreasing, TimerDecreasing, Counter }

/// <summary>How the generic bar is drawn.</summary>
public enum GenericHealthBarGraphics { Default, Dynamite }

/// <summary>
/// The level's generic bar (the original's, a script's <c>healthBar</c>): set up, then shown. A timer runs
/// (after its first delay) down from its time to 0, or up from 0 to it; a counter and a hit-point bar
/// stand where they were set (the original's also creep by a random amount every 4 s, a test left in;
/// ours do not). When it is done its trigger is set off once, in the scene then current. The HUD
/// draws it (OM-49).
/// </summary>
public sealed class ScriptHealthBar
{
    private float time, end, start, delay;
    private bool ticking, ended = true;

    public GenericHealthBarTypes Type { get; private set; }

    public GenericHealthBarGraphics Graphics { get; private set; }

    /// <summary>The look the script gave it: the name shown, whether it scales, its sprite, its fade.</summary>
    public HealthBarLook Look { get; private set; } = new(0, false, false, false, false, 16, 0, 1.1f, 0);

    public bool IsActive { get; private set; }

    /// <summary>How full it shows, 0..1.</summary>
    public float Shown { get; private set; } = 1;

    public bool IsDone { get; private set; }

    /// <summary>The trigger it sets off when done (0: none).</summary>
    public int OnEnd { get; private set; }

    /// <summary>Set up anew (a script's healthBar): a timer of the time, or a counter of the count; its trigger armed again.</summary>
    public void Setup(GenericHealthBarTypes type, GenericHealthBarGraphics graphics, float duration, float count, float initialDelay, float shown, int onEnd,
        HealthBarLook look)
    {
        (Type, Graphics, Look, delay, Shown, IsDone, ticking) = (type, graphics, look, initialDelay, shown, false, false);
        (start, time, end) = type switch
        {
            GenericHealthBarTypes.TimerDecreasing => (duration, duration, 0f),
            GenericHealthBarTypes.TimerIncreasing => (0f, 0f, duration),
            GenericHealthBarTypes.Counter => (0f, 0f, count),
            _ => (count, count, 0f),
        };
        OnEnd = onEnd;
        ended = onEnd == 0;
    }

    /// <summary>It shows; a timer with no delay left starts running.</summary>
    public void Activate()
    {
        IsActive = true;
        ticking |= Type is GenericHealthBarTypes.TimerDecreasing or GenericHealthBarTypes.TimerIncreasing && delay <= 0;
    }

    public void Remove() => (IsActive, ticking) = (false, false);

    /// <summary>As never set up (a checkpoint's restart: the replay sets it up again).</summary>
    public void Reset()
    {
        Setup(GenericHealthBarTypes.HP, GenericHealthBarGraphics.Default, 0, 0, 0, 1, 0, new HealthBarLook(0, false, false, false, false, 16, 0, 1.1f, 0));
        IsActive = false;
    }

    /// <summary>A frame: its delay and its timer run; the trigger to set off, once, when it is done (0 for none).</summary>
    internal int Update(float dt)
    {
        if (!IsActive)
        {
            return 0;
        }
        var fire = IsDone && !ended ? OnEnd : 0;
        ended |= fire != 0;
        if (delay > 0 && (delay -= dt) <= 0)
        {
            ticking = true;
        }
        switch (Type)
        {
            case GenericHealthBarTypes.TimerDecreasing:
                time -= ticking ? dt : 0;
                IsDone |= ticking && time <= end;
                Shown = start > 0 ? MathF.Max(time / start, 0) : 0;
                break;
            case GenericHealthBarTypes.TimerIncreasing:
                time += ticking ? dt : 0;
                IsDone |= ticking && time >= end;
                Shown = end > 0 ? MathF.Min(time / end, 1) : 1;
                break;
            case GenericHealthBarTypes.Counter:
                Shown = end > 0 ? MathF.Min(time / end, 1) : 1;
                IsDone |= time >= end;
                break;
        }
        return fire;
    }
}

/// <summary>A generic bar's look, as the HUD draws it (the name by its loc key's hash).</summary>
public sealed record HealthBarLook(int DisplayName, bool ShowsName, bool IsScaled, bool IsRed, bool HasAnimatedSprite, float SpriteOffsetY, float FadeTime,
    float Power, float Alpha);

/// <summary>
/// <c>healthBar type= time= count= graphicsType= initialTimerDelay= normalizedHealth= onEndTrigger= …</c>: the
/// generic bar is set up (not yet shown).
/// </summary>
public sealed class HealthBarAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    public string DisplayName { get; set; } = "";
    public float NormalizedHealth { get; set; } = 1;
    public float DisplayHealth { get; set; }
    public float Time { get; set; }
    public GenericHealthBarTypes Type { get; set; }
    public float Power { get; set; } = 1.1f;
    public float Alpha { get; set; }
    public float Count { get; set; }
    public GenericHealthBarGraphics GraphicsType { get; set; }
    public float InitialTimerDelay { get; set; }
    public bool IsScaled { get; set; }
    public bool IsColoredRed { get; set; }
    public bool ShowDisplayName { get; set; }
    public bool HasAnimatedSprite { get; set; }
    public float AnimationOffsetPositionY { get; set; } = 16;
    public float FadeTime { get; set; }
    public string OnEndTrigger { get; set; } = "";

    public override void Initialize()
    {
        if (!float.IsFinite(Time + Count + InitialTimerDelay + FadeTime + NormalizedHealth))
        {
            throw new InvalidDataException("<healthBar>: its times and count must be finite");
        }
    }

    protected override void Execute() => Scene.Game.HealthBar.Setup(Type, GraphicsType, Time, Count, InitialTimerDelay, NormalizedHealth,
        OnEndTrigger.Length > 0 ? Hash(OnEndTrigger) : 0, new HealthBarLook(DisplayName.Length > 0 ? Hash(DisplayName) : 0, ShowDisplayName, IsScaled,
            IsColoredRed, HasAnimatedSprite, AnimationOffsetPositionY, FadeTime, Power, Alpha));
}

/// <summary><c>activateHealthBar</c>: the generic bar shows (a timer starts running).</summary>
public sealed class ActivateHealthBarAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    protected override void Execute() => Scene.Game.HealthBar.Activate();
}

/// <summary><c>removeHealthBar</c>: the generic bar goes.</summary>
public sealed class RemoveHealthBarAction : ScriptAction
{
    /// <summary>As the original, a replay does it again.</summary>
    public override void Replay() => Execute();

    protected override void Execute() => Scene.Game.HealthBar.Remove();
}
