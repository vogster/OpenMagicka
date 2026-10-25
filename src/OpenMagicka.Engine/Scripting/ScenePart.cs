using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.XnAnimation;
using OpenMagicka.Engine.Navigation;
using OpenMagicka.Gameplay.Navigation;
using Microsoft.Xna.Framework;
using Numerics = System.Numerics;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// An animated level part at play (LEVELS_TRIGGERS.md §9.2): a door, a lift, machinery. Its clip
/// plays between a start and an end at a speed (backwards when negative), once or looping; each frame
/// its transform is the clip's pose at its time (keyframes blended linearly) times its parent's, and
/// its nav mesh and its collision follow (carrying what stands on it). Its children, locators and effects
/// come with it. The part's name is lowered,
/// its locators' names are kept as written (as the original keys them).
/// </summary>
public sealed class ScenePart
{
    private readonly Dictionary<int, ScenePart> children = [];
    private readonly Dictionary<int, LocatorContent> locators = [];
    private float time, start, end, speed = 1;

    public ScenePart(AnimatedLevelPartContent content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Name = content.Name.ToLowerInvariant();
        Key = LegacyStringHash.Compute(Name);
        (start, end) = (0, content.AnimationDuration);
        foreach (var child in content.Children)
        {
            var part = new ScenePart(child);
            children[part.Key] = part;                                        // as the original: a name used twice keeps the last
        }
        foreach (var locator in content.Locators)
        {
            locators[LegacyStringHash.Compute(locator.Name)] = locator;
        }
        World = Numerics.Matrix4x4.Identity;
    }

    public AnimatedLevelPartContent Content { get; }

    public string Name { get; }

    public int Key { get; }

    public IEnumerable<ScenePart> Children => children.Values;

    public ScenePart? Child(int key) => children.GetValueOrDefault(key);

    /// <summary>The part's collision in the physics world (the level gives it on entering the scene), moved with the part.</summary>
    public Physics.PartCollision? Collision { get; set; }

    /// <summary>The bodies attached to it (a script's items) while it has no collision: the scene was left.</summary>
    public IReadOnlyList<OpenMagicka.Physics.PhysicsBody> Attached { get; set; } = [];

    /// <summary>Whether it has taken a place (a part whose pose is not finite has none, and stays out of the world).</summary>
    public bool IsPlaced { get; private set; }

    /// <summary>The part's nav mesh in the level's (the running level gives it), moved with the part.</summary>
    public NavSurface? Navigation { get; set; }

    public bool IsPlaying { get; private set; }

    public bool IsLooping { get; private set; }

    public float Time => time;

    public float Speed => speed;

    /// <summary>Where it is now, in the level (its last frame's; the rest pose once the places first update).</summary>
    public Numerics.Matrix4x4 World { get; private set; }

    /// <summary>
    /// Plays its clip from <paramref name="from"/> to <paramref name="to"/> (−1: the clip's ends), at a
    /// speed; a negative speed plays backwards from the later to the earlier. Resuming keeps the time when
    /// it lies within, else the nearer end; otherwise it starts over. With its children, they play the
    /// same, starting over.
    /// </summary>
    public void Play(bool withChildren, float from, float to, float speed, bool loop, bool resume)
    {
        if (withChildren)
        {
            foreach (var child in children.Values)
            {
                child.Play(withChildren: true, from, to, speed, loop, resume: false);
            }
        }
        this.speed = speed;
        from = from < 0 ? 0 : from;
        to = to < 0 ? Content.AnimationDuration : to;
        (start, end) = speed < 0 ? (MathF.Max(from, to), MathF.Min(from, to)) : (MathF.Min(from, to), MathF.Max(from, to));
        time = !resume ? start
            : speed >= 0 ? Math.Clamp(time, start, end)
            : Math.Clamp(time, end, start);
        (IsLooping, IsPlaying) = (loop, true);
    }

    /// <summary>
    /// Plays on from where it is, at a speed: for a length (back from its time when the speed is
    /// negative), or, with none, to the clip's end. A loop given is taken; none given keeps its own. With
    /// its children, they go on the same.
    /// </summary>
    public void Resume(bool withChildren, float length, float speed, bool? loop)
    {
        if (withChildren)
        {
            foreach (var child in children.Values)
            {
                child.Resume(withChildren: true, length, speed, loop);
            }
        }
        this.speed = speed;
        IsLooping = loop ?? IsLooping;
        end = length > float.Epsilon ? (speed < 0 ? time - length : time + length) : Content.AnimationDuration;
        IsPlaying = true;
    }

    /// <summary>How long it still shows highlighted (a script's highlightAnim); not above 0, not.</summary>
    public float Highlighted { get; private set; } = -1;

