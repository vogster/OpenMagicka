using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// Risk check OM-20. Usage:
//   ShaderSpike <dir with .fxb>            load every effect, list techniques and parameters
//   ShaderSpike <dir with .fxb> <name>     load one effect and draw a quad with every pass
// .fxb files come from tools/research/extract_effects.py (game data, never in the repo).
if (args.Length is < 1 or > 2 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("usage: ShaderSpike <fxb dir> [effect name]");
    return 2;
}

using var spike = new ShaderSpikeGame(args[0], args.Length == 2 ? args[1] : null);
spike.Run();
return spike.ExitCode;

internal sealed class ShaderSpikeGame : Game
{
    private const int Size = 64;

    private readonly string dir;
    private readonly string? only;

    public ShaderSpikeGame(string dir, string? only)
    {
        this.dir = dir;
        this.only = only;
        _ = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = Size, PreferredBackBufferHeight = Size };
    }

    public int ExitCode { get; private set; }

    protected override void LoadContent()
    {
        try
        {
            if (only is null)
            {
                ListAll();
            }
            else
            {
                DrawAllPasses(only);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"FAIL {only ?? "*"}: {e.GetType().Name}: {e.Message}");
            Console.Error.WriteLine(e.InnerException?.ToString() ?? e.ToString());
            ExitCode = 1;
        }
        Exit();
    }

    private void ListAll()
    {
        var failed = 0;
        foreach (var path in Directory.GetFiles(dir, "*.fxb").Order())
        {
            var name = Path.GetFileNameWithoutExtension(path);
            try
            {
                using var effect = new Effect(GraphicsDevice, File.ReadAllBytes(path));
                var techniques = string.Join(", ", effect.Techniques.Select(t => $"{t.Name}[{t.Passes.Count}]"));
                Console.WriteLine($"LOAD {name}: {effect.Parameters.Count} params; {techniques}");
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}");
            }
        }
        ExitCode = failed == 0 ? 0 : 1;
    }

    private void DrawAllPasses(string name)
    {
        using var effect = new Effect(GraphicsDevice, File.ReadAllBytes(Path.Combine(dir, name + ".fxb")));
        using var target = new RenderTarget2D(GraphicsDevice, Size, Size);
        VertexPositionNormalTexture[] quad =
        [
            new(new Vector3(-1, -1, 0), Vector3.UnitZ, new Vector2(0, 1)),
            new(new Vector3(-1, 1, 0), Vector3.UnitZ, new Vector2(0, 0)),
            new(new Vector3(1, -1, 0), Vector3.UnitZ, new Vector2(1, 1)),
            new(new Vector3(1, 1, 0), Vector3.UnitZ, new Vector2(1, 0)),
        ];

        var passes = 0;
        GraphicsDevice.SetRenderTarget(target);
        try
        {
            foreach (var technique in effect.Techniques)
            {
                effect.CurrentTechnique = technique;
                foreach (var pass in technique.Passes)
                {
                    GraphicsDevice.Clear(Color.Black);
                    pass.Apply();
                    GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2);
                    passes++;
                }
            }
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"pass {passes} ({effect.CurrentTechnique.Name}): {e.Message}", e);
        }
        finally
        {
            GraphicsDevice.SetRenderTarget(null);
        }

        var pixels = new Color[Size * Size];
        target.GetData(pixels);
        var lit = pixels.Count(p => p != Color.Black);
        Console.WriteLine($"DRAW {name}: {passes} passes ok, last pass touched {lit}/{pixels.Length} px");
    }
}
