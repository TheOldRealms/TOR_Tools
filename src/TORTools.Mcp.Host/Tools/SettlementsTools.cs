using System.ComponentModel;
using System.Xml.Linq;
using ModelContextProtocol.Server;
using TORTools.Core.Workspace;

using static TORTools.Core.DocumentStore.StandaloneDocumentStore;

namespace TORTools.Mcp.Host.Tools;

/// <summary>
/// MCP tools for editing tor_settlements.xml. This file has no schema in TOR_Tools,
/// so edits bypass the schema framework and operate on raw XML with whitespace preserved.
/// </summary>
[McpServerToolType]
public class SettlementsTools(IWorkspaceService workspaceService)
{
    private const string SettlementsFileName = "tor_settlements.xml";

    private string? ResolveSettlementsPath()
    {
        var config = workspaceService.LoadConfig();
        if (string.IsNullOrEmpty(config.TorCorePath)) return null;
        var path = Path.Combine(config.TorCorePath, "ModuleData", SettlementsFileName);
        return File.Exists(path) ? path : null;
    }

    [McpServerTool, Description("Add a <Location> row to a settlement in tor_settlements.xml. Creates <Location id=\"<location_id>\" scene_name=\"<scene_name>\" /> inside the settlement's existing <Locations> container. Idempotent: returns Added=false if the location_id already exists in that settlement. Errors if the settlement is missing or has no <Locations> container.")]
    public SettlementAddLocationResult settlements_add_location(
        [Description("Settlement id, e.g. 'town_SY1', 'town_AL1', 'town_MC1'")]
        string settlement_id,
        [Description("Location id to add, e.g. 'tor_vampire_crypt', 'tor_spellsingers_grove', 'tor_shaman_hut'")]
        string location_id,
        [Description("Scene name assigned to the Location, e.g. 'TOR_vampire_magister_001', 'TOR_POI_spellsinger_cave_001_atmo_w_night'")]
        string scene_name)
    {
        Log("Tool", $"settlements_add_location(settlement={settlement_id}, location={location_id}, scene={scene_name})");

        var path = ResolveSettlementsPath();
        if (path == null)
            return new SettlementAddLocationResult { Error = $"{SettlementsFileName} not found under configured TOR_Core path" };

        XDocument doc;
        try
        {
            doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            return new SettlementAddLocationResult { Error = $"Failed to parse {SettlementsFileName}: {ex.Message}" };
        }

        var settlement = doc.Root?.Elements("Settlement")
            .FirstOrDefault(s => (string?)s.Attribute("id") == settlement_id);
        if (settlement == null)
            return new SettlementAddLocationResult { Error = $"Settlement '{settlement_id}' not found" };

        var locations = settlement.Element("Locations");
        if (locations == null)
            return new SettlementAddLocationResult { Error = $"Settlement '{settlement_id}' has no <Locations> container (not a town/village/castle?)" };

        var existing = locations.Elements("Location")
            .FirstOrDefault(l => (string?)l.Attribute("id") == location_id);
        if (existing != null)
        {
            return new SettlementAddLocationResult
            {
                SettlementId = settlement_id,
                LocationId = location_id,
                Added = false,
                Reason = "already exists"
            };
        }

        // Determine indent to match existing siblings; fall back to 6 spaces (file convention).
        string indent = "\n      ";
        var firstLocation = locations.Elements("Location").FirstOrDefault();
        if (firstLocation?.PreviousNode is XText prev)
            indent = prev.Value;

        var newLocation = new XElement("Location",
            new XAttribute("id", location_id),
            new XAttribute("scene_name", scene_name));

        // Insert BEFORE the final whitespace text node so </Locations> keeps its indent.
        if (locations.LastNode is XText trailing)
        {
            trailing.AddBeforeSelf(new XText(indent), newLocation);
        }
        else
        {
            locations.Add(new XText(indent), newLocation);
        }

        try
        {
            doc.Save(path, SaveOptions.DisableFormatting);
        }
        catch (Exception ex)
        {
            return new SettlementAddLocationResult { Error = $"Failed to save {SettlementsFileName}: {ex.Message}" };
        }

        return new SettlementAddLocationResult
        {
            SettlementId = settlement_id,
            LocationId = location_id,
            SceneName = scene_name,
            Added = true
        };
    }

    [McpServerTool, Description("List settlement ids with optional culture / owner filters. Useful for enumerating applicable towns before calling settlements_add_location.")]
    public SettlementListResult settlements_list(
        [Description("Filter by culture, e.g. 'Culture.empire', 'Culture.khuzait', 'Culture.battania'. Omit for all cultures.")]
        string? culture = null,
        [Description("Filter by owner faction, e.g. 'Faction.wissenland_clan_1'. Omit for all owners.")]
        string? owner = null,
        [Description("Only return settlements whose id starts with this prefix, e.g. 'town_SY', 'town_AL'. Omit for all.")]
        string? id_prefix = null,
        [Description("Max rows (default 200)")]
        int limit = 200)
    {
        Log("Tool", $"settlements_list(culture={culture}, owner={owner}, id_prefix={id_prefix}, limit={limit})");
        var path = ResolveSettlementsPath();
        if (path == null)
            return new SettlementListResult { Error = $"{SettlementsFileName} not found" };

        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch (Exception ex) { return new SettlementListResult { Error = ex.Message }; }

        var rows = doc.Root?.Elements("Settlement")
            .Where(s => culture == null || (string?)s.Attribute("culture") == culture)
            .Where(s => owner == null || (string?)s.Attribute("owner") == owner)
            .Where(s => id_prefix == null || ((string?)s.Attribute("id"))?.StartsWith(id_prefix) == true)
            .Take(limit)
            .Select(s => new SettlementSummary
            {
                Id = (string?)s.Attribute("id") ?? "",
                Name = (string?)s.Attribute("name") ?? "",
                Culture = (string?)s.Attribute("culture"),
                Owner = (string?)s.Attribute("owner"),
                HasLocations = s.Element("Locations") != null,
                LocationIds = s.Element("Locations")?.Elements("Location")
                    .Select(l => (string?)l.Attribute("id") ?? "")
                    .Where(id => id.Length > 0)
                    .ToList() ?? new List<string>()
            })
            .ToList() ?? new List<SettlementSummary>();

        return new SettlementListResult { Settlements = rows, Count = rows.Count };
    }
}

public record SettlementAddLocationResult
{
    public string? SettlementId { get; init; }
    public string? LocationId { get; init; }
    public string? SceneName { get; init; }
    public bool Added { get; init; }
    public string? Reason { get; init; }
    public string? Error { get; init; }
}

public record SettlementSummary
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Culture { get; init; }
    public string? Owner { get; init; }
    public bool HasLocations { get; init; }
    public List<string> LocationIds { get; init; } = new();
}

public record SettlementListResult
{
    public List<SettlementSummary> Settlements { get; init; } = new();
    public int Count { get; init; }
    public string? Error { get; init; }
}
