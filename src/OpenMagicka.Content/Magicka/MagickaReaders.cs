using OpenMagicka.Content.Xnb;

namespace OpenMagicka.Content.Magicka;

/// <summary>Content readers of the Magicka game itself (Magicka.ContentReaders.*).</summary>
internal static class MagickaReaders
{
    public static IReadOnlyDictionary<string, XnbTypeReaderFunc> All { get; } = new Dictionary<string, XnbTypeReaderFunc>
    {
        ["LevelModelReader"] = LevelModelReader.Read,
    };
}

/// <summary>Limits of the level reader, public for tests and tools.</summary>
public static class LevelModelReaderLimits
{
    public const int MaxPartDepth = LevelModelReader.MaxPartDepth;
}
