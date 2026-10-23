using System.Numerics;
using OpenMagicka.Physics;
using OpenMagicka.Physics.Characters;
using static OpenMagicka.Tests.Physics.CharacterBodyTests;

namespace OpenMagicka.Tests.Physics;

/// <summary>Lift-off, pushes, impacts and the rarer contact rules of docs/engine/ARCHITECTURE.md §7.4.</summary>
public class CharacterRulesTests
{
    private const float Tick = 1f / 60;
    private const float Gravity = 19.64f;

    private static readonly CharacterConditions Normal = CharacterConditions.Default;

    [Fact]
    public void AJump_LeavesTheGround_AndFliesItsWholeArc()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);

        Assert.True(wizard.Jump(new Vector3(0, 8, 2)));
        var peak = 0f;
        var airborne = 0;
        var refused = true;
        for (var tick = 0; tick < 30; tick++)
        {
            var reports = Run(world, Tick, wizard);
            Assert.All(reports, r => Assert.Null(r.ImpactSpeed));
            peak = MathF.Max(peak, wizard.Body.Position.Y);
            airborne += wizard.IsTouchingGround ? 0 : 1;
            refused &= tick == 0 || !wizard.Jump(new Vector3(0, 8, 0));
        }

        Assert.Equal(30, airborne);
        Assert.True(refused, "a jump in the air must be refused");
        Assert.Equal(2f, wizard.Body.Velocity.Z, 2);             // no steering in the air
        Assert.Equal(0.9f + 8 * 8 / (2 * Gravity), peak, 1);      // the full arc
        Assert.True(wizard.IsJumping);
        Run(world, 1, wizard);
        Assert.True(wizard.IsTouchingGround);
        Assert.False(wizard.IsJumping);
    }

    [Fact]
    public void AnUpwardPush_KeepsTheCharacterInTheAir_Unslowed()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);

        wizard.PushVelocity(new Vector3(6, 6, 0));                // 3 m/s up would still touch the ground after the step
        Run(world, 0.25f, wizard);

        Assert.False(wizard.IsTouchingGround);
        Assert.True(wizard.IsPushed);
        Assert.Equal(6f, wizard.Body.Velocity.X, 2);              // a push is not slowed in the air
    }

    [Fact]
    public void APushedCharacter_KnocksDownALighterOne_AndFallsOverAHeavierOne()
    {
        using var world = Floor();
        var pushed = Wizard(world, new Vector3(-2, 0.9f, 0), mass: 75);
        var lighter = Wizard(world, new Vector3(0, 0.9f, 0), mass: 60);
        var heavier = Wizard(world, new Vector3(-2, 0.9f, 3), mass: 200);
        var shoved = Wizard(world, new Vector3(-4, 0.9f, 3), mass: 75);
        var all = new[] { (pushed, Normal), (lighter, Normal), (heavier, Normal), (shoved, Normal) };
        Run(world, 0.3f, all);

        pushed.PushVelocity(new Vector3(12, 0, 0));               // still pushed when it arrives
        shoved.PushVelocity(new Vector3(12, 0, 0));
        var reports = Run(world, 0.5f, all);

        Assert.Contains(reports, r => r.KnockedDown.Contains(lighter));
        Assert.True(pushed.Body.Position.X > 0, "a pushed character passes through the one it knocks down");
        Assert.Contains(reports, r => r.KnockedDownSelf);
        Assert.DoesNotContain(reports, r => r.KnockedDown.Contains(heavier));
    }

    [Fact]
    public void AKnockedDownCharacter_StopsDeadWhenPushed()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);

        wizard.Push(new Vector3(750, 0, 0));
        Run(world, Tick, (wizard, Normal with { IsKnockedDown = true }));

        Assert.False(wizard.IsPushed);
        Assert.Equal(0f, wizard.Body.Velocity.X, 3);
    }

    [Fact]
    public void ImpactsAreIgnored_WhenTheCharacterIgnoresThem()
    {
        using var world = Floor();
        var wizard = Wizard(world, new Vector3(1, 6, 1));

        var reports = Run(world, 2, (wizard, Normal with { IgnoresImpacts = true }));

        Assert.All(reports, r => Assert.Null(r.ImpactSpeed));
    }

    [Fact]
    public void StandingOnAPhysicsObject_IsGenericGround()
    {
        using var world = Floor();
        world.AddBody(new BoxShape(new Vector3(4, 1, 4)), new Vector3(0, 0.5f, 0), mass: 1000);
        var wizard = Wizard(world, new Vector3(0, 1.95f, 0));

        Run(world, 1, wizard);

        Assert.True(wizard.IsTouchingGround);
        Assert.Equal(0, wizard.GroundMaterial);                   // the floor below would be Grass (2)
        Assert.Equal(1.9f, wizard.Body.Position.Y, 1);
    }

    [Fact]
    public void ADeadCharacter_KeepsItsLastPositionOnGround()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);
        var last = wizard.LastPositionOnGround;

        wizard.Movement = Vector3.UnitX;
        Run(world, 1, (wizard, Normal with { IsAlive = false }));

        Assert.True(wizard.Body.Position.X > last.X + 2);
        Assert.Equal(last, wizard.LastPositionOnGround);
    }

    [Fact]
    public void ALevitatingCharacter_CountsAsOnTheGround_AndDrifts()
    {
        using var world = Floor();
        var wizard = Wizard(world, new Vector3(1, 10, 1));
        var levitating = Normal with { IsLevitating = true };
        Run(world, 0.5f, (wizard, levitating));

        wizard.Movement = Vector3.UnitX;
        Run(world, Tick, (wizard, levitating));

        Assert.True(wizard.IsTouchingGround);
        Assert.Equal(5f * Tick, wizard.Body.Velocity.X, 2);       // slow to start, as on grease
    }

    [Fact]
    public void ExtraForce_IsShortenedByTheSpeedAlreadyAlongIt()
    {
        using var world = Floor();
        var wizard = Wizard(world, new Vector3(1, 20, 1));
        Run(world, 0.5f, wizard);                                 // falling: nothing steers
        wizard.Body.Velocity = wizard.Body.Velocity with { X = 2 };

        wizard.AddExtraForce(new Vector3(4, 0, 0));
        Run(world, Tick, wizard);

        Assert.Equal(2 + 4 * (1 - 2f / 4), wizard.Body.Velocity.X, 2);
    }

    [Fact]
    public void Commands_RejectValuesThatAreNotFinite()
    {
        using var world = Floor();
        var wizard = Wizard(world);

        Assert.Throws<ArgumentOutOfRangeException>(() => wizard.Movement = new Vector3(float.NaN, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => wizard.WantedDirection = new Vector3(float.PositiveInfinity, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => wizard.SpeedMultiplier = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => wizard.MaxSpeed = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => wizard.Slow = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => wizard.Push(new Vector3(float.NaN, 0, 0)));
        Assert.Throws<ArgumentNullException>(() => wizard.Conditions = null!);
        Assert.Throws<InvalidOperationException>(() => wizard.Body.Listener = null);

        wizard.WantedDirection = new Vector3(float.MaxValue, 0, float.MaxValue);
        Assert.Equal(1f, wizard.WantedDirection.Length(), 4);
        wizard.Movement = new Vector3(float.MaxValue, 0, 0);
        Assert.Equal(Vector3.UnitX, wizard.Movement);
    }
}
