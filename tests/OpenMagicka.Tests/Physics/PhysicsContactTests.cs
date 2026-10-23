using System.Numerics;
using OpenMagicka.Physics;

namespace OpenMagicka.Tests.Physics;

public class PhysicsContactTests
{
    private const float Tick = 1f / 60;

    private static readonly Vector3[] FloorCorners = [new(-50, 0, -50), new(50, 0, -50), new(50, 0, 50), new(-50, 0, 50)];
    private static readonly MeshTriangle[] UpFacing = [new(0, 2, 1), new(0, 3, 2)];

    private static StaticMesh AddFloor(PhysicsWorld world, float friction = 1) =>
        world.AddStaticMesh(2, FloorCorners, UpFacing, friction);

    private static void Run(PhysicsWorld world, float seconds)
    {
        for (var t = 0f; t < seconds; t += Tick)
        {
            world.Step(Tick);
        }
    }

    /// <summary>Records every contact and answers with a fixed response, or one chosen per contact.</summary>
    private sealed class Recorder(Func<Contact, ContactResponse>? respond = null) : IContactListener
    {
        public List<Contact> Contacts { get; } = [];

        public ContactResponse OnContact(PhysicsBody body, in Contact contact)
        {
            Contacts.Add(contact);
            return respond?.Invoke(contact) ?? ContactResponse.Collide;
        }
    }

    [Fact]
    public void AListener_HearsTheGround_WithTheNormalPointingAtTheBody()
    {
        using var world = new PhysicsWorld();
        var floor = AddFloor(world);
        var ball = world.AddBody(new SphereShape(0.5f), new Vector3(3, 0.49f, 1), mass: 1);   // off the diagonal both triangles share
        var recorder = new Recorder();
        ball.Listener = recorder;

        world.Step(Tick);

        var contact = Assert.Single(recorder.Contacts);
        Assert.Same(floor, contact.OtherMesh);
        Assert.Null(contact.OtherBody);
        Assert.True(contact.Normal.Y > 0.99f, $"normal {contact.Normal}");
        Assert.Equal(-0.01f, contact.Point.Y, 3);             // the bottom of the ball, sunk 1 cm
        Assert.Equal(0.01f, contact.Penetration, 3);
    }

    [Fact]
    public void BothBodies_HearAContactBetweenThem_EachFromItsOwnSide()
    {
        using var world = new PhysicsWorld(PhysicsSettings.Default with { Gravity = Vector3.Zero });
        var left = world.AddBody(new SphereShape(0.5f), new Vector3(-0.49f, 0, 0), mass: 1);
        var right = world.AddBody(new SphereShape(0.5f), new Vector3(0.49f, 0, 0), mass: 1);
        var (heardLeft, heardRight) = (new Recorder(), new Recorder());
        (left.Listener, right.Listener) = (heardLeft, heardRight);

        world.Step(Tick);

        Assert.Same(right, Assert.Single(heardLeft.Contacts).OtherBody);
        Assert.Same(left, Assert.Single(heardRight.Contacts).OtherBody);
        Assert.True(heardLeft.Contacts[0].Normal.X < -0.99f, "the push on the left body points left");
        Assert.True(heardRight.Contacts[0].Normal.X > 0.99f, "the push on the right body points right");
    }

    private static (PhysicsBody Heavy, PhysicsBody Light) Collide(ContactResponse heavyResponse)
    {
        var world = new PhysicsWorld(PhysicsSettings.Default with { Gravity = Vector3.Zero });
        var heavy = world.AddBody(new CapsuleShape(0.5f, 1), new Vector3(-2, 0, 0), mass: 100, BodyOptions.LockRotation | BodyOptions.Undamped);
        var light = world.AddBody(new CapsuleShape(0.5f, 1), Vector3.Zero, mass: 10, BodyOptions.LockRotation | BodyOptions.Undamped);
        heavy.Listener = new Recorder(c => c.OtherBody == light ? heavyResponse : ContactResponse.Collide);
        heavy.Velocity = new Vector3(5, 0, 0);
        Run(world, 1);
        return (heavy, light);
    }

    [Fact]
    public void PassingThrough_LeavesTheBodyUnstopped_ButStillPushesTheOther()
    {
        var (heavy, light) = Collide(ContactResponse.PassThrough);

        Assert.Equal(5f, heavy.Velocity.X, 2);
        Assert.True(light.Position.X > 0.5f, $"the light body should have been shoved: {light.Position}");
    }

