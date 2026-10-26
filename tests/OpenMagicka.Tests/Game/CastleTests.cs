using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Tests.Content;

namespace OpenMagicka.Tests.Game;

/// <summary>The castle's chapter on the game's files (0.0.3): the wizards unarmed till the cellar's rack, the scripts' "player1".</summary>
public class CastleTests
{
    private const float Tick = 1 / 60f;
    private static readonly Point Screen = new(1280, 720);

    private static ControlFrame Press(params Keys[] keys) => new(new KeyboardState(keys), default, default, default);

    private static ControlFrame Click() =>
        new(default, new MouseState(0, 0, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released),
            default, default);

    private static ControlFrame Middle(Keys held = Keys.None) =>
        new(new KeyboardState(held), new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released,
            ButtonState.Released), new KeyboardState(held), default);

    private static LevelPlay Castle()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var loader = new ContentLoader(files, XnbReaderRegistry.Magicka);
        var path = PlayGame.FindLevel(files, "Wizard_Castle")!;
        var play = LevelPlay.Open(LevelReader.Read(new MemoryStream(files.ReadAllBytes(path)), path), files, loader);
        play.Start();
        return play;
    }

    private static void Wait(LevelPlay play, float seconds)
    {
        for (var tick = 0; tick < seconds / Tick; tick++)
        {
            play.Tick(Tick, Press(), Screen);
        }
    }

    private static string? Held(Character wizard, int slot) => wizard.ItemIn(slot)?.Template.Id;

    private static bool Triggered(LevelPlay play, string trigger) =>
        play.Scene!.Scripts!.Triggers.Any(t => t.Key == ScriptScene.Hash(trigger) && t.HasTriggered);

    [GameContentFact]
    public void TheWizard_StartsUnarmed_AndTheCellarsRackGivesItsWeaponsBack()
    {
        using var play = Castle();
        var template = play.Player.Avatar!.Template;
        var sword = template.Equipment!.Last(e => e.Slot == Character.WeaponSlot).Item.Id;
        Wait(play, 0.5f);

        var wizard = play.Player.Avatar!;
        Assert.Contains("unarmed", Held(wizard, Character.WeaponSlot), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unarmed", Held(wizard, Character.StaffSlot), StringComparison.OrdinalIgnoreCase);

        play.Level.ChangeScene(new SpawnPoint(LegacyStringHash.Lower("wc_s3"), [LegacyStringHash.Compute("start")], true),
            ScriptTransition.None, 0, keep: false);
        Wait(play, 0.5f);
        Assert.Contains("unarmed", Held(play.Player.Avatar!, Character.WeaponSlot), StringComparison.OrdinalIgnoreCase);   // still, after the fall

        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("skip_tutorial"), null, ignoreConditions: true);   // ends the section, as the rack
        Wait(play, 0.1f);
        Assert.Equal(sword, Held(play.Player.Avatar!, Character.WeaponSlot));
    }

    [GameContentFact]
    public void TheCellarsScripts_FindPlayer1_TheFallHurtsAndHealingOneselfMovesTheTutorialOn()
    {
        using var play = Castle();
        play.Level.ChangeScene(new SpawnPoint(LegacyStringHash.Lower("wc_s3"), [LegacyStringHash.Compute("start")], true),
            ScriptTransition.None, 0, keep: false);
        Wait(play, 0.5f);
        var wizard = play.Player.Avatar!;
        Assert.Same(wizard, play.World.Entities.FindByUniqueId(ScriptScene.AvatarId(0)));
        Assert.True(wizard.HitPoints < wizard.MaxHitPoints);                    // the fall's damage reaches "player1"

        var scripts = play.Scene!.Scripts!;
        scripts.ExecuteTrigger(ScriptScene.Hash("Cutscene end"), null, ignoreConditions: true);   // the cellar's talk, done
        Wait(play, 3);
        Assert.NotNull(play.Level.Game.Tutorial.DialogHint);
        play.Tick(Tick, Click(), Screen);                                       // "intro done": the input back
        Wait(play, 0.5f);
        scripts.ExecuteTrigger(ScriptScene.Hash("unlock life"), null, ignoreConditions: true);   // as the weapon rack's hint does
        Wait(play, 1.5f);                                                      // the element comes a second later
        play.Tick(Tick, Press(Keys.W), Screen);                                 // Life
        Wait(play, 0.5f);
        Assert.True(Triggered(play, "Life conjured"));
        for (var tick = 0; tick < 60 && !Triggered(play, "Life casted"); tick++)
        {
            play.Tick(Tick, Middle(), Screen);                                 // cast on oneself
        }
        Assert.True(Triggered(play, "Life casted"));
    }

    [GameContentFact]
    public void AFreshWizardAtACheckpointInTheStretch_IsUnarmedToo()
    {
        using var play = Castle();
        play.Level.ChangeScene(new SpawnPoint(LegacyStringHash.Lower("wc_s3"), [LegacyStringHash.Compute("start")], true),
            ScriptTransition.None, 0, keep: false);
        Wait(play, 0.5f);
        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("Room1"), null, ignoreConditions: true);   // a checkpoint before the rack
        Wait(play, 0.5f);

        Assert.True(play.Level.RestartAtCheckpoint());
        Wait(play, 0.1f);

        var fresh = play.Player.Avatar!;
        Assert.Contains("unarmed", Held(fresh, Character.WeaponSlot), StringComparison.OrdinalIgnoreCase);
        Assert.Same(fresh, play.World.Entities.FindByUniqueId(ScriptScene.AvatarId(0)));   // the name went with it
    }

    [GameContentFact]
    public void TheFallIntoTheCellar_ReachesIt_ButAFallOutOfTheLevel_Kills()
    {
        using var play = Castle();
        play.Level.ChangeScene(new SpawnPoint(LegacyStringHash.Lower("wc_s2"), [LegacyStringHash.Compute("start")], true),
            ScriptTransition.None, 0, keep: false);
        Wait(play, 8);                                                         // the intro walks the wizard over the trap
        play.Scene!.Scripts!.ExecuteTrigger(ScriptScene.Hash("Propp does bad party trick"), null, ignoreConditions: true);
        for (var tick = 0; tick < 60 * 15 && play.Scene!.Definition.Name != "wc_s3"; tick++)
        {
            play.Tick(Tick, Press(), Screen);
        }
        Assert.Equal("wc_s3", play.Scene!.Definition.Name);                  // through the floor, alive
        Assert.False(play.Player.Avatar!.IsDead);

        var wizard = play.Player.Avatar!;
        wizard.Body.Body.Position = new System.Numerics.Vector3(wizard.Position.X, -60, wizard.Position.Z);   // off the level
        Wait(play, 0.1f);
        Assert.True(wizard.IsDead);
    }
}
