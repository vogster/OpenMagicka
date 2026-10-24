namespace OpenMagicka.Engine.Characters;

/// <summary>XNA maths types as System.Numerics ones (both use row vectors, so fields map one to one).</summary>
internal static class XnaConversions
{
    public static System.Numerics.Matrix4x4 Matrix(Microsoft.Xna.Framework.Matrix m) => new(
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);
}
