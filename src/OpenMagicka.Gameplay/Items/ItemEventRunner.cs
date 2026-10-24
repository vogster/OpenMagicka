using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Items;

/// <summary>What an item's events do, for its holder (docs/engine/ENTITIES_AI.md §8.2).</summary>
internal static class ItemEventRunner
{
    public static DamageResult Run(ItemEvent e, Vector3? at, Character owner, Item item, Entity? target)
    {
        switch (e)
        {
            case DamageEvent damage when target is not null:
                return target.Damage(owner.MeleeBoosted(damage.Damage), owner, StruckFrom(item, target));
            case SplashEvent splash when owner.Manager is { } manager:
                return Splash.Hit(manager, owner, owner, at ?? item.Position, splash.Radius, splash.Damage);   // the holder is spared, as the original
            case DamageOwnerEvent damage:
                return owner.Damage(owner.MeleeBoosted(damage.Damage), owner, item.Position);
            case SoundEvent sound:
                owner.RaiseCue(new SoundCue(sound.Cue, sound.Banks, at ?? item.Position, owner.Body.Facing, sound.Magnitude));
                return DamageResult.None;
            case EffectEvent effect:
                var along = -new Vector3(item.Transform.M31, item.Transform.M32, item.Transform.M33);
                var direction = effect.WorldAlign ? Vector3.UnitY : along.LengthSquared() > 1e-8f ? Vector3.Normalize(along) : -Vector3.UnitZ;
                owner.RaiseCue(new ItemEffectCue(effect.Effect, at ?? item.Position, direction, effect.Follow, item));
                return DamageResult.None;
            default:
                return DamageResult.None;
        }
    }

    /// <summary>A gun's damage strikes from the target's surface towards it (its melee too, as the original tests the class), anything else from where it is.</summary>
    private static Vector3 StruckFrom(Item item, Entity target)
    {
        if (!Guns.IsGun(item.WeaponClass))
        {
            return item.Position;
        }
        var towards = item.Position - target.Position;
        return target.Position + (towards.LengthSquared() > 1e-12f ? Vector3.Normalize(towards) * target.Radius : Vector3.Zero);
    }
}
