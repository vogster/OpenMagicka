using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Engine.Text;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The interact prompt, as the original's notifier button (docs/engine/ARCHITECTURE.md §18): a strip 32 high of
/// <c>UI/HUD/NotifierButton</c> (with a button's glyph in it, as bright as the prompt says) or <c>…_Keyboard</c> (none), its
/// middle stretched to its text (Maiandra14), its caps kept; placed 42 px left of and 20 below its anchor (the mouse, or the
/// avatar for a pad).
/// </summary>
public sealed partial class GuiPass
{
    public const string PadPrompt = "UI/HUD/NotifierButton", KeyboardPrompt = "UI/HUD/NotifierButton_Keyboard";

    /// <summary>The pad's B and X buttons and the space bar in the Maiandra fonts.</summary>
    public const char PadBGlyph = (char)0x325, PadXGlyph = (char)0x326, SpaceBarGlyph = (char)0x32A;

    private static readonly VertexPositionTexture[] PromptStrip = Strip();

    private readonly GameEffect prompt;
    private readonly Texture2D padPrompt, keyboardPrompt;

    /// <summary>The strip: columns at 0, 32, 48, 64 (u 0, ½, ¾, 1), top and bottom; the shader moves those past 40 right by the text's width.</summary>
    public static VertexPositionTexture[] Strip()
    {
        float[] x = [0, 32, 48, 64], u = [0, 0.5f, 0.75f, 1];
        return [.. Enumerable.Range(0, 4).SelectMany(i => new[]
        {
            new VertexPositionTexture(new Vector3(x[i], 0, 0), new Vector2(u[i], 0)), new VertexPositionTexture(new Vector3(x[i], 32, 0), new Vector2(u[i], 1)),
        })];
    }

    /// <summary>Where the prompt's strip starts for an anchor in pixels.</summary>
    public static Vector2 PromptCorner(Vector2 anchor) => anchor + new Vector2(-42, 20);

    /// <summary>How far the strip's middle stretches for a text that wide: all of it with a pad's button, 34 less on the keyboard's.</summary>
    public static float PromptWidth(int textWidth, bool pad) => pad ? textWidth : textWidth - 34;

    private void DrawPrompt(GraphicsDevice device, PromptPicture picture, Matrix viewProjection, int width, int height)
    {
        if (!(picture.Alpha > 0) || picture.Mouse is null && picture.Avatar is null)
        {
            return;
        }
        var font = Fonts["Maiandra14"];
        var pad = picture.Button is not null;
        var corner = PromptCorner(picture.Mouse ?? Project(picture.Avatar!.Value, viewProjection, width, height));
        var parameters = new FnaEffectParameters(prompt.Effect);
        parameters.SetValue("ScreenSize", new Vector2(width, height));
        parameters.SetValue("Position", corner);
        parameters.SetValue("Width", PromptWidth(font.Metrics.Measure(picture.Text).Width, pad));
        parameters.SetValue("Scale", Vector2.One);
        parameters.SetValue("Color", new Vector4(1, 1, 1, picture.Alpha));
        parameters.SetValue("Texture", pad ? padPrompt : keyboardPrompt);
        foreach (var pass in prompt.Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, PromptStrip, 0, PromptStrip.Length - 2);
        }
        var colour = new Vector4(1, 1, 1, picture.Alpha);
        if (picture.Button is { } button)
        {
            var lit = new Vector4(picture.ButtonIntensity, picture.ButtonIntensity, picture.ButtonIntensity, picture.Alpha);
            Text.Draw(device, font, button.ToString(), lit, corner + new Vector2(16, 3), 1, 1, width, height, TextAlignment.Center);
        }
        Text.Draw(device, font, picture.Text, colour, corner + new Vector2(pad ? 34 : 8, 8), 1, int.MaxValue, width, height);
    }
}
