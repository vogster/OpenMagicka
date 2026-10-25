using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Game;

/// <summary>Saving what the window shows (the viewer's and the game's --screenshot). Render thread only.</summary>
public static class Screenshots
{
    /// <summary>Saves the back buffer as an opaque PNG (textures with alpha leave alpha below 1 in it); false, said on stderr, when it cannot.</summary>
    public static bool Save(GraphicsDevice device, string path)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(path);
        var width = device.PresentationParameters.BackBufferWidth;
        var height = device.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        device.GetBackBufferData(pixels);
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i].A = 255;
        }
        using var texture = new Texture2D(device, width, height);
        texture.SetData(pixels);
        try
        {
            using var file = File.Create(path);
            texture.SaveAsPng(file, width, height);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"OpenMagicka: cannot save {path}: {e.Message}");
            return false;
        }
    }
}
