using System.Numerics;

namespace OpenMagicka.Physics.Characters;

public sealed partial class CharacterBody
{
    /// <summary>Surfaces facing up by more than this are ground (slopes under about 45°).</summary>
    public const float GroundSlope = 0.7f;

    /// <summary>Surfaces within this of vertical are walls.</summary>
    private const float WallSlope = 0.3f;

    /// <summary>
    /// A contact counts only if the shapes are still this close after the step: the original looks
    /// for contacts at the poses the step is about to reach, so a body lifting off has no ground.
    /// </summary>
    public const float ContactTolerance = 0.05f;

    private float stepTime = 1f / 60;
    private float sinceGroundBeforeStep;

    private float? impactSpeed;
    private bool knockedDownSelf;
    private readonly List<CharacterBody> knockedDown = [];
    private readonly List<CharacterBody> touched = [];
    private float? liquidDepth;
    private int liquidMaterial;
    private bool iceThisStep;

    /// <summary>What happened during the last step; clears it for the next one.</summary>
    public CharacterReport AfterStep()
    {
        TakeStagedPushes();
        IsInWater = liquidDepth is not null;
        if (liquidDepth is { } depth)
        {
            (WaterDepth, LiquidMaterial, GroundMaterial) = (depth, liquidMaterial, liquidMaterial);
        }
        IsOnIce = iceThisStep;
        (liquidDepth, iceThisStep) = (null, false);
        if (conditions.IsOnGrease)
        {
            conditions = conditions with { IsOnGrease = false };            // grease lasts only for the step it was reported for
        }
        if (impactSpeed is null && !knockedDownSelf && knockedDown.Count == 0 && touched.Count == 0)
        {
            return CharacterReport.Quiet;
        }
        var report = new CharacterReport(impactSpeed, knockedDownSelf, [.. knockedDown], [.. touched]);
        (impactSpeed, knockedDownSelf) = (null, false);
        knockedDown.Clear();
        touched.Clear();
        return report;
    }

    private ContactResponse OnContact(in Contact contact)
    {
        if (contact.Liquid)
        {
            var depth = contact.Point.Y - (Body.Position.Y + HeightOffset);   // the liquid's surface over the feet
            if (liquidDepth is not { } deepest || depth > deepest)
            {
                (liquidDepth, liquidMaterial) = (depth, contact.OtherMesh?.Surface?.LiquidMaterial ?? 0);
            }
            return ContactResponse.Ignore;                                    // felt, never pushing
        }
        if (contact.OtherBody is { } body && ReferenceEquals(body, IgnoredBody))
        {
            return ContactResponse.Ignore;                                    // gripper and victim pass through each other
        }
        var other = contact.OtherBody?.Listener is Contacts { Owner: var character } ? character : null;
        if (contact.OtherMesh is null && (Conditions.IsIntangible || other is { Conditions.IsIntangible: true }))
        {
            return ContactResponse.Ignore;                                 // only the level stops an intangible character
        }
        if (GapAfterStep(contact) > ContactTolerance)
        {
            return ContactResponse.Collide;                                // leaving it this step: no rules
        }
        var closing = Vector3.Dot(Body.Velocity, -contact.Normal);          // moving into the other shape
        var response = ContactResponse.Collide;
        if (other is not null)
        {
            Note(touched, other);
            if (WalksThrough(other))
            {
                response = ContactResponse.PassThrough;
                if (closing > 0)
                {
                    KnockDown(other);
                }
            }
        }
        var level = contact.OtherMesh is not null;
        if (level)
        {
            if (!IsTouchingGround && !IsJumping && !Conditions.IgnoresImpacts && closing > 0)
            {
                impactSpeed = MathF.Max(impactSpeed ?? 0, closing);
            }
            if (contact.Normal.Y > GroundSlope)
            {
                iceThisStep |= contact.OtherMesh!.Surface is { Slippery: true };   // a frozen lake's ice
                OnGround(contact.Normal, contact.OtherMesh!.Material);
            }
        }
        else if (other is null && contact.OtherBody is not null && contact.Normal.Y > GroundSlope)
        {
            OnGround(contact.Normal, material: 0);                         // a physics object: Generic
        }
        if (IsPushed || leavingGround)
        {
            if (other is not null && (closing > 0 || other.Conditions.IsKnockedDown))
            {
                if (other.Body.Mass <= Body.Mass)
                {
                    KnockDown(other);
                    response = ContactResponse.PassThrough;
                }
                else if (sinceGroundBeforeStep <= GroundGrace && Conditions.CanBeKnockedDown)
                {
                    knockedDownSelf = true;
                }
            }
            else if (other is null && level && closing > 0 && MathF.Abs(contact.Normal.Y) < WallSlope && Conditions.CanBeKnockedDown)
            {
                knockedDownSelf = true;
            }
            return response;
        }
        if (other is not null && IsTouchingGround && Flat(Body.Velocity).LengthSquared() < Epsilon && !other.WalksThrough(this))
        {
            response = ContactResponse.PassThrough;                        // idle characters do not shove each other
        }
        return response;
    }

    /// <summary>A heavy character walks through a small one that is not a player, shoving it.</summary>
    private bool WalksThrough(CharacterBody small) => !small.Conditions.IsPlayer && small.Body.Mass < Body.Mass * 0.5f;

    /// <summary>The gap between the shapes once both have moved for the step, gravity included.</summary>
    private float GapAfterStep(in Contact contact)
    {
        var gravity = world.Settings.Gravity * stepTime;
        var own = Body.Velocity + (Body.AffectedByGravity ? gravity : Vector3.Zero);
        var theirs = contact.OtherBody is { } body ? body.Velocity + (body.AffectedByGravity ? gravity : Vector3.Zero) : Vector3.Zero;
        return -contact.Penetration + stepTime * Vector3.Dot(own - theirs, contact.Normal);
    }

    private void OnGround(Vector3 normal, int material)
    {
        sinceGround = 0;
        GroundNormal = normal;
        GroundMaterial = material;
        if (Conditions.IsAlive)
        {
            LastPositionOnGround = Body.Position;
        }
    }

    private void KnockDown(CharacterBody other)
    {
        if (other.Conditions.CanBeKnockedDown)
        {
            Note(knockedDown, other);
        }
    }

    private static void Note(List<CharacterBody> list, CharacterBody body)
    {
        if (!list.Contains(body))
        {
            list.Add(body);
        }
    }

    /// <summary>The body's listener; kept private so gameplay cannot call the rules directly.</summary>
    private sealed class Contacts(CharacterBody owner) : IContactListener
    {
        public CharacterBody Owner { get; } = owner;

        public ContactResponse OnContact(PhysicsBody body, in Contact contact) => Owner.OnContact(contact);
    }
}
