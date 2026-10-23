using System.Collections;
using System.Reflection;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Tests.Content;

/// <summary>Finds every ExternalReference in a loaded object graph (test helper, reflection based).</summary>
internal static class ExternalReferenceWalker
{
    public static IEnumerable<ExternalReference> Find(object? root)
    {
        var found = new List<ExternalReference>();
        Walk(root, found, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return found;
    }

    private static void Walk(object? value, List<ExternalReference> found, HashSet<object> seen)
    {
        switch (value)
        {
            case null or string or byte[]:
                return;
            case ExternalReference reference:
                found.Add(reference);
                return;
        }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("Microsoft.Xna", StringComparison.Ordinal) == true)
        {
            return;
        }
        if (!type.IsValueType && !seen.Add(value))
        {
            return;
        }
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                Walk(entry.Value, found, seen);
            }
            return;
        }
        if (value is IEnumerable items)
        {
            if (type.IsArray && type.GetElementType() is { } element && (element.IsPrimitive || element.Namespace?.StartsWith("Microsoft.Xna", StringComparison.Ordinal) == true))
            {
                return;
            }
            foreach (var item in items)
            {
                Walk(item, found, seen);
            }
            return;
        }
        if (type.Namespace?.StartsWith("OpenMagicka.Content", StringComparison.Ordinal) != true)
        {
            return;
        }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0 && property.Name != "EqualityContract")
            {
                Walk(property.GetValue(value), found, seen);
            }
        }
    }
}
