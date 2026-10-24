using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Animation.Actions;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Items leaving hands and lying in the world (ENTITIES_AI.md §8.1.5).</summary>
public class DropTests
{
    private static readonly ItemBounds Stick = new(new Vector3(-0.05f, -0.05f, -0.5f), new Vector3(0.05f, 0.05f, 0.5f));

    private static readonly ItemTemplate Sword = new("sword", WeaponClass.SlashMedium, BlockValue: 10, PassiveAbility: PassiveAbility.MoveSpeed, PassiveValue: 1.2f,
        Pickable: true, Bounds: Stick);

    private static readonly ItemTemplate Club = new("club", WeaponClass.CrushMedium, Bounds: Stick);

    private static readonly ItemTemplate Staff = new("staff", WeaponClass.Staff, Pickable: true, Bounds: Stick);

    private static Character Holder(GameWorld world, ItemTemplate item, Vector3? at = null, CharacterTemplate? template = null)
    {
        var holder = Animated(world, template, at, new CharacterAnimations(Clips().Sets, SkeletonTests.Arm()));
        holder.Equip(0, new Item(item), "hand");
        Run(world, 0.05f);
        return holder;
    }

    private static WorldItem Lying(GameWorld world, ItemTemplate template, Vector3 at)
    {
        var lying = new WorldItem(world.Physics, new Item(template), Matrix4x4.CreateTranslation(at));
        world.Entities.Add(lying);
        return lying;
    }

    [Fact]
    public void Releasing_LeavesAPickableCopy_AndTheSlotKeepsItsItemDetached()
    {
        using var world = World();
        var holder = Holder(world, Sword);
        var held = holder.ItemIn(0)!;

        var dropped = holder.Release(0, new Vector3(0, 0, -5))!;

        Assert.NotSame(held, dropped.Item);
        Assert.Same(held, holder.ItemIn(0));
        Assert.False(held.IsAttached);
        Assert.Null(holder.HeldItem(0));
        Assert.Equal(10, holder.BlockValue);                                          // it still blocks, as the original
        Assert.False(holder.HasPassiveAbility(PassiveAbility.MoveSpeed));
        Assert.Equal(Vector3.Zero, dropped.Velocity);                                 // the copy never gets the velocity
        Assert.True(Vector3.Distance(held.Position, dropped.Position) < 1e-3f, $"{held.Position} {dropped.Position}");
        Assert.Contains(dropped, world.Entities.Entities);
        Assert.Null(holder.Release(0));                                               // nothing in hand to let go
    }

    [Fact]
    public void AnItemNoOneCanPickUp_LiesItself_FliesWithTheVelocity_AndGoesAfterItsLifetime()
    {
        using var world = World();
        var holder = Holder(world, Club);

        var dropped = holder.Release(0, new Vector3(3, 2, 0))!;

        Assert.Same(holder.ItemIn(0), dropped.Item);
        Assert.False(dropped.IsPickable);
        Assert.Equal(new Vector3(3, 2, 0), dropped.Velocity);
        Run(world, WorldItem.Lifetime - 1);
        Assert.Contains(dropped, world.Entities.Entities);
        Run(world, 1.5f);
        Assert.DoesNotContain(dropped, world.Entities.Entities);
        Assert.True(dropped.Body.IsRemoved);
    }

    [Fact]
    public void AnItemWithoutAModel_NeverLeavesItsSlot()
    {
        using var world = World();
        var holder = Holder(world, Sword with { Bounds = null });

        Assert.Null(holder.Release(0));
        Assert.True(holder.ItemIn(0)!.IsAttached);
        Assert.Throws<ArgumentException>(() => new WorldItem(world.Physics, new Item(Sword with { Bounds = null }), Matrix4x4.Identity));
    }

    [Fact]
    public void ALyingItem_SettlesOnTheFloor_AndCharactersPassThrough()
    {
        using var world = World();
        var lying = Lying(world, Sword, new Vector3(0, 1.5f, 0));
        var walker = Animated(world, at: new Vector3(-2, 0.9f, 0));

        Run(world, 1);
        Assert.InRange(lying.Position.Y, 0.03f, 0.1f);                                // half its 0.1 m height
        var before = lying.Position;
        walker.Body.Movement = Vector3.UnitX;
        Run(world, 1.5f);

        Assert.True(walker.Position.X > 0.5f, $"walker at {walker.Position}");
        Assert.True(Vector3.Distance(before, lying.Position) < 0.05f, $"{before} → {lying.Position}");
    }

