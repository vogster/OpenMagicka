using OpenMagicka.Content;

namespace OpenMagicka.Tests.Content;

public class LegacyStringHashTests
{
    [Fact]
    public void EmptyString_MatchesTheKnownFrameworkValue()
    {
        // String.Empty.GetHashCode() of the 32-bit .NET Framework.
        Assert.Equal(757602046, LegacyStringHash.Compute(""));
    }

    /// <summary>Values printed by String.GetHashCode() of the 32-bit .NET Framework 4.8 (same algorithm as 2.0-3.5).</summary>
    [Theory]
    [InlineData("a", -842352705)]
    [InlineData("ab", -840386625)]
    [InlineData("abc", 536991770)]
    [InlineData("abcd", 1594742810)]
    [InlineData("abcde", 398757997)]
    [InlineData("goblin_die", 1107279161)]
    [InlineData("torch_fire", 1495317565)]
    [InlineData("levels/ruins/ru_s5", 1350603887)]
    [InlineData("any", -1694236580)]
    [InlineData("#item_staff_life", -1805031676)]
    [InlineData("Fire/torch", 1160579610)]
    [InlineData("chr_wizard_hit", 549415133)]
    [InlineData("\u8000", -842385504)]
    [InlineData("\u8000x", -838715488)]
    [InlineData("ab\uFFFF", -1134308738)]
    [InlineData("\uD83D\uDE00", -2083885165)]
    [InlineData("\u0416\u0418\u0412", 1100879130)]
    [InlineData(" x\u9000yz", 1834282506)]
    public void MatchesTheFramework(string value, int expected)
    {
        Assert.Equal(expected, LegacyStringHash.Compute(value));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    [InlineData("abcde")]
    [InlineData("goblin_die")]
    public void IsDeterministic_AndDependsOnEveryCharacter(string value)
    {
        var hash = LegacyStringHash.Compute(value);

        Assert.Equal(hash, LegacyStringHash.Compute(new string(value.ToCharArray())));
        Assert.NotEqual(hash, LegacyStringHash.Compute(value[..^1] + (char)(value[^1] + 1)));
        Assert.NotEqual(hash, LegacyStringHash.Compute(value + "x"));
    }

    [Fact]
    public void FollowsTheReferenceAlgorithmStepByStep()
    {
        // Independent implementation of the algorithm in docs/formats/XNB_READERS.md §0.4.
        static int Reference(string s)
        {
            int h1 = 352654597, h2 = 352654597;
            var words = new List<int>();
            for (var i = 0; i < s.Length; i += 2)
            {
                words.Add(s[i] | (i + 1 < s.Length ? s[i + 1] << 16 : 0));
            }
            var remaining = s.Length;
            var w = 0;
            while (remaining > 2)
            {
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ words[w++];
                h2 = ((h2 << 5) + h2 + (h2 >> 27)) ^ words[w++];
                remaining -= 4;
            }
            if (remaining > 0)
            {
                h1 = ((h1 << 5) + h1 + (h1 >> 27)) ^ words[w];
            }
            return unchecked(h1 + h2 * 1566083941);
        }

        foreach (var s in new[] { "", "x", "xy", "xyz", "fire_torch", "Levels/Ruins/ru_s5", "Ж-unicode" })
        {
            Assert.Equal(Reference(s), LegacyStringHash.Compute(s));
        }
    }

    [Fact]
    public void Lower_HashesTheInvariantLowerCase()
    {
        Assert.Equal(LegacyStringHash.Compute("torch_fire"), LegacyStringHash.Lower("Torch_FIRE"));
    }
}
