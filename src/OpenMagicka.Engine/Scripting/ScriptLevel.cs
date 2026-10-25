namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// What a level's scripts keep across its scenes (LEVELS_TRIGGERS.md §5.6): counters, and timers of
/// two kinds, the plain and the "played" ones, by the hash of their names.
/// </summary>
public sealed class ScriptLevel
{
    /// <summary>The stage the scenes share: the camera, the fade, the cutscene, the input locks, the requests (§9.3).</summary>
    public ScriptStage Stage { get; } = new();

    private sealed class Clock(int id, float time)
    {
        public int Id { get; } = id;

        public float Time { get; set; } = time;

        public bool Paused { get; set; }
    }

    /// <summary>The counters and timers as they are now (a checkpoint's).</summary>
    public sealed record Snapshot(IReadOnlyDictionary<int, int> Counters, IReadOnlyList<(int Id, float Time, bool Paused)> Timers,
        IReadOnlyList<(int Id, float Time, bool Paused)> Played);

    public Snapshot Save() => new(new Dictionary<int, int>(counters), [.. timers.Select(t => (t.Id, t.Time, t.Paused))],
        [.. played.Select(t => (t.Id, t.Time, t.Paused))]);

    /// <summary>The counters and timers back as they were saved, every other one gone.</summary>
    public void Load(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        counters.Clear();
        foreach (var (id, value) in snapshot.Counters)
        {
            counters[id] = value;
        }
        timers.Clear();
        timers.AddRange(snapshot.Timers.Select(t => new Clock(t.Id, t.Time) { Paused = t.Paused }));
        played.Clear();
        played.AddRange(snapshot.Played.Select(t => new Clock(t.Id, t.Time) { Paused = t.Paused }));
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
