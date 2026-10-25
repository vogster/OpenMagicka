using Microsoft.Xna.Framework.Input;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Engine.ScriptWorldActionsTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Numerics = System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Tests.Engine;

/// <summary>A pad player's controls on its avatar (OM-31).</summary>
public class GamePadControlTests
{
    private sealed record Setup(GameWorld World, Player Player, Character Avatar, ScriptScene Scene, GamePadControl Control) : IDisposable
    {
        public GamePadState Last { get; set; }

        public void Dispose() => World.Dispose();

        public void Step(GamePadState pad)
        {
            Control.Update(new PadFrame(pad, Last, new PadBindings()), Player, Scene);
            Last = pad;
        }
    }

    private static Setup Start()
    {
        var session = new GameSession();
        var world = new GameWorld(session: session);
        world.Physics.AddStaticMesh(2, [new(-50, 0, -50), new(50, 0, -50), new(50, 0, 50), new(-50, 0, 50)],
            [new OpenMagicka.Physics.MeshTriangle(0, 2, 1), new OpenMagicka.Physics.MeshTriangle(0, 3, 2)]);
        var player = session.AddPlayer();
        var avatar = Avatar(world, player);
        return new Setup(world, player, avatar, Scene(world, ""), new GamePadControl());
    }

    private static GamePadState Pad(Xna.Vector2 left = default, Xna.Vector2 right = default, float leftTrigger = 0, float rightTrigger = 0,
        params Buttons[] buttons) => new(left, right, leftTrigger, rightTrigger, buttons);

    [Fact]
    public void ARightStickGesture_GivesTheElementOfItsPair_AndHoldsUntilTheStickRests()
    {
        using var s = Start();

        s.Step(Pad(right: new(0, 1)));                                                            // up
        s.Step(Pad(right: new(-0.8f, 0.6f)));                                                     // turned left
        Assert.Equal(1, s.Avatar.SpellQueue.Count);
        Assert.Equal(Elements.Life, s.Avatar.SpellQueue.Entries[0].Element);
        s.Step(Pad(right: new(0, -1)));                                                           // held: nothing more
        Assert.Equal(1, s.Avatar.SpellQueue.Count);

        s.Step(Pad());
        s.Step(Pad(right: new(0, 0.45f)));                                                        // only shown on the wheel
        Assert.Equal(ControllerDirection.Up, s.Control.Shown);
        s.Step(Pad(right: new(-1, 0)));
        s.Step(Pad(right: new(-0.7f, 0.71f)));                                                    // left, then up: water
        Assert.Equal(2, s.Avatar.SpellQueue.Count);
    }

    [Fact]
    public void TheLeftStick_WalksAsFarAsItIsPushed()
    {
        using var s = Start();

        s.Step(Pad(left: new(0.5f, 0)));
        Assert.Equal(0.5f, s.Avatar.Body.Movement.X, 3);
        s.Step(Pad(left: new(0, 1)));
        Assert.Equal(-1, s.Avatar.Body.Movement.Z, 3);                                            // up on the pad is away from the camera
        s.Step(Pad(left: new(0.2f, 0)));
        Assert.Equal(Numerics.Vector3.Zero, s.Avatar.Body.Movement);                              // within the dead zone
    }

    [Fact]
    public void TheTriggersCast_TheButtonsAttackBlockAndUse_AndThePadPicksTheMagick()
    {
        using var s = Start();
        s.Player.Unlock(MagickType.Haste);
        s.Step(Pad(right: new(1, 0)));
        s.Step(Pad(right: new(0.6f, -0.8f)));                                                     // right, down: fire

        s.Step(Pad(rightTrigger: 1));
        Assert.Equal(CastType.Force, s.Avatar.HeldCast);
        s.Step(Pad());
        Assert.Equal(CastType.None, s.Avatar.HeldCast);

        s.Avatar.CastType = CastType.None;
        s.Step(Pad(buttons: Buttons.A));
        Assert.True(s.Avatar.Attacking);
        s.Step(Pad(buttons: Buttons.RightShoulder));
        Assert.True(s.Avatar.IsBlocking);
        s.Step(Pad(buttons: Buttons.DPadDown));
        Assert.Equal(MagickType.Haste, s.Player.SelectedMagick);
    }

    [Fact]
    public void X_PicksUpTheItemAhead()
    {
        using var s = Start();
        var lying = new WorldItem(s.World.Physics, new Item(new ItemTemplate("pick", Bounds: new ItemBounds(new(-0.1f, 0, -0.1f), new(0.1f, 0.5f, 0.1f)), Pickable: true)),
            Numerics.Matrix4x4.CreateTranslation(s.Avatar.Position.X, 0, -1.5f));                // ahead: the avatar faces −Z
        s.World.Entities.Add(lying);
        s.World.Tick(Tick);

        s.Step(Pad(buttons: Buttons.X));

        Assert.Same(lying.Item, s.Avatar.ItemIn(0));
    }

    [Fact]
    public void TheDeviceLastTouched_DrivesThePlayer()
    {
        var idle = new InputState(new KeyboardState(), new MouseState(), true, default, Pad());
        var stick = idle with { Pad = Pad(left: new(1, 0)) };
        var key = idle with { Keyboard = new KeyboardState(Keys.Q) };

        Assert.True(PlaySimulation.Device(stick, idle, usingPad: false));
        Assert.True(PlaySimulation.Device(idle, stick, usingPad: true));                          // the pad at rest keeps it
        Assert.False(PlaySimulation.Device(key, idle, usingPad: true));
        Assert.False(PlaySimulation.Device(idle with { Pad = default }, idle, usingPad: true));   // unplugged
    }
}