    [Fact]
    public void AThinItem_NeverFallsThroughTheLevel_AndOneDroppedUnderItStartsOnIt()
    {
        using var world = World();
        var leaf = new ItemTemplate("leaf", Pickable: true, Bounds: new ItemBounds(new Vector3(-0.3f, -0.005f, -0.3f), new Vector3(0.3f, 0.005f, 0.3f)));
        var falling = Lying(world, leaf, new Vector3(0, 4, 0));
        var buried = new WorldItem(world.Physics, new Item(leaf), Matrix4x4.CreateTranslation(3, -0.5f, 0), from: new Vector3(3, 1, 0));
        world.Entities.Add(buried);

        Run(world, 2);

        Assert.InRange(falling.Position.Y, -0.001f, 0.1f);
        Assert.InRange(buried.Position.Y, -0.001f, 0.1f);
    }

    [Fact]
    public void TheBox_GoesWhereTheBoundsAre_TurnedWithTheItem()
    {
        using var world = World();
        var blade = new ItemTemplate("blade", Pickable: true, Bounds: new ItemBounds(new Vector3(-0.05f, -0.05f, -1.2f), new Vector3(0.05f, 0.05f, 0)));
        var held = Matrix4x4.CreateScale(2) * Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 1, 0);   // −Z turned to −X

        var lying = new WorldItem(world.Physics, new Item(blade), held);

