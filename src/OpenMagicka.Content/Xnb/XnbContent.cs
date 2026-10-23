using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.Xnb;

/// <summary>The objects of one XNB file: the primary object and the shared resources it points to.</summary>
public sealed class XnbContent
{
    private XnbContent(object? root, IReadOnlyList<object?> sharedResources)
    {
        Root = root;
        SharedResources = sharedResources;
    }

    public object? Root { get; }

    public IReadOnlyList<object?> SharedResources { get; }

    public object? Resolve(SharedResourceRef reference)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reference.Index, nameof(reference));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(reference.Index, SharedResources.Count, nameof(reference));
        return SharedResources[reference.Index];
    }

    /// <summary>
    /// Reads every object of the file. Throws <see cref="InvalidDataException"/> on malformed
    /// data (including bytes left over) and <see cref="XnbUnsupportedReaderException"/> when the
    /// file uses a type the registry does not know.
    /// </summary>
    public static XnbContent Load(XnbFile file, XnbReaderRegistry registry)
    {
        var reader = new XnbContentReader(file, registry);
        try
        {
            var root = reader.ReadObject();
            // Every shared resource takes at least one byte (its type id).
            if (reader.SharedResourceCount > reader.Remaining)
            {
                throw new InvalidDataException(
                    $"{reader.SharedResourceCount} shared resources declared, {reader.Remaining} bytes left");
            }
            var shared = new object?[reader.SharedResourceCount];
            for (var i = 0; i < shared.Length; i++)
            {
                shared[i] = reader.ReadObject();
            }
            if (reader.Remaining != 0)
            {
                throw new InvalidDataException($"{reader.Remaining} bytes left after the last object");
            }
            return new XnbContent(root, shared);
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            throw new InvalidDataException("XNB object data is truncated or malformed", e);
        }
        catch (Exception e) when (e is not (InvalidDataException or XnbUnsupportedReaderException
                                            or OutOfMemoryException or OperationCanceledException))
        {
            // A reader failing in any other way met data it did not expect.
            throw new InvalidDataException($"XNB content reader failed: {e.Message}", e);
        }
    }
}
