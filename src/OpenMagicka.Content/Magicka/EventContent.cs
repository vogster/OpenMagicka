using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Magicka;

[Flags]
public enum EventConditionType : byte
{
    None = 0,
    Default = 0x1,
    Hit = 0x2,
    Collision = 0x4,
    Damaged = 0x8,
    Timer = 0x10,
    Death = 0x20,
    OverKill = 0x40,
}

/// <summary>Events that fire when the condition is met (at most 5 per collection).</summary>
public sealed record EventCollectionContent(
    EventConditionType Condition,
    int Hitpoints,
    Elements Elements,
    float Threshold,
    float Time,
    bool Repeat,
    IReadOnlyList<EventContent> Events);

/// <summary>One event of a character, item or physics object. One subtype per event type.</summary>
public abstract record EventContent;

public sealed record DamageEvent(DamageContent Damage, bool VelocityBased) : EventContent;

/// <summary>Area damage; the file stores the amount as an integer.</summary>
public sealed record SplashEvent(DamageContent Damage, float Radius) : EventContent;

public sealed record SoundEvent(SoundBanks Banks, string Cue, float Magnitude, bool StopOnRemove) : EventContent;
public sealed record EffectEvent(bool Follow, bool WorldAlign, string Effect) : EventContent;
public sealed record RemoveEvent(int Bounce) : EventContent;
public sealed record CameraShakeEvent(float Ttl, float Magnitude, bool FromPosition) : EventContent;

/// <summary>Decal index = A + 8 * B.</summary>
public sealed record DecalEvent(int A, int B, int Scale) : EventContent;

/// <summary>Spawns a character from Data/Characters; rotation in degrees.</summary>
public sealed record SpawnEvent(string CharacterType, string IdleAnimation, string SpawnAnimation, float Health,
    Order Order, ReactTo ReactTo, Order Reaction, float Rotation, Vector3 Offset) : EventContent;

public sealed record OverkillEvent : EventContent;
public sealed record SpawnGibsEvent(int StartIndex, int EndIndex) : EventContent;

/// <summary>An item from Data/Items/Wizard, or "random".</summary>
public sealed record SpawnItemEvent(string Item) : EventContent;

/// <summary>A MagickType name; "random" means Revive in the original.</summary>
public sealed record SpawnMagickEvent(string Magick) : EventContent;

public sealed record SpawnMissileEvent(string Type, Vector3 Velocity, bool Facing) : EventContent;
public sealed record LightEvent(PointLightContent Light) : EventContent;
public sealed record CastMagickEvent(string Magick, IReadOnlyList<Elements> Elements) : EventContent;
public sealed record DamageOwnerEvent(DamageContent Damage, bool VelocityBased) : EventContent;