        Assert.True(Vector3.Distance(new Vector3(-0.6f, 1, 0), lying.Position) < 1e-4f, $"{lying.Position}");
    }

    [Fact]
    public void AnItemFallingOutOfTheLevel_Goes()
    {
        using var world = World();
        var lost = Lying(world, Sword, new Vector3(80, 0, 0));                         // off the floor

        Run(world, 4);

        Assert.DoesNotContain(lost, world.Entities.Entities);
    }

    [Fact]
    public void AnItemNotInTheWorld_CannotBePickedUp()
    {
        using var world = World();
        var avatar = Holder(world, Sword);
        var stray = new WorldItem(world.Physics, new Item(Sword), Matrix4x4.Identity);

        Assert.False(avatar.PickUp(stray));
        Assert.Throws<ArgumentOutOfRangeException>(() => avatar.Release(0, new Vector3(float.NaN)));
    }

    [Fact]
    public void TheDead_DropWhatTheyHold_ButNotTheUndying()
    {
        using var world = World();
        var mortal = Holder(world, Sword);
        var undying = Holder(world, Sword, new Vector3(5, 0.9f, 0), Goblin with { Undying = true });

        foreach (var victim in new[] { mortal, undying })
        {
            victim.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 400, 1), null, Vector3.Zero);
        }
        Run(world, 0.1f);

        Assert.Same(CharacterStates.Dead, mortal.State);
        Assert.Same(CharacterStates.Dead, undying.State);
        Assert.False(mortal.ItemIn(0)!.IsAttached);
        Assert.True(undying.ItemIn(0)!.IsAttached);
        Assert.Single(world.Entities.Entities.OfType<WorldItem>());
    }

    [Fact]
    public void FindPickUp_TakesOneUnderfoot_ElseOneInFront_WhenLookingAhead()
    {
        using var world = World();
        var avatar = Animated(world);
        var behind = Lying(world, Sword, new Vector3(0, 0.05f, 1.5f));
        Lying(world, Club, new Vector3(0, 0.05f, -1.5f));
        Run(world, 0.05f);

        Assert.Equal(-Vector3.UnitZ, avatar.Body.Facing);
        Assert.Null(avatar.FindPickUp(ahead: true));                                  // the club ahead is not pickable, the sword is behind
        Assert.Same(behind, avatar.FindPickUp(ahead: false));
        var underfoot = Lying(world, Sword, new Vector3(0.2f, 0.05f, 0.2f));
        Run(world, 0.05f);
        Assert.Same(underfoot, avatar.FindPickUp(ahead: true));
    }

    [Fact]
    public void PickingUp_TradesPlacesWithTheHeldItem_AndPlaysThePickUp()
    {
        using var world = World();
        var avatar = Holder(world, Sword);
        var old = avatar.ItemIn(0)!;
        var lying = Lying(world, Sword with { Id = "better" }, new Vector3(0, 0.05f, -1));
        Run(world, 0.05f);

        Assert.True(avatar.PickUp(lying));
        Assert.Equal(("pickup_weapon", true), (avatar.NextAttackAnimation, avatar.Attacking));
        Run(world, Tick);

        Assert.Same(lying.Item, avatar.ItemIn(0));
        Assert.True(lying.Item.IsAttached);
        Assert.DoesNotContain(lying, world.Entities.Entities);
        Assert.Single(world.Entities.Entities.OfType<WorldItem>(), w => ReferenceEquals(w.Item, old));
        Assert.False(avatar.PickUp(lying));                                           // already taken
    }

    [Fact]
    public void AStaff_GoesToItsOwnSlot_AndABoundItemRefuses()
    {
        using var world = World();
        var avatar = Holder(world, Sword with { Bound = true });
        avatar.Equip(1, new Item(Staff), "hand");
        var staff = Lying(world, Staff with { Id = "staff_better" }, new Vector3(1, 0.05f, 0));
        var sword = Lying(world, Sword, new Vector3(-1, 0.05f, 0));

        Assert.False(avatar.PickUp(sword));
        Assert.True(avatar.PickUp(staff));

        Assert.Equal("staff_better", avatar.ItemIn(1)!.Template.Id);
        Assert.Equal("pickup_staff", avatar.NextAttackAnimation);
        Assert.Equal(1, avatar.SpellSource);
    }

    [Fact]
    public void ASlotWithNoBone_CannotTakeAnItem()
    {
        using var world = World();
        var avatar = Animated(world);
        var lying = Lying(world, Sword, new Vector3(0, 0.05f, -1));

        Assert.False(avatar.PickUp(lying));
        Assert.Contains(lying, world.Entities.Entities);
    }

    [Fact]
    public void DetachItem_ReleasesOnItsFirstFrame_WithTheVelocityTurnedToTheFacing()
    {
        using var world = World();
        var holder = Holder(world, Club);
        var action = new DetachItem(0, 1, 0, new Vector3(0, 1, -4));                // forward and up, in the holder's frame

        action.Run(holder, first: false);
        Assert.Empty(world.Entities.Entities.OfType<WorldItem>());
        action.Run(holder, first: true);

        var dropped = Assert.Single(world.Entities.Entities.OfType<WorldItem>());
        var forward = Vector3.Dot(dropped.Velocity, holder.Body.Facing);
        Assert.True(MathF.Abs(forward - 4) < 1e-3f && MathF.Abs(dropped.Velocity.Y - 1) < 1e-3f, $"v {dropped.Velocity}");   // −Z is ahead
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetachItem(0, 1, Character.Slots, Vector3.Zero));
    }

    [Fact]
    public void AReleasedItem_NeitherStrikesNorFiresNorSpreadsItsAuras()
    {
        using var world = World();
        var holder = Holder(world, Sword);
        var target = Animated(world, Goblin with { Factions = Factions.Human }, at: new Vector3(0, 0.9f, -1));
        holder.Release(0);
        var strike = new DealDamage(0, 1, 0, DamageTargets.All);

        strike.Run(holder, first: true);
        strike.Stop(holder);
        Run(world, 0.1f);

        Assert.Equal(300f, target.HitPoints);
        var placed = holder.ItemIn(0)!.Transform;
        Run(world, 0.2f);
        Assert.Equal(placed, holder.ItemIn(0)!.Transform);                            // its holder no longer moves it
    }

    [Fact]
    public void BadBounds_FailOnLoad()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Sword with { Bounds = new ItemBounds(Vector3.One, Vector3.Zero) }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Sword with { Bounds = new ItemBounds(new Vector3(float.NaN), Vector3.One) }).Validate());
    }
}
