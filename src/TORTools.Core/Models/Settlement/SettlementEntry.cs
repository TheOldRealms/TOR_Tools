using System.Xml.Linq;

namespace TORTools.Core.Models.Settlement;

/// <summary>
/// Represents a settlement from tor_settlements.xml with all its properties.
/// </summary>
public class SettlementEntry
{
    /// <summary>
    /// Reference to the original XElement for formatting preservation during save.
    /// </summary>
    public XElement? OriginalElement { get; set; }

    /// <summary>
    /// Settlement ID (e.g., "oak_of_ages", "sigmar_shrine_01").
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Raw name attribute including translation key (e.g., "{=str_...}Display Name").
    /// </summary>
    public string RawName { get; set; } = "";

    /// <summary>
    /// Display name extracted from the translation string.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Translation ID extracted from {=id}Name format.
    /// </summary>
    public string TranslationId { get; set; } = "";

    /// <summary>
    /// Raw text/description attribute including translation key.
    /// </summary>
    public string RawText { get; set; } = "";

    /// <summary>
    /// Display text/description extracted from translation string.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Description translation ID.
    /// </summary>
    public string TextTranslationId { get; set; } = "";

    /// <summary>
    /// Owner clan ID (e.g., "Faction.athel_loren_clan_1").
    /// </summary>
    public string Owner { get; set; } = "";

    /// <summary>
    /// Culture ID (e.g., "Culture.battania", "Culture.empire").
    /// </summary>
    public string Culture { get; set; } = "";

    /// <summary>
    /// X position on the world map.
    /// </summary>
    public double PosX { get; set; }

    /// <summary>
    /// Y position on the world map.
    /// </summary>
    public double PosY { get; set; }

    /// <summary>
    /// Gate X position.
    /// </summary>
    public double GatePosX { get; set; }

    /// <summary>
    /// Gate Y position.
    /// </summary>
    public double GatePosY { get; set; }

    /// <summary>
    /// Gate rotation angle.
    /// </summary>
    public double GateRotation { get; set; }

    /// <summary>
    /// Settlement component type (Shrine, HerdStone, OakOfAges, etc.).
    /// </summary>
    public SettlementComponentType ComponentType { get; set; } = SettlementComponentType.Unknown;

    /// <summary>
    /// Religion for shrine settlements (e.g., "cult_of_sigmar", "cult_of_taal").
    /// </summary>
    public string? Religion { get; set; }

    /// <summary>
    /// Village production type (for village settlements only).
    /// </summary>
    public string? VillageType { get; set; }

    /// <summary>
    /// Prosperity value (for towns) or hearth (for villages).
    /// </summary>
    public double Prosperity { get; set; }

    /// <summary>
    /// Parent settlement ID that this village is bound to.
    /// </summary>
    public string? BoundTo { get; set; }

    /// <summary>
    /// Whether this is a castle (Town with is_castle=true).
    /// </summary>
    public bool IsCastle { get; set; }

    /// <summary>
    /// Background mesh name for the settlement menu.
    /// </summary>
    public string BackgroundMesh { get; set; } = "";

    /// <summary>
    /// Wait mesh name.
    /// </summary>
    public string WaitMesh { get; set; } = "";

    /// <summary>
    /// Background crop position.
    /// </summary>
    public string BackgroundCropPosition { get; set; } = "";

    /// <summary>
    /// Location scenes within this settlement (center, tavern, lordshall, etc.).
    /// Key is location ID, value is the location data.
    /// </summary>
    public Dictionary<string, SettlementLocation> Locations { get; } = new();

    // ============ Resolved display data (populated from cross-file lookup) ============

    /// <summary>
    /// Resolved owner clan display name.
    /// </summary>
    public string OwnerDisplayName { get; set; } = "";

    /// <summary>
    /// Resolved culture display name.
    /// </summary>
    public string CultureDisplayName { get; set; } = "";

    /// <summary>
    /// Faction color (from kingdom primary_banner_color) for map display.
    /// </summary>
    public string FactionColor { get; set; } = "";

    /// <summary>
    /// Whether this settlement has been modified since loading.
    /// </summary>
    public bool IsModified { get; set; }

    /// <summary>
    /// Gets a display-friendly component type string.
    /// </summary>
    public string ComponentTypeDisplay
    {
        get
        {
            if (ComponentType == SettlementComponentType.Shrine && !string.IsNullOrEmpty(Religion))
            {
                // Extract religion name from "cult_of_X" format
                var religionName = Religion.Replace("cult_of_", "").Replace("_", " ");
                return $"Shrine ({char.ToUpper(religionName[0]) + religionName.Substring(1)})";
            }
            return ComponentType.ToString();
        }
    }

