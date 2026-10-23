using System.Text;
using OpenMagicka.Content.Localization;

namespace OpenMagicka.Tests.Localization;

public class LocTableReaderTests
{
    internal static string Workbook(params string[] rows) =>
        $"""
        <?xml version="1.0" ?>
        <?mso-application progid="Excel.Sheet"?>
        <Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
          <Worksheet ss:Name="sheet">
            <Table>
              {string.Join("\n", rows)}
            </Table>
          </Worksheet>
        </Workbook>
        """;

    internal static string Row(params string[] cells) =>
        "<Row>" + string.Concat(cells.Select(c => $"<Cell><Data ss:Type=\"String\">{c}</Data></Cell>")) + "</Row>";

    internal static IReadOnlyList<LocEntry> Read(string xml) =>
        LocTableReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void RowsWithAHashKey_GiveKeyAndTextFromTheFirstTwoCells()
    {
        var entries = Read(Workbook(
            Row("ID", "eng", "Comments"),
            Row("#Item_Staff", "Staff", "ignored"),
            Row("#item_cup_d", "Restores health. #PA;None")));

        Assert.Equal([new("#Item_Staff", "Staff"), new("#item_cup_d", "Restores health. #PA;None")], entries);
    }

    [Fact]
    public void ShortRowsEmptyTextsAndKeysWithoutHash_AreSkipped()
    {
        var entries = Read(Workbook(
            Row("#only_key"),
            Row("#empty", ""),
            Row("", "no key"),
            Row("key", "no hash"),
            "<Row></Row>",
            Row("#kept", "yes")));

        Assert.Equal([new("#kept", "yes")], entries);
    }

    [Fact]
    public void CellsArePositional_TextKeepsInnerWhitespaceAndJoinsNestedMarkup()
    {
        var entries = Read(Workbook(
            "<Row><Cell><Data>#a</Data></Cell><Cell ss:Index=\"5\"><Data>  two\nlines </Data></Cell></Row>",
            "<Row><Cell><Data>#b</Data></Cell><Cell><ss:Data><Font>bold</Font> rest</ss:Data></Cell></Row>"));

        Assert.Equal([new("#a", "  two\nlines "), new("#b", "bold rest")], entries);
    }

    [Fact]
    public void ElementNames_AreMatchedIgnoringCase_AndOtherElementsAreIgnored()
    {
        var xml = """
            <WORKBOOK><Styles/><worksheet><Names/><table><Column/><ROW><Cell>#k</Cell><Cell>v</Cell></ROW></table></worksheet></WORKBOOK>
            """;

        Assert.Equal([new("#k", "v")], Read(xml));
    }

    [Fact]
    public void IndentationBetweenCells_IsNotAChildNode_ButCommentsAndPrefixedNamesAreTakenLiterally()
    {
        var entries = Read(Workbook(
            "<Row>\n  <Cell><Data>#indented</Data></Cell>\n  <Cell><Data>yes</Data></Cell>\n</Row>",
            "<Row><!--note--><Cell>#commented</Cell><Cell>skipped: the key is the comment</Cell></Row>",
            "<ss:Row><Cell>#prefixed</Cell><Cell>skipped: the name is ss:Row</Cell></ss:Row>"));

        Assert.Equal([new("#indented", "yes")], entries);
    }

    [Fact]
    public void SeveralWorksheets_AreReadInOrder()
    {
        var xml = Workbook(Row("#a", "1")).Replace("</Workbook>",
            "<Worksheet><Table>" + Row("#b", "2") + "</Table></Worksheet></Workbook>");

        Assert.Equal(["#a", "#b"], Read(xml).Select(e => e.Key));
    }

    [Theory]
    [InlineData("<Book><Row><Cell>#k</Cell><Cell>v</Cell></Row></Book>")]   // no Workbook
    [InlineData("<Workbook><Worksheet>")]                                    // not well-formed
    [InlineData("<!DOCTYPE Workbook [<!ENTITY e \"x\">]><Workbook/>")]      // DTDs are not processed
    [InlineData("")]
    public void InvalidFiles_AreInvalidData(string xml)
    {
        Assert.Throws<InvalidDataException>(() => Read(xml));
    }
}
