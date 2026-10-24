using System.Numerics;
using OpenMagicka.Physics;

namespace OpenMagicka.Engine.Scripting;

/// <summary>A named effect of the scene: what plays, where (on a part, with it), and whether it plays now.</summary>
public sealed class SceneEffect
{
    internal SceneEffect(int key, string effect, Matrix4x4 local, float range, ScenePart? part)
    {
        (Key, Effect, Local, Range, Part, IsActive) = (key, effect, local, range, part, true);
        World = part is null ? local : local * part.World;
    }

    public int Key { get; }

    /// <summary>The visual effect's name (Effects/*.xml), lowered.</summary>
    public string Effect { get; }

    /// <summary>Where it is: in the level, or on its part.</summary>
    public Matrix4x4 Local { get; }

    /// <summary>A beam's length (0 for none): it is shortened to what is in the way.</summary>
    public float Range { get; }

    public ScenePart? Part { get; }

    public bool IsActive { get; internal set; }

    /// <summary>Where it is drawn this frame (a beam scaled down to its free length).</summary>
    public Matrix4x4 World { get; internal set; }

    /// <summary>How much of a beam's range is free (1 for a whole one, or no beam).</summary>
    public float Reach { get; internal set; } = 1;

    /// <summary>It began again (its timeline starts over, for the renderer).</summary>
    public int Starts { get; internal set; } = 1;
}

/// <summary>
/// The scene's named effects (LEVELS_TRIGGERS.md §9.2): the level model's and its parts', all started
/// when the scene is built, and those the scripts spawn. A spawn under a name that plays already
/// changes nothing; a start begins a stopped one again; a stop stops it, kept for a later start. Each
/// frame an active one follows its part, and a beam is shortened to the level in its way. The renderer
/// draws the active ones, and tells a one-shot effect that ended (<see cref="Ended"/>).
/// </summary>
public sealed class SceneEffects
{
    private readonly SortedList<int, SceneEffect> effects = [];
    private int lastGenerated;

    internal SceneEffects(ScriptPlaces places)
    {
        foreach (var placed in places.Model?.Effects ?? [])
        {
            Add(Content.LegacyStringHash.Lower(placed.Id), placed.Effect, Placed(placed), placed.Range, null, replace: false);
        }
        foreach (var part in places.Parts)
        {
            AddPart(part);
        }
    }

    public IEnumerable<SceneEffect> All => effects.Values;

    public SceneEffect? Find(int key) => effects.GetValueOrDefault(key);

    /// <summary>A spawned effect (spawnEffect): made anew unless one under the name plays already.</summary>
    public void Spawn(int key, string effect, Matrix4x4 at, float range) => Add(key, effect, at, range, null, replace: true);

    public void Start(int key)
    {
        if (effects.TryGetValue(key, out var effect) && !effect.IsActive)
        {
            effect.IsActive = true;
            effect.Starts++;
        }
    }

    public bool Stop(int key)
    {
        if (!effects.TryGetValue(key, out var effect))
        {
            return false;
        }
        effect.IsActive = false;
        return true;
    }

    /// <summary>A one-shot effect played to its end (the renderer says): it stops, as the original's does by itself.</summary>
    public void Ended(int key) => Stop(key);

    /// <summary>A key no effect has, after the last one given (a start with no name asks for one, as the original).</summary>
    public int Generated()
    {
        var key = lastGenerated + 1;
        while (effects.ContainsKey(key) && key != 0)
        {
            key++;
        }
        return lastGenerated = key;
    }

    /// <summary>A frame: the active ones follow their parts, and the beams reach as far as the level lets them.</summary>
    public void Update(PhysicsWorld physics)
    {
        ArgumentNullException.ThrowIfNull(physics);
        foreach (var effect in effects.Values)
        {
            if (!effect.IsActive)
            {
                continue;
            }
            var world = effect.Part is { } part ? effect.Local * part.World : effect.Local;
            var reach = 1f;
            if (effect.Range > 0 && !physics.IsDisposed)
            {
                var from = world.Translation;
                var forward = ScriptPlaces.Forward(world);
                reach = physics.CastSegment(from, from + forward * effect.Range, levelOnly: true)?.Fraction ?? 1;
                world = Matrix4x4.CreateScale(reach) * WithoutTranslation(world) * Matrix4x4.CreateTranslation(from);
            }
            (effect.World, effect.Reach) = (world, reach);
        }
    }

    private void AddPart(ScenePart part)
    {
        foreach (var placed in part.Content.Effects)
        {
            Add(Content.LegacyStringHash.Lower(placed.Id), placed.Effect, Placed(placed), placed.Range, part, replace: false);
        }
        foreach (var child in part.Children)
        {
            AddPart(child);
        }
    }

    private void Add(int key, string effect, Matrix4x4 at, float range, ScenePart? part, bool replace)
    {
        if (effects.TryGetValue(key, out var known) && (!replace || known.IsActive))
        {
            return;                                                           // the level's: the first of a name stays (the original fails on a second)
        }
        effects[key] = new SceneEffect(key, effect.ToLowerInvariant(), at, range, part);
    }

    private static Matrix4x4 Placed(Content.Magicka.VisualEffectPlacement placed)
    {
        var (position, forward) = (new Vector3(placed.Position.X, placed.Position.Y, placed.Position.Z), new Vector3(placed.Forward.X, placed.Forward.Y, placed.Forward.Z));
        return forward.LengthSquared() > 1e-12f && MathF.Abs(Vector3.Dot(Vector3.Normalize(forward), Vector3.UnitY)) < 0.9999f
            ? Matrix4x4.CreateWorld(position, Vector3.Normalize(forward), Vector3.UnitY)
            : Matrix4x4.CreateTranslation(position);                          // straight up or down: no way to turn it, as the original would fail
    }

    private static Matrix4x4 WithoutTranslation(Matrix4x4 m) => m with { M41 = 0, M42 = 0, M43 = 0 };
}
