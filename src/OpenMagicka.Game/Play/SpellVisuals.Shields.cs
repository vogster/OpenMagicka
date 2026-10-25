using System.Numerics;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The spell shields' looks, as the original's (docs/engine/ARCHITECTURE.md §17): a shield fades in fast while it
/// stands and out slower when it falls (still drawn after it is gone, till faded); its noise drifts the faster the
/// more it is worn; each blow on it ripples where it struck, the ripples fading in a fifth of a second.
/// </summary>
public sealed partial class SpellVisuals
{
    /// <summary>How fast a shield fades in, and out.</summary>
    public const float FadeIn = 25, FadeOut = 10;

    /// <summary>How fast a ripple fades, and how strong one can be.</summary>
    public const float RippleFade = 50, RippleMost = 10;

    private readonly Dictionary<SpellShield, ShieldLook> shields = new(ReferenceEqualityComparer.Instance);

    /// <summary>A shield's look: what lasts of it between frames.</summary>
    public sealed class ShieldLook(SpellShield shield)
    {
        public SpellShield Shield { get; } = shield;
        public float Fade { get; private set; }
        public Vector2 Noise0 { get; private set; }
        public Vector2 Noise1 { get; private set; }
        public Vector2 Noise2 { get; private set; }
        public Xna.Vector4[] Ripples { get; } = new Xna.Vector4[ShieldDrawing.Ripples];
        public bool IsGone { get; private set; }

        /// <summary>A frame: up while it stands, down once it fell or went; the noise drifting by its wear; the ripples fading.</summary>
        public void Update(float dt, bool standing)
        {
            var target = standing ? 1 : 0;
            Fade += (target - Fade) * MathF.Min(1, dt * (standing ? FadeIn : FadeOut));
            IsGone = !standing && Fade <= 0.01f;
            var wear = 0.5f * (ShieldRules.ShieldMaxHp - Math.Clamp(Shield.HitPoints, 0, ShieldRules.ShieldMaxHp)) / ShieldRules.ShieldMaxHp + 0.05f;
            Noise2 += new Vector2(0, -wear * dt);
            Noise0 += new Vector2(-0.7f * wear * dt, 0.3f * wear * dt);
            Noise1 += new Vector2(0.4f * wear * dt, 0.7f * wear * dt);
            for (var i = 0; i < Ripples.Length; i++)
            {
                Ripples[i].W = MathF.Max(0, Ripples[i].W - RippleFade * dt);
            }
        }

        /// <summary>
        /// A blow at a point: into the first spent ripple, or, failing that, the first within 0.2 of it, which it
        /// strengthens (as the original's walk, which stops at the first of either).
        /// </summary>
        public void Hit(Vector3 at, float amount)
        {
            var strength = Math.Clamp((int)amount * 0.025f, 0, RippleMost);
            if (strength <= 0)
            {
                return;
            }
            for (var i = 0; i < Ripples.Length; i++)
            {
                var ripple = Ripples[i];
                if (ripple.W <= 0)
                {
                    Ripples[i] = new Xna.Vector4(at.X, at.Y, at.Z, strength);
                    return;
                }
                if (Vector3.DistanceSquared(new Vector3(ripple.X, ripple.Y, ripple.Z), at) < 0.2f)
                {
                    Ripples[i].W = MathF.Min(RippleMost, ripple.W + strength);
                    return;
                }
            }
        }
    }

    private void AgeShields(float dt)
    {
        foreach (var shield in world.Entities.Entities.OfType<SpellShield>())
        {
            if (!shields.ContainsKey(shield) && !shield.IsDead)
            {
                shields[shield] = new ShieldLook(shield);
            }
        }
        foreach (var (shield, look) in shields.ToList())
        {
            look.Update(dt, !shield.IsDead && shield.HitPoints > 0 && shield.Manager is not null);
            if (look.IsGone)
            {
                shields.Remove(shield);
            }
        }
    }

    private void Strike(ShieldHitCue hit)
    {
        if (shields.TryGetValue(hit.Shield, out var look))
        {
            look.Hit(hit.At, hit.Amount);
        }
    }

    private void WriteShields(SpellPictures into)
    {
        foreach (var look in shields.Values)
        {
            into.Shields.Add(Picture(look));
        }
    }

    /// <summary>A shield's picture: a sphere or disc scaled to its radius and tipped 40° about X (as the original's), a wall as it stands.</summary>
    public static ShieldPicture Picture(ShieldLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        var shield = look.Shield;
        var forward = shield.Facing;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var turn = new Xna.Matrix(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, -forward.X, -forward.Y, -forward.Z, 0, 0, 0, 0, 1);
        var (form, minDot) = shield.Shape switch
        {
            ShieldShape.Wall => (ShieldForm.Wall, -1f),
            ShieldShape.Disc => (ShieldForm.Disc, MathF.Cos(SpellShield.DiscAngle)),
            _ => (ShieldForm.Sphere, -1f),
        };
        var place = form == ShieldForm.Wall ? turn : Xna.Matrix.CreateScale(shield.ShellRadius) * turn * Xna.Matrix.CreateRotationX(Degrees(-40));
        place.Translation = ToXna(shield.Position);
        return new ShieldPicture(form, place, ToXna(shield.Colour), look.Fade, ToXna2(look.Noise0), ToXna2(look.Noise1), ToXna2(look.Noise2),
            [.. look.Ripples], minDot, ToXna(forward), MathF.Ceiling(shield.ShellRadius * 0.666f));
    }

    private static float Degrees(float degrees) => degrees * MathF.PI / 180;

    private static Xna.Vector2 ToXna2(Vector2 v) => new(v.X, v.Y);
}
