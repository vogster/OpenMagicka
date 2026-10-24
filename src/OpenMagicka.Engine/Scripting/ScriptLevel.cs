namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// What a level's scripts keep across its scenes (LEVELS_TRIGGERS.md §5.6): counters, and timers of
/// two kinds, the plain and the "played" ones, by the hash of their names.
/// </summary>
public sealed class ScriptLevel
{
    private sealed class Clock(int id, float time)
    {
        public int Id { get; } = id;

        public float Time { get; set; } = time;

        public bool Paused { get; set; }
    }

    private readonly Dictionary<int, int> counters = [];
    private readonly List<Clock> timers = [];
    private readonly List<Clock> played = [];

    /// <summary>A counter's value; 0 for one never touched.</summary>
    public int Counter(int id) => counters.GetValueOrDefault(id);

    public void SetCounter(int id, int value) => counters[id] = value;

    public void AddToCounter(int id, int value) => counters[id] = Counter(id) + value;

    /// <summary>Starts a timer at a value; one of that name already going is left alone.</summary>
    public void AddTimer(int id, bool played, float value)
    {
        var list = played ? this.played : timers;
        if (list.All(t => t.Id != id))
        {
            list.Add(new Clock(id, value));
        }
    }

    /// <summary>A timer's time (the played timers looked at first); 0 for none.</summary>
    public float Timer(int id) => Find(id)?.Time ?? 0;

    public void SetTimer(int id, float value)
    {
        if (Find(id) is { } clock)
        {
            clock.Time = value;
        }
    }

    public void PauseTimer(int id, bool paused)
    {
        if (Find(id) is { } clock)
        {
            clock.Paused = paused;
        }
    }

    /// <summary>
    /// The timers go on while the level runs (not paused, not over): the plain ones always, the played
    /// ones out of cutscenes.
    /// </summary>
    public void Update(float dt, bool inCutscene)
    {
        if (!inCutscene)
        {
            Tick(played, dt);
        }
        Tick(timers, dt);
    }

    private static void Tick(List<Clock> clocks, float dt)
    {
        foreach (var clock in clocks)
        {
            if (!clock.Paused)
            {
                clock.Time += dt;
            }
        }
    }

    private Clock? Find(int id) => played.Find(t => t.Id == id) ?? timers.Find(t => t.Id == id);
}
