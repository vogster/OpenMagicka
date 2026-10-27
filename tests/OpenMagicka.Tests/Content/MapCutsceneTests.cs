using System.Numerics;
using System.Text;
using OpenMagicka.Content;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Tests.Content;

/// <summary>The world map's cutscenes (OM-84): <c>Levels/Cutscenes.xml</c> read, the camera eased from key to key.</summary>
public class MapCutsceneTests
{
    private static IReadOnlyDictionary<int, MapCutscene> Read(string xml) => MapCutsceneReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void ACutscene_IsEveryChapterItsIdLists_ItsKeysSorted_ItsCueInItsBank()
    {
        var read = Read("""
            <Cutscenes>
              <Cutscene id="2, 5"><camera><zoom time="8">0.5</zoom><zoom>1</zoom><position time="4">10,20</position></camera>
                <Dialog subtitles="#Cutscene_03">waveBank/CutS_cut03</Dialog></Cutscene>
              <cutscene id="7"><CAMERA><Zoom time="1">2</Zoom><Position time="1">0,0</Position></CAMERA><dialog>cuts_x</dialog></cutscene>
            </Cutscenes>
            """);

        Assert.Equal([2, 5, 7], read.Keys.Order());
        var two = read[2];
        Assert.Same(two, read[5]);
        Assert.Equal([0f, 8f], two.Zoom.Select(k => k.Time));
        Assert.Equal(8, two.Duration);                                          // the later of the two tracks' last keys
        Assert.Equal(("#cutscene_03", SoundBanks.WaveBank, "cuts_cut03"), (two.Subtitles, two.Bank, two.Cue));
        Assert.Equal((SoundBanks.WaveBank, "cuts_x", (string?)null), (read[7].Bank, read[7].Cue, read[7].Subtitles));
    }

    [Fact]
    public void TheCamera_EasesFromKeyToKey_HoldingTheFirstBeforeAndTheLastAfter()
    {
        var cutscene = Read("""
            <Cutscenes><Cutscene id="6"><camera>
              <zoom time="0">1.1</zoom><zoom time="4">0.75</zoom><zoom time="9">1.05</zoom>
              <position time="0">1150,370</position><position time="9">1085,1655</position>
            </camera></Cutscene></Cutscenes>
            """)[6];

        Assert.Equal(MapCutscene.SmoothStep(1.1f, 0.75f, 0.5f), cutscene.Camera(2).Zoom, 4);
        Assert.Equal(0.925f, cutscene.Camera(2).Zoom, 3);
        Assert.Equal((new Vector2(1085, 1655), 1.05f), cutscene.Camera(20));
        Assert.Equal((new Vector2(1150, 370), 1.1f), cutscene.Camera(-1));
        Assert.Equal(0.75f, cutscene.Camera(4).Zoom, 4);                        // a middle key: the ease stops there an instant
    }

    [Theory]
    [InlineData("<Cutscenes><Cutscene id=\"1\"><camera><position>1</position></camera></Cutscene></Cutscenes>")]
    [InlineData("<Cutscenes><Cutscene id=\"1\"><Dialog>a/b/c</Dialog></Cutscene></Cutscenes>")]
    [InlineData("<Nothing/>")]
    public void AMalformedFile_IsAnError(string xml) => Assert.Throws<InvalidDataException>(() => Read(xml));

    [GameContentFact]
    public void TheGamesCutscenes_AreTheChaptersAfterTheFirst_WithTheirDurations()
    {
        var files = new ContentFileSystem(GameContent.ContentDirectory!);
        var read = MapCutsceneReader.Read(new MemoryStream(files.ReadAllBytes(MapCutsceneReader.Path)));

        Assert.Equal(Enumerable.Range(0, 12), read.Keys.Order());
        Assert.Equal([0f, 8, 8, 8, 8, 6, 9, 8, 8, 7, 11, 8], Enumerable.Range(0, 12).Select(i => read[i].Duration));
        Assert.Equal("cuts_cut02", read[1].Cue);
        Assert.Equal("#cutscene_12", read[11].Subtitles);
    }
}
