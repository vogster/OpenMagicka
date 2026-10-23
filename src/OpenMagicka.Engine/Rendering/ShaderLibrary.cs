using System.Collections.Concurrent;
using System.Collections.Frozen;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Content;
using OpenMagicka.Content.Effects;
using OpenMagicka.Content.Xna;
using OpenMagicka.Engine.Threading;

namespace OpenMagicka.Engine.Rendering;

/// <summary>An effect the game does not load because it crashes MojoShader natively (where it cannot be caught).</summary>
public sealed class KnownBrokenEffectException(string message) : NotSupportedException(message);

/// <summary>A game effect ready for the GPU: patched bytecode and the parameters it marks shared.</summary>
public sealed record ShaderProgram(string Name, byte[] Bytecode, IReadOnlyList<string> SharedParameters);

/// <summary>
/// The game's effects from <c>Content/Shaders</c>, patched for FNA (<see cref="EffectPatcher"/>),
/// by name ("RenderDeferredEffectWOVT", "Maya/SkinnedModelEffect_0") or content path. Thread-safe;
/// results are cached, failures are not. No GPU work: <see cref="ShaderLibrary"/> creates the effects.
/// </summary>
public sealed class ShaderSource
{
    public const string Directory = "Shaders";

    public static readonly FrozenDictionary<string, string> KnownBroken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ArcaneEffect"] = "heap corruption in MOJOSHADER_parse (OM-62)",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly ContentLoader loader;
    private readonly ConcurrentDictionary<string, Lazy<ShaderProgram>> cache = new(StringComparer.OrdinalIgnoreCase);

    public ShaderSource(ContentLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        this.loader = loader;
        Names = loader.Files.ListFiles(Directory, recursive: true)
            .Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[(Directory.Length + 1)..^4])
            .ToArray();
    }

    /// <summary>The effect names under Shaders/, in name order.</summary>
    public IReadOnlyList<string> Names { get; }

    public static bool IsKnownBroken(string name) => KnownBroken.ContainsKey(Path.GetFileName(Normalize(name)));

    public ShaderProgram Get(string name)
    {
        var key = Normalize(name);
        if (KnownBroken.TryGetValue(Path.GetFileName(key), out var reason))
        {
            throw new KnownBrokenEffectException($"effect {key} is not loaded: {reason}");
        }
        var lazy = cache.GetOrAdd(key, k => new Lazy<ShaderProgram>(() => Load(k)));
        try
        {
            return lazy.Value;
        }
        catch
        {
            cache.TryRemove(new KeyValuePair<string, Lazy<ShaderProgram>>(key, lazy));
            throw;
        }
    }

    public byte[] Bytecode(string name) => Get(name).Bytecode;

    /// <summary>"Shaders/Maya/X", "maya\\x" and "Maya/X" are the same effect: "Maya/X" as spelled.</summary>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = ContentFileSystem.Normalize(name) ?? throw new ArgumentException($"invalid effect name \"{name}\"", nameof(name));
        return normalized.StartsWith(Directory + "/", StringComparison.OrdinalIgnoreCase) ? normalized[(Directory.Length + 1)..] : normalized;
    }

    private ShaderProgram Load(string key)
    {
        var asset = Directory + "/" + key;
        if (!loader.Files.Exists(asset + ".xnb"))
        {
            throw new FileNotFoundException($"no effect {asset}", asset);
        }
        if (loader.Load(asset).Root is not EffectContent effect)
        {
            throw new InvalidDataException($"{asset} is not an effect");
        }
        try
        {
            var patched = EffectPatcher.Patch(effect.Bytecode).Bytecode;
            var shared = Fx2Effect.Parse(patched).Parameters.Where(p => p.IsShared).Select(p => p.Name).ToArray();
            return new ShaderProgram(key, patched, shared);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{asset}: {e.Message}", e);
        }
    }
}

/// <summary>A game effect on the GPU with its global parameter binding.</summary>
public sealed class GameEffect(ShaderProgram program, Effect effect) : IDisposable
{
    public string Name { get; } = program.Name;

    public Effect Effect { get; } = effect;

    public GlobalEffectBinding Globals { get; } = new(new FnaEffectParameters(effect), program.SharedParameters);

    public void Dispose() => Effect.Dispose();
}

/// <summary>
/// Creates game effects: the bytecode is read and patched on the calling thread, the FNA effect
/// is created on the render thread through the GPU queue. Each effect is created once; a failed
/// or cancelled creation is forgotten, so the next request tries again.
/// </summary>
public sealed class ShaderLibrary(ShaderSource source, RenderQueue<GraphicsDevice> gpu) : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<GameEffect>>> effects = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool disposed;

    public ShaderSource Source { get; } = source;

    public Task<GameEffect> Load(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        ObjectDisposedException.ThrowIf(disposed, this);
        string key;
        try
        {
            key = ShaderSource.Normalize(name);
        }
        catch (ArgumentException e)
        {
            return Task.FromException<GameEffect>(e);
        }
        var lazy = effects.GetOrAdd(key, k => new Lazy<Task<GameEffect>>(() => Create(k)));
        // An earlier request that failed or was cancelled may not be evicted yet (its continuation
        // runs asynchronously): drop it now and start again.
        if (lazy.IsValueCreated && lazy.Value is { IsFaulted: true } or { IsCanceled: true })
        {
            effects.TryRemove(new KeyValuePair<string, Lazy<Task<GameEffect>>>(key, lazy));
            lazy = effects.GetOrAdd(key, k => new Lazy<Task<GameEffect>>(() => Create(k)));
        }
        var task = lazy.Value;
        task.ContinueWith(_ => effects.TryRemove(new KeyValuePair<string, Lazy<Task<GameEffect>>>(key, lazy)),
            CancellationToken.None, TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    /// <summary>Disposes the effects created so far (render thread, after the GPU queue is closed).</summary>
    public void Dispose()
    {
        disposed = true;
        foreach (var task in effects.Values.Where(l => l.IsValueCreated).Select(l => l.Value).Where(t => t.IsCompletedSuccessfully))
        {
            task.Result.Dispose();
        }
    }

    private Task<GameEffect> Create(string key)
    {
        ShaderProgram program;
        try
        {
            program = Source.Get(key);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Task.FromException<GameEffect>(e);
        }
        return gpu.Enqueue(device => new GameEffect(program, new Effect(device, program.Bytecode)));
    }
}
