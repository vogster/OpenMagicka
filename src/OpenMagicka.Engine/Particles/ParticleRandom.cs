using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Particles;

/// <summary>
/// The particles' random numbers, as the original draws them: a table of 65536 floats in [0, 1)
/// and then 65536 bytes in [0, 100), both from <c>new Random(0)</c> (the seeded generator gives the
/// same sequence on .NET Framework and .NET). Each emitter walks the table from its own offset,
/// which wraps at the end (docs/engine/ARCHITECTURE.md §5.3).
/// </summary>
public static class ParticleRandom
{
    public const int Size = 65536;

    private static readonly float[] Floats;
    private static readonly byte[] Percents;

    static ParticleRandom()
    {
        var random = new Random(0);
        Floats = new float[Size];
        Percents = new byte[Size];
        for (var i = 0; i < Size; i++)
        {
            Floats[i] = (float)random.NextDouble();
        }
        for (var i = 0; i < Size; i++)
        {
            Percents[i] = (byte)random.Next(100);
        }
    }

    /// <summary>The float at a place in the table, without moving.</summary>
    public static float Peek(ushort at) => Floats[at];

    public static float Next(ref ushort at) => Floats[at++];

    public static float Range(ref ushort at, float min, float max) => Floats[at++] * (max - min) + min;

    /// <summary>A value between the bounds, the random number raised to the distribution first (above 1 favours min).</summary>
    public static float Range(ref ushort at, float min, float max, float distribution) =>
        MathF.Pow(Floats[at++], distribution) * (max - min) + min;

    /// <summary>-1 for about <paramref name="percentNegative"/> in 100 draws, otherwise 1.</summary>
    public static int Sign(ref ushort at, int percentNegative) => Percents[at++] >= percentNegative ? 1 : -1;

    /// <summary>
    /// A direction in a cone along the emitter's Z axis: z = r^distribution × (2 × angle / π) − 1
    /// and a random turn around the axis, then turned by <paramref name="orientation"/>.
    /// </summary>
    public static Vector3 Cone(ref ushort at, Matrix orientation, float angle, float distribution)
    {
        var z = MathF.Pow(Floats[at++], distribution) * (angle / MathF.PI) * 2 - 1;
        var turn = Floats[at++] * MathF.Tau;
        var radius = MathF.Sqrt(1 - z * z);
        return Vector3.TransformNormal(new Vector3(radius * MathF.Cos(turn), radius * MathF.Sin(turn), z), orientation);
    }

    /// <summary>
    /// A direction in an arc ahead of the emitter (-Z): a horizontal angle within ±<paramref name="horizontal"/>
    /// and an elevation between the vertical bounds, each skewed by its distribution towards the
    /// middle, then turned by <paramref name="orientation"/>.
    /// </summary>
    public static Vector3 Arc(ref ushort at, Matrix orientation, float horizontal, float horizontalDistribution,
        float verticalMin, float verticalMax, float verticalDistribution)
    {
        var h = Floats[at++] * 2 - 1;
        h = MathF.Pow(MathF.Abs(h), horizontalDistribution) * MathF.Sign(h) * horizontal;
        var v = Floats[at++] * 2 - 1;
        v = (MathF.Pow(MathF.Abs(v), verticalDistribution) * MathF.Sign(v) * (verticalMax - verticalMin) + (verticalMin + verticalMax)) * 0.5f;
        var level = MathF.Cos(v);
        return Vector3.TransformNormal(new Vector3(MathF.Sin(h) * level, MathF.Sin(v), -MathF.Cos(h) * level), orientation);
    }
}
