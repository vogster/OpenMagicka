using OpenMagicka.Engine.Hud;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Game.Play;

/// <summary>
/// The original HUD's part of a level (OM-54, docs/engine/ARCHITECTURE.md §19): the health bars, and each player's
/// element queue under its wizard, animated from its conjures and kept in step with the queue itself (a cast, a gift
/// to a weapon or a death empties it), with the selected magick's name and recipe. Logic thread.
/// </summary>
public sealed partial class LevelPlay
{
    private readonly Dictionary<Player, (QueueIcons Icons, MagickHint Magick)> queues = [];
    private readonly ElementKeys keys = new();
    private readonly BossBar bossBar = new();
    private readonly FloatingNumbers numbers = new();
    private readonly SpellWheel wheel = new();
    private bool padUsed;

    /// <summary>Which health bars the HUD shows (the original's option, On by default).</summary>
    public HealthBarOption HealthBarOption { get; set; }

    /// <summary>Whether damage and heal numbers show over heads (the original's option, on by default).</summary>
    public bool ShowsDamageNumbers { get; set; } = true;

    /// <summary>A player's queue on the HUD (made on first use).</summary>
    public QueueIcons QueueOf(Player player) => Queue(player).Icons;

    private (QueueIcons Icons, MagickHint Magick) Queue(Player player)
    {
        if (!queues.TryGetValue(player, out var queue))
        {
            queues[player] = queue = (new QueueIcons(), new MagickHint());
        }
        return queue;
    }

    private void OnHudCue(Entity source, AnimationCue cue)
    {
        if (cue is ConjureCue conjure && source is Character { IsPlayer: true, Player: { } player })
        {
            Queue(player).Icons.ApplyAll(conjure.Changes);
            if (ReferenceEquals(player, Player))
            {
                keys.Press(conjure.Element);                                  // the local player's key flashes
            }
        }
    }

    /// <summary>The HUD's step, after the level's: the keys, the boss bar, the numbers, the pad's wheel and the queues (in step with the real ones, animated outside cutscenes).</summary>
    private void StepHud(float dt, bool pad)
    {
        padUsed = pad;
        keys.Update(dt, Level.Game.Tutorial.DisabledElements);
        bossBar.Update(dt, Level.Game.BossFight.IsRunning, Level.Game.BossFight.NormalizedHealth);
        if (ShowsDamageNumbers)
        {
            numbers.Update(dt, World.Entities.Entities.OfType<Character>(), IsInCutscene);
        }
        else
        {
            numbers.Clear();
        }
        wheel.Update(dt, pad && !IsInCutscene && Player.Avatar is { IsDead: false } ? Pad.Shown : ControllerDirection.Center, pad && Pad.WantsWheel && !IsInCutscene);
        foreach (var player in Session.Players)
        {
            if (player.Avatar is not { } avatar)
            {
                continue;
            }
            var (icons, magick) = Queue(player);
            Resync(icons, avatar.IsDead ? Gameplay.Spells.ElementQueue.Empty : avatar.SpellQueue);
            if (!IsInCutscene)
            {
                icons.Update(dt);                                             // a dead wizard's queue still shrinks away
            }
            if (!IsInCutscene && !avatar.IsDead)
            {
                magick.Update(dt, player.SelectedMagick);
            }
        }
    }

    /// <summary>A queue changed otherwise than by a conjure (cast, given, emptied): the icons go, the queue's own come back.</summary>
    private static void Resync(QueueIcons icons, ElementQueue actual)
    {
        if (Matches(icons, actual))
        {
            return;
        }
        icons.ClearAll();
        foreach (var entry in actual.Entries)
        {
            icons.Apply(new QueueAdded(entry.Element));
        }
    }

    private static bool Matches(QueueIcons icons, ElementQueue actual)
    {
        if (icons.Elements.Count != actual.Count || icons.Staying != actual.Count)
        {
            return false;
        }
        for (var i = 0; i < actual.Count; i++)
        {
            if (icons.Elements[i] != actual.Entries[i].Element)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The HUD's pictures of this tick into the frame's.</summary>
    public void WriteHud(GuiPictures into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var ui = Level.Level.Stage.IsUiEnabled;
        HealthBars.Collect(World.Entities.Entities, HealthBarOption, ui, into.Bars);
        into.TopBar = TopBar();
        if (ShowsDamageNumbers)
        {
            numbers.Write(into.Numbers);
        }
        if (padUsed && wheel.IsShown && Player.Avatar is { IsDead: false } wizard)
        {
            var icons = new List<WheelIcon>();
            wheel.Write(icons);
            into.Wheel = (new Xna.Vector3(wizard.Position.X, wizard.Position.Y, wizard.Position.Z), icons);
        }
        if (!ui)
        {
            return;
        }
        keys.Write(Controls.Bindings, into.Keys);
        foreach (var player in Session.Players)
        {
            if (player.Avatar is not { IsDead: false } avatar || !queues.TryGetValue(player, out var queue))
            {
                continue;
            }
            var feet = avatar.Position - System.Numerics.Vector3.UnitY * (avatar.Template.Length / 2 + avatar.Template.Radius);
            var icons = new List<QueueIcon>();
            queue.Icons.Write(icons);
            var name = queue.Magick.Shown != MagickType.None ? Strings?.Get(MagickHint.NameKey(queue.Magick.Shown)) : null;
            into.Queues.Add(new QueuePicture(new Xna.Vector3(feet.X, feet.Y, feet.Z), icons, queue.Magick.Recipe, queue.Icons.Staying, name, queue.Magick.Alpha));
        }
    }

    /// <summary>The boss fight's bar (not hidden with the UI); else the scripts' own bar while it is up.</summary>
    private TopBarPicture? TopBar()
    {
        if (bossBar.IsShown)
        {
            return new TopBarPicture(Level.Game.BossFight.HealthBarWidth, bossBar.Shown, new Xna.Vector4(1, 0, 0, 1), bossBar.Alpha);
        }
        var scripted = Level.Game.HealthBar;
        return scripted is { IsActive: true, Graphics: Engine.Scripting.GenericHealthBarGraphics.Default }      // the dynamite's look is not drawn yet
            ? new TopBarPicture(BossFightWidth, scripted.Shown, new Xna.Vector4(MathF.Max(scripted.Look.Power, 1), 0, 0, 1), scripted.Look.Alpha > 0 ? scripted.Look.Alpha : 1)
            : null;
    }

    /// <summary>The scripts' bar's width (the boss bar's default).</summary>
    private const float BossFightWidth = 0.8f;

    private void SetUpHud() => World.Entities.Cue += OnHudCue;

    private void DisposeHud() => World.Entities.Cue -= OnHudCue;
}
