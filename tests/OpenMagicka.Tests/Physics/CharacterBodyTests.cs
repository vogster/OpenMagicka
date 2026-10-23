using System.Numerics;
using OpenMagicka.Physics;
using OpenMagicka.Physics.Characters;

namespace OpenMagicka.Tests.Physics;

public class CharacterBodyTests
{
    private const float Tick = 1f / 60;
    private const int Grass = 2;

    private static readonly CharacterConditions Normal = CharacterConditions.Default;

    internal static PhysicsWorld Floor(float size = 50)
    {
        var world = new PhysicsWorld();
        Vector3[] corners = [new(-size, 0, -size), new(size, 0, -size), new(size, 0, size), new(-size, 0, size)];
        world.AddStaticMesh(Grass, corners, [new MeshTriangle(0, 2, 1), new MeshTriangle(0, 3, 2)]);
        return world;
    }

    /// <summary>A wizard-sized character standing on the floor: centre 0.9 m up.</summary>
    internal static CharacterBody Wizard(PhysicsWorld world, Vector3? position = null, float mass = 75, float speed = 5) =>
        new(world, position ?? new Vector3(1, 0.9f, 1), new CharacterShape(Radius: 0.4f, Length: 1, Mass: mass, Speed: speed));

    internal static List<CharacterReport> Run(PhysicsWorld world, float seconds, params (CharacterBody Body, CharacterConditions Conditions)[] characters)
    {
        var reports = new List<CharacterReport>();
        for (var t = 0f; t < seconds; t += Tick)
        {
            foreach (var (body, conditions) in characters)
            {
                body.Conditions = conditions;
                body.BeforeStep(Tick);
            }
            world.Step(Tick);
            reports.AddRange(characters.Select(c => c.Body.AfterStep()));
        }
        return reports;
    }

    internal static List<CharacterReport> Run(PhysicsWorld world, float seconds, CharacterBody body) => Run(world, seconds, (body, body.Conditions));

    [Fact]
    public void ACharacter_StandsOnTheGround_AndKnowsWhatItStandsOn()
    {
        using var world = Floor();
        var wizard = Wizard(world);

        Run(world, 1, wizard);

        Assert.True(wizard.IsTouchingGround);
        Assert.Equal(Grass, wizard.GroundMaterial);
        Assert.Equal(0.9f, wizard.Body.Position.Y, 1);
        Assert.Equal(-0.9f, wizard.HeightOffset, 5);
        Assert.Equal(wizard.Body.Position.X, wizard.LastPositionOnGround.X, 3);
        Assert.True(wizard.GroundNormal.Y > 0.99f);
        Assert.Equal((0f, 0f, true), (wizard.Body.Friction, wizard.Body.Restitution, wizard.Body.IsAwake));
    }

