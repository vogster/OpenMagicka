using System.Text.Json;
using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Tools;

namespace OpenMagicka.Tests.Tools;

public class ContentJsonTests
{
    private abstract record Shape(string Name);

    private sealed record Circle(string Name, float Radius) : Shape(Name);

    private sealed record Holder(Shape Shape, IReadOnlyList<int> Numbers, byte[] Data, Vector3 Position, Elements Elements);

    private static JsonElement Dump(object? value, int maxItems = 256) =>
        JsonDocument.Parse(ContentJson.Serialize(value, maxItems)).RootElement;

    [Fact]
    public void Records_AreObjectsOfTheirRuntimeType_WithEveryPublicProperty()
    {
        var json = Dump(new Holder(new Circle("c", 2), [1, 2], [1, 2, 3], new Vector3(1, 2, 3), Elements.Fire | Elements.Water));

        Assert.Equal("Holder", json.GetProperty("$type").GetString());
        var shape = json.GetProperty("Shape");
        Assert.Equal("Circle", shape.GetProperty("$type").GetString());           // declared as Shape
        Assert.Equal(2, shape.GetProperty("Radius").GetSingle());
        Assert.Equal([1, 2], json.GetProperty("Numbers").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(3, json.GetProperty("Data").GetProperty("$bytes").GetInt32());
        Assert.Equal(3, json.GetProperty("Position").GetProperty("Z").GetSingle()); // fields of structs
        Assert.Equal("Water, Fire", json.GetProperty("Elements").GetString());
    }

    [Theory]
    [InlineData(float.NaN, "NaN")]
    [InlineData(float.PositiveInfinity, "Infinity")]
    [InlineData(float.NegativeInfinity, "-Infinity")]
    public void NonFiniteFloats_AreStrings(float value, string expected)
    {
        Assert.Equal(expected, Dump(value).GetString());
    }

    [Fact]
    public void Primitives_Strings_AndNull()
    {
        Assert.Equal(JsonValueKind.Null, Dump(null).ValueKind);
        Assert.Equal("x\"y", Dump("x\"y").GetString());
        Assert.Equal(-5, Dump((short)-5).GetInt32());
        Assert.Equal(ulong.MaxValue, Dump(ulong.MaxValue).GetUInt64());
        Assert.True(Dump(true).GetBoolean());
        Assert.Equal("a", Dump('a').GetString());
        Assert.Equal(1.5, Dump(1.5).GetDouble());
    }

    [Fact]
    public void LongLists_AreCut_WithTheNumberLeftOut()
    {
        var json = Dump(Enumerable.Range(0, 10).ToArray(), maxItems: 4);

        Assert.Equal(5, json.GetArrayLength());
        Assert.Equal(6, json[4].GetProperty("$more").GetInt32());
        Assert.Equal(10, Dump(Enumerable.Range(0, 10).ToArray(), maxItems: 0).GetArrayLength());   // 0: no limit
    }

    [Fact]
    public void Dictionaries_AreObjectsKeyedByTheirKeys()
    {
        var json = Dump(new Dictionary<int, string> { [7] = "seven" });

        Assert.Equal("seven", json.GetProperty("7").GetString());
    }

    [Fact]
    public void DeepGraphs_StopAtTheDepthLimit()
    {
        object node = "leaf";
        for (var i = 0; i < ContentJson.MaxDepth + 10; i++)
        {
            node = new[] { node };
        }

        var text = ContentJson.Serialize(node, 0);

        Assert.Contains("$depth", text, StringComparison.Ordinal);
    }

    [Fact]
    public void APropertyThatThrows_IsReportedInPlace()
    {
        var json = Dump(new Throwing());

        Assert.Equal("broken", json.GetProperty("Value").GetProperty("$error").GetString());
    }

    [Fact]
    public void Cycles_AreMarked_NotFollowed()
    {
        var node = new Node();
        node.Children.Add(node);
        node.Children.Add(node);

        var json = Dump(node);

        Assert.Equal("Node", json.GetProperty("Children")[0].GetProperty("$cycle").GetString());
        Assert.Equal(2, json.GetProperty("Children").GetArrayLength());
    }

    [Fact]
    public void Dictionaries_AreCutLikeLists()
    {
        var json = Dump(Enumerable.Range(0, 10).ToDictionary(i => i, i => i), maxItems: 4);

        Assert.Equal(6, json.GetProperty("$more").GetInt32());
        Assert.Equal(5, json.EnumerateObject().Count());
    }

    [Fact]
    public void OneShotSequences_AreEnumeratedOnce()
    {
        var runs = 0;
        IEnumerable<int> Numbers()
        {
            runs++;
            for (var i = 0; i < 10; i++)
            {
                yield return i;
            }
        }

        var json = Dump(new { Items = Numbers() }, maxItems: 3);

        Assert.Equal(1, runs);
        Assert.Equal(7, json.GetProperty("Items")[3].GetProperty("$more").GetInt32());
    }

    [Fact]
    public void ListsOfPairs_AreObjects_AndThrowingSequencesAreReported()
    {
        Assert.Equal(2, Dump(new List<KeyValuePair<string, int>> { new("a", 2) }).GetProperty("a").GetInt32());
        Assert.Equal("bad", Dump(new { Items = Failing() }).GetProperty("Items").GetProperty("$error").GetString());

        static IEnumerable<int> Failing()
        {
            yield return 1;
            throw new InvalidOperationException("bad");
        }
    }

    [Fact]
    public void MatricesAndVectors_WriteOnlyTheirFields()
    {
        var matrix = Dump(Matrix.Identity);

        Assert.Equal(16, matrix.EnumerateObject().Count());
        Assert.Equal(1, matrix.GetProperty("M44").GetSingle());
    }

    [Fact]
    public void Text_IsWrittenAsIs_AndValueTypesWithoutMembersAsText()
    {
        Assert.Contains("Привет", ContentJson.Serialize("Привет", 0), StringComparison.Ordinal);
        Assert.Equal("1.5", Dump((Half)1.5f).GetString());
    }

    private sealed class Node
    {
        public List<Node> Children { get; } = [];
    }

    private sealed class Throwing
    {
        public int Value => throw new InvalidOperationException("broken");
    }
}
