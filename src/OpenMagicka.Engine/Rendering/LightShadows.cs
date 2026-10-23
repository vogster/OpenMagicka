using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The shadow maps of the point and spot lights in view that cast shadows, drawn each frame before
/// the lights (<see cref="LightShadowViews"/>). The maps are R32F with a depth buffer, kept in pools
/// by size, reused from frame to frame and released when no longer needed. Render thread only.
/// </summary>
internal sealed class LightShadows : IDisposable
{
    /// <summary>The maps are cleared far beyond any caster, so where nothing was drawn is lit.</summary>
    private static readonly Vector4 NoCaster = new(1e6f);

    private readonly List<RenderTargetCube> cubes = [];
    private readonly List<RenderTarget2D> flats = [];
    private readonly Dictionary<PointLighting, TextureCube> pointMaps = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SpotLighting, (Texture2D Map, Matrix View)> spotMaps = new(ReferenceEqualityComparer.Instance);

    /// <summary>A point light's cube map this frame, or null when it has none.</summary>
    public TextureCube? For(PointLighting light) => pointMaps.GetValueOrDefault(light);

    /// <summary>A spot light's map and the view it was drawn with this frame, or null when it has none.</summary>
    public (Texture2D Map, Matrix View)? For(SpotLighting light) => spotMaps.TryGetValue(light, out var map) ? map : null;

    /// <summary>
    /// Draws the maps of the shadow-casting lights whose reach is in <paramref name="view"/>;
    /// <paramref name="drawCasters"/> draws the opaque geometry with a view-projection and a culling into the
    /// bound target. As in the original, the cube faces cull clockwise triangles (their view is mirrored,
    /// so these are the ones facing away from the light), the spot lights counter-clockwise ones: a
    /// light set in a wall is not shut in by the wall's far side.
    /// </summary>
    public void Draw(GraphicsDevice device, GpuLevel level, BoundingFrustum view, Action<Matrix, RasterizerState> drawCasters)
    {
        pointMaps.Clear();
        spotMaps.Clear();
        var usedCubes = 0;
        var usedFlats = 0;
        foreach (var light in level.PointLights.Where(l => l.ShadowMapSize > 0 && view.Intersects(new BoundingSphere(l.Position, l.Radius))))
        {
            var map = Take(cubes, ref usedCubes, light.ShadowMapSize, size => new RenderTargetCube(device, size, false, SurfaceFormat.Single, DepthFormat.Depth24));
            var faces = LightShadowViews.PointFaces(light.Position, light.Radius);
            for (var face = 0; face < faces.Count; face++)
            {
                device.SetRenderTarget(map, (CubeMapFace)face);
                device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, NoCaster, 1, 0);
                drawCasters(faces[face], RasterizerState.CullClockwise);
            }
            pointMaps[light] = map;
        }
        foreach (var light in level.SpotLights.Where(l => l.ShadowMapSize > 0 && view.Intersects(new BoundingSphere(l.Position, l.Range))))
        {
            var map = Take(flats, ref usedFlats, light.ShadowMapSize, size => new RenderTarget2D(device, size, size, false, SurfaceFormat.Single, DepthFormat.Depth24));
            var lightView = LightShadowViews.Spot(light);
            device.SetRenderTarget(map);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, NoCaster, 1, 0);
            drawCasters(lightView, RasterizerState.CullCounterClockwise);
            spotMaps[light] = (map, lightView);
        }
        device.SetRenderTarget(null);
        Trim(cubes, usedCubes);
        Trim(flats, usedFlats);
    }

    /// <summary>Releases the maps this frame did not use, so the pool follows the lights in view.</summary>
    private static void Trim<T>(List<T> pool, int used)
        where T : Texture
    {
        for (var i = pool.Count - 1; i >= used; i--)
        {
            pool[i].Dispose();
            pool.RemoveAt(i);
        }
    }

    /// <summary>The next unused map of the size, created when the pool has none.</summary>
    private static T Take<T>(List<T> pool, ref int used, int size, Func<int, T> create)
        where T : Texture
    {
        var at = pool.FindIndex(used, t => Size(t) == size);
        if (at < 0)
        {
            pool.Add(create(size));
            at = pool.Count - 1;
        }
        (pool[used], pool[at]) = (pool[at], pool[used]);
        return pool[used++];
    }

    private static int Size(Texture texture) => texture switch
    {
        TextureCube cube => cube.Size,
        Texture2D flat => flat.Width,
        _ => 0,
    };

    public void Dispose()
    {
        foreach (var map in cubes.Cast<Texture>().Concat(flats))
        {
            map.Dispose();
        }
    }
}
