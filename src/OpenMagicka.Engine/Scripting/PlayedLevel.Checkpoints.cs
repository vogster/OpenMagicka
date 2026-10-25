using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells.Magicks;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// A level's checkpoints (docs/formats/LEVELS_TRIGGERS.md §6.2), as the original: a <c>triggerCheckpoint</c>
/// keeps the level's state; a restart at it puts the state back, replays each scene's log (the ignored
/// triggers reset and left out), and brings fresh wizards in at its spawn points.
/// </summary>
public sealed partial class PlayedLevel
{
    /// <summary>The last checkpoint taken; none before the first.</summary>
    public LevelCheckpoint? Checkpoint { get; private set; }

    /// <summary>The level's state now, as a checkpoint at those spawn points, leaving those triggers out on loading.</summary>
    public LevelCheckpoint TakeCheckpoint(IReadOnlyList<Matrix4x4> spawnPoints, IReadOnlyList<int> ignored)
    {
        ArgumentNullException.ThrowIfNull(spawnPoints);
        ArgumentNullException.ThrowIfNull(ignored);
        var states = scenes.Values.ToDictionary(s => s.Key, s => new SceneCheckpoint(s.IsFirstEntry, s.Scripts?.Save(),
            [.. (ReferenceEquals(s, Current) ? World.Entities.Entities : s.Kept).Select(EntityState.Of).OfType<EntityState>()
                .Concat(s.Pending)]));
        var magicks = (World.Session?.Players ?? []).Aggregate(0UL, (all, p) => all | p.UnlockedMagicks);
        return Checkpoint = new LevelCheckpoint(Current?.Key ?? 0, Level.Save(), states, [.. spawnPoints], [.. ignored], magicks)
        {
            Dialogs = Game.Dialogs?.Save(),
        };
    }

    /// <summary>
    /// Back to the last checkpoint (the players died, or a restart was asked): false with none. The world is
    /// emptied, each scene takes its kept state (one entered only since is as never entered), the
    /// checkpoint's scene is entered and its log replayed, then the counters and timers come back and
    /// fresh wizards stand at the spawn points with the magicks unlocked then.
    /// </summary>
    public bool RestartAtCheckpoint()
    {
        if (Checkpoint is not { } checkpoint || !scenes.TryGetValue(checkpoint.Scene, out var target) || World.Physics.IsStepping
            || World.Entities.IsBusy)
        {
            return false;
        }
        var from = Current?.Key ?? 0;
        pending = null;
        Game.InCutscene = false;
        Game.BossFight.Reset(Game);                                           // ours: the replay's spawns bring the bosses in anew
        Game.HealthBar.Reset();                                               // ours: the replay sets the bar up again
        Level.Stage.Reset();
        Game.Dialogs?.Restore(checkpoint.Dialogs);
        requests.Clear();
        Empty();
        foreach (var scene in scenes.Values)
        {
            Apply(scene, checkpoint.Scenes.GetValueOrDefault(scene.Key), ReferenceEquals(scene, target) ? checkpoint.Ignored : null);
        }
        Prepare(target);
        Enter(target, new SpawnPoint(target.Key, [], SpawnPlayers: false), replay: false);
        Respawn(checkpoint);
        ReplayEntry(target);                                                  // after the fresh wizards: the replay's items and factions are theirs
        Level.Load(checkpoint.Level);
        Game.PlayerDiedInLevel = true;
        requests.Enqueue(new SceneChangedRequest(from, target.Key, Kept: false));
        return true;
    }

    /// <summary>Everything out of the world for good: what is in it, what waits outside, the players' old wizards (dead ones left behind too).</summary>
    private void Empty()
    {
        Current?.Scripts?.Leave();
        foreach (var effect in World.Entities.Entities.OfType<IMagickEffect>().ToList())
        {
            effect.End();
        }
        var (avatars, _) = World.Entities.Store(keep: false);
        var outside = scenes.Values.SelectMany(s => s.Kept).Concat(avatars).Concat(waitingAvatars)
            .Concat((World.Session?.Players ?? []).Select(p => p.Avatar).OfType<Character>().Where(a => a.Manager is null))
            .Distinct().ToList();
        foreach (var entity in outside)
        {
            World.Entities.Discard(entity);
        }
        waitingAvatars.Clear();
        foreach (var scene in scenes.Values)
        {
            scene.Kept.Clear();
        }
        foreach (var mesh in collision)
        {
            World.Physics.RemoveLevelMesh(mesh);
        }
        collision = [];
        if (Current?.Scripts is { } scripts)
        {
            Physics.LevelCollision.UnloadParts(scripts.Places);
        }
        Current = null;
    }

    /// <summary>A scene takes the checkpoint's state of it; with none (not entered by then) it is as never entered.</summary>
    private static void Apply(LevelScene scene, SceneCheckpoint? state, IReadOnlyCollection<int>? ignored)
    {
        scene.Pending.Clear();
        scene.Replay.Clear();
        if (state?.Scripts is not { } scripts || scene.Scripts is null)
        {
            (scene.Scripts, scene.NavMesh, scene.IsFirstEntry) = (null, null, true);
            return;
        }
        scene.IsFirstEntry = state.FirstEntry;
        scene.Pending.AddRange(state.Entities);
        scene.Replay.AddRange(scene.Scripts.Load(scripts, ignored));
    }

    /// <summary>
    /// Fresh wizards, whole, at the checkpoint's spawn points (a player past them at the first): down onto
    /// the floor (up to 5 below; none found: the world's origin, as the original) and their radius and half
    /// length over it; with the magicks unlocked at the checkpoint.
    /// </summary>
    private void Respawn(LevelCheckpoint checkpoint)
    {
        var players = World.Session?.Players ?? [];
        foreach (var player in players)
        {
            player.UnlockedMagicks = checkpoint.UnlockedMagicks;
            if (!player.Playing || player.Avatar is not { } avatar || checkpoint.SpawnPoints.Count == 0)
            {
                continue;
            }
            var spot = checkpoint.SpawnPoints[player.Id < checkpoint.SpawnPoints.Count ? player.Id : 0];
            var floor = World.Physics.CastSegment(spot.Translation, spot.Translation - Vector3.UnitY * 5, levelOnly: true)?.Position ?? Vector3.Zero;
            var wizard = avatar.Successor(floor + Vector3.UnitY * (avatar.Template.Radius + avatar.Template.Length / 2), players.Count(p => p.Playing));
            try
            {
                World.Entities.Add(wizard);
            }
            catch (InvalidOperationException)
            {
                World.Entities.Discard(wizard);                               // a full level: the player waits for a revival
                continue;
            }
            wizard.Body.Face(ScriptPlaces.Forward(spot));
            player.Avatar = wizard;
        }
    }
}
