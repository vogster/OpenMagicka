namespace OpenMagicka.Content.Xnb;

/// <summary>An entry of the type reader table: assembly-qualified reader name and its version.</summary>
public sealed record XnbTypeReader(string Name, int Version)
{
    /// <summary>
    /// Reader type name without namespace and assembly, e.g. <c>Texture2DReader</c> or
    /// <c>ListReader`1[[System.String, ...]]</c>. Generic arguments are kept as written.
    /// </summary>
    public string ShortName
    {
        get
        {
            var generic = Name.IndexOf("[[", StringComparison.Ordinal);
            var close = generic >= 0 ? Name.LastIndexOf("]]", StringComparison.Ordinal) : -1;
            if (close <= generic)
            {
                generic = -1; // no well-formed generic argument list
            }
            var end = generic >= 0 ? close + 2 : Name.IndexOf(',');
            var typeName = end > 0 ? Name[..end] : Name;
            if (typeName.Length == 0)
            {
                return typeName;
            }
            var searchFrom = generic >= 0 ? Math.Min(generic, typeName.Length - 1) : typeName.Length - 1;
            var lastDot = typeName.LastIndexOf('.', searchFrom);
            return typeName[(lastDot + 1)..];
        }
    }
}
