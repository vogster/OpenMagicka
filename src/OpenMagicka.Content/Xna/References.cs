namespace OpenMagicka.Content.Xna;

/// <summary>
/// An asset stored in another file: path relative to the directory of the referencing file,
/// without extension, with <c>\</c> separators (for example <c>..\..\Models\Items\staff_0</c>).
/// </summary>
public sealed record ExternalReference(string Path);

/// <summary>0-based index into the shared resources of the same file.</summary>
public readonly record struct SharedResourceRef(int Index);

/// <summary>
/// Value of an object field that holds either an inline asset or a reference to another file
/// (XNA writes an ExternalReference object in place of the asset when it lives elsewhere).
/// </summary>
public readonly record struct ContentRef<T> where T : class
{
    private ContentRef(T? inline, ExternalReference? external)
    {
        Inline = inline;
        External = external;
    }

    public T? Inline { get; }

    public ExternalReference? External { get; }

    /// <summary>Neither an inline asset nor a reference (the default value).</summary>
    public bool IsNull => Inline is null && External is null;

    public static ContentRef<T> FromInline(T inline) => new(inline ?? throw new ArgumentNullException(nameof(inline)), null);

    public static ContentRef<T> FromExternal(ExternalReference external) =>
        new(null, external ?? throw new ArgumentNullException(nameof(external)));
}
