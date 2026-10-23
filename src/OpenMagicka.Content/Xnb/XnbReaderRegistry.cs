using System.Collections.Frozen;
using OpenMagicka.Content.Xnb.Readers;

namespace OpenMagicka.Content.Xnb;

/// <summary>Reads the data of one content type; the type id has already been consumed.</summary>
public delegate object? XnbTypeReaderFunc(XnbContentReader reader);

/// <summary>Immutable map from <see cref="XnbTypeReader.Key"/> to the code that reads that type.</summary>
public sealed class XnbReaderRegistry
{
    private readonly FrozenDictionary<string, XnbTypeReaderFunc> readers;

    private XnbReaderRegistry(FrozenDictionary<string, XnbTypeReaderFunc> readers)
    {
        this.readers = readers;
    }

    /// <summary>The XNA 3.1 readers used by Magicka's content.</summary>
    public static XnbReaderRegistry Standard { get; } = new(StandardReaders.All.ToFrozenDictionary());

    /// <summary>Returns a new registry that also knows the given readers (game-specific ones).</summary>
    public XnbReaderRegistry With(IEnumerable<KeyValuePair<string, XnbTypeReaderFunc>> additional)
    {
        var merged = new Dictionary<string, XnbTypeReaderFunc>(readers);
        foreach (var (key, reader) in additional)
        {
            merged[key] = reader;
        }
        return new XnbReaderRegistry(merged.ToFrozenDictionary());
    }

    public bool TryGet(string key, out XnbTypeReaderFunc reader) => readers.TryGetValue(key, out reader!);
}

/// <summary>The file uses a content type that has no reader in the registry.</summary>
public sealed class XnbUnsupportedReaderException(string readerKey)
    : NotSupportedException($"no reader for XNB content type '{readerKey}'")
{
    public string ReaderKey { get; } = readerKey;
}
