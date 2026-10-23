using OpenMagicka.Content.Xnb;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Content;

/// <summary>Location of a Magicka installation for tests that need the real game files.</summary>
internal static class GameContent
{
    public const string Variable = "MAGICKA_DIR";

    public static string? ContentDirectory
    {
        get
        {
            var root = Environment.GetEnvironmentVariable(Variable);
            var content = root is null ? null : Path.Combine(root, "Content");
            return content is not null && Directory.Exists(content) ? content : null;
        }
    }
}

/// <summary>Runs only when MAGICKA_DIR points at a Magicka installation; skipped otherwise.</summary>
public sealed class GameContentFactAttribute : FactAttribute
{
    public GameContentFactAttribute()
    {
        if (GameContent.ContentDirectory is null)
        {
            Skip = $"Set {GameContent.Variable} to a Magicka installation to run tests on the game files.";
        }
    }
}

/// <summary>A theory that runs only when MAGICKA_DIR points at a Magicka installation; skipped otherwise.</summary>
public sealed class GameContentTheoryAttribute : TheoryAttribute
{
    public GameContentTheoryAttribute()
    {
        if (GameContent.ContentDirectory is null)
        {
            Skip = $"Set {GameContent.Variable} to a Magicka installation to run tests on the game files.";
        }
    }
}

public class GameContentTests(ITestOutputHelper output)
{
    [GameContentFact]
    public void EveryXnbFile_DecompressesAndHasAReaderTable()
    {
        var files = Directory.GetFiles(GameContent.ContentDirectory!, "*.xnb", SearchOption.AllDirectories);
        var failures = new List<string>();

        foreach (var path in files)
        {
            try
            {
                var xnb = XnbFile.Read(File.ReadAllBytes(path));
                if (xnb.TypeReaders.Count == 0 || xnb.Objects.Length == 0)
                {
                    failures.Add($"{path}: empty");
                }
            }
            catch (Exception e)
            {
                failures.Add($"{path}: {e.GetType().Name}: {e.Message}");
            }
        }

        Assert.True(files.Length > 4000, $"only {files.Length} .xnb files found");
        Assert.Empty(failures);
    }

    [GameContentFact]
    public void StandardXnaContent_LoadsCompletely()
    {
        var loaded = LoadAll(XnbReaderRegistry.Standard, allowedMissing: name => !name.StartsWith("Microsoft.Xna", StringComparison.Ordinal));

        Assert.Equal(2296, loaded.GetValueOrDefault("Texture2DReader"));
        Assert.Equal(52, loaded.GetValueOrDefault("EffectReader"));
        Assert.Equal(13, loaded.GetValueOrDefault("TextureCubeReader"));
        Assert.Equal(7, loaded.GetValueOrDefault("Texture3DReader"));
        Assert.Equal(1, loaded.GetValueOrDefault("ListReader<String>"));
    }

    [GameContentFact]
    public void MagickaContent_LoadsCompletely()
    {
        // Every reader the game uses is implemented: nothing may be missing.
        var loaded = LoadAll(XnbReaderRegistry.Magicka, allowedMissing: _ => false);

        Assert.Equal(553, loaded.GetValueOrDefault("ModelReader"));
        Assert.Equal(327, loaded.GetValueOrDefault("SkinnedModelReader"));
        Assert.Equal(66, loaded.GetValueOrDefault("BitmapFontReader"));
        Assert.Equal(2296, loaded.GetValueOrDefault("Texture2DReader"));
        Assert.Equal(132, loaded.GetValueOrDefault("LevelModelReader"));
        Assert.Equal(454, loaded.GetValueOrDefault("ItemReader"));
        Assert.Equal(338, loaded.GetValueOrDefault("CharacterTemplateReader"));
        Assert.Equal(69, loaded.GetValueOrDefault("PhysicsEntityTemplateReader"));
        Assert.Equal(4308, loaded.Values.Sum());
    }

