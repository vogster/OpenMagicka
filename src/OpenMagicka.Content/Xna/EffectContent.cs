using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Xna;

/// <summary>Compiled DX9 effect (fx_2_0) bytecode.</summary>
public sealed record EffectContent(byte[] Bytecode);

public sealed record BasicEffectContent(
    ExternalReference? Texture,
    Vector3 DiffuseColor,
    Vector3 EmissiveColor,
    Vector3 SpecularColor,
    float SpecularPower,
    float Alpha,
    bool VertexColorEnabled);

/// <summary>An effect in another file plus parameter values (floats, bools, vectors, matrices, texture references).</summary>
public sealed record EffectMaterialContent(ExternalReference? Effect, IReadOnlyDictionary<string, object?> Parameters);
