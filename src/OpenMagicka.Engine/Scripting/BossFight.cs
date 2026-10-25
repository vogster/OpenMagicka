using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Props;

namespace OpenMagicka.Engine.Scripting;

/// <summary>What a script's bossMessage tells the bosses (the original's names, any case).</summary>
public enum BossMessages
{
    OrcBipedal, GunterFight, KingTalk1, KingTalk2, FafnirFight, VladMortal, CloneDone, GrimnirHurt, FutureVladDone, KillGrimnir, Assatur_Cut, KillAssatur,
    StartFight,
}

/// <summary>A boss in a fight: its hit points, and how it takes the scripts' messages. The named bosses' own come with OM-42.</summary>
public interface IFightBoss
{
    float HitPoints { get; }

    float MaxHitPoints { get; }

    bool IsDead { get; }

    /// <summary>It comes into the world at the place (the fight's start, or a replay's); one still there stays.</summary>
    void Enter(ScriptScene scene, Matrix4x4 at);

    void Message(BossMessages message);
}

/// <summary>
/// A boss fight (the original's, one for the game): the bosses the scripts spawned, their hit points
/// summed (0..1, shown on its bar once it runs), and whether all are dead. The scripts set it up, start
/// it, tell its bosses things and end it; the HUD draws its bar and titles (OM-49).
/// </summary>
public sealed class ScriptBossFight : IScriptBoss
{
    private readonly List<IFightBoss> bosses = [];

    public IReadOnlyList<IFightBoss> Bosses => bosses;

    /// <summary>Set up once (its bar's width and the freeze it was given), as the original.</summary>
    public bool IsSetup { get; private set; }

    public float HealthBarWidth { get; private set; } = 0.8f;

    public float FreezeTime { get; private set; }

    public bool IsRunning => bosses.Count > 0 && running;

    private bool running;

    public bool Dead { get; private set; }

    public float NormalizedHealth { get; private set; }

    /// <summary>The bar's width and the freeze; as the original, the health's delay given is not kept (the bar shows at once).</summary>
    public void Setup(float freezeTime, float healthBarWidth) => (FreezeTime, HealthBarWidth, IsSetup) = (freezeTime, healthBarWidth, true);

    /// <summary>A boss comes in at the place and joins the fight (once); the fight waits to start, and the game sees it.</summary>
    public void Join(ScriptScene scene, IFightBoss boss, Matrix4x4 at)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(boss);
        boss.Enter(scene, at);
        if (!bosses.Contains(boss))
        {
            bosses.Add(boss);
        }
        scene.Game.Boss = this;
        (Dead, running) = (false, false);
        NormalizedHealth = Share();
    }

    public void Start() => running = true;

    /// <summary>The fight is over (a script's endBossFight, a checkpoint's restart): no bosses, the game sees none.</summary>
    public void Reset(ScriptGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        bosses.Clear();
        if (ReferenceEquals(game.Boss, this))
        {
            game.Boss = null;
        }
        (NormalizedHealth, Dead, running) = (0, false, false);
    }

    public void Message(BossMessages message)
    {
        foreach (var boss in bosses)
        {
            boss.Message(message);
        }
    }

    /// <summary>A frame: while it runs, the health and whether all the bosses are dead; before, a sliver of a bar.</summary>
    internal void Update()
    {
        if (!IsRunning)
        {
            NormalizedHealth = 0.0001f;
            return;
        }
        Dead = bosses.All(b => b.IsDead);
        NormalizedHealth = Share();
    }

    private float Share()
    {
        var most = bosses.Sum(b => b.MaxHitPoints);
        return most > 0 ? Math.Clamp(bosses.Sum(b => MathF.Max(b.HitPoints, 0)) / most, 0, 1) : 0;
    }
}

/// <summary>
/// A prop as a boss (a lock, a door, a tower): <c>Data/PhysicsEntities/&lt;name&gt;</c> at the place, with its
/// triggers; as the original, its damage trigger is set only when a death trigger is too. A checkpoint
/// does not keep it: the fight's spawn makes it again.
/// </summary>
public sealed class PropBoss(PropTemplate template, int uniqueId, int onDeath, int onDamage) : IFightBoss
{
    public PhysicsProp? Prop { get; private set; }

    public float HitPoints => Prop?.HitPoints ?? 0;

    public float MaxHitPoints => template.MaxHitPoints;

    public bool IsDead => Prop is null or { IsDead: true };

    public void Enter(ScriptScene scene, Matrix4x4 at)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (Prop is { IsDead: false, Manager: not null } || scene.World.Physics.IsStepping)
        {
            return;
        }
        Prop = ScriptProps.Place(scene.World, template, at);
        (Prop.UniqueId, Prop.IsBoss) = (uniqueId, true);
        if (onDeath != 0)
        {
            (Prop.OnDeathTrigger, Prop.OnDamageTrigger) = (onDeath, onDamage);
        }
    }

    public void Message(BossMessages message)
    {
    }
}

/// <summary>A character as a boss (a generic boss): the fight follows its hit points; it is in the world already.</summary>
public sealed class CharacterBoss(Character character) : IFightBoss
{
    public Character Character { get; } = character ?? throw new ArgumentNullException(nameof(character));

    public float HitPoints => Character.HitPoints;

    public float MaxHitPoints => Character.MaxHitPoints;

    public bool IsDead => Character.IsDead;

    public void Enter(ScriptScene scene, Matrix4x4 at)
    {
    }

    public void Message(BossMessages message)
    {
    }
}
