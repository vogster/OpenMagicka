using OpenMagicka.Gameplay.Animation;

namespace OpenMagicka.Game.Menu;

/// <summary>
/// The Tome's riffle (OM-83, docs/engine/ARCHITECTURE.md §20, "The book"), as the original's: up to ten turning sheets,
/// each a clock on the sheet's clip at twice its speed (a sheet turns in half a second). All the sheets start at once;
/// then, one every sixteenth of a second, the highest not yet let go is started again from its beginning, moved on by
/// how late it is (and a hundredth of a second more), so the first ones drop back flat and turn again: a flurry, the
/// last sheet landing last. It is over when every sheet has finished. A simulation object: it changes in place.
/// </summary>
public sealed class TomeRiffle
{
    public const int Sheets = 10;

    /// <summary>The sheets' speed, and the time between two let go (the clip's second × 0.625 / 10, whatever the count).</summary>
    public const float Speed = 2, Step = 0.0625f, Late = 0.01f;

    private readonly AnimationClock[] clocks = new AnimationClock[Sheets];
    private AnimationClip? clip;
    private int count, waiting;
    private float timer;

    public TomeRiffle()
    {
        for (var i = 0; i < Sheets; i++)
        {
            clocks[i] = new AnimationClock { ClipSpeed = Speed };
        }
    }

    /// <summary>How many sheets turn (none when over).</summary>
    public int Count => IsOver ? 0 : count;

    public bool IsOver { get; private set; } = true;

    /// <summary>A sheet's clock (its pose: the clip at its time).</summary>
    public AnimationClock this[int sheet] => clocks[sheet];

    /// <summary>A riffle of a number of pages (at most ten sheets turn) on a clip, the one running replaced.</summary>
    public void Start(AnimationClip clip, int pages)
    {
        ArgumentNullException.ThrowIfNull(clip);
        this.clip = clip;
        count = Math.Clamp(pages, 0, Sheets);
        (waiting, timer) = (count, 0);
        for (var i = 0; i < count; i++)
        {
            clocks[i].Start(clip, false);
        }
        IsOver = count == 0;
    }

    public void Update(float dt)
    {
        if (IsOver || !(dt >= 0) || !float.IsFinite(dt))
        {
            return;
        }
        timer -= dt;
        while (timer <= 0 && waiting > 0)
        {
            var sheet = clocks[--waiting];
            sheet.Start(clip!, false);
            sheet.Update(-timer + Late);                                      // its lateness (at the sheet's speed, the clock's own)
            timer += Step;
        }
        if (waiting == 0)
        {
            timer = 0;
        }
        var finished = true;
        for (var i = 0; i < count; i++)
        {
            if (clocks[i].IsPlaying)
            {
                clocks[i].Update(dt);
            }
            finished &= clocks[i].HasFinished;
        }
        IsOver = waiting == 0 && finished;
    }
}
