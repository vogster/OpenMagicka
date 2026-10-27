using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine.Hud;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Engine;

/// <summary>The clips beside the tutorial's hints (OM-86): their pictures, timers, order and the keys' letters.</summary>
public class HintClipsTests
{
    private const string Letters = "QWERASDF";                                // the default keys, in the clips' letter order

    private static List<ClipPiece> At(HintAnimations clip, float time, bool pad = false)
    {
        var pieces = new List<ClipPiece>();
        HintClips.Pieces(clip, time, pad, Letters, pieces);
        return pieces;
    }

    [Fact]
    public void AClipsFlags_PlayInTheirBitOrder_NotTheScripts_AndLoop()
    {
        var clip = HintAnimations.ConjureArcane | HintAnimations.ConjureLightning;    // the castle's room 4 asks Arcane first

        Assert.Equal(4, HintClips.Duration(clip, pad: false));
        Assert.Equal('A', Assert.Single(At(clip, 0.5f)).Letter);                // Lightning (A by default) first
        Assert.Equal('S', Assert.Single(At(clip, 2.5f)).Letter);                // then Arcane
        Assert.Equal('A', Assert.Single(At(clip, 4.5f)).Letter);                // and round again
        Assert.Equal(10, HintClips.Duration(clip, pad: true));
        Assert.Empty(At(HintAnimations.None, 1));
    }

    [Fact]
    public void TheKeyboardsPictures_SwitchOnTheirTimers()
    {
        Assert.Equal(new Rectangle(432, 0, 80, 64), Assert.Single(At(HintAnimations.Interact, 0.4f)).Source);
        Assert.Equal(new Rectangle(432, 64, 80, 64), Assert.Single(At(HintAnimations.Interact, 0.6f)).Source);   // the left button lit
        Assert.Equal(new Rectangle(384, 96, 48, 48), Assert.Single(At(HintAnimations.ConjureFire, 1.3f)).Source);
        Assert.Equal(new Rectangle(336, 96, 48, 48), Assert.Single(At(HintAnimations.ConjureFire, 1.4f)).Source);  // lit at a third of its 2 s
        var area = At(HintAnimations.CastArea, 1.2f);
        Assert.Equal([new Vector2(-48, 0), new Vector2(-12, 0), new Vector2(32, 0)], area.Select(p => p.Offset));
        Assert.Equal([new Rectangle(368, 48, 64, 48), new Rectangle(192, 0, 32, 32), new Rectangle(432, 192, 80, 64)], area.Select(p => p.Source));
        Assert.Equal(new Rectangle(432, 64, 80, 64), At(HintAnimations.Attack, 1.2f)[2].Source);               // the same, with the left button
        Assert.Equal("ASF", new string([.. At(HintAnimations.Conjure, 0).Select(p => p.Letter)]));             // Haste's keys
    }

    [Fact]
    public void ThePadsPictures_AndItsStickMovingTowardTheElementThenRound()
    {
        Assert.Equal(new Rectangle(0, 128, 64, 64), Assert.Single(At(HintAnimations.Attack, 0.6f, pad: true)).Source);     // A lit
        var force = At(HintAnimations.CastForce, 0.6f, pad: true);
        Assert.Equal([new Rectangle(192, 192, 64, 36), new Rectangle(192, 164, 64, 28)], force.Select(p => p.Source));
        var life = At(HintAnimations.ConjureLife, 1.5f, pad: true);
        Assert.Equal([new Rectangle(128, 0, 64, 64), new Rectangle(64, 0, 64, 64)], life.Select(p => p.Source));     // the well, then the stick
        Assert.Equal(new Vector2(0, -9), life[1].Offset);                       // half way up
        Assert.Equal(Vector2.Zero, HintClips.StickAt(Elements.Life, 1));
        Assert.Equal(new Vector2(0, -18), HintClips.StickAt(Elements.Life, 2.5f));
        Assert.True(Vector2.Distance(new Vector2(-18, 0), HintClips.StickAt(Elements.Life, 4.5f)) < 1e-4f);   // then round to the left
        var half = HintClips.StickAt(Elements.Fire, 3.5f);
        Assert.Equal(18, half.Length(), 3);                                     // on the circle as it turns
        Assert.True(half.X > 0 && half.Y > 0);                                  // from right toward down
    }

