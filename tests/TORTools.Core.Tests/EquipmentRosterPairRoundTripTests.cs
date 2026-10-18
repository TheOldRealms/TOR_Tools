using System.IO;
using System.Linq;
using System.Xml.Linq;
using TORTools.Core.Models;
using TORTools.Core.Services;

namespace TORTools.Core.Tests;

/// <summary>
/// End-to-end checks for the equipment-roster picker: simulates what
/// FileTabView does after the dialog closes (call SetEquipmentRosterPair
/// with the user's combat / civilian ids) and then runs the real save
/// path through XmlDocumentService, re-reads the file, and asserts the
/// XML on disk looks right.
/// </summary>
public class EquipmentRosterPairRoundTripTests
{
    private const string SampleDoc =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<NPCCharacters>\n" +
        "  <NPCCharacter id=\"tor_test_lord\" occupation=\"Lord\">\n" +
        "    <Equipments>\n" +
        "      <EquipmentSet id=\"old_combat\" />\n" +
        "      <EquipmentSet id=\"old_combat\" equipmentType=\"Civilian\" />\n" +
        "    </Equipments>\n" +
        "  </NPCCharacter>\n" +
        "</NPCCharacters>\n";

    [Fact]
    public void DialogOk_WithMirrorChecked_WritesSameIdOnBothSets()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, SampleDoc);

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            var entry = svc.GetEntries(doc).First();

            // Dialog with mirror ON produces (combat, combat) — simulate
            // FileTabView's call site.
            entry.SetEquipmentRosterPair("new_combat", "new_combat");
            svc.Save(doc, tempPath, compactFormat: true);

            var reloaded = XDocument.Load(tempPath).Root!.Elements().First();
            var sets = reloaded.Element("Equipments")!.Elements("EquipmentSet").ToList();
            sets.Should().HaveCount(2);
            sets[0].Attribute("id")!.Value.Should().Be("new_combat");
            sets[0].Attribute("equipmentType").Should().BeNull();
            sets[1].Attribute("id")!.Value.Should().Be("new_combat");
            sets[1].Attribute("equipmentType")!.Value.Should().Be("Civilian");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void DialogOk_WithMirrorUnchecked_WritesDivergentIds()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, SampleDoc);

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            var entry = svc.GetEntries(doc).First();

            entry.SetEquipmentRosterPair("knight_combat", "noble_civilian");
            svc.Save(doc, tempPath, compactFormat: true);

            var reloaded = XDocument.Load(tempPath).Root!.Elements().First();
            var sets = reloaded.Element("Equipments")!.Elements("EquipmentSet").ToList();
            sets.Should().HaveCount(2);
            var combat = sets.Single(s => s.Attribute("equipmentType")?.Value != "Civilian");
            var civilian = sets.Single(s => s.Attribute("equipmentType")?.Value == "Civilian");
            combat.Attribute("id")!.Value.Should().Be("knight_combat");
            civilian.Attribute("id")!.Value.Should().Be("noble_civilian");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void DialogOk_StartingFromNoEquipments_CreatesBothSetsOnDisk()
    {
        // Simulates a brand-new lord with no <Equipments> element at all —
        // the picker should populate the whole structure on OK.
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<NPCCharacters>\n" +
                "  <NPCCharacter id=\"brand_new_lord\" occupation=\"Lord\" />\n" +
                "</NPCCharacters>\n");

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            var entry = svc.GetEntries(doc).First();

            entry.GetCivilianEquipmentId().Should().BeNull();

            entry.SetEquipmentRosterPair("new_combat", "new_civilian");
            svc.Save(doc, tempPath, compactFormat: true);

            var text = File.ReadAllText(tempPath);
            text.Should().Contain("<EquipmentSet id=\"new_combat\"");
            text.Should().Contain("equipmentType=\"Civilian\"");
            text.Should().Contain("id=\"new_civilian\"");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void InlineEditOfCombat_PreservesExplicitlyDivergedCivilian()
    {
        // After the picker set civilian to something custom, an inline edit
        // of the combat cell (SyncChangesToXml path) must not clobber it.
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<NPCCharacters>\n" +
                "  <NPCCharacter id=\"lord\" occupation=\"Lord\">\n" +
                "    <Equipments>\n" +
                "      <EquipmentSet id=\"combat_old\" />\n" +
                "      <EquipmentSet id=\"civ_custom\" equipmentType=\"Civilian\" />\n" +
                "    </Equipments>\n" +
                "  </NPCCharacter>\n" +
                "</NPCCharacters>\n");

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            var entry = svc.GetEntries(doc).First();

            // User edits the combat cell inline — FileSaverService path.
            entry.SetEquipmentRosterWithCivilianPair("combat_new");
            svc.Save(doc, tempPath, compactFormat: true);

            var reloaded = XDocument.Load(tempPath).Root!.Elements().First();
            var combat = reloaded.Element("Equipments")!.Elements("EquipmentSet")
                .Single(s => s.Attribute("equipmentType")?.Value != "Civilian");
            var civilian = reloaded.Element("Equipments")!.Elements("EquipmentSet")
                .Single(s => s.Attribute("equipmentType")?.Value == "Civilian");
            combat.Attribute("id")!.Value.Should().Be("combat_new");
            civilian.Attribute("id")!.Value.Should().Be("civ_custom");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void InlineEditOfCombat_WhenMirrored_ContinuesToMirror()
    {
        // Default case: civilian was tracking combat; inline-editing combat
        // should keep the mirror intact.
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, SampleDoc);

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            var entry = svc.GetEntries(doc).First();

            entry.SetEquipmentRosterWithCivilianPair("new_mirrored");
            svc.Save(doc, tempPath, compactFormat: true);

            var reloaded = XDocument.Load(tempPath).Root!.Elements().First();
            var ids = reloaded.Element("Equipments")!.Elements("EquipmentSet")
                .Select(s => s.Attribute("id")!.Value)
                .ToList();
            ids.Should().OnlyContain(id => id == "new_mirrored");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void DialogOk_ClearingCombat_RemovesEntireEquipmentsBlock()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, SampleDoc);

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            var entry = svc.GetEntries(doc).First();

            entry.SetEquipmentRosterPair(null, "something");
            svc.Save(doc, tempPath, compactFormat: true);

            var reloaded = XDocument.Load(tempPath).Root!.Elements().First();
            reloaded.Element("Equipments").Should().BeNull();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void VanillaShape_WithEmptyEquipmentRosterPrefix_IsPreserved()
    {
        // Vanilla Bannerlord's lords.xml (SandBox/ModuleData/lords.xml) uses
        // the reference-based <Equipments> pattern, but puts an empty
        // <EquipmentRoster /> element in front of the <EquipmentSet> siblings
        // (that element is where inline equipment can live). TOR doesn't use
        // this prefix, but the editor should still produce vanilla-compatible
        // XML and leave an existing prefix element alone.
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<NPCCharacters>\n" +
                "\t<NPCCharacter id=\"vanilla_lord\" occupation=\"Lord\">\n" +
                "\t\t<Equipments>\n" +
                "\t\t\t<EquipmentRoster />\n" +
                "\t\t\t<EquipmentSet id=\"stu_bat_template_medium\" />\n" +
                "\t\t\t<EquipmentSet id=\"stu_civ_template_default\" equipmentType=\"Civilian\" />\n" +
                "\t\t</Equipments>\n" +
                "\t</NPCCharacter>\n" +
                "</NPCCharacters>\n");

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            var entry = svc.GetEntries(doc).First();

            // Simulate the picker: user keeps the vanilla divergence but
            // retargets combat to a new roster.
            entry.SetEquipmentRosterPair("stu_bat_template_heavy", "stu_civ_template_default");
            svc.Save(doc, tempPath, compactFormat: true);

            var reloaded = XDocument.Load(tempPath).Root!.Elements().First();
            var equipments = reloaded.Element("Equipments")!;
            equipments.Element("EquipmentRoster").Should().NotBeNull(
                because: "the vanilla <EquipmentRoster /> prefix must survive the write");

            var sets = equipments.Elements("EquipmentSet").ToList();
            sets.Should().HaveCount(2);
            sets.Single(s => s.Attribute("equipmentType")?.Value != "Civilian")
                .Attribute("id")!.Value.Should().Be("stu_bat_template_heavy");
            sets.Single(s => s.Attribute("equipmentType")?.Value == "Civilian")
                .Attribute("id")!.Value.Should().Be("stu_civ_template_default");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void DialogOk_ClearingOnlyCivilian_LeavesCombatIntact()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, SampleDoc);

            var svc = new XmlDocumentService();
            var doc = svc.Load(tempPath);
            var entry = svc.GetEntries(doc).First();

            entry.SetEquipmentRosterPair("keep_combat", null);
            svc.Save(doc, tempPath, compactFormat: true);

            var reloaded = XDocument.Load(tempPath).Root!.Elements().First();
            var sets = reloaded.Element("Equipments")!.Elements("EquipmentSet").ToList();
            sets.Should().HaveCount(1);
            sets[0].Attribute("id")!.Value.Should().Be("keep_combat");
            sets[0].Attribute("equipmentType").Should().BeNull();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
