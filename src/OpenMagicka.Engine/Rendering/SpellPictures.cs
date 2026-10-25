using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// What the spells show this frame of their own (docs/engine/ARCHITECTURE.md §17): the pictures the original
/// draws with renderers of their own rather than particles. Filled on the logic thread, drawn on the render thread.
/// </summary>
public sealed class SpellPictures
{
    public List<BeamPicture> Beams { get; } = [];

    public List<BoltPicture> Bolts { get; } = [];

    public List<DiscPicture> Discs { get; } = [];

    /// <summary>The lights along the beams and bolts.</summary>
    public List<CapsuleLighting> Lights { get; } = [];

    /// <summary>The blasts' passing lights, lit with the level's.</summary>
    public List<PointLighting> Points { get; } = [];

    public void Clear()
    {
        Beams.Clear();
        Bolts.Clear();
        Discs.Clear();
        Lights.Clear();
        Points.Clear();
    }
}

/// <summary>The rows of the beams' picture sheet a beam's other elements show along it.</summary>
[Flags]
public enum BeamRows : byte
{
    None = 0,
    Water = 1,
    Cold = 2,
    Fire = 4,
    Lightning = 8,
    Steam = 16,
    Poison = 32,
}

/// <summary>
/// A beam: where it starts and runs, how far, the time its waves have run (30 a second), the helices wound
/// round it (a branch's stay narrow), its colour, and the rows of its other elements (with any, only those
/// show, as the original).
/// </summary>
public readonly record struct BeamPicture(Vector3 Origin, Vector3 Direction, float Length, float Time, int Rings, bool Branch, Vector3 Colour,
    BeamRows Rows);

/// <summary>
/// A lightning bolt: from where it left to where it ended, its colour, how bright it still is (above 1 at first,
/// down to 0), and which of the zig-zags it takes.
/// </summary>
public readonly record struct BoltPicture(Vector3 From, Vector3 To, Vector3 Colour, float Brightness, int Shape);

/// <summary>
/// An arcane or life blast's disc: its centre, its radius now, how far it has turned, how much it still shows,
/// and the elements whose bands it shows (by their bits, Earth the lowest).
/// </summary>
public readonly record struct DiscPicture(Vector3 Centre, float Radius, float Turn, float Alpha, int Elements);
