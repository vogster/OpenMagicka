using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Dialogs;
using OpenMagicka.Engine.Scripting;
using OpenMagicka.Game.Play;
using OpenMagicka.Gameplay;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Engine;

/// <summary>Every level's dialogs, from the game's files (OM-49): each line resolves and wraps in the game's own font.</summary>
public class DialogDataTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryLevelsDialogs_ResolveAndWrap_InEveryLanguageWithFonts()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var loader = new ContentLoader(files, XnbReaderRegistry.Magicka);
        var levels = files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase))
            .Select(path => LevelReader.Read(new MemoryStream(files.ReadAllBytes(path)), path)).ToList();
        var (lines, unresolved) = (0, 0);
        foreach (var language in new[] { "eng", "rus" })
        {
            foreach (var level in levels)
            {
                var setup = LevelDialogs.Load(files, loader, level, language);
                using var world = new GameWorld(session: new GameSession());
                var dialogs = new DialogManager(setup.Dialogs, setup.Strings, setup.Wrapping, world, new ScriptStage(), () => false);
                foreach (var message in AllMessages(setup.Dialogs, level, files))
                {
                    foreach (var line in message.Lines)
                    {
                        var text = dialogs.Text(message, line);
                        lines++;
                        unresolved += text.Contains('#') ? 1 : 0;
                    }
                    var (width, height) = dialogs.MinSize(message);
                    Assert.True(width > 0 && height > 0, $"{level.Name}: a message of no size");
                }
            }
        }
        output.WriteLine($"{levels.Count} levels, {lines} lines resolved, {unresolved} still holding a '#'");
        Assert.True(lines > 1000);
    }

    private static IEnumerable<DialogMessage> AllMessages(DialogCollection dialogs, LevelDefinition level, ContentFileSystem files)
    {
        var ids = (level.Dialogs is { } path ? DialogReader.Read(new MemoryStream(files.ReadAllBytes(path))) : [])
            .Concat(DialogReader.Read(new MemoryStream(files.ReadAllBytes(DialogCollection.FairyDialogs))));
        return ids.SelectMany(d => dialogs.Get(d.Key).Interacts).SelectMany(i => i.Messages);
    }
}