    [Fact]
    public void Ignoring_LetsBothBodiesPassThrough()
    {
        var (heavy, light) = Collide(ContactResponse.Ignore);

        Assert.Equal(5f, heavy.Velocity.X, 2);
        Assert.Equal(Vector3.Zero, light.Position);
    }

    [Fact]
    public void Colliding_SlowsTheHeavyBody()
    {
        var (heavy, _) = Collide(ContactResponse.Collide);

        Assert.True(heavy.Velocity.X < 4.9f, $"{heavy.Velocity}");
    }

    [Theory]
    [InlineData(0f, 1f, 5f)]          // a frictionless body slides on a rough floor
    [InlineData(1f, 1f, 0f)]
    [InlineData(0.25f, 1f, 5 - 0.5f * 19.64f * 0.25f)]   // μ = √0.25 = 0.5 (the larger would stop it, the smaller barely slow it)
    public void Friction_IsTheGeometricMeanOfBothSurfaces(float bodyFriction, float floorFriction, float speedAfter)
    {
        using var world = new PhysicsWorld();
        AddFloor(world, floorFriction);
        var body = world.AddBody(new CapsuleShape(0.4f, 1), new Vector3(0, 0.9f, 0), mass: 75, BodyOptions.LockRotation | BodyOptions.Undamped);
        body.Friction = bodyFriction;
        Run(world, 0.25f);

        body.Velocity = new Vector3(5, 0, 0);
        Run(world, 0.25f);

        Assert.Equal(speedAfter, body.Velocity.X, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => body.Friction = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => body.Restitution = 2);
    }

    [Fact]
    public void ABodyThatNeverSleeps_KeepsReportingItsGround()
    {
        using var world = new PhysicsWorld();
        AddFloor(world);
        var sleeper = world.AddBody(new SphereShape(0.5f), new Vector3(0, 0.5f, 0), mass: 1);
        var watcher = world.AddBody(new SphereShape(0.5f), new Vector3(3, 0.5f, 0), mass: 1, BodyOptions.NeverSleeps);
        Run(world, 3);
        var recorder = new Recorder();
        watcher.Listener = recorder;

        world.Step(Tick);

        Assert.False(sleeper.IsAwake);
        Assert.True(watcher.IsAwake);
        Assert.NotEmpty(recorder.Contacts);
    }

    [Fact]
    public void ABodyCanIgnoreGravity()
    {
        using var world = new PhysicsWorld();
        var balloon = world.AddBody(new SphereShape(0.5f), new Vector3(0, 5, 0), mass: 1, BodyOptions.NeverSleeps);

        balloon.AffectedByGravity = false;
        Run(world, 1);

        Assert.Equal(5f, balloon.Position.Y, 3);
    }

    [Fact]
    public void ABodyHasOneListener_AndTheWorldCannotChangeDuringAStep()
    {
        using var world = new PhysicsWorld();
        AddFloor(world);
        var ball = world.AddBody(new SphereShape(0.5f), new Vector3(3, 0.49f, 1), mass: 1);
        var errors = new List<Exception>();
        var recorder = new Recorder(_ =>
        {
            foreach (var change in new Action[] { () => world.Remove(ball), () => world.Step(Tick), () => world.AddBody(new SphereShape(1), Vector3.Zero, 1) })
            {
                try
                {
                    change();
                }
                catch (InvalidOperationException e)
                {
                    errors.Add(e);
                }
            }
            return ContactResponse.Collide;
        });
        ball.Listener = recorder;
        ball.Listener = recorder;                                  // the same one again is fine

        world.Step(Tick);

        Assert.Throws<InvalidOperationException>(() => ball.Listener = new Recorder());
        Assert.Equal(3 * recorder.Contacts.Count, errors.Count);
        Assert.False(world.IsStepping);
        Assert.False(ball.IsRemoved);
    }

    [Fact]
    public void ABody_CarriesItsOwnersTag()
    {
        using var world = new PhysicsWorld();
        var body = world.AddBody(new SphereShape(1), Vector3.Zero, mass: 1);

        body.Tag = "goblin";

        Assert.Equal("goblin", body.Tag);
    }
}
