using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>What a scene post effect draws with: the scene as it stood before the stage (none when no effect reads it), its depth, the frame's globals.</summary>
internal readonly record struct ScenePost(Texture2D? Scene, Texture2D Depth, GlobalEffectValues Globals);

/// <summary>One of the scene's post effects (OM-67): drawn after the particles, in the order of its z, onto the scene.</summary>
internal interface IScenePostEffect
{
    /// <summary>Its place in the stage: lower first, as the original's (equal ones in the order they came).</summary>
    int ZIndex { get; }

    /// <summary>It reads the scene as it stood (a refraction), so the stage first copies it (and leaves the copy's states: an effect sets its own).</summary>
    bool ReadsScene { get; }

    void Draw(GraphicsDevice device, ScenePost post);
}

public sealed partial class DeferredRenderer
{
    /// <summary>The shields' place in the stage, as the original's.</summary>
    internal const int ShieldZ = 90;

    private readonly List<IScenePostEffect> postEffects = [];
    private readonly ShieldsPost shieldsPost = new();

    /// <summary>
    /// The scene's post effects (OM-67, docs/engine/ARCHITECTURE.md §3.2), as the original's DrawPost: the frame's
    /// effects in the order of their z, drawn onto the scene with its own depth and stencil kept; when one reads the
    /// scene, the scene is copied first and that copy is what they read (the original drew into its second buffer
    /// reading the first; ours the other way round, the same picture). Only the shields so far.
    /// </summary>
    private void DrawPost(GraphicsDevice device, DeferredFrame frame)
    {
        postEffects.Clear();
        if (spells is not null && frame.Spells is { Shields.Count: > 0 } shielded)
        {
            (shieldsPost.Spells, shieldsPost.Pictures) = (spells, shielded);
            postEffects.Add(shieldsPost);
        }
        if (postEffects.Count == 0)
        {
            return;
        }
        Order(postEffects);
        Texture2D? scene = null;
        if (postEffects.Exists(e => e.ReadsScene))
        {
            UnbindTextures(device);
            device.SetRenderTarget(copy);
            Blit(device, main!);
            UnbindTextures(device);
            device.SetRenderTarget(main);                                     // kept: its depth and stencil are the scene's
            scene = copy;
        }
        var post = new ScenePost(scene, depth!, frame.Globals);
        foreach (var effect in postEffects)
        {
            effect.Draw(device, post);
        }
        postEffects.Clear();
        (shieldsPost.Spells, shieldsPost.Pictures) = (null, null);            // the frame's pictures not kept past it
    }

    /// <summary>By z, equal ones kept in their order (a list's own sort is not stable).</summary>
    internal static void Order(List<IScenePostEffect> effects)
    {
        for (var i = 1; i < effects.Count; i++)
        {
            var moved = effects[i];
            var j = i - 1;
            for (; j >= 0 && effects[j].ZIndex > moved.ZIndex; j--)
            {
                effects[j + 1] = effects[j];
            }
            effects[j + 1] = moved;
        }
    }

    /// <summary>The frame's shields as one post effect (OM-76's drawing): additive, cut by the scene's depth; they read no scene.</summary>
    private sealed class ShieldsPost : IScenePostEffect
    {
        public SpellPass? Spells { get; set; }

        public SpellPictures? Pictures { get; set; }

        public int ZIndex => ShieldZ;

        public bool ReadsScene => false;

        public void Draw(GraphicsDevice device, ScenePost post)
        {
            if (Spells is { } spells && Pictures is { } pictures)
            {
                spells.DrawShields(device, pictures, post.Globals, post.Depth);
            }
        }
    }
}
