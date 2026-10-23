namespace OpenMagicka.Content.Xna;

/// <summary>
/// An asset stored in another file: path relative to the directory of the referencing file,
/// without extension, with <c>\</c> separators (for example <c>..\..\Models\Items\staff_0</c>).
/// </summary>
public sealed record ExternalReference(string Path);

/// <summary>0-based index into the shared resources of the same file.</summary>
public readonly record struct SharedResourceRef(int Index);
