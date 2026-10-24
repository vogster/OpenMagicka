using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Gameplay.AI;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using Factions = OpenMagicka.Gameplay.Characters.Factions;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// A scene's trigger area (LEVELS_TRIGGERS.md §2.3): an oriented box, and who is in it this frame.
/// An entity is in when the box comes within its radius of its centre (the original's own reckoning
/// when it fills an area by hand; its physics fills it by contact). The dead count only as present,
/// not among the characters, unless undying.
/// </summary>
public sealed class ScriptArea
{
    private readonly List<Entity> entities = [];
    private readonly List<Character> characters = [];
    private readonly Dictionary<string, int> types = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, int> factions = [];
    private readonly Quaternion inverse;
    private EntityManager? pending;
    private bool everyone;

    internal ScriptArea(int key, string name, Vector3 corner, Vector3 sides, Quaternion orientation)
    {
        (Key, Name, Corner, Sides, Orientation) = (key, name, corner, sides, Quaternion.Normalize(orientation));
        inverse = Quaternion.Inverse(Orientation);
    }

    public int Key { get; }

    public string Name { get; }

    public Vector3 Corner { get; }

    public Vector3 Sides { get; }

    public Quaternion Orientation { get; }

    public Vector3 Center => Corner + Vector3.Transform(Sides / 2, Orientation);

    public IReadOnlyList<Entity> Entities
    {
        get
        {
            Fill();
            return entities;
        }
    }

    /// <summary>The characters present (the dead too).</summary>
    public IReadOnlyList<Character> Characters
    {
        get
        {
            Fill();
            return characters;
        }
    }

    /// <summary>The living (or undying) characters counted.</summary>
    public int Count
    {
        get
        {
            Fill();
            return count;
        }
    }

    private int count;

    /// <summary>Of a template (by its id), counted.</summary>
    public int CountOf(string type)
    {
        Fill();
        return types.GetValueOrDefault(type);
    }

    /// <summary>Of any of the factions (their own, not as confused or charmed), counted.</summary>
    public int CountOf(Factions faction)
    {
        Fill();
        var count = 0;
        for (var i = 0; i <= 15; i++)
        {
            if (((int)faction & (1 << i)) != 0)
            {
                count += factions.GetValueOrDefault(1 << i);
            }
        }
        return count;
    }

    /// <summary>The box's point nearest a point.</summary>
    public Vector3 Closest(Vector3 point)
    {
        var local = Vector3.Transform(point - Corner, inverse);
        return Corner + Vector3.Transform(Vector3.Clamp(local, Vector3.Min(Vector3.Zero, Sides), Vector3.Max(Vector3.Zero, Sides)), Orientation);
    }

    public bool Holds(Entity entity) => Vector3.Distance(Closest(entity.Position), entity.Position) <= entity.Radius;

    /// <summary>A point anywhere in the box.</summary>
    public Vector3 RandomPoint(Random random) =>
        Corner + Vector3.Transform(new Vector3(random.NextSingle() * Sides.X, random.NextSingle() * Sides.Y, random.NextSingle() * Sides.Z), Orientation);

    /// <summary>A new frame: it is filled again when first asked (most areas are not asked every frame).</summary>
    internal void Refill(EntityManager manager, bool everyone) => (pending, this.everyone) = (manager, everyone);

    private void Fill()
    {
        if (pending is not { } manager)
        {
            return;
        }
        pending = null;
        entities.Clear();
        characters.Clear();
        types.Clear();
        factions.Clear();
        count = 0;
        var all = manager.Entities;
        for (var i = 0; i < all.Count; i++)
        {
            if (everyone || Holds(all[i]))
            {
                Add(all[i]);
            }
        }
    }

    private void Add(Entity entity)
    {
        entities.Add(entity);
        if (entity is not Character character)
        {
            return;
        }
        characters.Add(character);
        if (character.IsDead && !character.Template.Undying)
        {
            return;
        }
        types[character.Template.Id] = types.GetValueOrDefault(character.Template.Id) + 1;
        for (var i = 0; i <= 15; i++)
        {
            if (((int)character.Template.Factions & (1 << i)) != 0)
            {
                factions[1 << i] = factions.GetValueOrDefault(1 << i) + 1;
            }
        }
        count++;
    }
}

/// <summary>
/// A scene's places (§2.3): its trigger areas (names lowered, then hashed), the built-in "any" (the
/// whole level), and its locators (names hashed as written, so lower-case script references find only
/// lower-case names, as the original). The minds look here for zones and locators.
/// </summary>
public sealed class ScriptPlaces : IAiPlaces
{
    public static readonly int Any = LegacyStringHash.Compute("any");

    private readonly Dictionary<int, ScriptArea> areas = [];
    private readonly Dictionary<int, LocatorContent> locators = [];