    [GameContentFact]
    public void EveryExternalReference_PointsToAnExistingAsset()
    {
        var loader = new OpenMagicka.Content.ContentLoader(new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var root = loader.Files.Root;
        var total = 0;
        var missing = new SortedSet<string>();
        foreach (var path in Directory.GetFiles(root, "*.xnb", SearchOption.AllDirectories))
        {
            var asset = Path.GetRelativePath(root, path)[..^4].Replace(Path.DirectorySeparatorChar, '/');
            var content = loader.Load(asset);
            foreach (var reference in ExternalReferenceWalker.Find(content.Root).Concat(content.SharedResources.SelectMany(ExternalReferenceWalker.Find)))
            {
                total++;
                var target = loader.ResolveReference(asset, reference);
                if (target is null || !loader.Files.Exists(target + ".xnb"))
                {
                    missing.Add($"{asset} -> {reference.Path}");
                }
            }
        }

        output.WriteLine($"{total} external references, {missing.Count} distinct unresolved");
        foreach (var m in missing.Take(40))
        {
            output.WriteLine("  missing " + m);
        }
        Assert.True(total > 1000, $"only {total} references found");
        Assert.Empty(missing);
    }

    [GameContentFact]
    public void EveryOriginalEffect_IsPatchedIntoAnFnaCompatibleForm()
    {
        var loader = new OpenMagicka.Content.ContentLoader(new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var shaders = Path.Combine(loader.Files.Root, "Shaders");
        var patched = 0;
        foreach (var path in Directory.GetFiles(shaders, "*.xnb", SearchOption.AllDirectories))
        {
            var asset = Path.GetRelativePath(loader.Files.Root, path)[..^4];
            var effect = Assert.IsType<OpenMagicka.Content.Xna.EffectContent>(loader.Load(asset).Root);
            var result = OpenMagicka.Content.Effects.EffectPatcher.Patch(effect.Bytecode);
            var parsed = OpenMagicka.Content.Effects.Fx2Effect.Parse(result.Bytecode);

            Assert.DoesNotContain(parsed.Passes.SelectMany(p => p.States),
                s => OpenMagicka.Content.Effects.EffectPatcher.IsUnsupportedRenderState(s.Operation));
            Assert.DoesNotContain(parsed.SamplerStates, s => OpenMagicka.Content.Effects.EffectPatcher.IsUnsupportedSamplerState(s.Operation, s.Value));
            foreach (var resource in parsed.Resources.Where(r => r.Technique >= 0))
            {
                foreach (var preshader in OpenMagicka.Content.Effects.Preshader.Find(resource.Code))
                {
                    var (allocated, needed) = OpenMagicka.Content.Effects.Preshader.TempUsage(preshader);
                    Assert.True(allocated >= needed, $"{asset}: preshader needs {needed} temps, gets {allocated}");
                }
            }
            if (!ReferenceEquals(result.Bytecode, effect.Bytecode))
            {
                patched++;
            }
        }
        output.WriteLine($"{patched} effects patched");
        Assert.Equal(20, patched);
    }

    [GameContentFact]
    public void EveryLanguage_LoadsAndHoldsTheItemNames()
    {
        var files = new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!);
        var languages = OpenMagicka.Content.Localization.LanguagePack.Available(files);
        Assert.Equal(["deu", "eng", "fra", "hun", "ita", "pol", "rus", "spa"], languages);

        var loader = new OpenMagicka.Content.ContentLoader(files, XnbReaderRegistry.Magicka);
        var keys = files.ListFiles("Data/Items", recursive: true)
            .Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            .Select(p => loader.Load(p[..^4]).Root)
            .OfType<OpenMagicka.Content.Magicka.ItemContent>()
            .SelectMany(item => new[] { item.DisplayName, item.Description })
            .Where(key => key.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(keys.Length > 300, $"only {keys.Length} item string keys");

        foreach (var language in languages)
        {
            var table = OpenMagicka.Content.Localization.LanguagePack.Load(files, language);
            var missing = keys.Where(k => !table.TryGetRaw(OpenMagicka.Content.Localization.StringTable.Id(k), out _)).ToArray();
            output.WriteLine($"{language}: {table.Count} strings, {missing.Length} of {keys.Length} item keys missing: {string.Join(' ', missing.Take(8))}");
            Assert.True(table.Count > 3000, $"{language}: only {table.Count} strings");
            // 84 item keys (unused staffs and the like) have no string in any shipped table;
            // the original shows "NOT FOUND" for them too. French lacks one more.
            Assert.Equal(language == "fra" ? 85 : 84, missing.Length);
        }
    }

    [GameContentFact]
    public void EveryVisualEffect_Loads_WithEveryEmitterAndLight()
    {
        var files = new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!);
        var library = OpenMagicka.Content.Particles.EffectLibrary.Load(files);
        Assert.Equal(904, library.Count);

        // Counted independently from the raw text.
        var paths = files.ListFiles("Effects", recursive: true).Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToArray();
        var texts = paths.Select(p => System.Text.Encoding.UTF8.GetString(files.ReadAllBytes(p))).ToArray();
        int Count(string tag) => texts.Sum(t => t.Split(tag).Length - 1);

        var effects = paths.Select(p => library.Get(OpenMagicka.Content.Particles.EffectLibrary.Id(Path.GetFileNameWithoutExtension(p)))).ToArray();
        var emitters = effects.SelectMany(e => e.Emitters).ToArray();
        output.WriteLine($"{emitters.Length} emitters, {effects.Count(e => e.Light is not null)} lights");
        Assert.Equal(Count("<ContinuousEmitter"), emitters.Count(e => e.Kind == OpenMagicka.Content.Particles.EmitterKind.Continuous));
        Assert.Equal(Count("<PulseEmitter"), emitters.Count(e => e.Kind == OpenMagicka.Content.Particles.EmitterKind.Pulse));
        Assert.Equal(Count("<Light>"), effects.Count(e => e.Light is not null));
        Assert.Equal((1381, 1344, 31), (emitters.Count(e => e.Kind == OpenMagicka.Content.Particles.EmitterKind.Continuous),
            emitters.Count(e => e.Kind == OpenMagicka.Content.Particles.EmitterKind.Pulse), effects.Count(e => e.Light is not null)));
    }

    [GameContentFact]
    public void EveryLevel_AndEveryScene_ItReferences_Loads()
    {
        var files = new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!);
        var levelPaths = files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase)).ToArray();
        var levels = levelPaths.Select(p => OpenMagicka.Content.Levels.LevelReader.Read(new MemoryStream(files.ReadAllBytes(p)), p)).ToArray();
        var scenePaths = levels.SelectMany(l => l.Scenes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var scenes = scenePaths.Select(p => OpenMagicka.Content.Levels.SceneReader.Read(new MemoryStream(files.ReadAllBytes(p)), p)).ToArray();

        output.WriteLine($"{levels.Length} levels, {scenes.Length} scenes, {scenes.Sum(s => s.Triggers.Count)} triggers, "
            + $"{scenes.Sum(s => s.Interactables.Count)} interactables, {scenes.Sum(s => s.Waves.Count)} waves");
        Assert.Equal(48, levels.Length);
        Assert.Equal(141, scenes.Length);
        Assert.Equal((2516, 285, 316), (scenes.Sum(s => s.Triggers.Count), scenes.Sum(s => s.Interactables.Count), scenes.Sum(s => s.Waves.Count)));
        Assert.All(levels.Where(l => l.Dialogs is not null), l => Assert.True(files.Exists(l.Dialogs!), l.Dialogs));
        Assert.All(scenes, s => Assert.True(files.Exists(s.Model + ".xnb"), s.Model));
        Assert.Equal((17, 3, 12), (scenes.Count(s => s.Ruleset?.Type == OpenMagicka.Content.Levels.RulesetType.Survival),
            scenes.Count(s => s.Ruleset?.Type == OpenMagicka.Content.Levels.RulesetType.TimedObjective),
            scenes.Count(s => s.Ruleset?.Type == OpenMagicka.Content.Levels.RulesetType.Versus)));

        var dialogs = levels.Select(l => OpenMagicka.Content.Levels.DialogCollection.Load(files, l.Dialogs)).ToArray();
        var fairy = OpenMagicka.Content.Levels.DialogCollection.Load(files, null).Count;
        var dialogFiles = files.ListFiles("Levels", recursive: true).Where(p => Path.GetFileName(p).StartsWith("Dialog_", StringComparison.OrdinalIgnoreCase)).ToArray();
        var allDialogs = dialogFiles.Select(p => OpenMagicka.Content.Levels.DialogReader.Read(new MemoryStream(files.ReadAllBytes(p)))).ToArray();
        var interacts = allDialogs.SelectMany(d => d).SelectMany(d => d.Interacts).ToArray();
        var messages = interacts.SelectMany(i => i.Messages).ToArray();
        Assert.Equal((18, 615, 728, 759, 907),
            (dialogFiles.Length, allDialogs.Sum(d => d.Count), interacts.Length, messages.Length, messages.Sum(m => m.Lines.Count)));
        foreach (var (level, collection) in levels.Zip(dialogs))
        {
            var own = level.Dialogs is null ? 0 : OpenMagicka.Content.Levels.DialogReader.Read(new MemoryStream(files.ReadAllBytes(level.Dialogs))).Count;
            Assert.Equal(fairy + own, collection.Count);
        }

        // The one scene no level uses has an attribute the original rejects.
        var orphan = "Levels/Havindr/havindr_s5HBF.xml";
        Assert.Throws<InvalidDataException>(() => OpenMagicka.Content.Levels.SceneReader.Read(new MemoryStream(files.ReadAllBytes(orphan)), orphan));
    }

    [GameContentFact]
    public void EveryEnumValueInTheLevelScripts_Parses()
    {
        var parsers = new Dictionary<string, Func<string, bool>>(StringComparer.OrdinalIgnoreCase)
        {
            ["element"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.Elements>(v, true, out _),
            ["elements"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.Elements>(v, true, out _),
            ["faction"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.Factions>(v, true, out _),
            ["factions"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.Factions>(v, true, out _),
            ["order"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.Order>(v, true, out _),
            ["reaction"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.Order>(v, true, out _),
            ["reactto"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.ReactTo>(v, true, out _),
            ["casttype"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.CastType>(v, true, out _),
            ["magick"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.MagickType>(v, true, out _),
            ["magicktype"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.MagickType>(v, true, out _),
            ["statuseffect"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.StatusEffects>(v, true, out _),
            ["animation"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.Animation>(v, true, out _),
            ["idleanimation"] = v => Enum.TryParse<OpenMagicka.Content.Magicka.Animation>(v, true, out _),
        };
        var files = new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!);
        var seen = new Dictionary<string, int>();
        var bad = new SortedSet<string>();
        foreach (var path in files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var document = new System.Xml.XmlDocument();
            document.Load(new System.Xml.XmlTextReader(new MemoryStream(files.ReadAllBytes(path))) { DtdProcessing = System.Xml.DtdProcessing.Prohibit });
            foreach (System.Xml.XmlAttribute attribute in document.SelectNodes("//@*")!)
            {
                // "animation" names a character animation only on the AI event of that name;
                // elsewhere (tutorial hints, level parts) it is something else.
                var isAnimation = attribute.Name.Equals("animation", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.Equals("idleAnimation", StringComparison.OrdinalIgnoreCase);
                if (isAnimation && !attribute.OwnerElement!.Name.Equals("animation", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (parsers.TryGetValue(attribute.Name, out var parse))
                {
                    seen[attribute.Name.ToLowerInvariant()] = seen.GetValueOrDefault(attribute.Name.ToLowerInvariant()) + 1;
                    if (!parse(attribute.Value))
                    {
                        bad.Add($"{attribute.Name}=\"{attribute.Value}\"");
                    }
                }
            }
        }
        output.WriteLine(string.Join(", ", seen.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")));
        foreach (var b in bad)
        {
            output.WriteLine("  unparsed " + b);
        }
        Assert.True(seen.Values.Sum() > 5000, "too few enum attributes found");
        Assert.Empty(bad);
    }

    [GameContentFact]
    public void Omx_DumpsEveryXnbFileAsJson_AndChecksTheWholeGame()
    {
        var root = GameContent.ContentDirectory!;
        var failures = new List<string>();
        foreach (var path in Directory.GetFiles(root, "*.xnb", SearchOption.AllDirectories))
        {
            var dump = new StringWriter();
            if (OpenMagicka.Tools.Omx.Run(["dump", path], dump, TextWriter.Null) != 0)
            {
                failures.Add(path);
                continue;
            }
            using var json = System.Text.Json.JsonDocument.Parse(dump.ToString());
        }
        Assert.Empty(failures);

        var report = new StringWriter();
        Assert.Equal(0, OpenMagicka.Tools.Omx.Run(["check", Path.GetDirectoryName(root)!], report, TextWriter.Null));
        output.WriteLine(report.ToString());
        Assert.Contains("xnb: 4308 of 4308 read", report.ToString(), StringComparison.Ordinal);
    }

    [GameContentFact]
    public void SharedEffectParameters_AreTheGlobalOnes()
    {
        var loader = new OpenMagicka.Content.ContentLoader(new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        var shared = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var path in loader.Files.ListFiles("Shaders", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            var effect = Assert.IsType<OpenMagicka.Content.Xna.EffectContent>(loader.Load(path[..^4]).Root);
            foreach (var parameter in OpenMagicka.Content.Effects.Fx2Effect.Parse(effect.Bytecode).Parameters.Where(p => p.IsShared))
            {
                var key = $"{parameter.Name} ({parameter.Class} {parameter.Type})";
                if (!shared.TryGetValue(key, out var users))
                {
                    shared[key] = users = new SortedSet<string>(StringComparer.Ordinal);
                }
                users.Add(Path.GetFileNameWithoutExtension(path));
            }
        }
        foreach (var (key, users) in shared)
        {
            output.WriteLine($"{key}: {users.Count} effects: {string.Join(' ', users)}");
        }
        // The 7 globals of the EffectPool, plus three per-object values some effects mark shared.
        Assert.Equal(
        [
            "CameraPosition (Vector Float)", "CubeMapRotation (MatrixRows Float)", "EyeOfTheBeholder (Vector Float)",
            "InverseViewProjection (MatrixRows Float)", "Projection (MatrixRows Float)", "ProjectionMapMatrix (MatrixRows Float)",
            "Time (Scalar Float)", "View (MatrixRows Float)", "ViewProjection (MatrixRows Float)", "World (MatrixRows Float)",
        ], shared.Keys);
    }

    [GameContentFact]
    public void EveryLevelModel_TurnsIntoDrawableBatches()
    {
        var loader = new OpenMagicka.Content.ContentLoader(new OpenMagicka.Content.ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        int levels = 0, statics = 0, parts = 0;
        foreach (var path in loader.Files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            var file = loader.LoadUncached(path[..^4]);
            if (file.Root is not OpenMagicka.Content.Magicka.LevelModelContent level)
            {
                continue;
            }
            levels++;
            var levelBatches = (level.Geometry is null ? [] : OpenMagicka.Engine.Rendering.LevelGeometry.Batches(level.Geometry)).ToList();
            statics += levelBatches.Count;
            var partBatches = OpenMagicka.Engine.Rendering.LevelParts.Batches(level.AnimatedParts, file.SharedResources);
            parts += partBatches.Count;
            Assert.All(levelBatches.Concat(partBatches), b => Assert.True(float.IsFinite(b.Bounds.Min.X + b.Bounds.Max.X), $"{path}: bounds {b.Bounds}"));
        }
        output.WriteLine($"{levels} levels: {statics} static batches, {parts} animated part batches");
        Assert.Equal(132, levels);
        Assert.Equal((9500, 1512), (statics, parts));     // 6 energy rings of ch_end_of_world have a NaN rest pose
    }

    /// <summary>Loads every file; a missing reader is only allowed when its name passes the filter.</summary>
    private Dictionary<string, int> LoadAll(XnbReaderRegistry registry, Func<string, bool> allowedMissing)
    {
        var files = Directory.GetFiles(GameContent.ContentDirectory!, "*.xnb", SearchOption.AllDirectories);
        var loaded = new Dictionary<string, int>();
        var unsupported = new Dictionary<string, int>();
        var failures = new List<string>();

        foreach (var path in files)
        {
            var xnb = XnbFile.Read(File.ReadAllBytes(path));
            var rootKey = xnb.TypeReaders[0].Key;
            try
            {
                XnbContent.Load(xnb, registry);
                loaded[rootKey] = loaded.GetValueOrDefault(rootKey) + 1;
            }
            catch (XnbUnsupportedReaderException e)
            {
                if (xnb.TypeReaders.Any(t => t.Key == e.ReaderKey && !allowedMissing(t.Name)))
                {
                    failures.Add($"{path}: missing reader {e.ReaderKey}");
                }
                unsupported[e.ReaderKey] = unsupported.GetValueOrDefault(e.ReaderKey) + 1;
            }
            catch (Exception e)
            {
                failures.Add($"{path}: {e.GetType().Name}: {e.Message}");
            }
        }

        foreach (var (key, count) in loaded.OrderByDescending(p => p.Value))
        {
            output.WriteLine($"loaded {count,5} {key}");
        }
        foreach (var (key, count) in unsupported.OrderByDescending(p => p.Value))
        {
            output.WriteLine($"later  {count,5} {key}");
        }
        Assert.Empty(failures);
        return loaded;
    }
}
