using System.Numerics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>What a template looks like: the asset its model is in (a model, or a prop's template that holds one) and its tint.</summary>
public sealed record ModelLook(string Asset, Vector3 Tint)
{
    /// <summary>The book of magick lying in the world, as the original draws every one.</summary>
    public static ModelLook Book { get; } = new("Models/Items_Wizard/magickbook_major", Vector3.One);
}