    public void Highlight(float time) => Highlighted = time;

    public void Stop(bool withChildren)
    {
        if (withChildren)
        {
            foreach (var child in children.Values)
            {
                child.Stop(withChildren: true);
            }
        }
        IsPlaying = false;
    }

    /// <summary>
    /// A frame: its time runs on (past the end it loops round, or stops there), it takes its pose, then
    /// its children theirs. The original loops back by one span only on the frame it crosses the end, so a
    /// frame longer than the loop (or a loop of no length) leaves it running on past the end for good;
    /// ours wraps it into the loop, and stops a loop of no length.
    /// </summary>
    public void Update(float dt, Numerics.Matrix4x4 parent)
    {
        Highlighted = MathF.Max(-1, Highlighted - dt);
        if (IsPlaying)
        {
            time += dt * speed;
            if (speed > 0 ? time > end : speed < 0 && time < end)
            {
                var span = end - start;
                if (IsLooping && span != 0)
                {
                    time = end - span + (time - end) % span;                       // ours: a frame longer than the loop wraps too
                }
                else
                {
                    (time, IsPlaying) = (end, false);
                }
            }
        }
        Place(parent);
        Collision?.Update(dt, World, IsPlaying);
        foreach (var child in children.Values)
        {
            child.Update(dt, World);
        }
    }

    /// <summary>Where its clip is, for a checkpoint.</summary>
    public readonly record struct State(float Time, float Start, float End, float Speed, bool Playing, bool Looping);

    public State Save() => new(time, start, end, speed, IsPlaying, IsLooping);

    /// <summary>Its clip back where it was (it takes that pose on its next frame).</summary>
    public void Load(State state) => (time, start, end, speed, IsPlaying, IsLooping) = (state.Time, state.Start, state.End, state.Speed, state.Playing, state.Looping);

    /// <summary>It and every part under it, the parent first.</summary>
    public IEnumerable<ScenePart> Self() => [this, .. children.Values.SelectMany(c => c.Self())];

    /// <summary>A locator of the part or of its children, where it is now.</summary>
    public Numerics.Matrix4x4? Locator(int key)
    {
        if (locators.TryGetValue(key, out var locator))
        {
            return LevelNavigation.ToNumerics(locator.Transform) * World;
        }
        foreach (var child in children.Values)
        {
            if (child.Locator(key) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>The clip's pose at a time: the keyframe at or before it, blended toward the next (none past the last).</summary>
    public static Matrix Pose(IReadOnlyList<AnimationKeyframe> keys, float at)
    {
        if (keys.Count == 0)
        {
            return Matrix.Identity;
        }
        var index = KeyAt(keys, at);
        var next = (index + 1) % keys.Count;
        var span = next != 0 ? keys[next].Time - keys[index].Time : 0;
        if (!(span > 0))
        {
            return Rendering.LevelParts.PoseMatrix(keys[index].Pose);
        }
        var amount = Math.Clamp((at - keys[index].Time) / span, 0, 1);
        var (a, b) = (keys[index].Pose, keys[next].Pose);
        return Rendering.LevelParts.PoseMatrix(new BonePose(Vector3.Lerp(a.Translation, b.Translation, amount),
            Quaternion.Lerp(a.Orientation, b.Orientation, amount), Vector3.Lerp(a.Scale, b.Scale, amount)));
    }

    /// <summary>XNAnimation's search: the keyframe at the time, else the one before it (the first for a time before all).</summary>
    private static int KeyAt(IReadOnlyList<AnimationKeyframe> keys, float at)
    {
        var (middle, low, high) = (0, 0, keys.Count - 1);
        while (high >= low)
        {
            middle = (low + high) / 2;
            if (keys[middle].Time < at)
            {
                low = middle + 1;
            }
            else if (keys[middle].Time > at)
            {
                high = middle - 1;
            }
            else
            {
                break;
            }
        }
        return middle > 0 && keys[middle].Time > at ? middle - 1 : middle;
    }

    private void Place(Numerics.Matrix4x4 parent)
    {
        var world = LevelNavigation.ToNumerics(Pose(Content.Animation, time)) * parent;
        if (!Finite(world))
        {
            return;
        }
        IsPlaced = true;
        if (world != World)
        {
            World = world;
            Navigation?.Place(world);
        }
    }

    private static bool Finite(Numerics.Matrix4x4 m) =>
        float.IsFinite(m.M11 + m.M12 + m.M13 + m.M14 + m.M21 + m.M22 + m.M23 + m.M24 + m.M31 + m.M32 + m.M33 + m.M34 + m.M41 + m.M42 + m.M43 + m.M44);
}
