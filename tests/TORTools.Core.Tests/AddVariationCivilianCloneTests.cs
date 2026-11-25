using System.IO;
using System.Linq;
using System.Xml.Linq;
using TORTools.Core.Commands;
using TORTools.Core.Services;

namespace TORTools.Core.Tests;

/// <summary>
/// Civilian-variant regeneration on save.
///
/// Vanilla's SandBox/ModuleData/sandbox_equipment_sets.xml shape is: N combat
/// &lt;EquipmentSet&gt; siblings followed by exactly one trailing
/// &lt;EquipmentSet equipmentType="Civilian"&gt; sibling, regardless of how many
/// combat variants the roster has. These tests pin that invariant plus
/// idempotency when the file already carries legacy civilian="true" or stray
/// equipmentType="Civilian" entries (the exact damage seen in TOR_Core's
/// tor_equipment_sets.xml before the fix).
///
/// The real generator lives in FileSaverService (App layer); this test file
/// inlines the same logic to exercise it without pulling in Avalonia.
/// </summary>
public class AddVariationCivilianCloneTests
{
    [Fact]
    public void AddVariation_ThenGenerateCivilianClones_EmitsSingleCivilianSibling()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<EquipmentRosters>\n" +
                "  <EquipmentRoster id=\"test_roster\" culture=\"Culture.empire\">\n" +
                "    <EquipmentSet>\n" +
                "      <Equipment slot=\"Item0\" id=\"Item.sword\" />\n" +
                "    </EquipmentSet>\n" +
                "    <EquipmentSet civilian=\"true\">\n" +
                "      <Equipment slot=\"Item0\" id=\"Item.sword\" />\n" +
                "    </EquipmentSet>\n" +
                "  </EquipmentRoster>\n" +
                "</EquipmentRosters>\n");

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            var roster = svc.GetEntries(doc).First();

            var cmd = new AddVariationCommand(doc, roster, "EquipmentSet");
            cmd.Execute();
            cmd.AddedVariation.Should().NotBeNull();
            cmd.AddedVariation!.OriginalElement.Add(
                new XElement("Equipment",
                    new XAttribute("slot", "Head"),
                    new XAttribute("id", "Item.helmet")));

            RegenerateCivilianClones(roster.OriginalElement, "EquipmentSet");

            var sets = roster.OriginalElement.Elements("EquipmentSet").ToList();
            var combat = sets.Where(e => !IsCivilian(e)).ToList();
            var civilian = sets.Where(IsCivilian).ToList();
            combat.Should().HaveCount(2, because: "both combat variations survive");
            civilian.Should().HaveCount(1, because: "vanilla roster has exactly one civilian set regardless of combat count");
            civilian[0].Attribute("equipmentType")!.Value.Should().Be("Civilian");
            civilian[0].Attribute("civilian").Should().BeNull(because: "the legacy marker is stripped, not re-emitted");
            sets.Last().Should().BeSameAs(civilian[0], because: "civilian goes at the end, after all combat sets");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void RegenerateCivilianClones_StripsQuadrupledDamage()
    {
        // The exact damage pattern in TOR_Core/ModuleData/tor_equipment_sets.xml
        // pre-fix: every logical loadout has 4 identical siblings (combat +
        // civilian="true" + equipmentType="Civilian" + both). Regenerating must
        // collapse to combat + one trailing equipmentType="Civilian".
        var roster = new XElement("EquipmentRoster",
            new XAttribute("id", "damaged"),
            new XElement("EquipmentSet",
                new XElement("Equipment", new XAttribute("slot", "Item0"), new XAttribute("id", "Item.sword"))),
            new XElement("EquipmentSet",
                new XAttribute("civilian", "true"),
                new XElement("Equipment", new XAttribute("slot", "Item0"), new XAttribute("id", "Item.sword"))),
            new XElement("EquipmentSet",
                new XAttribute("equipmentType", "Civilian"),
                new XElement("Equipment", new XAttribute("slot", "Item0"), new XAttribute("id", "Item.sword"))),
            new XElement("EquipmentSet",
                new XAttribute("equipmentType", "Civilian"),
                new XAttribute("civilian", "true"),
                new XElement("Equipment", new XAttribute("slot", "Item0"), new XAttribute("id", "Item.sword"))));

        RegenerateCivilianClones(roster, "EquipmentSet");

        var sets = roster.Elements("EquipmentSet").ToList();
        sets.Should().HaveCount(2);
        sets[0].Attribute("equipmentType").Should().BeNull();
        sets[0].Attribute("civilian").Should().BeNull();
        sets[1].Attribute("equipmentType")!.Value.Should().Be("Civilian");
        sets[1].Attribute("civilian").Should().BeNull();
    }

    [Fact]
    public void RegenerateCivilianClones_IsIdempotent()
    {
        var roster = new XElement("EquipmentRoster",
            new XElement("EquipmentSet",
                new XElement("Equipment", new XAttribute("slot", "Item0"), new XAttribute("id", "Item.sword"))));

        RegenerateCivilianClones(roster, "EquipmentSet");
        var firstPass = roster.Elements("EquipmentSet").Count();
        RegenerateCivilianClones(roster, "EquipmentSet");
        var secondPass = roster.Elements("EquipmentSet").Count();

        firstPass.Should().Be(2);
        secondPass.Should().Be(2, because: "repeated saves must not accumulate civilian siblings");
    }

    [Fact]
    public void AddRoster_WithEmptyVariation_ThenGenerateCivilianClones_EmitsCivilianSibling()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<EquipmentRosters>\n" +
                "  <EquipmentRoster id=\"existing\" culture=\"Culture.empire\">\n" +
                "    <EquipmentSet>\n" +
                "      <Equipment slot=\"Item0\" id=\"Item.sword\" />\n" +
                "    </EquipmentSet>\n" +
                "  </EquipmentRoster>\n" +
                "</EquipmentRosters>\n");

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            var entries = new System.Collections.ObjectModel.ObservableCollection<TORTools.Core.Models.XmlEntry>(svc.GetEntries(doc));

            var addRoster = new AddRowCommand(doc, entries);
            addRoster.Execute();

            var newRoster = entries.Last();
            var addVariation = new AddVariationCommand(doc, newRoster, "EquipmentSet");
            addVariation.Execute();
            addVariation.AddedVariation!.OriginalElement.Add(
                new XElement("Equipment",
                    new XAttribute("slot", "Head"),
                    new XAttribute("id", "Item.helmet")));

            foreach (var roster in entries)
            {
                RegenerateCivilianClones(roster.OriginalElement, "EquipmentSet");
            }

            svc.Save(doc, tempPath, compactFormat: false);

            var output = File.ReadAllText(tempPath);
            output.Should().Contain("<EquipmentSet equipmentType=\"Civilian\"",
                because: "a brand-new roster must also produce a civilian clone on save");
            output.Should().NotContain("civilian=\"true\"",
                because: "the legacy marker must not be emitted");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void SetEquipmentRoster_OnNewLord_CreatesCombatSetFromScratch()
    {
        // Before the fix, SetNestedValue with "EquipmentSet[1]" could not create
        // the first EquipmentSet because indexed paths require it to exist.
        var elem = new XElement("NPCCharacter", new XAttribute("id", "test_lord"));
        var entry = new TORTools.Core.Models.XmlEntry(elem);
        entry.SetNestedValue("Equipments/EquipmentSet[1]/@id", "tor_test_template");

        var combat = elem.Element("Equipments")?.Elements("EquipmentSet")
            .FirstOrDefault(e => e.Attribute("equipmentType")?.Value != "Civilian");
        combat.Should().NotBeNull();
        combat!.Attribute("id")!.Value.Should().Be("tor_test_template");
    }

    [Fact]
    public void SetEquipmentRosterWithCivilianPair_CreatesBothSets()
    {
        var elem = new XElement("NPCCharacter", new XAttribute("id", "test_lord"));
        var entry = new TORTools.Core.Models.XmlEntry(elem);
        entry.SetEquipmentRosterWithCivilianPair("tor_test_template");

        var sets = elem.Element("Equipments")!.Elements("EquipmentSet").ToList();
        sets.Should().HaveCount(2);
        sets[0].Attribute("equipmentType")?.Value.Should().NotBe("Civilian");
        sets[0].Attribute("id")!.Value.Should().Be("tor_test_template");
        sets[1].Attribute("equipmentType")!.Value.Should().Be("Civilian");
        sets[1].Attribute("id")!.Value.Should().Be("tor_test_template");
    }

    [Fact]
    public void SetEquipmentRosterWithCivilianPair_UpdatesExistingBothSets()
    {
        var elem = new XElement("NPCCharacter",
            new XAttribute("id", "test_lord"),
            new XElement("Equipments",
                new XElement("EquipmentSet", new XAttribute("id", "old_id")),
                new XElement("EquipmentSet",
                    new XAttribute("id", "old_id"),
                    new XAttribute("equipmentType", "Civilian"))));
        var entry = new TORTools.Core.Models.XmlEntry(elem);
        entry.SetEquipmentRosterWithCivilianPair("new_id");

        var sets = elem.Element("Equipments")!.Elements("EquipmentSet").ToList();
        sets.Should().HaveCount(2);
        sets.All(s => s.Attribute("id")!.Value == "new_id").Should().BeTrue();
    }

    [Fact]
    public void SetEquipmentRosterWithCivilianPair_PreservesDivergedCivilian()
    {
        // Prototype requirement: inline edits of the combat id must leave an
        // explicitly-diverged civilian id alone. The dedicated picker dialog
        // owns changing the civilian value.
        var elem = new XElement("NPCCharacter",
            new XAttribute("id", "test_lord"),
            new XElement("Equipments",
                new XElement("EquipmentSet", new XAttribute("id", "combat_old")),
                new XElement("EquipmentSet",
                    new XAttribute("id", "civilian_custom"),
                    new XAttribute("equipmentType", "Civilian"))));
        var entry = new TORTools.Core.Models.XmlEntry(elem);
        entry.SetEquipmentRosterWithCivilianPair("combat_new");

        var combat = elem.Element("Equipments")!.Elements("EquipmentSet")
            .First(e => e.Attribute("equipmentType")?.Value != "Civilian");
        var civilian = elem.Element("Equipments")!.Elements("EquipmentSet")
            .First(e => e.Attribute("equipmentType")?.Value == "Civilian");
        combat.Attribute("id")!.Value.Should().Be("combat_new");
        civilian.Attribute("id")!.Value.Should().Be("civilian_custom");
    }

    [Fact]
    public void SetEquipmentRosterPair_WritesDivergentIds()
    {
        var elem = new XElement("NPCCharacter", new XAttribute("id", "test_lord"));
        var entry = new TORTools.Core.Models.XmlEntry(elem);
        entry.SetEquipmentRosterPair("combat_a", "civilian_b");

        var sets = elem.Element("Equipments")!.Elements("EquipmentSet").ToList();
        sets.Should().HaveCount(2);
        sets.First(s => s.Attribute("equipmentType")?.Value != "Civilian")
            .Attribute("id")!.Value.Should().Be("combat_a");
        sets.First(s => s.Attribute("equipmentType")?.Value == "Civilian")
            .Attribute("id")!.Value.Should().Be("civilian_b");
    }

    [Fact]
    public void SetEquipmentRosterPair_NullCivilian_DropsCivilianElement()
    {
        var elem = new XElement("NPCCharacter",
            new XAttribute("id", "test_lord"),
            new XElement("Equipments",
                new XElement("EquipmentSet", new XAttribute("id", "combat")),
                new XElement("EquipmentSet",
                    new XAttribute("id", "civilian"),
                    new XAttribute("equipmentType", "Civilian"))));
        var entry = new TORTools.Core.Models.XmlEntry(elem);
        entry.SetEquipmentRosterPair("combat", null);

        var sets = elem.Element("Equipments")!.Elements("EquipmentSet").ToList();
        sets.Should().HaveCount(1);
        sets[0].Attribute("equipmentType").Should().BeNull();
    }

    [Fact]
    public void GetCivilianEquipmentId_ReturnsCorrectId()
    {
        var elem = new XElement("NPCCharacter",
            new XElement("Equipments",
                new XElement("EquipmentSet", new XAttribute("id", "combat_only")),
                new XElement("EquipmentSet",
                    new XAttribute("id", "civ_xyz"),
                    new XAttribute("equipmentType", "Civilian"))));
        var entry = new TORTools.Core.Models.XmlEntry(elem);
        entry.GetCivilianEquipmentId().Should().Be("civ_xyz");
    }

    [Fact]
    public void SetEquipmentRosterWithCivilianPair_WithNull_RemovesBoth()
    {
        var elem = new XElement("NPCCharacter",
            new XAttribute("id", "test_lord"),
            new XElement("Equipments",
                new XElement("EquipmentSet", new XAttribute("id", "foo")),
                new XElement("EquipmentSet",
                    new XAttribute("id", "foo"),
                    new XAttribute("equipmentType", "Civilian"))));
        var entry = new TORTools.Core.Models.XmlEntry(elem);
        entry.SetEquipmentRosterWithCivilianPair(null);

        elem.Element("Equipments").Should().BeNull(because: "clearing the roster id should drop the whole Equipments block");
    }

    // Inline copy of FileSaverService.GenerateCivilianClones.
    private static void RegenerateCivilianClones(XElement rosterElement, string variationElementName)
    {
        var civilianElements = rosterElement.Elements(variationElementName)
            .Where(IsCivilian)
            .ToList();
        foreach (var c in civilianElements) c.Remove();

        var combatElements = rosterElement.Elements(variationElementName).ToList();
        if (combatElements.Count == 0) return;

        var clone = new XElement(combatElements[0]);
        clone.Attribute("civilian")?.Remove();
        clone.SetAttributeValue("equipmentType", "Civilian");
        combatElements[^1].AddAfterSelf(clone);
    }

    private static bool IsCivilian(XElement e)
    {
        var civ = e.Attribute("civilian")?.Value;
        if (!string.IsNullOrEmpty(civ) && civ.Equals("true", System.StringComparison.OrdinalIgnoreCase))
            return true;
        var type = e.Attribute("equipmentType")?.Value;
        return !string.IsNullOrEmpty(type) && type.Equals("Civilian", System.StringComparison.OrdinalIgnoreCase);
    }
}
