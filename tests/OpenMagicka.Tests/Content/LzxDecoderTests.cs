using System.Text;
using OpenMagicka.Content.Lzx;

namespace OpenMagicka.Tests.Content;

public class LzxDecoderTests
{
    private static byte[] Decode(LzxDecoder decoder, byte[] frame, int size)
    {
        var output = new byte[size];
        decoder.Decompress(frame, output);
        return output;
    }

    [Fact]
    public void VerbatimBlock_Literals_AreCopied()
    {
        var text = "Magicka"u8.ToArray();
        var encoder = new LzxTestEncoder().VerbatimBlock(text.Length);
        foreach (var b in text)
        {
            encoder.Literal(b);
        }

        var output = Decode(new LzxDecoder(), encoder.TakeFrame(), text.Length);

        Assert.Equal(text, output);
    }

    [Fact]
    public void RepeatMatch_UsesInitialOffsetOne()
    {
        var frame = new LzxTestEncoder()
            .VerbatimBlock(9)
            .Literal((byte)'a')
            .RepeatMatch(8)
            .TakeFrame();

        var output = Decode(new LzxDecoder(), frame, 9);

        Assert.Equal("aaaaaaaaa", Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void OffsetMatch_CopiesFromWindowAndBecomesRepeatOffset()
    {
        var frame = new LzxTestEncoder()
            .VerbatimBlock(10)
            .Literal((byte)'a').Literal((byte)'b').Literal((byte)'c')
            .OffsetMatch(offset: 3, length: 3)   // abc
            .RepeatMatch(4)                      // abca, offset 3 is now R0
            .TakeFrame();

        var output = Decode(new LzxDecoder(), frame, 10);

        Assert.Equal("abcabcabca", Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void UncompressedBlock_IsCopiedAndSetsRepeatOffsets()
    {
        var frame = new LzxTestEncoder()
            .UncompressedBlock("xyz"u8, r0: 2)
            .TakeFrame();

        var output = Decode(new LzxDecoder(), frame, 3);

        Assert.Equal("xyz", Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void BlockSpanningTwoFrames_KeepsTreesAndWindow()
    {
        var encoder = new LzxTestEncoder().VerbatimBlock(6).Literal((byte)'h').Literal((byte)'o');
        var first = encoder.TakeFrame();
        var second = encoder.OffsetMatch(offset: 2, length: 2).RepeatMatch(2).TakeFrame();
        var decoder = new LzxDecoder();

        var a = Decode(decoder, first, 2);
        var b = Decode(decoder, second, 4);

        Assert.Equal("hohoho", Encoding.ASCII.GetString([.. a, .. b]));
    }

    [Fact]
    public void SecondBlock_AfterUncompressedBlock_ContinuesTheWindow()
    {
        var encoder = new LzxTestEncoder().UncompressedBlock("ab"u8);
        var first = encoder.TakeFrame();
        var second = encoder.VerbatimBlock(4).OffsetMatch(offset: 2, length: 4).TakeFrame();
        var decoder = new LzxDecoder();

        var a = Decode(decoder, first, 2);
        var b = Decode(decoder, second, 4);

        Assert.Equal("ababab", Encoding.ASCII.GetString([.. a, .. b]));
    }

    [Fact]
    public void MatchCrossingTheEndOfAFrame_ContinuesInTheNextFrame()
    {
        // One 8-byte match against output frames of 3 and 6 bytes.
        var frame = new LzxTestEncoder().VerbatimBlock(9).Literal((byte)'z').RepeatMatch(8).TakeFrame();
        var decoder = new LzxDecoder();

        var a = Decode(decoder, frame, 3);
        var b = Decode(decoder, [], 6);

        Assert.Equal("zzzzzzzzz", Encoding.ASCII.GetString([.. a, .. b]));
    }

    [Fact]
    public void EmptyBlock_Throws()
    {
        var frame = new LzxTestEncoder().VerbatimBlock(0).TakeFrame();

        Assert.Throws<InvalidDataException>(() => Decode(new LzxDecoder(), frame, 4));
    }

    [Fact]
    public void MatchBeforeTheStartOfTheData_Throws()
    {
        var frame = new LzxTestEncoder().VerbatimBlock(4).Literal((byte)'a').OffsetMatch(offset: 3, length: 3).TakeFrame();

        Assert.Throws<InvalidDataException>(() => Decode(new LzxDecoder(), frame, 4));
    }

    [Fact]
    public void AfterAFailure_TheDecoderRefusesFurtherFrames()
    {
        var decoder = new LzxDecoder();
        var bad = new LzxTestEncoder().RawHeader(blockType: 5, size: 4).TakeFrame();
        Assert.Throws<InvalidDataException>(() => Decode(decoder, bad, 4));

        var good = new LzxTestEncoder().VerbatimBlock(1).Literal(1).TakeFrame();

        Assert.Throws<InvalidOperationException>(() => Decode(decoder, good, 1));
    }

    [Fact]
    public void UnknownBlockType_Throws()
    {
        var frame = new LzxTestEncoder().RawHeader(blockType: 5, size: 4).TakeFrame();

        Assert.Throws<InvalidDataException>(() => Decode(new LzxDecoder(), frame, 4));
    }

    [Fact]
    public void TruncatedUncompressedBlock_Throws()
    {
        var frame = new LzxTestEncoder().UncompressedBlock("abcdef"u8).TakeFrame();

        Assert.Throws<InvalidDataException>(() => Decode(new LzxDecoder(), frame[..^4], 6));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(20)]
    [InlineData(22)]
    public void Constructor_RejectsUnsupportedWindowSizes(int windowBits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LzxDecoder(windowBits));
    }
}
