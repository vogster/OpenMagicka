using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Engine.ScriptWorldActionsTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;

namespace OpenMagicka.Tests.Engine;

/// <summary>A keyboard-and-mouse player's controls on its avatar (OM-31).</summary>
public class KeyboardMouseControlTests
{
    private static readonly ItemTemplate Pick = new("pick", Bounds: new ItemBounds(new(-0.1f, 0, -0.1f), new(0.1f, 0.5f, 0.1f)), Pickable: true);

    private sealed record Setup(GameWorld World, Player Player, Character Avatar, ScriptScene Scene, KeyboardMouseControl Control) : IDisposable
    {
        public void Dispose() => World.Dispose();
    }

    private static Setup Start(string scripts = "")
    {
        var session = new GameSession();
        var world = new GameWorld(session: session);
        world.Physics.AddStaticMesh(2, [new(-50, 0, -50), new(50, 0, -50), new(50, 0, 50), new(-50, 0, 50)],
            [new OpenMagicka.Physics.MeshTriangle(0, 2, 1), new OpenMagicka.Physics.MeshTriangle(0, 3, 2)]);
        var player = session.AddPlayer();
        var avatar = Avatar(world, player);
        return new Setup(world, player, avatar, Scene(world, scripts), new KeyboardMouseControl(new ControlBindings()));
    }

    private static MouseState Mouse(bool left = false, bool right = false, bool middle = false, int wheel = 0) =>
        new(0, 0, wheel, Button(left), Button(middle), Button(right), ButtonState.Released, ButtonState.Released);

    private static ButtonState Button(bool down) => down ? ButtonState.Pressed : ButtonState.Released;

    /// <summary>The cursor straight over a point of the ground.</summary>
    private static CursorPick At(Numerics.Vector3 point) => new(new Segment(point + Numerics.Vector3.UnitY * 50, -Numerics.Vector3.UnitY * 100), point);

    /// <summary>One tick from one input to the next.</summary>
    private static void Step(Setup s, (Keys[] Keys, MouseState Mouse) now, (Keys[] Keys, MouseState Mouse) before, Numerics.Vector3? cursor = null, float dt = Tick) =>
        s.Control.Update(new ControlFrame(new KeyboardState(now.Keys), now.Mouse, new KeyboardState(before.Keys), before.Mouse), s.Player, s.Scene,
            At(cursor ?? s.Avatar.Position + new Numerics.Vector3(0, 0, -3)), dt);

    private static readonly (Keys[], MouseState) Nothing = ([], Mouse());

    [Fact]
    public void AnElementKey_GivesItsElement_OncePerCooldown_AndNotWhileForbidden()
    {
        using var s = Start("""<Trigger id="lock" autorun="false"><Then><disableElement element="fire"/></Then></Trigger>""");

        Step(s, ([Keys.Q], Mouse()), Nothing);
        Step(s, Nothing, ([Keys.Q], Mouse()));
        Step(s, ([Keys.Q], Mouse()), Nothing);                                                     // too soon
        Assert.Equal(1, s.Avatar.SpellQueue.Count);
        Step(s, Nothing, ([Keys.Q], Mouse()), dt: 0.2f);
        Step(s, ([Keys.Q], Mouse()), Nothing);
        Assert.Equal(2, s.Avatar.SpellQueue.Count);

        s.Scene.ExecuteTrigger(ScriptScene.Hash("lock"), null);
        s.Scene.Update(Tick);
        Step(s, ([Keys.F], Mouse()), Nothing);
        Assert.Equal(2, s.Avatar.SpellQueue.Count);
    }

    [Fact]
    public void TheCastButton_CastsForce_OrAreaWithShift_AndTheMiddleOneOnOneself()
    {
        using var s = Start();
        Step(s, ([Keys.F], Mouse()), Nothing);

        Step(s, ([], Mouse(right: true)), Nothing);
        Assert.Equal(CastType.Force, s.Avatar.HeldCast);
        Step(s, Nothing, ([], Mouse(right: true)));
        Assert.Equal(CastType.None, s.Avatar.HeldCast);

        s.Avatar.CastType = CastType.None;
        Step(s, ([Keys.F], Mouse()), Nothing, dt: 0.2f);                                       // a press combines the queue: one more
        Step(s, ([Keys.LeftShift], Mouse(right: true)), Nothing);
        Assert.Equal(CastType.Area, s.Avatar.HeldCast);
        Step(s, Nothing, ([Keys.LeftShift], Mouse(right: true)));

        s.Avatar.CastType = CastType.None;
        Step(s, ([Keys.F], Mouse()), Nothing, dt: 0.2f);
        Step(s, ([], Mouse(middle: true)), Nothing);
        Assert.Equal(CastType.Self, s.Avatar.HeldCast);
    }

