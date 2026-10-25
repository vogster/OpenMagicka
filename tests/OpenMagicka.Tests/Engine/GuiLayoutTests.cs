using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Tests.Engine;

/// <summary>The GUI's layout (OM-49): glyph quads, speech boxes' sizes and places, the frame's 9-slice.</summary>
public class GuiLayoutTests
{
    [Fact]
    public void Text_IsLaidGlyphByGlyph_SpacingFirst_ColouredByTags_LinesAligned()
    {
        var font = BitmapTextTests.Font();
        var red = new Vector4(1, 0, 0, 1);

        var quads = TextLayout.Lay(font, "ab[c=1,0,0]c[/c]\nd", Vector4.One, TextAlignment.Left, 512, 512);

        Assert.Equal(4, quads.Count);
        Assert.Equal(new Vector2(1, 0), quads[0].TopLeft);                                              // the spacing before the first glyph
        Assert.Equal(new Vector2(12, 0), quads[1].TopLeft);
        Assert.Equal(red, quads[2].Colour);
        Assert.Equal(Vector4.One, quads[3].Colour);
        Assert.Equal(new Vector2(1, 20), quads[3].TopLeft);                                             // the next line
        Assert.Equal(new Vector2(10f / 512, 0), quads[1].UvTopLeft);

        var centred = TextLayout.Lay(font, "ab", Vector4.One, TextAlignment.Center, 512, 512);
        Assert.Equal(-10, centred[0].TopLeft.X);                                                          // half of 22, floored, to the left
        Assert.Equal(new Vector4(0.5f, 1, 0, 0.25f), TextLayout.Colour("c=0.5,1,0,0.25"));
        Assert.Null(TextLayout.Colour("p=1"));
    }

    [Fact]
    public void ASpeechBox_IsItsTextOrItsLeastSize_GrownForAName_AndTheCutscenes640Wide()
    {
        var font = BitmapTextTests.Font();
        var box = new SpeechBoxPicture("abc", 3, null, 1, Vector3.Zero, null, new Point(10, 10), true, false);

        Assert.Equal(new Point(32, 20), GuiPass.Size(box, font, font));
        Assert.Equal(new Point(100, 20), GuiPass.Size(box with { MinSize = new Point(100, 5) }, font, font));
        Assert.Equal(new Point(54, 30), GuiPass.Size(box with { Name = "abcde" }, font, font));        // name 54 wide; 20 + 10 < 54; its line on the least
        Assert.Equal(GuiPass.CutsceneWidth, GuiPass.Size(box with { Cutscene = true }, font, font).X);
    }

    [Fact]
    public void ASpeechBox_SitsLeftAndAboveItsSpeaker_KeptOnTheScreen_TheCutscenesLowInTheMiddle()
    {
        var box = new SpeechBoxPicture("abc", 3, null, 1, Vector3.Zero, null, new Point(100, 40), true, false);
        var size = new Point(100, 40);

        Assert.Equal(new Vector2(400 - (50 + 64) / 3f, 300 - (20 + 64)), GuiPass.Place(box, size, new Vector2(400, 300), 1280, 720));
        Assert.Equal(new Vector2(66 + 64, 36 + 36), GuiPass.Place(box, size, new Vector2(0, 0), 1280, 720));   // 5 % margins
        Assert.Equal(new Vector2(640, 576), GuiPass.Place(box with { Cutscene = true }, size, null, 1280, 720));
        Assert.Equal(720 - (200 + 32) / 2 - 72, GuiPass.Place(box with { Cutscene = true }, new Point(100, 200), null, 1280, 720).Y);   // above the bar
        Assert.Equal(new Vector2(320, 360), GuiPass.Place(box with { OnScreen = new Vector2(-0.5f, 0), ForceOnScreen = false }, size, null, 1280, 720));
    }

    [Fact]
    public void TheFrame_IsANineSliceGrid_ItsOuterRingPushedOutByTheBorder()
    {
        var (vertices, indices) = GuiPass.Grid();

        Assert.Equal(16, vertices.Length);
        Assert.Equal(54, indices.Length);
        Assert.Equal(new Vector3(-0.5f, -0.5f, 0), vertices[0].Position);
        Assert.Equal(new Vector3(-1, -1, 0), vertices[0].Normal);
        Assert.Equal(new Vector3(0, 0, 0), vertices[5].Normal);                                             // an inner corner: not pushed
        Assert.Equal(new Vector2(0.25f, 0.25f), vertices[5].TextureCoordinate);
    }

    [Fact]
    public void AWorldPoint_IsProjectedToPixelsFromTheTopLeft()
    {
        var view = Matrix.CreateLookAt(new Vector3(0, 0, 10), Vector3.Zero, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1280 / 720f, 0.1f, 100);

        Assert.Equal(new Vector2(640, 360), GuiPass.Project(Vector3.Zero, view * projection, 1280, 720));
        Assert.True(GuiPass.Project(new Vector3(0, 1, 0), view * projection, 1280, 720).Y < 360);
    }
}