    [Fact]
    public void TheRestOfBothSets_AndTheDeviceChoosingTheSegment()
    {
        Assert.Equal(new Rectangle(352, 192, 80, 64), Assert.Single(At(HintAnimations.NavMagick, 1)).Source);
        Assert.Equal(new Rectangle(432, 192, 80, 64), Assert.Single(At(HintAnimations.CastForce, 1)).Source);
        Assert.Equal(new Rectangle(432, 128, 80, 64), Assert.Single(At(HintAnimations.CastSelf, 1)).Source);
        Assert.Equal(new Rectangle(240, 48, 128, 48), Assert.Single(At(HintAnimations.CastMagick, 1)).Source);
        Assert.Equal(new Rectangle(304, 144, 64, 48), Assert.Single(At(HintAnimations.Block, 0.5f)).Source);
        Assert.Equal(new Rectangle(0, 192, 64, 64), Assert.Single(At(HintAnimations.Interact, 0.5f, pad: true)).Source);
        Assert.Equal(new Rectangle(128, 64, 64, 64), Assert.Single(At(HintAnimations.NavMagick, 0.5f, pad: true)).Source);
        Assert.Equal(new Rectangle(64, 0, 64, 64), Assert.Single(At(HintAnimations.Conjure, 1.9f, pad: true)).Source);
        Assert.Equal(new Rectangle(128, 192, 64, 36), At(HintAnimations.CastArea, 0.5f, pad: true)[0].Source);
        Assert.Equal(new Rectangle(64, 192, 64, 64), Assert.Single(At(HintAnimations.CastSelf, 0.5f, pad: true)).Source);
        Assert.Equal(new Rectangle(64, 128, 64, 64), Assert.Single(At(HintAnimations.CastMagick, 0.5f, pad: true)).Source);
        Assert.Equal([new Rectangle(192, 228, 64, 28), new Rectangle(192, 128, 64, 36)], At(HintAnimations.Block, 0.5f, pad: true).Select(p => p.Source));

        var room1a = HintAnimations.ConjureEarth | HintAnimations.CastForce;
        Assert.Equal((3.5f, 6.5f), (HintClips.Duration(room1a, false), HintClips.Duration(room1a, true)));
        Assert.Equal('D', At(room1a, 1)[0].Letter);                             // the keyboard: Earth's key for 2 s,
        Assert.Equal(new Rectangle(432, 0, 80, 64), At(room1a, 2.5f)[0].Source);   // then the mouse for the force cast;
        Assert.Equal(new Rectangle(128, 0, 64, 64), At(room1a, 2.5f, pad: true)[0].Source);   // the pad still at Earth's stick
    }

    [Theory]
    [InlineData(Elements.Life, 0, -1, -1, 0)]
    [InlineData(Elements.Arcane, 0, -1, 1, 0)]
    [InlineData(Elements.Shield, 0, 1, 1, 0)]
    [InlineData(Elements.Earth, 0, 1, -1, 0)]
    [InlineData(Elements.Water, -1, 0, 0, -1)]
    [InlineData(Elements.Lightning, -1, 0, 0, 1)]
    [InlineData(Elements.Cold, 1, 0, 0, -1)]
    [InlineData(Elements.Fire, 1, 0, 0, 1)]
    public void TheStick_GoesTheElementsFirstWayThenItsSecond(Elements element, float x1, float y1, float x2, float y2)
    {
        Assert.True(Vector2.Distance(new Vector2(x1, y1) * 9, HintClips.StickAt(element, 1.5f)) < 1e-4f);
        Assert.True(Vector2.Distance(new Vector2(x1, y1) * 18, HintClips.StickAt(element, 2.5f)) < 1e-4f);
        Assert.Equal(18, HintClips.StickAt(element, 3.5f).Length(), 3);
        Assert.True(Vector2.Distance(new Vector2(x2, y2) * 18, HintClips.StickAt(element, 4.5f)) < 1e-4f);
    }

    [Fact]
    public void AKeysLetter_IsWhatItTypes_AButtonsNamesFirst_ElseItsNamesFirst()
    {
        string Text(string key) => key switch { "#mouse_right" => "right", _ => "?" };

        Assert.Equal('Q', HintClips.Letter(ControlBinding.Of(Keys.Q), Text));
        Assert.Equal('7', HintClips.Letter(ControlBinding.Of(Keys.D7), Text));
        Assert.Equal(';', HintClips.Letter(ControlBinding.Of(Keys.OemSemicolon), Text));
        Assert.Equal('S', HintClips.Letter(ControlBinding.Of(Keys.Space), Text));
        Assert.Equal('L', HintClips.Letter(ControlBinding.Of(Keys.LeftControl), Text));
        Assert.Equal('r', HintClips.Letter(ControlBinding.Of(MouseButton.Right), Text));      // as the language writes it
        Assert.Equal('4', HintClips.Letter(ControlBinding.Of(Keys.NumPad4), Text));
        Assert.Equal('T', HintClips.Letter(ControlBinding.Of(Keys.Tab), Text));
        Assert.Equal(' ', HintClips.Letter(default, Text));
        Assert.Equal(Letters, HintClips.Letters(new ControlBindings(), Text));
    }
}
