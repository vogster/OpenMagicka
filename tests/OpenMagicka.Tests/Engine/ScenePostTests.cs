using Microsoft.Xna.Framework.Graphics;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Tests.Engine;

/// <summary>The scene's post effects' stage (OM-67 phase 4): their order.</summary>
public class ScenePostTests
{
    private sealed record Effect(int ZIndex, string Name) : IScenePostEffect
    {
        public bool ReadsScene => false;

        public void Draw(GraphicsDevice device, ScenePost post)
        {
        }
    }

    [Fact]
    public void TheEffects_GoByTheirZ_EqualOnesInTheOrderTheyCame()
    {
        List<IScenePostEffect> effects = [new Effect(10000, "blizzard"), new Effect(90, "shield a"), new Effect(0, "zap"), new Effect(90, "shield b"),
            new Effect(89, "wave")];

        DeferredRenderer.Order(effects);

        Assert.Equal(["zap", "wave", "shield a", "shield b", "blizzard"], effects.Cast<Effect>().Select(e => e.Name));
    }
}
