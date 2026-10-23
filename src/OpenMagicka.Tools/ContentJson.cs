using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenMagicka.Tools;

/// <summary>
/// Writes any loaded content as readable JSON: objects of their runtime type (with "$type" for
/// classes) and all public properties and fields (structs with fields: only the fields), enums
/// as names, non-finite floats as strings, byte buffers as their length only, long lists and
/// dictionaries cut after a number of items ("$more"), cycles marked ("$cycle"), and failures
/// while reading a member reported in its place ("$error").
/// </summary>
public static class ContentJson
{
    /// <summary>Nesting at which the dump stops; content trees are much shallower.</summary>
    public const int MaxDepth = 64;

    /// <param name="maxItems">Items written per list or dictionary before the rest is counted; 0 for all.</param>
    public static string Serialize(object? value, int maxItems)
    {
        using var stream = new MemoryStream();
        Write(stream, value, maxItems);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(Stream stream, object? value, int maxItems)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(maxItems);
        var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using var writer = new Utf8JsonWriter(stream, options);
        new Dumper(writer, maxItems).Value(value, 0);
    }

    private sealed class Dumper(Utf8JsonWriter writer, int maxItems)
    {
        private readonly HashSet<object> path = new(ReferenceEqualityComparer.Instance);

        public void Value(object? value, int depth)
        {
            if (depth > MaxDepth)
            {
                Marker("$depth", "nested too deep");
                return;
            }
            switch (value)
            {
                case null: writer.WriteNullValue(); break;
                case string s: writer.WriteStringValue(s); break;
                case char c: writer.WriteStringValue(c.ToString()); break;
                case bool b: writer.WriteBooleanValue(b); break;
                case Enum e: writer.WriteStringValue(e.ToString()); break;
                case float f when float.IsFinite(f): writer.WriteNumberValue(f); break;
                case double d when double.IsFinite(d): writer.WriteNumberValue(d); break;
                case float or double: writer.WriteStringValue(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture)); break;
                case decimal m: writer.WriteNumberValue(m); break;
                case byte or sbyte or short or ushort or int: writer.WriteNumberValue(Convert.ToInt32(value, CultureInfo.InvariantCulture)); break;
                case uint u: writer.WriteNumberValue(u); break;
                case long l: writer.WriteNumberValue(l); break;
                case ulong ul: writer.WriteNumberValue(ul); break;
                case byte[] bytes: Marker("$bytes", bytes.Length); break;
                case ReadOnlyMemory<byte> memory: Marker("$bytes", memory.Length); break;
                case Memory<byte> memory: Marker("$bytes", memory.Length); break;
                default: Composite(value, depth); break;
            }
        }

        /// <summary>Objects and sequences; a reference already being written is a cycle.</summary>
        private void Composite(object value, int depth)
        {
            if (!value.GetType().IsValueType && !path.Add(value))
            {
                Marker("$cycle", value.GetType().Name);
                return;
            }
            try
            {
                switch (value)
                {
                    case IDictionary dictionary: Sequence(Entries(dictionary), depth); break;
                    case IEnumerable sequence when Pairs(sequence) is { } pairs: Sequence(pairs, depth); break;
                    case IEnumerable sequence: Sequence(sequence.Cast<object?>(), depth); break;
                    default: Object(value, depth); break;
                }
            }
            finally
            {
                path.Remove(value);
            }
        }

        /// <summary>
        /// Takes the items to write and counts the rest in one pass, so that a lazy sequence runs
        /// once and a failing one is reported before anything of it is written.
        /// </summary>
        private void Sequence<T>(IEnumerable<T> items, int depth)
        {
            var taken = new List<T>();
            var more = 0;
            try
            {
                foreach (var item in items)
                {
                    if (maxItems == 0 || taken.Count < maxItems)
                    {
                        taken.Add(item);
                    }
                    else
                    {
                        more++;
                    }
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Marker("$error", e.Message);
                return;
            }
            if (taken is List<(object Key, object? Value)> pairs)
            {
                writer.WriteStartObject();
                foreach (var (key, item) in pairs)
                {
                    writer.WritePropertyName(Convert.ToString(key, CultureInfo.InvariantCulture) ?? "");
                    Value(item, depth + 1);
                }
                if (more > 0)
                {
                    writer.WriteNumber("$more", more);
                }
                writer.WriteEndObject();
                return;
            }
            writer.WriteStartArray();
            foreach (var item in taken)
            {
                Value(item, depth + 1);
            }
            if (more > 0)
            {
                Marker("$more", more);
            }
            writer.WriteEndArray();
        }

        /// <summary>A non-generic dictionary's entries (its plain enumerator may yield other types).</summary>
        private static IEnumerable<(object Key, object? Value)> Entries(IDictionary dictionary)
        {
            var entries = dictionary.GetEnumerator();
            while (entries.MoveNext())
            {
                yield return (entries.Key, entries.Value);
            }
        }

        /// <summary>The pairs of a sequence of KeyValuePair (a generic dictionary), or null.</summary>
        private static IEnumerable<(object Key, object? Value)>? Pairs(IEnumerable sequence)
        {
            var element = sequence.GetType().GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(i => i.GetGenericArguments()[0])
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>));
            if (element is null)
            {
                return null;
            }
            var key = element.GetProperty("Key")!;
            var value = element.GetProperty("Value")!;
            return sequence.Cast<object>().Select(p => (key.GetValue(p)!, value.GetValue(p)));
        }

        private void Object(object value, int depth)
        {
            var type = value.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            // Structs such as vectors and matrices are their fields; their properties are derived.
            var properties = type.IsValueType && fields.Length > 0
                ? []
                : type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !p.PropertyType.IsByRefLike && !p.PropertyType.IsPointer)
                    .ToArray();
            if (type.IsValueType && fields.Length == 0 && properties.Length == 0)
            {
                writer.WriteStringValue(value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString());
                return;
            }
            writer.WriteStartObject();
            if (!type.IsValueType)
            {
                writer.WriteString("$type", type.Name);
            }
            foreach (var property in properties)
            {
                writer.WritePropertyName(property.Name);
                Member(() => property.GetValue(value), depth);
            }
            foreach (var field in fields.Where(f => !f.FieldType.IsByRefLike && !f.FieldType.IsPointer))
            {
                writer.WritePropertyName(field.Name);
                Member(() => field.GetValue(value), depth);
            }
            writer.WriteEndObject();
        }

        private void Member(Func<object?> read, int depth)
        {
            object? member;
            try
            {
                member = read();
            }
            catch (Exception e) when (e is TargetInvocationException or NotSupportedException or MemberAccessException)
            {
                Marker("$error", (e.InnerException ?? e).Message);
                return;
            }
            Value(member, depth + 1);
        }

        private void Marker(string name, string text)
        {
            writer.WriteStartObject();
            writer.WriteString(name, text);
            writer.WriteEndObject();
        }

        private void Marker(string name, int number)
        {
            writer.WriteStartObject();
            writer.WriteNumber(name, number);
            writer.WriteEndObject();
        }
    }
}