    /// <summary>
    /// Parses a settlement from an XElement.
    /// </summary>
    public static SettlementEntry FromXml(XElement element)
    {
        var entry = new SettlementEntry
        {
            OriginalElement = element,
            Id = element.Attribute("id")?.Value ?? "",
            RawName = element.Attribute("name")?.Value ?? "",
            RawText = element.Attribute("text")?.Value ?? "",
            Owner = element.Attribute("owner")?.Value ?? "",
            Culture = element.Attribute("culture")?.Value ?? "",
            PosX = ParseDouble(element.Attribute("posX")?.Value),
            PosY = ParseDouble(element.Attribute("posY")?.Value),
            GatePosX = ParseDouble(element.Attribute("gate_posX")?.Value),
            GatePosY = ParseDouble(element.Attribute("gate_posY")?.Value),
            GateRotation = ParseDouble(element.Attribute("gate_rotation")?.Value)
        };

        // Parse translation ID from name
        ParseTranslationString(entry.RawName, out var name, out var translationId);
        entry.Name = name;
        entry.TranslationId = translationId;

        // Parse translation ID from text
        ParseTranslationString(entry.RawText, out var text, out var textTranslationId);
        entry.Text = text;
        entry.TextTranslationId = textTranslationId;

        // Parse Components to determine type
        var components = element.Element("Components");
        if (components != null)
        {
            ParseComponents(entry, components);
        }

        // Parse Locations for scene data
        var locations = element.Element("Locations");
        if (locations != null)
        {
            ParseLocations(entry, locations);
        }

        return entry;
    }

    private static void ParseComponents(SettlementEntry entry, XElement components)
    {
        // Check for TOR-specific components first
        var shrine = components.Element("Shrine");
        if (shrine != null)
        {
            entry.ComponentType = SettlementComponentType.Shrine;
            entry.Religion = shrine.Attribute("religion")?.Value;
            ParseCommonComponentAttributes(entry, shrine);
            return;
        }

        var herdStone = components.Element("HerdStone");
        if (herdStone != null)
        {
            entry.ComponentType = SettlementComponentType.HerdStone;
            ParseCommonComponentAttributes(entry, herdStone);
            return;
        }

        var oakOfAges = components.Element("OakOfAges");
        if (oakOfAges != null)
        {
            entry.ComponentType = SettlementComponentType.OakOfAges;
            ParseCommonComponentAttributes(entry, oakOfAges);
            return;
        }

        var worldRoots = components.Element("WorldRoots");
        if (worldRoots != null)
        {
            entry.ComponentType = SettlementComponentType.WorldRoots;
            ParseCommonComponentAttributes(entry, worldRoots);
            return;
        }

        var chaosPortal = components.Element("ChaosPortal");
        if (chaosPortal != null)
        {
            entry.ComponentType = SettlementComponentType.ChaosPortal;
            ParseCommonComponentAttributes(entry, chaosPortal);
            return;
        }

        var slaverCamp = components.Element("SlaverCamp");
        if (slaverCamp != null)
        {
            entry.ComponentType = SettlementComponentType.SlaverCamp;
            ParseCommonComponentAttributes(entry, slaverCamp);
            return;
        }

        // Standard Bannerlord components
        var town = components.Element("Town");
        if (town != null)
        {
            entry.IsCastle = town.Attribute("is_castle")?.Value == "true";
            entry.ComponentType = entry.IsCastle ? SettlementComponentType.Castle : SettlementComponentType.Town;
            entry.Prosperity = ParseDouble(town.Attribute("prosperity")?.Value);
            entry.GateRotation = ParseDouble(town.Attribute("gate_rotation")?.Value);
            ParseCommonComponentAttributes(entry, town);
            return;
        }

        var village = components.Element("Village");
        if (village != null)
        {
            entry.ComponentType = SettlementComponentType.Village;
            entry.VillageType = village.Attribute("village_type")?.Value?.Replace("VillageType.", "");
            entry.Prosperity = ParseDouble(village.Attribute("hearth")?.Value);
            entry.BoundTo = village.Attribute("bound")?.Value?.Replace("Settlement.", "");
            entry.GateRotation = ParseDouble(village.Attribute("gate_rotation")?.Value);
            ParseCommonComponentAttributes(entry, village);
            return;
        }

        var hideout = components.Element("Hideout");
        if (hideout != null)
        {
            entry.ComponentType = SettlementComponentType.Hideout;
            ParseCommonComponentAttributes(entry, hideout);
        }
    }

    private static void ParseCommonComponentAttributes(SettlementEntry entry, XElement component)
    {
        entry.BackgroundMesh = component.Attribute("background_mesh")?.Value ?? "";
        entry.WaitMesh = component.Attribute("wait_mesh")?.Value ?? "";
        entry.BackgroundCropPosition = component.Attribute("background_crop_position")?.Value ?? "";
    }

    private static void ParseLocations(SettlementEntry entry, XElement locationsElement)
    {
        foreach (var location in locationsElement.Elements("Location"))
        {
            var id = location.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id)) continue;

            var loc = new SettlementLocation
            {
                Id = id,
                SceneName = location.Attribute("scene_name")?.Value ?? "",
                SceneName1 = location.Attribute("scene_name_1")?.Value ?? "",
                SceneName2 = location.Attribute("scene_name_2")?.Value ?? "",
                SceneName3 = location.Attribute("scene_name_3")?.Value ?? ""
            };

            entry.Locations[id] = loc;
        }
    }

    private static void ParseTranslationString(string raw, out string displayValue, out string translationId)
    {
        displayValue = raw;
        translationId = "";

        if (string.IsNullOrEmpty(raw)) return;

        // Parse {=id}text format
        if (raw.StartsWith("{=") && raw.Contains("}"))
        {
            var closeBrace = raw.IndexOf('}');
            translationId = raw.Substring(2, closeBrace - 2);
            displayValue = raw.Substring(closeBrace + 1);

            // {=!} means no translation/empty
            if (translationId == "!")
            {
                translationId = "";
            }
        }
    }

    private static double ParseDouble(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        return double.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0;
    }
}
