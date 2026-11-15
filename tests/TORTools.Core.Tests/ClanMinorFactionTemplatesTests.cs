using System.Xml.Linq;
using TORTools.Core.Models;
using Xunit;

namespace TORTools.Core.Tests;

/// <summary>
/// Pins the tagList shape used by the new tor_clans.json
/// `minor_faction_character_templates` field (container element
/// minor_faction_character_templates, item element template, name
/// attribute id). Covers tor_clans.xml's real minor-faction shape.
/// </summary>
public class ClanMinorFactionTemplatesTests
{
    private const string Container = "minor_faction_character_templates";
    private const string Item = "template";
    private const string Attr = "id";

    [Fact]
    public void GetTagList_ReadsNpcCharacterTemplatesIntoCsv()
    {
        var faction = XElement.Parse("""
<Faction id="tor_chaos_clan_1" name="Chaos" tier="5">
  <minor_faction_character_templates>
    <template id="NPCCharacter.tor_chaos_lord_0" />
    <template id="NPCCharacter.tor_chaos_lord_1" />
    <template id="NPCCharacter.tor_chaos_lord_2" />
  </minor_faction_character_templates>
</Faction>
""");
        var entry = new XmlEntry(faction);

        var csv = entry.GetTagList(Container, Item, Attr);

        Assert.Equal("NPCCharacter.tor_chaos_lord_0, NPCCharacter.tor_chaos_lord_1, NPCCharacter.tor_chaos_lord_2", csv);
    }

    [Fact]
    public void SetTagList_WritesContainerAndTemplateChildrenFromCsv()
    {
        var faction = XElement.Parse("""<Faction id="new_minor" name="N" tier="1" />""");
        var entry = new XmlEntry(faction);

        entry.SetTagList(
            "NPCCharacter.tor_minor_a, NPCCharacter.tor_minor_b",
            Container, Item, Attr);

        var container = faction.Element(Container);
        Assert.NotNull(container);
        var ids = container!.Elements(Item).Select(e => e.Attribute(Attr)?.Value).ToList();
        Assert.Equal(new[] { "NPCCharacter.tor_minor_a", "NPCCharacter.tor_minor_b" }, ids);
    }

    [Fact]
    public void SetTagList_EmptyValueDropsContainer()
    {
        var faction = XElement.Parse("""
<Faction id="x" name="N" tier="1">
  <minor_faction_character_templates>
    <template id="NPCCharacter.old" />
  </minor_faction_character_templates>
</Faction>
""");
        var entry = new XmlEntry(faction);

        entry.SetTagList("", Container, Item, Attr);

        Assert.Null(faction.Element(Container));
    }
}
