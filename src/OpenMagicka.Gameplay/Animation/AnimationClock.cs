namespace OpenMagicka.Gameplay.Animation;

/// <summary>A clip of a skeleton, as far as timing goes.</summary>
/// <param name="Channels">Each animated bone's keys by bone name (exact case, as the original), in time order; none for timing alone.</param>
public sealed record AnimationClip(string Name, float Duration, IReadOnlyDictionary<string, IReadOnlyList<Keyframe>>? Channels = null)
{
    public IReadOnlyDictionary<string, IReadOnlyList<Keyframe>>? Channels { get; } = CheckChannels(Name, Channels);

    internal void Validate()
    {
        if (!(Duration > 0) || !float.IsFinite(Duration))
        {
            throw new ArgumentOutOfRangeException(nameof(Duration), Duration, $"clip {Name} needs a positive duration");
        }
    }

    /// <summary>Every channel has keys, in time order, with finite poses and rotations that are not zero.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<Keyframe>>? CheckChannels(string name, IReadOnlyDictionary<string, IReadOnlyList<Keyframe>>? channels)
    {
        foreach (var (bone, keys) in channels ?? new Dictionary<string, IReadOnlyList<Keyframe>>())
        {
            if (keys is null || keys.Count == 0)
            {
                throw new ArgumentException($"clip {name}: bone {bone} has no keys", nameof(channels));
            }
            for (var i = 0; i < keys.Count; i++)
            {
                var (time, pose) = (keys[i].Time, keys[i].Pose);
                if (!float.IsFinite(time) || (i > 0 && time < keys[i - 1].Time) || !Finite(pose.Translation) || !Finite(pose.Scale)
                    || !float.IsFinite(pose.Rotation.LengthSquared()) || pose.Rotation.LengthSquared() <= 0)
                {
                    throw new ArgumentException($"clip {name}: bone {bone} has a bad key {i}", nameof(channels));
                }
            }
        }
        return channels;
    }

    private static bool Finite(System.Numerics.Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

/// <summary>What happened to the clock during an update.</summary>
[Flags]
public enum ClockEvents
{
    None = 0,

    /// <summary>A looping clip wrapped around.</summary>
    Looped = 1,

    /// <summary>A crossfade ended and its clip started.</summary>
    CrossfadeFinished = 2,
}

/// <summary>
/// The timing of a skinned model's animation controller (docs/engine/ENTITIES_AI.md §4.1): one clip,
/// at most one crossfade towards the next and one queued after it. No bones: rendering poses the
/// skeleton from this time. A simulation object: it changes in place.
/// </summary>
public sealed class AnimationClock
{
    private float speed = 1;
    private AnimationClip? fadeClip;
    private bool fadeLoops;
    private (AnimationClip Clip, float Time)? queued;

    public AnimationClip? Clip { get; private set; }

    /// <summary>Seconds into <see cref="Clip"/>.</summary>
    public float Time { get; private set; }

    /// <summary>The character's animation speed (0 freezes it); never negative.</summary>
    public float Speed
    {
        get => speed;
        set => speed = float.IsFinite(value) ? MathF.Max(0, value) : throw new ArgumentOutOfRangeException(nameof(Speed), value, "must be finite");
    }

    /// <summary>The playing clip's own speed (taken from the next clip as soon as its crossfade starts, as the original).</summary>
    public float ClipSpeed
    {
        get => clipSpeed;
        set => clipSpeed = float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(ClipSpeed), value, "must be finite");
    }

    private float clipSpeed = 1;

    public bool IsLooping { get; private set; }

    public bool IsPlaying { get; private set; }

    /// <summary>A clip that does not loop reached its end, with no crossfade running.</summary>
    public bool HasFinished { get; private set; }

    public bool IsCrossFading { get; private set; }

    public AnimationClip? CrossFadeClip => IsCrossFading ? fadeClip : null;

    public float CrossFadeTime { get; private set; }

    public float CrossFadeDuration { get; private set; }

    /// <summary>The share of the way through the clip, 0 to 1.</summary>
    public float NormalizedTime => Clip is null ? 0 : Time / Clip.Duration;

    /// <summary>Starts a clip at once, from its beginning.</summary>
    public void Start(AnimationClip clip, bool loop)
    {
        ArgumentNullException.ThrowIfNull(clip);
        clip.Validate();
        (Clip, Time, IsLooping, IsPlaying, HasFinished, IsCrossFading) = (clip, 0, loop, true, false, false);
    }

    /// <summary>
    /// Blends towards a clip; during a running blend it is queued instead, and the blend hurries. As the
    /// original, the loop flag given last is the one the running blend's clip starts with.
    /// </summary>
    public void CrossFade(AnimationClip clip, float fadeTime, bool loop)
    {
        ArgumentNullException.ThrowIfNull(clip);
        clip.Validate();
        if (!(fadeTime >= 0) || !float.IsFinite(fadeTime))
        {
            throw new ArgumentOutOfRangeException(nameof(fadeTime), fadeTime, "must be finite and not negative");
        }
        IsPlaying = true;
        fadeLoops = loop;
        if (IsCrossFading)
        {
            queued = (clip, fadeTime);
            CrossFadeDuration = CrossFadeTime + (CrossFadeDuration - CrossFadeTime) * 0.5f;
            return;
        }
        (fadeClip, CrossFadeDuration, CrossFadeTime, IsCrossFading, HasFinished) = (clip, fadeTime, 0, true, false);
    }

    /// <summary>The clip stops where it is (kept, not finished), as XNAnimation's controller's stop; a blend is dropped.</summary>
    public void Stop() => (IsPlaying, HasFinished, IsCrossFading, queued) = (false, false, false, null);

    /// <summary>Forgets a queued crossfade.</summary>
    public void ClearQueue() => queued = null;

    public ClockEvents Update(float dt)
    {
        if (!(dt >= 0) || !float.IsFinite(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "must not be negative");
        }
        if (Clip is null)
        {
            return ClockEvents.None;
        }
        var elapsed = dt * speed * ClipSpeed;
        var events = IsPlaying ? Advance(elapsed) : ClockEvents.None;
        return IsCrossFading ? events | Fade(elapsed) : events;
    }

    private ClockEvents Advance(float elapsed)
    {
        Time += elapsed;
        if (!(Time > Clip!.Duration) && !(Time < 0))
        {
            return ClockEvents.None;
        }
        if (IsLooping)
        {
            Time = Wrap(Time, Clip.Duration);
            return ClockEvents.Looped;
        }
        Time = Time > Clip.Duration ? Clip.Duration : 0;
        if (!IsCrossFading)
        {
            (IsPlaying, HasFinished) = (false, true);
        }
        return ClockEvents.None;
    }

    /// <summary>Into [0, duration]; past the end by an exact multiple stays at the end, as the original's subtraction.</summary>
    private static float Wrap(float time, float duration)
    {
        if (time > duration)
        {
            var turns = MathF.Ceiling(time / duration) - 1;
            return time - turns * duration;
        }
        return time - MathF.Floor(time / duration) * duration;
    }

    private ClockEvents Fade(float elapsed)
    {
        CrossFadeTime += elapsed;
        if (!(CrossFadeTime > CrossFadeDuration))
        {
            return ClockEvents.None;
        }
        (IsCrossFading, CrossFadeTime, CrossFadeDuration) = (false, 0, 0);
        Start(fadeClip!, fadeLoops);
        if (queued is { } next)
        {
            queued = null;
            CrossFade(next.Clip, next.Time, fadeLoops);
        }
        return ClockEvents.CrossfadeFinished;
    }
}
