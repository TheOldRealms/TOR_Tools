using System.IO;
using System.Linq;
using System.Xml.Linq;
using TORTools.Core.Commands;
using TORTools.Core.Services;

namespace TORTools.Core.Tests;

/// <summary>
/// Reproduces #14: when the user adds a new EquipmentSet via AddVariationCommand,
/// the save path's civilian-clone generator should also emit a matching
/// &lt;EquipmentSet civilian="true"&gt; sibling.
///
/// The civilian clone is produced by FileSaverService.GenerateCivilianClones which
/// lives in the App layer; this test exercises the Core-level primitive directly
/// so it can be driven without an Avalonia window.
/// </summary>
public class AddVariationCivilianCloneTests
{
    [Fact]
    public void AddVariation_ThenGenerateCivilianClones_EmitsCivilianSibling()
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

            // Simulate the user clicking "Add Variation" in the UI.
            var cmd = new AddVariationCommand(doc, roster, "EquipmentSet");
            cmd.Execute();
            cmd.AddedVariation.Should().NotBeNull();

            // Simulate the user filling in equipment on the new combat variation.
            cmd.AddedVariation!.OriginalElement.Add(
                new XElement("Equipment",
                    new XAttribute("slot", "Head"),
                    new XAttribute("id", "Item.helmet")));

            // Count: 1 original combat, 1 original civilian, 1 new combat (no civilian clone yet).
            var combatBefore = roster.OriginalElement.Elements("EquipmentSet")
                .Count(e => e.Attribute("civilian")?.Value != "true");
            var civilianBefore = roster.OriginalElement.Elements("EquipmentSet")
                .Count(e => e.Attribute("civilian")?.Value == "true");
            combatBefore.Should().Be(2);
            civilianBefore.Should().Be(1);

            // Now run the exact same logic as FileSaverService.GenerateCivilianClones.
            RegenerateCivilianClones(roster.OriginalElement, "EquipmentSet");

            var combatAfter = roster.OriginalElement.Elements("EquipmentSet")
                .Count(e => e.Attribute("civilian")?.Value != "true");
            var civilianAfter = roster.OriginalElement.Elements("EquipmentSet")
                .Count(e => e.Attribute("civilian")?.Value == "true");
            combatAfter.Should().Be(2, because: "the two combat sets must still be present");
            civilianAfter.Should().Be(2, because: "every combat set must have a civilian clone, including the newly-added one");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void AddRoster_WithEmptyVariation_ThenGenerateCivilianClones_EmitsCivilianSibling()
    {
        // Simulates the full "add new roster" flow (AddRowCommand + AddVariationCommand)
        // plus the save-time civilian generation.
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
            // Fill some equipment on the new variation (as the user would).
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
            System.Console.WriteLine(output);
            output.Should().Contain("<EquipmentSet civilian=\"true\"",
                because: "a brand-new roster/variation must also produce a civilian clone on save");
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

    // Inline copy of FileSaverService.GenerateCivilianClones so we can test the
    // behaviour without pulling in the App assembly / Avalonia.
    private static void RegenerateCivilianClones(XElement rosterElement, string variationElementName)
    {
        var civilianElements = rosterElement.Elements(variationElementName)
            .Where(e => e.Attribute("civilian")?.Value?.Equals("true", System.StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        foreach (var c in civilianElements) c.Remove();

        var combatElements = rosterElement.Elements(variationElementName)
            .Where(e => e.Attribute("civilian")?.Value?.Equals("true", System.StringComparison.OrdinalIgnoreCase) != true)
            .ToList();

        foreach (var combatElement in combatElements)
        {
            var civilianClone = new XElement(combatElement);
            civilianClone.SetAttributeValue("civilian", "true");
            combatElement.AddAfterSelf(civilianClone);
        }
    }
}