    [Fact]
    public void ShiftAndMove_Attack_BlockHolds_AndTheWheelPicksTheMagicksThePlayersMayCast()
    {
        using var s = Start();
        s.Player.Unlock(MagickType.Haste);
        s.Player.Unlock(MagickType.Teleport);

        Step(s, ([Keys.LeftShift], Mouse(left: true)), Nothing);
        Assert.True(s.Avatar.Attacking);
        Assert.StartsWith("attack_melee", s.Avatar.NextAttackAnimation);

        Step(s, ([Keys.LeftControl], Mouse()), Nothing);
        Assert.True(s.Avatar.IsBlocking);
        Step(s, Nothing, ([Keys.LeftControl], Mouse()));
        Assert.False(s.Avatar.IsBlocking);

        Step(s, ([], Mouse(wheel: -120)), Nothing);
        Assert.Equal(MagickType.Haste, s.Player.SelectedMagick);
        Step(s, ([], Mouse(wheel: -240)), ([], Mouse(wheel: -120)));
        Assert.Equal(MagickType.Teleport, s.Player.SelectedMagick);
        Step(s, ([], Mouse(wheel: -120)), ([], Mouse(wheel: -240)));
        Assert.Equal(MagickType.Haste, s.Player.SelectedMagick);
    }

    [Fact]
    public void TheAvatar_FacesTheCursor_WalksWhileMoveIsHeld_AndStandsWhileInputIsHeld()
    {
        using var s = Start("""<Trigger id="hold" autorun="false"><Then><disableInput/></Then></Trigger>""");
        var east = s.Avatar.Position + new Numerics.Vector3(5, 0, 0);

        Step(s, Nothing, Nothing, east);
        Assert.Equal(Numerics.Vector3.Zero, s.Avatar.Body.Movement);
        Assert.True(Numerics.Vector3.Dot(s.Avatar.Body.WantedDirection, Numerics.Vector3.UnitX) > 0.99f);     // it turns to the cursor
        Step(s, ([], Mouse(left: true)), Nothing, east);
        Step(s, ([], Mouse(left: true)), ([], Mouse(left: true)), east);
        Step(s, ([], Mouse(left: true)), ([], Mouse(left: true)), east);
        Assert.True(s.Avatar.Body.Movement.X > 0.99f);

        Step(s, ([Keys.F, Keys.LeftControl], Mouse(left: true, right: true)), ([], Mouse(left: true)), east);
        Assert.Equal(CastType.Force, s.Avatar.HeldCast);
        s.Scene.ExecuteTrigger(ScriptScene.Hash("hold"), null);
        s.Scene.Update(Tick);
        Step(s, ([Keys.LeftControl], Mouse(left: true, right: true)), ([Keys.LeftControl], Mouse(left: true, right: true)), east);
        Assert.Equal(Numerics.Vector3.Zero, s.Avatar.Body.Movement);
        Assert.Equal(CastType.None, s.Avatar.HeldCast);                                           // held input lets go of the cast...
        Assert.False(s.Avatar.IsBlocking);                                                         // ...and the block
    }

    [Fact]
    public void APressOnAnItem_PicksItUpInReach_OrWalksToIt()
    {
        using var s = Start();
        var near = new WorldItem(s.World.Physics, new Item(Pick), Numerics.Matrix4x4.CreateTranslation(s.Avatar.Position.X + 1, 0, 0));
        var far = new WorldItem(s.World.Physics, new Item(Pick), Numerics.Matrix4x4.CreateTranslation(s.Avatar.Position.X + 8, 0, 0));
        s.World.Entities.Add(near);
        s.World.Entities.Add(far);
        s.World.Tick(Tick);                                                                        // the grid finds them from the next tick

        Step(s, ([], Mouse(left: true)), Nothing, near.Position);
        Assert.Same(near.Item, s.Avatar.ItemIn(0));

        Step(s, Nothing, ([], Mouse(left: true)), far.Position);
        Step(s, ([], Mouse(left: true)), Nothing, far.Position);
        for (var i = 0; i < 180 && !ReferenceEquals(s.Avatar.ItemIn(0), far.Item); i++)
        {
            Step(s, ([], Mouse(left: true)), ([], Mouse(left: true)), far.Position);
            s.World.Tick(Tick);
        }
        Assert.Same(far.Item, s.Avatar.ItemIn(0));                                                // walked there and took it
    }
}
