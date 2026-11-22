using TORTools.Core.SpritePack.Atlas;

namespace TORTools.Core.Tests.SpritePack;

public class ConfigParserTests
{
    [Fact]
    public void Parses_SpriteTest_Config_xml()
    {
        var xml = """
            <Config>
                <SpriteCategory Name="ui_spritetest">
                    <AlwaysLoad/>
                </SpriteCategory>
            </Config>
            """;
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);
        var configs = ConfigParser.Parse(doc);

        configs.Should().HaveCount(1);
        var cat = configs["ui_spritetest"];
        cat.AlwaysLoad.Should().BeTrue();
        cat.Compress.Should().BeFalse();
        cat.HasEdge.Should().BeFalse();
    }

    [Fact]
    public void Compress_and_EdgeSize_both_set()
    {
        var xml = """
            <Config>
                <SpriteCategory Name="ui_icons">
                    <Compress/>
                    <EdgeSize Value="2"/>
                </SpriteCategory>
            </Config>
            """;
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);
        var cat = ConfigParser.Parse(doc)["ui_icons"];
        cat.Compress.Should().BeTrue();
        cat.HasEdge.Should().BeTrue();
        cat.EdgeSize.Should().Be(2);
    }

    [Fact]
    public void Duplicate_category_names_throw()
    {
        var xml = """
            <Config>
                <SpriteCategory Name="dup"><AlwaysLoad/></SpriteCategory>
                <SpriteCategory Name="dup"><Compress/></SpriteCategory>
            </Config>
            """;
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);
        var act = () => ConfigParser.Parse(doc);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("duplicate category 'dup'");
    }

    [Fact]
    public void Missing_Name_attribute_throws()
    {
        var xml = "<Config><SpriteCategory><AlwaysLoad/></SpriteCategory></Config>";
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);
        var act = () => ConfigParser.Parse(doc);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*without Name attribute*");
    }

    [Fact]
    public void Negative_EdgeSize_throws()
    {
        var xml = """
            <Config>
                <SpriteCategory Name="x"><EdgeSize Value="-1"/></SpriteCategory>
            </Config>
            """;
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);
        var act = () => ConfigParser.Parse(doc);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*negative EdgeSize*");
    }
}
