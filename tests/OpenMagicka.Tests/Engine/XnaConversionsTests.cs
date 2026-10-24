using OpenMagicka.Engine.Characters;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;

namespace OpenMagicka.Tests.Engine;

public class XnaConversionsTests
{
    [Fact]
    public void AMatrix_KeepsEveryFieldInPlace()
    {
        var xna = new XnaMatrix(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);

        var m = XnaConversions.Matrix(xna);

        Assert.Equal((1f, 2f, 5f, 13f, 16f), (m.M11, m.M12, m.M21, m.M41, m.M44));
        Assert.Equal(new System.Numerics.Vector3(13, 14, 15), m.Translation);
    }
}
