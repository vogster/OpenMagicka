using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine.Input;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Engine;

/// <summary>The controls' bindings and rules (OM-31): the defaults and our file, the presses, the spell tree, the element cooldowns.</summary>
public class ControlsTests
{
    [Fact]
    public void TheDefaults_AreTheOriginals()
    {
        var bindings = new ControlBindings();

        Assert.Equal(ControlBinding.Of(Keys.Q), bindings[KeyboardBindings.Water]);
        Assert.Equal(ControlBinding.Of(Keys.F), bindings[KeyboardBindings.Fire]);
        Assert.Equal(ControlBinding.Of(MouseButton.Right), bindings[KeyboardBindings.Cast]);
        Assert.Equal(ControlBinding.Of(MouseButton.Left), bindings[KeyboardBindings.Move]);
        Assert.Equal(ControlBinding.Of(Keys.LeftControl), bindings[KeyboardBindings.Block]);
        Assert.Equal(ControlBinding.Of(MouseButton.ScrollDown), bindings[KeyboardBindings.NextMagick]);
    }

    [Fact]
    public void ABindingTaken_LeavesItsOldFunctionUnbound_AndSomeKeysCannotBeBound()
    {
        var bindings = new ControlBindings();

        Assert.True(bindings.Set(KeyboardBindings.Boost, ControlBinding.Of(Keys.Q)));
        Assert.False(bindings.Set(KeyboardBindings.Boost, ControlBinding.Of(Keys.Escape)));
        Assert.False(bindings.Set(KeyboardBindings.Boost, default));

        Assert.Equal(ControlBinding.Of(Keys.Q), bindings[KeyboardBindings.Boost]);
        Assert.False(bindings[KeyboardBindings.Water].IsBound);
    }

    [Fact]
    public void TheBindings_GoThroughOurFile_TheDefaultsFillingWhatItLacks()
    {
        var bindings = new ControlBindings();
        bindings.Set(KeyboardBindings.Block, ControlBinding.Of(MouseButton.X1));

        var loaded = ControlBindings.Load(bindings.Save());
        var partial = ControlBindings.Load("""{ "Fire": { "Key": "G", "Mouse": "Invalid" } }""");

        Assert.Equal(ControlBinding.Of(MouseButton.X1), loaded[KeyboardBindings.Block]);
        Assert.Equal(ControlBinding.Of(Keys.G), partial[KeyboardBindings.Fire]);
        Assert.Equal(ControlBinding.Of(Keys.Q), partial[KeyboardBindings.Water]);
        Assert.Throws<InvalidDataException>(() => ControlBindings.Load("{ nope"));
        Assert.Throws<InvalidDataException>(() => ControlBindings.Load("""{ "99": { "Key": "G", "Mouse": "Invalid" } }"""));
        var escape = ControlBindings.Load("""{ "Boost": { "Key": "Escape", "Mouse": "Invalid" }, "Fire": { "Key": "Q", "Mouse": "Invalid" } }""");
        Assert.Equal(ControlBinding.Of(Keys.Space), escape[KeyboardBindings.Boost]);              // Escape cannot be bound
        Assert.False(escape[KeyboardBindings.Water].IsBound);                                      // one key, one function
    }

    [Fact]
    public void APress_IsDownNowAndUpBefore_AndTheWheelPressesByItsTurn()
    {
        var up = new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        var right = new MouseState(0, 0, 120, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
        var pressed = new ControlFrame(new KeyboardState(Keys.Q), right, new KeyboardState(), up);
        var held = new ControlFrame(new KeyboardState(Keys.Q), right, new KeyboardState(Keys.Q), right);
        var released = new ControlFrame(new KeyboardState(), up, new KeyboardState(Keys.Q), right);

        Assert.True(pressed.IsPressed(ControlBinding.Of(Keys.Q)) && pressed.IsPressed(ControlBinding.Of(MouseButton.Right)));
        Assert.True(pressed.IsPressed(ControlBinding.Of(MouseButton.ScrollUp)));
        Assert.False(pressed.IsPressed(ControlBinding.Of(MouseButton.ScrollDown)));
        Assert.True(held.IsDown(ControlBinding.Of(Keys.Q)) && !held.IsPressed(ControlBinding.Of(Keys.Q)));
        Assert.True(released.IsReleased(ControlBinding.Of(Keys.Q)) && released.IsReleased(ControlBinding.Of(MouseButton.Right)));
        Assert.True(released.IsPressed(ControlBinding.Of(MouseButton.ScrollDown)));
        Assert.False(pressed.IsDown(default));
    }

    [Theory]
    [InlineData(Elements.Earth)]
    [InlineData(Elements.Water)]
    [InlineData(Elements.Cold)]
    [InlineData(Elements.Fire)]
    [InlineData(Elements.Lightning)]
    [InlineData(Elements.Arcane)]
    [InlineData(Elements.Life)]
    [InlineData(Elements.Shield)]
    public void EachElement_IsItsPairOfDirections(Elements element)
    {
        var combos = new SpellCombos();
        var (first, second) = SpellCombos.Pairs[element];

        Assert.Equal(Elements.None, combos.Give(first, _ => true));
        Assert.Equal(element, combos.Give(second, _ => true));
        Assert.Empty(combos.Kept);
    }

    [Fact]
    public void ADirectionWithNoBranch_DropsTheOldest_AndAForbiddenElementClearsThem()
    {
        var combos = new SpellCombos();

        combos.Give(ControllerDirection.Up, _ => true);
        Assert.Equal(Elements.None, combos.Give(ControllerDirection.Up, _ => true));             // up, up: no branch, the first up goes
        Assert.Equal([ControllerDirection.Up], combos.Kept);
        Assert.Equal(Elements.Life, combos.Give(ControllerDirection.Left, _ => true));
        combos.Give(ControllerDirection.Center, _ => true);
        Assert.Empty(combos.Kept);

        combos.Give(ControllerDirection.Right, _ => true);
        Assert.Equal(Elements.None, combos.Give(ControllerDirection.Down, e => e != Elements.Fire));
        Assert.Empty(combos.Kept);
    }

    [Fact]
    public void AnElementKey_WaitsForItsCooldown_AndTheTutorialsLock()
    {
        var cooldowns = new ElementCooldowns();

        Assert.True(cooldowns.CanCast(Elements.Fire, Elements.None));
        cooldowns.Start(Elements.Fire);
        Assert.False(cooldowns.CanCast(Elements.Fire, Elements.None));
        Assert.True(cooldowns.CanCast(Elements.Water, Elements.None));
        cooldowns.Update(0.1f);
        Assert.False(cooldowns.CanCast(Elements.Fire, Elements.None));
        cooldowns.Update(0.03f);
        Assert.True(cooldowns.CanCast(Elements.Fire, Elements.None));
        Assert.False(cooldowns.CanCast(Elements.Fire, Elements.Fire | Elements.Cold));
    }
}
