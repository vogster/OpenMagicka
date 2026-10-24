using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;
using Actions = OpenMagicka.Gameplay.Animation.Actions;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Items' special abilities (ENTITIES_AI.md §8.1.7).</summary>
public class SpecialAbilityTests
{
    private static ItemTemplate Staff(string type = "SpecialAnimation", float recharge = 4, string animation = "special1") =>
        new("staff", WeaponClass.Staff, Pickable: true, Bounds: new ItemBounds(-Vector3.One / 10, Vector3.One / 10),
            Special: new SpecialAbilityTemplate(type, animation), SpecialRechargeTime: recharge);

    private static Character Holder(GameWorld world, ItemTemplate staff)
    {
        var holder = Animated(world, clips: new CharacterAnimations(Clips().Sets, SkeletonTests.Arm()));
        holder.Equip(1, new Item(staff), "hand");
        return holder;
    }

    [Fact]
    public void UsingAReadyItem_PlaysItsAnimation_AndStartsTheRecharge()
    {
        using var world = World();
        var holder = Holder(world, Staff());
        var staff = holder.ItemIn(1)!;

        Assert.True(staff.IsSpecialReady);
        Assert.True(holder.UseSpecialAbility(1));

        Assert.Equal(("special1", true, false), (holder.NextAttackAnimation, holder.Attacking, holder.AllowAttackRotate));
        Assert.Equal(4f, staff.SpecialCooldown);
        Assert.False(holder.UseSpecialAbility(1));                                   // still recharging
        Assert.False(holder.UseSpecialAbility(0));                                   // nothing there
    }

    [Fact]
    public void AnAbilityThatDoesNothingYet_StillPlaysAndRecharges()
    {
        using var world = World();
        var holder = Holder(world, Staff("Starfall"));

        Assert.False(SpecialAbilities.Works("Starfall"));
        Assert.True(SpecialAbilities.Works("specialanimation"));                     // by name, ignoring case
        Assert.True(holder.UseSpecialAbility(1));
        Assert.Equal(4f, holder.ItemIn(1)!.SpecialCooldown);
    }

    [Fact]
    public void TheRecharge_SignalsOnce_WhenTheItemComesReady()
    {
        using var world = World();
        var cues = new List<AnimationCue>();
        world.Entities.Cue += (_, cue) => cues.Add(cue);
        var holder = Holder(world, Staff(recharge: 0.5f));
        Run(world, 0.2f);
        Assert.Empty(cues.OfType<SoundCue>());                                       // ready from the start: no signal
        holder.UseSpecialAbility(1);

        Run(world, 0.4f);
        Assert.False(holder.ItemIn(1)!.IsSpecialReady);
        Assert.InRange(holder.ItemIn(1)!.SpecialLight, 0.05f, 1);
        Run(world, 0.2f);

        Assert.True(holder.ItemIn(1)!.IsSpecialReady);
        Assert.Equal(1f, holder.ItemIn(1)!.SpecialLight);
        Assert.Single(cues.OfType<SoundCue>(), s => s.Cue == SpecialAbilities.RechargeSound && s.Banks == SpecialAbilities.WeaponsBank);
        Assert.Single(cues.OfType<ItemEffectCue>(), e => e.Effect == SpecialAbilities.ReadyEffect);
    }

