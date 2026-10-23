using System.Text;
using Microsoft.Xna.Framework;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.PolygonHead;

/// <summary>Static level geometry: one root per material, each with an AABB tree over its index range.</summary>
public sealed record BiTreeModelContent(IReadOnlyList<BiTreeRootContent> Roots);

/// <summary>
/// One batch of static geometry. The stride is stored here, not in the declaration. The effect
/// is inline (usually <see cref="RenderDeferredEffectContent"/> or <see cref="AdditiveEffectContent"/>).
/// Drawn as a triangle list with base vertex 0.
/// </summary>
public sealed record BiTreeRootContent(
    bool Visible,
    bool CastShadows,
    float Sway,
    float EntityInfluence,
    float GroundLevel,
    int NumVertices,
    int VertexStride,
    VertexDeclarationContent? Declaration,
    VertexBufferContent? VertexBuffer,
    IndexBufferContent? IndexBuffer,
    object? Effect,
    BiTreeNodeContent Tree);

/// <summary>A node's index range covers the ranges of its children.</summary>
public sealed record BiTreeNodeContent(
    int PrimitiveCount,
    int StartIndex,
    BoundingBox Bounds,
    BiTreeNodeContent? ChildA,
    BiTreeNodeContent? ChildB);

/// <summary>Bitmap font: glyph rectangles in an atlas texture, in pixels.</summary>
public sealed record BitmapFontContent(
    int LineHeight,
    int BaseLine,
    Rune DefaultCharacter,
    ContentRef<Texture2DContent> Texture,
    IReadOnlyList<GlyphContent> Glyphs,
    IReadOnlyList<KerningPair> Kerning);

public sealed record GlyphContent(
    Rune Character, int X, int Y, int Width, int Height, int AdvanceWidth, int LeftSideBearing, bool ForceWhite);

public readonly record struct KerningPair(Rune First, Rune Second, int Amount);
