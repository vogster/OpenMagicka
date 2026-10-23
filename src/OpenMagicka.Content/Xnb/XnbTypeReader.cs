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

    /// <summary>
    /// Stable lookup key: type names without namespace, assembly or arity, generic arguments
    /// included recursively, e.g. <c>ListReader&lt;Vector3&gt;</c>,
    /// <c>DictionaryReader&lt;String,Object&gt;</c> or <c>ListReader&lt;List&lt;String&gt;&gt;</c>.
    /// A malformed name is returned unchanged, so it never matches a registered reader.
    /// </summary>
    public string Key
    {
        get
        {
            var position = 0;
            var key = TypeKey(Name, ref position);
            var rest = Name.AsSpan(position).TrimStart();
            return key is not null && (rest.IsEmpty || rest[0] == ',') ? key : Name;
        }
    }

    /// <summary>Parses "Namespace.Type`N[[Arg, Asm],[Arg, Asm]][]" starting at position.</summary>
    private static string? TypeKey(string name, ref int position)
    {
        var start = position;
        while (position < name.Length && name[position] is not ('`' or '[' or ',' or ']'))
        {
            position++;
        }
        var fullName = name[start..position].Trim();
        if (fullName.Length == 0)
        {
            return null;
        }
        var key = fullName[(fullName.LastIndexOf('.') + 1)..];

        if (position < name.Length && name[position] == '`')
        {
            var arityStart = ++position;
            while (position < name.Length && char.IsAsciiDigit(name[position]))
            {
                position++;
            }
            if (!int.TryParse(name.AsSpan(arityStart, position - arityStart), out var arity) || arity < 1)
            {
                return null;
            }
            var arguments = GenericArguments(name, ref position);
            if (arguments is null || arguments.Count != arity)
            {
                return null;
            }
            key += $"<{string.Join(',', arguments)}>";
        }

        while (position + 1 < name.Length && name[position] == '[' && name[position + 1] == ']')
        {
            key += "[]";
            position += 2;
        }
        return key;
    }

    /// <summary>Parses "[[Type, Assembly],[Type, Assembly]]" and returns the argument keys.</summary>
    private static List<string>? GenericArguments(string name, ref int position)
    {
        if (position >= name.Length || name[position] != '[')
        {
            return null;
        }
        position++;
        var arguments = new List<string>();
        while (true)
        {
            if (position >= name.Length || name[position] != '[')
            {
                return null;
            }
            position++;
            var argument = TypeKey(name, ref position);
            if (argument is null || !SkipAssembly(name, ref position))
            {
                return null;
            }
            arguments.Add(argument);
            if (position < name.Length && name[position] == ',')
            {
                position++;
                continue;
            }
            if (position < name.Length && name[position] == ']')
            {
                position++;
                return arguments;
            }
            return null;
        }
    }

    /// <summary>Skips ", Assembly, Version=..." up to and including the closing ']' of an argument.</summary>
    private static bool SkipAssembly(string name, ref int position)
    {
        var depth = 0;
        for (; position < name.Length; position++)
        {
            switch (name[position])
            {
                case '[':
                    depth++;
                    break;
                case ']' when depth == 0:
                    position++;
                    return true;
                case ']':
                    depth--;
                    break;
            }
        }
        return false;
    }
}