    private readonly Dictionary<int, ScenePart> parts = [];

    public ScriptPlaces(LevelModelContent? level)
        : this(level?.TriggerAreas ?? [], level?.Locators ?? [], level?.AnimatedParts)
    {
        Model = level;
    }

    /// <param name="animatedParts">The level's animated parts (its locators and effects move with them).</param>
    public ScriptPlaces(IEnumerable<TriggerAreaContent> triggerAreas, IEnumerable<LocatorContent> sceneLocators,
        IEnumerable<AnimatedLevelPartContent>? animatedParts = null)
    {
        foreach (var content in animatedParts ?? [])
        {
            var part = new ScenePart(content);
            parts[part.Key] = part;
            part.Update(0, Matrix4x4.Identity);                                // at rest
        }
        foreach (var area in triggerAreas)
        {
            var key = LegacyStringHash.Lower(area.Name);
            areas[key] = new ScriptArea(key, area.Name.ToLowerInvariant(), Numerics(area.Corner), Numerics(area.SideLengths),
                new Quaternion(area.Orientation.X, area.Orientation.Y, area.Orientation.Z, area.Orientation.W));
        }
        areas[Any] = AnyArea;
        foreach (var locator in sceneLocators)
        {
            locators[LegacyStringHash.Compute(locator.Name)] = locator;
        }
    }

    public ScriptArea AnyArea { get; } = new(Any, "any", Vector3.Zero, Vector3.Zero, Quaternion.Identity);

    /// <summary>The level model they come from (its effects too); none for bare places.</summary>
    public LevelModelContent? Model { get; }

    /// <summary>The level's animated parts, the top ones (their children under them).</summary>
    public IEnumerable<ScenePart> Parts => parts.Values;

    /// <summary>A part by its path of names ("parent/child", any case); null when one of them is missing.</summary>
    public ScenePart? Part(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var names = path.Split('/');
        var part = parts.GetValueOrDefault(LegacyStringHash.Lower(names[0]));
        for (var i = 1; i < names.Length && part is not null; i++)
        {
            part = part.Child(LegacyStringHash.Lower(names[i]));
        }
        return part;
    }

    /// <summary>A frame of the parts: each plays on, its children with it.</summary>
    public void UpdateParts(float dt)
    {
        foreach (var part in parts.Values)
        {
            part.Update(dt, Matrix4x4.Identity);
        }
    }

    public IEnumerable<ScriptArea> Areas => areas.Values;

    public ScriptArea? Area(int key) => areas.GetValueOrDefault(key);

    public ScriptArea? Area(string name) => Area(LegacyStringHash.Lower(name));

    public Vector3? LocatorPosition(int key) => LocatorTransform(key)?.Translation;

    /// <summary>A scene locator's transform, else a part's (where the part is now), as the original looks them up.</summary>
    public Matrix4x4? LocatorTransform(int key)
    {
        if (locators.TryGetValue(key, out var locator))
        {
            return Navigation.LevelNavigation.ToNumerics(locator.Transform);
        }
        foreach (var part in parts.Values)
        {
            if (part.Locator(key) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Where a script puts something at a place (the original's scene locator): a locator's transform;
    /// an area's random point, turned a random way about the vertical; a part's locator, where the part
    /// is now; else the origin.
    /// </summary>
    public Matrix4x4 Placement(int key, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (locators.TryGetValue(key, out var locator))
        {
            return Navigation.LevelNavigation.ToNumerics(locator.Transform);
        }
        if (key != Any && Area(key) is { } area)
        {
            var turned = Matrix4x4.CreateRotationY(random.NextSingle() * MathF.Tau);
            turned.Translation = area.RandomPoint(random);
            return turned;
        }
        return LocatorTransform(key) ?? Matrix4x4.Identity;
    }

    /// <summary>The way a transform faces (its forward, −Z), of unit length (a scaled locator's too).</summary>
    public static Vector3 Forward(Matrix4x4 transform) =>
        new Vector3(-transform.M31, -transform.M32, -transform.M33) is var forward && forward.LengthSquared() > 1e-12f ? Vector3.Normalize(forward) : -Vector3.UnitZ;

    /// <summary>Who is where this frame: each area refilled, "any" with everything.</summary>
    public void Refill(EntityManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        foreach (var area in areas.Values)
        {
            area.Refill(manager, everyone: ReferenceEquals(area, AnyArea));
        }
    }

    Vector3? IAiPlaces.ClosestInArea(int area, Vector3 point) => area == Any ? null : Area(area)?.Closest(point);

    Vector3? IAiPlaces.Locator(int locator) => LocatorPosition(locator);

    private static Vector3 Numerics(Microsoft.Xna.Framework.Vector3 v) => new(v.X, v.Y, v.Z);
}
