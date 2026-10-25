using OpenMagicka.Engine.Hud;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>The floating numbers and the pad's wheel (OM-54 phase D).</summary>
public class HudPolishTests
{
    private static List<FloatingNumber> Shown(FloatingNumbers numbers)
    {
        var shown = new List<FloatingNumber>();
        numbers.Write(shown);
        return shown;
    }

    [Fact]
    public void Blows_WithinAThirdOfASecond_AddUpInOneNumber_ThatThenGoes()
    {
        using var world = World();
        var goblin = Spawn(world);
        var numbers = new FloatingNumbers();
        numbers.Update(Tick, [goblin], false);

        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 50, 1), null, goblin.Position);
        numbers.Update(Tick, [goblin], false);
        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 50, 1), null, goblin.Position);
        numbers.Update(Tick, [goblin], false);

        var number = Assert.Single(Shown(numbers));
        Assert.Equal("100", number.Text);
        Assert.Equal(FloatingNumbers.Damage, number.Colour);
        Assert.Equal(0.55f, number.Scale, 3);                                                       // a hundred: a little over half size
        for (var t = 0f; t < 0.8f; t += Tick)
        {
            numbers.Update(Tick, [goblin], false);
        }
        Assert.Empty(Shown(numbers));
    }

    [Fact]
    public void Healing_AddsUpAndShowsOnceASecond_WithAPlus()
    {
        using var world = World();
        var goblin = Spawn(world);
        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 100, 1), null, goblin.Position);
        var numbers = new FloatingNumbers();
        numbers.Update(Tick, [goblin], false);

        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Life, -30, 1), null, goblin.Position);
        for (var t = 0f; t < 1.05f; t += Tick)
        {
            numbers.Update(Tick, [goblin], false);
        }

        var heal = Assert.Single(Shown(numbers), n => n.Text.StartsWith('+'));
        Assert.Equal(FloatingNumbers.Heal, heal.Colour);
    }

    [Fact]
    public void ANumber_IsItsGlyphsCentred_AndThePlusIsTheEleventhCell()
    {
        var quads = new List<GuiSprite>();
        GuiPass.Number(new FloatingNumber(default, "+12", new System.Numerics.Vector4(0, 1, 0, 1), 1, 1), new Xna.Vector2(100, 100), 264, 32, quads);

        Assert.Equal(3, quads.Count);
        Assert.Equal(new Xna.Vector2(67, 84), quads[0].TopLeft);
        Assert.Equal(220 / 264f, quads[0].UvTopLeft.X, 4);
        Assert.Equal(new Xna.Vector2(133, 116), quads[2].BottomRight);
    }

    [Fact]
    public void TheWheel_ShowsAWaysPair_SlidingOut_ThenFades_AndHelpShowsAll()
    {
        var wheel = new SpellWheel();
        wheel.Update(0.01f, ControllerDirection.Right, false);
        var icons = new List<WheelIcon>();
        wheel.Write(icons);
        Assert.Equal([Elements.Cold, Elements.Fire], icons.Select(i => i.Element));
        Assert.Equal(64, icons[0].Offset.X, 0);                                                    // still near the way's point

        wheel.Update(0.3f, ControllerDirection.Right, false);
        icons.Clear();
        wheel.Write(icons);
        Assert.Equal(64 * MathF.Cos(MathF.PI / 8), icons[0].Offset.X, 2);
        Assert.Equal(-64 * MathF.Sin(MathF.PI / 8), icons[0].Offset.Y, 2);                        // up the screen

        wheel.Update(0.1f, ControllerDirection.Center, false);
        icons.Clear();
        wheel.Write(icons);
        Assert.Equal(0.5f, icons[0].Alpha, 2);
        wheel.Update(0.2f, ControllerDirection.Center, false);
        Assert.False(wheel.IsShown);

        wheel.Update(0.1f, ControllerDirection.Center, true);
        icons.Clear();
        wheel.Write(icons);
        Assert.Equal(8, icons.Count);
    }

    [Fact]
    public void Burning_ShowsInFiresColour_AndAScriptsLossShowsNothing()
    {
        using var world = World();
        var goblin = Spawn(world);
        var numbers = new FloatingNumbers();
        numbers.Update(Tick, [goblin], false);

        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Fire, 60, 1), null, goblin.Position);
        numbers.Update(Tick, [goblin], false);
        numbers.Clear();                                                                             // the blow's own number aside
        numbers.Update(Tick, [goblin], false);
        for (var t = 0f; t < 1.05f; t += Tick)
        {
            world.Tick(Tick);
            numbers.Update(Tick, [goblin], false);
        }
        Assert.Contains(Shown(numbers), n => n.Colour == FloatingNumbers.Burning);

        var (calm, other) = (new FloatingNumbers(), Spawn(world));
        calm.Update(Tick, [other], false);
        other.SetHealth(0.5f);
        for (var t = 0f; t < 1.05f; t += Tick)
        {
            calm.Update(Tick, [other], false);
        }
        Assert.Empty(Shown(calm));
    }
}
