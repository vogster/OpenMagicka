using OpenMagicka.Engine.Hud;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Spells;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The element keys in the corner and the bar across the top (OM-54 phase C).</summary>
public class HudCornerTests
{
    private const float Tick = 1 / 60f;

    [Fact]
    public void TheKeys_AreLabelledByTheirBindings_InTheOriginalsLayout()
    {
        var keys = new List<ElementKey>();
        new ElementKeys().Write(new ControlBindings(), keys);

        Assert.Equal(["Q", "A", "W", "S", "E", "D", "R", "F"], keys.Select(k => k.Label));
        Assert.Equal((Elements.Water, 40f, 0), (keys[0].Element, keys[0].X, keys[0].Row));
        Assert.Equal((Elements.Fire, 215f, 1), (keys[7].Element, keys[7].X, keys[7].Row));
        Assert.Equal("1", ElementKeys.Label(ControlBinding.Of(Microsoft.Xna.Framework.Input.Keys.D1)));
    }

    [Fact]
    public void APressedKey_DimsThenFlashes_ThenSettles_AndADisabledOneGreys()
    {
        var keys = new ElementKeys();
        keys.Press(Elements.Fire);
        keys.Update(Tick, Elements.None);
        Assert.Equal(0.5f, keys[Elements.Fire].Saturation);

        for (var t = 0f; t < 0.2f; t += Tick)
        {
            keys.Update(Tick, Elements.None);
        }
        Assert.True(keys[Elements.Fire].Intensity > 1);                                             // the flash
        for (var t = 0f; t < 0.3f; t += Tick)
        {
            keys.Update(Tick, Elements.None);
        }
        Assert.Equal((1f, 1f), keys[Elements.Fire]);

        for (var t = 0f; t < 0.5f; t += Tick)
        {
            keys.Update(Tick, Elements.Arcane);
        }
        Assert.Equal(0, keys[Elements.Arcane].Saturation);
    }

    [Fact]
    public void TheBossBar_FadesInEmpty_FillsToTheShare_AndFadesWhenTheyFall()
    {
        var bar = new BossBar();
        bar.Update(0.5f, running: true, share: 0.7f);
        Assert.Equal((0.25f, 0f), (bar.Alpha, bar.Shown));

        for (var t = 0f; t < 3; t += Tick)
        {
            bar.Update(Tick, running: true, share: 0.7f);
        }
        Assert.Equal(1, bar.Alpha);
        Assert.Equal(0.7f, bar.Shown, 2);

        bar.Update(1, running: true, share: 0);
        Assert.Equal(0.5f, bar.Alpha);
        Assert.True(bar.Shown < 0.1f);
        bar.Update(1, running: false, share: 0);
        bar.Update(Tick, running: false, share: 0);
        Assert.False(bar.IsShown);
    }

    [Fact]
    public void TheTopBar_IsAFrameAFillInsetAndAnOrnament_Centred()
    {
        var quads = new List<GuiSprite>();
        GuiPass.TopBar(new TopBarPicture(0.5f, 0.5f, new Xna.Vector4(1, 0, 0, 1), 1), 1000, 512, 512, quads);

        Assert.Equal(7, quads.Count);
        Assert.Equal(new Xna.Vector2(250, 8), quads[0].TopLeft);
        Assert.Equal(new Xna.Vector2(750, 32), quads[2].BottomRight);
        Assert.Equal(new Xna.Vector2(266, 8), quads[3].TopLeft);                                     // the fill, 16 px in
        Assert.Equal(266 + (500 - 32) * 0.5f, quads[3].BottomRight.X);
        Assert.Equal(new Xna.Vector2(0, 24 / 512f), quads[4].UvTopLeft);                             // the ornament over it
    }

    [Fact]
    public void AKey_IsAShadowItsIconAndAPlate_AtItsRow()
    {
        var quads = new List<GuiSprite>();
        GuiPass.Key(new ElementKey(Elements.Arcane, "S", 115, 1, 1, 1), 720, 512, 512, quads);

        Assert.Equal(3, quads.Count);
        Assert.Equal(new Xna.Vector2(110, 645), quads[0].TopLeft);                                   // the shadow, 5 left and down
        Assert.Equal(new Xna.Vector2(115, 640), quads[1].TopLeft);
        Assert.Equal(new Xna.Vector2(0, 206 / 512f), quads[1].UvTopLeft);                           // Arcane's cell, the keys' 50-px rows
        Assert.Equal(new Xna.Vector2(103, 628), quads[2].TopLeft);
    }
}
