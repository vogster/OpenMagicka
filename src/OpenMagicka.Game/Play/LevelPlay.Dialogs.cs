using Microsoft.Xna.Framework.Input;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Localization;
using OpenMagicka.Content.PolygonHead;
using OpenMagicka.Engine.Dialogs;
using OpenMagicka.Engine.Input;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Engine.Text;
using OpenMagicka.Gameplay.Characters;

namespace OpenMagicka.Game.Play;

/// <summary>What a level's dialogs need: its dialogs (and the fairy's), the language's strings, and the font lines wrap in.</summary>
public sealed record LevelDialogs(DialogCollection Dialogs, StringTable Strings, BitmapFont Wrapping)
{
    /// <summary>The font dialog lines are wrapped and sized in, as the original's.</summary>
    public const string WrappingFont = "Maiandra16";

    /// <summary>A language's font: its own, or English's when it ships none (as the original).</summary>
    public static string Font(ContentFileSystem files, string language, string name)
    {
        ArgumentNullException.ThrowIfNull(files);
        var own = $"Languages/{language}/Font/{name}";
        return files.Exists(own + ".xnb") ? own : $"Languages/{LanguagePack.Fallback}/Font/{name}";
    }

    /// <summary>A level's dialogs from the game's content, in a language.</summary>
    public static LevelDialogs Load(ContentFileSystem files, ContentLoader loader, LevelDefinition level, string language)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(level);
        var font = loader.Load(Font(files, language, WrappingFont)).Root as BitmapFontContent
            ?? throw new InvalidDataException($"{WrappingFont} is not a bitmap font");
        return new LevelDialogs(DialogCollection.Load(files, level.Dialogs), LanguagePack.Load(files, language), new BitmapFont(font, BitmapFont.SpacingOf(WrappingFont)));
    }
}

public sealed partial class LevelPlay
{
    private static readonly ControlBinding[] TalkKeys = [ControlBinding.Of(Keys.Enter), ControlBinding.Of(Keys.Space), ControlBinding.Of(MouseButton.Left)];

    /// <summary>The level's dialogs at play (OM-49); none without the game's content.</summary>
    public DialogManager? Dialogs { get; private set; }

    /// <summary>The language's strings; none without the game's content.</summary>
    public StringTable? Strings { get; private set; }

    private void SetUpDialogs(LevelDialogs? setup)
    {
        if (setup is null)
        {
            return;
        }
        Strings = setup.Strings;
        Dialogs = new DialogManager(setup.Dialogs, setup.Strings, setup.Wrapping, World, Level.Level.Stage, () => IsInCutscene);
        Level.Game.Dialogs = Dialogs;
    }

    /// <summary>
    /// A press of the player's talk keys (Enter, Space, the left button) or its pad's Interact goes to the dialog it
    /// drives; true when the player's own input is held by one, or the press went to one (its wizard is not steered:
    /// the press that ends a dialog neither boosts nor walks).
    /// </summary>
    private bool Talk(ControlFrame? input, PadFrame? pad)
    {
        if (Dialogs is not { } dialogs || !dialogs.CanAdvance(Player))
        {
            return false;
        }
        var pressed = (input is { } keys && TalkKeys.Any(keys.IsPressed)) || pad?.IsPressed(ControllerFunction.Interact) == true;
        if (pressed)
        {
            dialogs.Advance(Player);
        }
        return pressed || dialogs.Holds(Player);
    }

    /// <summary>A script's dialog request, taken: true when it was one.</summary>
    private bool Take(ScriptRequest request)
    {
        switch (request)
        {
            case StartDialogRequest { Interactor: { } talker, Speaker: { } npc } when Dialogs is { } dialogs && PlayerOf(talker) is { } player:
                dialogs.Talk(npc, player);
                return true;
            case StartDialogRequest start when Dialogs is { } dialogs:
                var interactor = start.Speaker is { IsPlayer: true } avatar ? PlayerOf(avatar) : null;
                dialogs.Start(start.Dialog, new DialogAnchor(start.Speaker, start.Speaker is null ? start.At : null, start.OnScreen), interactor);
                return true;
            case EndDialogsRequest when Dialogs is { } dialogs:
                dialogs.EndAll();
                return true;
            default:
                return false;
        }
    }

    private Gameplay.Player? PlayerOf(Character avatar) => World.Session?.Players.FirstOrDefault(p => ReferenceEquals(p.Avatar, avatar));

    /// <summary>A dialog's step, after the level's: its boxes, and the world frozen while a locking one is shown.</summary>
    private void StepDialogs(float dt)
    {
        if (Dialogs is not { } dialogs)
        {
            return;
        }
        dialogs.Update(dt);
        World.IsPhysicsFrozen = dialogs.IsFrozen;
    }
}