    [Fact]
    public void ACorpsesItem_DoesNotRecharge_ButADroppedCopyKeepsItsCooldown_AndRechargesOnTheGround()
    {
        using var world = World();
        var holder = Holder(world, Staff(recharge: 10));
        Run(world, 0.1f);
        holder.UseSpecialAbility(1);

        holder.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 400, 1), null, Vector3.Zero);
        Run(world, 0.2f);

        var lying = Assert.Single(world.Entities.Entities.OfType<WorldItem>());
        Assert.InRange(lying.Item.SpecialCooldown, 9.6f, 9.9f);
        var detached = holder.ItemIn(1)!.SpecialCooldown;
        Run(world, 1);
        Assert.Equal(detached, holder.ItemIn(1)!.SpecialCooldown);
        Assert.InRange(lying.Item.SpecialCooldown, 8.6f, 8.9f);
    }

    [Fact]
    public void ALyingItem_SignalsWhereItLies_AndAPickedUpOneOnlyOnce()
    {
        using var world = World();
        var effects = new List<ItemEffectCue>();
        world.Entities.Cue += (_, cue) => { if (cue is ItemEffectCue e && e.Effect == SpecialAbilities.ReadyEffect) effects.Add(e); };
        var dropper = Holder(world, Staff(recharge: 0.6f));
        var taker = Animated(world, at: new Vector3(3, 0.9f, 0), clips: new CharacterAnimations(Clips().Sets, SkeletonTests.Arm()));
        taker.Equip(1, new Item(Staff() with { Id = "plain" }), "hand");
        Run(world, 0.1f);
        dropper.UseSpecialAbility(1);
        var lying = dropper.Release(1)!;
        Run(world, 0.2f);

        Assert.True(taker.PickUp(lying));
        Run(world, 1);

        var ready = Assert.Single(effects);                                          // the holder's, not the taken box's too
        Assert.Same(lying.Item.PassiveSource, ready.Source);
        Assert.True(Vector3.Distance(ready.Position, taker.ItemIn(1)!.Tip) < 0.5f, $"at {ready.Position}");
    }

    [Fact]
    public void ALyingItem_SignalsWhereItLies()
    {
        using var world = World();
        var effects = new List<ItemEffectCue>();
        world.Entities.Cue += (_, cue) => { if (cue is ItemEffectCue e && e.Effect == SpecialAbilities.ReadyEffect) effects.Add(e); };
        var dropper = Holder(world, Staff(recharge: 0.6f));
        Run(world, 0.1f);
        dropper.UseSpecialAbility(1);
        var lying = dropper.Release(1)!;

        Run(world, 1);

        var ready = Assert.Single(effects);
        Assert.True(Vector3.Distance(ready.Position, lying.Position) < 0.5f && ready.Position.LengthSquared() > 0, $"at {ready.Position}, lying at {lying.Position}");
    }

    [Fact]
    public void Invisibility_HidesTheHolderFor15Seconds()
    {
        using var world = World();
        var sounds = new List<SoundCue>();
        world.Entities.Cue += (_, cue) => { if (cue is SoundCue s) sounds.Add(s); };
        var holder = Holder(world, Staff("invisibility", animation: SpecialAbilities.DefaultAnimation));

        holder.UseSpecialAbility(1);
        Run(world, SpecialAbilities.InvisibilityTime - 0.5f);
        Assert.True(holder.IsInvisible);
        Run(world, 1);

        Assert.False(holder.IsInvisible);
        Assert.Contains(sounds, s => s.Cue == SpecialAbilities.InvisibilitySound && s.Banks == SpecialAbilities.SpellsBank);
    }

    [Fact]
    public void TheAction_UsesTheSlotsItem_OrRunsItsOwnAbility_WithNoRecharge()
    {
        using var world = World();
        var holder = Holder(world, Staff());
        var bySlot = new Actions.SpecialAbility(0, 1, 1, null);
        var own = new Actions.SpecialAbility(0, 1, -1, new SpecialAbilityTemplate("Invisibility"));

        bySlot.Run(holder, first: false);
        Assert.True(holder.ItemIn(1)!.IsSpecialReady);
        bySlot.Run(holder, first: true);
        Assert.False(holder.ItemIn(1)!.IsSpecialReady);
        holder.Attacking = false;
        own.Run(holder, first: true);

        Assert.True(holder.IsInvisible);
        Assert.False(holder.Attacking);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Actions.SpecialAbility(0, 1, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Actions.SpecialAbility(0, 1, Character.Slots, null));
    }

    [Fact]
    public void BadTemplates_FailOnLoad()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Staff() with { SpecialRechargeTime = float.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (Staff() with { Special = new SpecialAbilityTemplate("") }).Validate());
    }
}