    [Fact]
    public void ACharacter_WalksAtItsSpeed_AndStopsDead()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);

        wizard.Movement = new Vector3(3, 0, 0);                   // longer than 1: clamped
        Run(world, 1, wizard);

        Assert.Equal(5f, wizard.Body.Velocity.X, 1);
        Assert.True(wizard.IsMoving);
        Assert.Equal(Vector3.UnitX, wizard.WantedDirection);

        wizard.Movement = Vector3.Zero;
        Run(world, Tick, wizard);
        Assert.Equal(0f, wizard.Body.Velocity.X, 3);              // no sliding
        Assert.False(wizard.IsMoving);
        Assert.Equal(Vector3.UnitX, wizard.WantedDirection);      // a zero movement keeps the direction
    }

    [Fact]
    public void SpeedIsScaledBySlowAndTheMultiplier_AndGatedByAllowMove()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);

        wizard.Movement = Vector3.UnitX;
        wizard.SpeedMultiplier = 0.5f;
        Run(world, 0.5f, wizard);
        Assert.Equal(2.5f, wizard.Body.Velocity.X, 1);

        wizard.Slow = 0;
        Run(world, Tick, wizard);
        Assert.Equal(1f, wizard.Slow);                            // reset after each tick
        Assert.Equal(0f, wizard.Body.Velocity.X, 3);             // horizontal speed dropped, nothing added

        wizard.AllowMove = false;
        Run(world, 0.5f, wizard);
        Assert.Equal(0f, wizard.Body.Velocity.X, 3);

        wizard.SpeedMultiplier = 500;
        Assert.Equal(100f, wizard.SpeedMultiplier);
    }

    [Fact]
    public void ACharacter_TurnsTowardsWhereItWantsToGo_ByTurnSpeedPerSecond()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        var turning = Normal with { TurnSpeed = 6 };
        Assert.Equal(-Vector3.UnitZ, wizard.Facing);

        wizard.WantedDirection = new Vector3(5, 0, 0);
        Run(world, Tick, (wizard, turning));
        Assert.True(wizard.Facing.X is > 0.05f and < 0.3f, $"one tick turns a little: {wizard.Facing}");

        Run(world, 2, (wizard, turning));
        Assert.Equal(1f, wizard.Facing.X, 3);

        wizard.RunBackward = true;
        Run(world, 2, (wizard, turning));
        Assert.Equal(-1f, wizard.Facing.X, 3);
    }

    [Fact]
    public void LeavingTheGround_CountsAsFallingOnlyAfterAFifthOfASecond()
    {
        using var world = Floor();
        var wizard = Wizard(world, new Vector3(1, 30, 1));

        Run(world, 0.15f, wizard);
        Assert.True(wizard.IsTouchingGround);
        Run(world, 0.1f, wizard);
        Assert.False(wizard.IsTouchingGround);
    }

    [Fact]
    public void AJump_WorksOnlyFromTheGround()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);

        Assert.True(wizard.Jump(new Vector3(0, 8, 2)));
        Assert.False(wizard.Jump(new Vector3(0, 8, 2)));         // already lifting off
        Run(world, Tick, wizard);

        Assert.True(wizard.IsJumping);
        Assert.False(wizard.IsTouchingGround);
        Assert.True(wizard.Body.Velocity.Y > 7);
        Assert.Equal(2f, wizard.Body.Velocity.Z, 2);             // no steering in the air
        Run(world, 1.5f, wizard);
        Assert.True(wizard.IsTouchingGround);
        Assert.False(wizard.IsJumping);
    }

    [Fact]
    public void APush_SlidesTheCharacter_ThenWearsOff()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        var facingBack = Normal with { TurnSpeed = 100 };
        Run(world, 0.5f, wizard);

        wizard.Push(new Vector3(75 * 10, 0, 0));                 // 10 m/s for a 75 kg wizard
        Run(world, Tick, (wizard, facingBack));
        Assert.True(wizard.IsPushed);
        Assert.Equal(10f * MathF.Pow(0.02f, Tick), wizard.Body.Velocity.X, 1);
        Assert.Equal(-1f, wizard.Facing.X, 2);                    // faces away from the motion

        Run(world, 1, (wizard, facingBack));
        Assert.False(wizard.IsPushed);                            // below √0.33 · max speed
    }

    [Fact]
    public void AnUpwardPush_LiftsTheCharacterOffTheGround()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);

        wizard.PushVelocity(new Vector3(0, 3, 0));
        Run(world, Tick, wizard);

        Assert.False(wizard.IsTouchingGround);
    }

    [Fact]
    public void AnEntangledCharacter_CannotBePushedOrJump()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        var entangled = Normal with { IsEntangled = true };
        Run(world, 0.5f, wizard);

        wizard.Conditions = entangled;                            // set before the tick's commands
        wizard.Push(new Vector3(1000, 0, 0));
        wizard.AddExtraForce(new Vector3(3, 0, 0));
        Assert.False(wizard.Jump(new Vector3(0, 8, 0)));
        Run(world, Tick, (wizard, entangled));

        Assert.False(wizard.IsPushed);
        Assert.Equal(0f, wizard.Body.Velocity.X, 3);
    }

    [Fact]
    public void ExtraForce_TopsTheVelocityUpOnce()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);
        wizard.AllowMove = false;

        wizard.AddExtraForce(new Vector3(4, 0, 0));
        Run(world, Tick, wizard);
        Assert.Equal(4f, wizard.Body.Velocity.X, 2);

        Run(world, Tick, wizard);
        Assert.Equal(0f, wizard.Body.Velocity.X, 2);             // dropped on the ground, not added again
    }

    [Fact]
    public void Grease_MakesStartingSlow()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);

        wizard.Movement = Vector3.UnitX;
        Run(world, Tick, (wizard, Normal with { IsOnGrease = true }));

        Assert.Equal(5f * Tick, wizard.Body.Velocity.X, 2);
    }

    [Fact]
    public void AFlyingCharacter_HoversAboveTheNavMesh()
    {
        using var world = Floor();
        var bat = new CharacterBody(world, new Vector3(0, 6, 0), new CharacterShape(0.3f, 0.2f, 5, 4, Flies: true));
        var flying = Normal with { FlyGround = 0.5f };

        Run(world, 10, (bat, flying));

        Assert.Equal(2.5f, bat.Body.Position.Y, 1);
        Assert.True(bat.IsTouchingGround);
        Assert.False(bat.Body.AffectedByGravity);
    }

    [Theory]
    [InlineData(5f, true)]
    [InlineData(0.3f, false)]
    public void HittingTheGroundFromHigh_IsAnImpact(float height, bool hard)
    {
        using var world = Floor();
        var wizard = Wizard(world, new Vector3(1, 0.9f + height, 1));

        var impacts = Run(world, 2, wizard).Where(r => r.ImpactSpeed is not null).ToList();

        var expected = MathF.Sqrt(2 * 19.64f * height);
        if (hard)
        {
            Assert.InRange(Assert.Single(impacts).ImpactSpeed!.Value, expected - 1, expected + 0.5f);
        }
        else
        {
            Assert.Empty(impacts);                                // landed within the 0.2 s grace
        }
    }

    [Fact]
    public void ABigCharacter_WalksThroughASmallOne_AndKnocksItDown()
    {
        using var world = Floor();
        var troll = Wizard(world, new Vector3(-3, 0.9f, 0), mass: 300);
        var goblin = Wizard(world, new Vector3(0, 0.9f, 0), mass: 50);
        Run(world, 0.3f, (troll, Normal), (goblin, Normal));

        troll.Movement = Vector3.UnitX;
        var reports = Run(world, 1, (troll, Normal), (goblin, Normal));

        Assert.True(troll.Body.Position.X > 1, $"the troll should be past the goblin: {troll.Body.Position}");
        Assert.True(new Vector2(goblin.Body.Position.X, goblin.Body.Position.Z).Length() > 0.3f, $"the goblin should be shoved: {goblin.Body.Position}");
        Assert.Contains(reports, r => r.KnockedDown.Contains(goblin));
    }

    [Fact]
    public void ASmallPlayer_IsNotWalkedThrough()
    {
        using var world = Floor();
        var troll = Wizard(world, new Vector3(-3, 0.9f, 0), mass: 300);
        var player = Wizard(world, new Vector3(0, 0.9f, 0), mass: 50);
        var isPlayer = Normal with { IsPlayer = true };
        Run(world, 0.3f, (troll, Normal), (player, isPlayer));

        troll.Movement = Vector3.UnitX;
        var reports = Run(world, 1, (troll, Normal), (player, isPlayer));

        Assert.True(troll.Body.Position.X < player.Body.Position.X);
        Assert.DoesNotContain(reports, r => r.KnockedDown.Contains(player));
        Assert.Contains(reports, r => r.Touched.Contains(player));
    }

    [Fact]
    public void AStandingCharacter_IsNotShovedByAnotherWalkingIntoIt()
    {
        using var world = Floor();
        var walker = Wizard(world, new Vector3(-3, 0.9f, 0));
        var stander = Wizard(world, new Vector3(0, 0.9f, 0));
        Run(world, 0.3f, (walker, Normal), (stander, Normal));

        walker.Movement = Vector3.UnitX;
        Run(world, 1.5f, (walker, Normal), (stander, Normal));

        Assert.Equal(0f, stander.Body.Position.X, 2);
        Assert.InRange(walker.Body.Position.X, -1f, -0.7f);       // stopped against it: two radii apart
    }

    [Fact]
    public void BeingPushedIntoAWall_KnocksTheCharacterDown()
    {
        using var world = Floor();
        Vector3[] wall = [new(2, 0, -5), new(2, 5, -5), new(2, 5, 5), new(2, 0, 5)];
        world.AddStaticMesh(0, wall, [new MeshTriangle(0, 2, 1), new MeshTriangle(0, 3, 2)]);   // facing −X
        var wizard = Wizard(world);
        Run(world, 0.3f, wizard);

        wizard.PushVelocity(new Vector3(20, 0, 0));
        var reports = Run(world, 0.5f, wizard);

        Assert.Contains(reports, r => r.KnockedDownSelf);
    }

    [Fact]
    public void BadShapes_AreRejected()
    {
        using var world = Floor();

        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterBody(world, Vector3.Zero, new CharacterShape(0.4f, 0.001f, 75, 5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterBody(world, Vector3.Zero, new CharacterShape(0.4f, 1, 75, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Wizard(world).BeforeStep(0));
    }
}
