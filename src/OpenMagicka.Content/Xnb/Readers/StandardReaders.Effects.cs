using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.Xnb.Readers;

internal static partial class StandardReaders
{
    private static EffectContent ReadEffect(XnbContentReader r) => new(r.ReadSizedBytes());

    private static BasicEffectContent ReadBasicEffect(XnbContentReader r) => new(
        Texture: r.ReadExternalReference(),
        DiffuseColor: r.ReadVector3(),
        EmissiveColor: r.ReadVector3(),
        SpecularColor: r.ReadVector3(),
        SpecularPower: r.ReadSingle(),
        Alpha: r.ReadSingle(),
        VertexColorEnabled: r.ReadBoolean());

    private static EffectMaterialContent ReadEffectMaterial(XnbContentReader r)
    {
        var effect = r.ReadExternalReference();
        var parameters = r.ReadObject() switch
        {
            null => new Dictionary<string, object?>(),
            IReadOnlyDictionary<string, object?> found => found,
            var other => throw new InvalidDataException($"effect material parameters of type {other.GetType().Name}"),
        };
        return new EffectMaterialContent(effect, parameters);
    }
}
