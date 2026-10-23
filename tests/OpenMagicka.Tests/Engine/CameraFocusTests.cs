using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Cameras;

namespace OpenMagicka.Tests.Engine;

public class CameraFocusTests
{
    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Fact]
    public void TheCentre_IsTheMeanPosition_AtTheMeanFeetHeightOfThoseOnTheGround()
    {
        CameraSubject[] players = [new(new Vector3(0, 5, 0), Feet: 4), new(new Vector3(10, 9, 4), Feet: 2), new(new Vector3(2, 40, 2), Feet: null)];

        Near(new Vector3(4, 3, 2), CameraFocus.Centre(players)!.Value);
        Assert.Null(CameraFocus.Centre([]));
    }

    [Fact]
    public void TheCentre_KeepsTheMeanHeight_WhenNobodyStandsOrTheFeetAddUpToZero()
    {
        Near(new Vector3(0, 7, 0), CameraFocus.Centre([new(new Vector3(0, 7, 0), null)])!.Value);
        Near(new Vector3(0, 6, 0), CameraFocus.Centre([new(new Vector3(0, 4, 0), 1), new(new Vector3(0, 8, 0), -1)])!.Value);
    }

    [Fact]
    public void ThePull_WeighsEachEnemyByItsSquaredDistance_AndAverages()
    {
        var ground = new Vector3(1, 0, 1);
        var near = new Vector3(4, 0, 1);    // 3 away: 3 × 0.99775^9
        var far = new Vector3(1, 0, -19);   // 20 away: −20 × 0.99775^400

        Near(new Vector3(3 * MathF.Pow(0.99775f, 9), 0, 0), CameraFocus.Pull(ground, [near]));
        Near(new Vector3(3 * MathF.Pow(0.99775f, 9), 0, -20 * MathF.Pow(0.99775f, 400)) / 2, CameraFocus.Pull(ground, [near, far]));
        Assert.Equal(Vector3.Zero, CameraFocus.Pull(ground, []));
    }

    [Fact]
    public void ThePlayers_AreTheLocalOnesFirst_ThenTheNetworkOnes_PlusThePull()
    {
        CameraSubject[] local = [new(new Vector3(2, 0, 0), null)];
        CameraSubject[] network = [new(new Vector3(-2, 0, 0), null)];
        Vector3[] enemies = [new(0, 0, 3)];
        var pull = CameraFocus.Pull(Vector3.Zero, enemies);

        Near(new Vector3(2, 0, 0) + pull, CameraFocus.Players(local, network, Vector3.Zero, enemies)!.Value);
        Near(new Vector3(-2, 0, 0) + pull, CameraFocus.Players([], network, Vector3.Zero, enemies)!.Value);
        Assert.Null(CameraFocus.Players([], [], Vector3.Zero, enemies));
    }
}
