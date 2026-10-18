using System.IO;
using System.Xml.Linq;
using TORTools.Core.Services;
using TORTools.Core.Models;

namespace TORTools.Core.Tests;

public class XmlOpenCloseRoundTripTests
{
    [Fact]
    public void ExplicitOpenCloseWithWhitespaceBody_PreservesClosingTag_MultiLine()
    {
        // Covers #19: the last entry in tor_clans.xml is authored as
        //   <Faction attrs>
        //       </Faction>
        // which XDocument parses as an XElement whose only node is a whitespace
        // text node. The writer previously collapsed this to <Faction attrs />
        // and dropped the closing </Faction> tag.
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<Factions>\n" +
                "    <Faction id=\"explicit_empty\"\n" +
                "             tier=\"1\" >\n" +
                "\t\t\t </Faction>\n" +
                "</Factions>\n");

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            svc.Save(doc, tempPath, compactFormat: false);

            File.ReadAllText(tempPath).Should().Contain("</Faction>");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void ExplicitOpenCloseWithWhitespaceBody_PreservesClosingTag_Compact()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<Items>\n" +
                "    <Item id=\"a\"> </Item>\n" +
                "</Items>\n");

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            svc.Save(doc, tempPath, compactFormat: true);

            File.ReadAllText(tempPath).Should().Contain("</Item>");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void TrulySelfClosingElement_StaysSelfClosing()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<Items>\n" +
                "    <Item id=\"a\" />\n" +
                "</Items>\n");

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            svc.Save(doc, tempPath, compactFormat: true);

            var output = File.ReadAllText(tempPath);
            output.Should().Contain("/>");
            output.Should().NotContain("</Item>");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
