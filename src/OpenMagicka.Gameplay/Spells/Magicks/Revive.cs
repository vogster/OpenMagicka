using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Entities;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>
/// Revive: the player dead the longest rises 4 ahead of the caster with a tenth of its hit points
/// (MAGICKS.md §3.5). It fails with nobody to revive.
/// </summary>
public sealed class Revive() : Magick(MagickType.Revive, SelfClip)
{
    public const float Ahead = 4;

    /// <summary>A revival by the magick; one by a script or a shrine brings back all of them.</summary>
    public const float HitPointShare = 0.1f;

    public const string CastSound = "magick_revive_cast";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { Session: { } session } manager || manager.Entities.Count + 2 > EntityManager.Capacity
            || Revivee(session) is not { } player)
        {
            return Fail(caster);                                              // the effect and the new wizard need room
        }
        Sound(caster, CastSound);
        manager.Add(new ReviveEffect(player, caster.World, caster.Position + caster.Body.Facing * Ahead, HitPointShare));
        return true;
    }

    /// <summary>
    /// The playing player dead the longest whose wizard will not rise by itself (gone, overkilled, or
    /// dead and not undying), and that no revival is already on its way to.
    /// </summary>
    public static Player? Revivee(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Player? best = null;
        foreach (var player in session.Players)
        {
            if (player.Playing && !player.Ressing && player.Avatar is { } avatar && (avatar.IsOverkilled || (avatar.IsDead && !avatar.IsReviving))
                && player.DeadAge > float.Epsilon && player.DeadAge > (best?.DeadAge ?? 0))
            {
                best = player;
            }
        }
        return best;
    }
}

/// <summary>
/// A revival on its way: after a quarter of a second the player's wizard rises at the spot (its
/// corpse goes), and the light lingers for 3 s more. Its looks come with the renderer. A simulation object.
/// </summary>
public sealed class ReviveEffect : Entity, IMagickEffect
{
    public const float RiseDelay = 0.25f, Linger = 3;

    public const string RiseSound = "magick_revive";

    private readonly Physics.PhysicsWorld world;
    private readonly Vector3 position;
    private readonly float share;
    private float timer;
    private bool risen;

    internal ReviveEffect(Player player, Physics.PhysicsWorld world, Vector3 position, float share)
    {
        (Player, this.world, this.position, this.share) = (player, world, position, share);
        player.Ressing = true;
    }

    public Player Player { get; }

    public override Vector3 Position => position;

    public override float Radius => 1;

    public override bool IsDead => risen && timer <= 0;

    public override bool IsRemovable => IsDead;

    /// <summary>Nullify ends it.</summary>
    public void End() => (risen, timer, Player.Ressing) = (true, 0, false);

    public override void Update(float dt)
    {
        if (risen)
        {
            timer -= dt;
            return;
        }
        timer += dt;
        if (timer * 4 <= 1)
        {
            return;
        }
        (risen, timer) = (true, Linger);
        Player.Ressing = false;
        if (Player.Playing && Player.Avatar is { } old && (old.IsDead || old.IsOverkilled) && Manager is { } manager
            && manager.Entities.Count < EntityManager.Capacity && !world.IsDisposed && !world.IsStepping)
        {
            Player.Avatar = Rise(manager, old);                               // not if it rose some other way meanwhile
        }
    }

    protected internal override void OnRemoved() => Player.Ressing = false;

    /// <summary>A new wizard from the old one's template, clips and script name, at the spot; the corpse goes.</summary>
    private Character Rise(EntityManager manager, Character old)
    {
        var floor = world.CastSegment(position + Vector3.UnitY, position - Vector3.UnitY * Teleport.Depth, levelOnly: true)?.Position;
        var at = floor is { } ground ? ground + Vector3.UnitY * (0.1f - old.Body.HeightOffset) : position;
        var players = manager.Session?.Players.Count(p => p.Playing) ?? 1;
        var wizard = new Character(old.Template, world, at, players, animations: old.Animator?.Animations)
        {
            IsPlayer = true,
            Player = Player,
            RemoveAfterDeath = old.RemoveAfterDeath,
        };
        try
        {
            manager.Add(wizard);
        }
        catch
        {
            world.Remove(wizard.Body.Body);
            throw;
        }
        var name = old.UniqueId;
        old.UniqueId = 0;
        old.RemoveCorpse();
        wizard.UniqueId = name;
        wizard.Rise(share);
        wizard.RaiseCue(new Animation.SoundCue(RiseSound, MagickEffects.SpellsBank, wizard.Position, wizard.Body.Facing));
        return wizard;
    }
}
