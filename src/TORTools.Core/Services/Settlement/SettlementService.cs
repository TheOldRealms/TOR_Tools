using System.Xml.Linq;
using TORTools.Core.Models.Settlement;

namespace TORTools.Core.Services.Settlement;

/// <summary>
/// Service for loading and saving settlement XML files.
/// </summary>
public class SettlementService
{
    private XDocument? _document;
    private string? _filePath;

    /// <summary>
    /// Gets all loaded settlements.
    /// </summary>
    public List<SettlementEntry> Settlements { get; } = new();

    /// <summary>
    /// Gets whether the service has loaded data.
    /// </summary>
    public bool IsLoaded => _document != null;

    /// <summary>
    /// Loads settlements from the specified XML file.
    /// </summary>
    public void Load(string filePath)
    {
        _filePath = filePath;
        Settlements.Clear();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Settlement file not found: {filePath}");
        }

        _document = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);

        var root = _document.Root;
        if (root == null || root.Name.LocalName != "Settlements")
        {
            throw new InvalidDataException($"Invalid settlement file: root element must be 'Settlements'");
        }

        foreach (var element in root.Elements("Settlement"))
        {
            var entry = SettlementEntry.FromXml(element);
            Settlements.Add(entry);
        }
    }

    /// <summary>
    /// Gets settlements filtered by component type.
    /// </summary>
    public IEnumerable<SettlementEntry> GetByType(SettlementComponentType type)
    {
        return Settlements.Where(s => s.ComponentType == type);
    }

    /// <summary>
    /// Gets settlements filtered by culture.
    /// </summary>
    public IEnumerable<SettlementEntry> GetByCulture(string cultureId)
    {
        return Settlements.Where(s =>
            s.Culture.Equals(cultureId, StringComparison.OrdinalIgnoreCase) ||
            s.Culture.Equals($"Culture.{cultureId}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets settlements filtered by owner clan.
    /// </summary>
    public IEnumerable<SettlementEntry> GetByOwner(string ownerId)
    {
        return Settlements.Where(s =>
            s.Owner.Equals(ownerId, StringComparison.OrdinalIgnoreCase) ||
            s.Owner.Equals($"Faction.{ownerId}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets a settlement by ID.
    /// </summary>
    public SettlementEntry? GetById(string id)
    {
        return Settlements.FirstOrDefault(s =>
            s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all unique component types in the loaded settlements.
    /// </summary>
    public IEnumerable<SettlementComponentType> GetComponentTypes()
    {
        return Settlements.Select(s => s.ComponentType).Distinct().OrderBy(t => t);
    }

    /// <summary>
    /// Gets all unique cultures in the loaded settlements.
    /// </summary>
    public IEnumerable<string> GetCultures()
    {
        return Settlements.Select(s => s.Culture).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c);
    }

    /// <summary>
    /// Gets all unique owners in the loaded settlements.
    /// </summary>
    public IEnumerable<string> GetOwners()
    {
        return Settlements.Select(s => s.Owner).Where(o => !string.IsNullOrEmpty(o)).Distinct().OrderBy(o => o);
    }

    /// <summary>
    /// Gets all unique religions in the loaded settlements (for shrines).
    /// </summary>
    public IEnumerable<string> GetReligions()
    {
        return Settlements
            .Where(s => s.ComponentType == SettlementComponentType.Shrine && !string.IsNullOrEmpty(s.Religion))
            .Select(s => s.Religion!)
            .Distinct()
            .OrderBy(r => r);
    }

    /// <summary>
    /// Gets the map bounds (min/max X and Y coordinates).
    /// </summary>
    public (double minX, double maxX, double minY, double maxY) GetMapBounds()
    {
        if (Settlements.Count == 0)
            return (0, 0, 0, 0);

        return (
            Settlements.Min(s => s.PosX),
            Settlements.Max(s => s.PosX),
            Settlements.Min(s => s.PosY),
            Settlements.Max(s => s.PosY)
        );
    }

    /// <summary>
    /// Updates a settlement's attribute in the original XML.
    /// </summary>
    public void UpdateAttribute(SettlementEntry entry, string attributeName, string value)
    {
        if (entry.OriginalElement == null) return;

        var attr = entry.OriginalElement.Attribute(attributeName);
        if (attr != null)
        {
            attr.Value = value;
        }
        else
        {
            entry.OriginalElement.SetAttributeValue(attributeName, value);
        }

        entry.IsModified = true;
    }

    /// <summary>
    /// Updates a settlement's location scene in the original XML.
    /// </summary>
    public void UpdateLocationScene(SettlementEntry entry, string locationId, string sceneAttribute, string value)
    {
        if (entry.OriginalElement == null) return;

        var locationsElement = entry.OriginalElement.Element("Locations");
        if (locationsElement == null) return;

        var locationElement = locationsElement.Elements("Location")
            .FirstOrDefault(l => l.Attribute("id")?.Value == locationId);

        if (locationElement == null) return;

        var attr = locationElement.Attribute(sceneAttribute);
        if (attr != null)
        {
            attr.Value = value;
        }
        else
        {
            locationElement.SetAttributeValue(sceneAttribute, value);
        }

        // Update in-memory location
        if (entry.Locations.TryGetValue(locationId, out var location))
        {
            switch (sceneAttribute)
            {
                case "scene_name":
                    location.SceneName = value;
                    break;
                case "scene_name_1":
                    location.SceneName1 = value;
                    break;
                case "scene_name_2":
                    location.SceneName2 = value;
                    break;
                case "scene_name_3":
                    location.SceneName3 = value;
                    break;
            }
        }

        entry.IsModified = true;
    }

    /// <summary>
    /// Saves changes back to the original file.
    /// </summary>
    public void Save()
    {
        if (_document == null || string.IsNullOrEmpty(_filePath))
        {
            throw new InvalidOperationException("No document loaded");
        }

        SaveTo(_filePath);
    }

    /// <summary>
    /// Saves the document to a specific path.
    /// </summary>
    public void SaveTo(string filePath)
    {
        if (_document == null)
        {
            throw new InvalidOperationException("No document loaded");
        }

        // Save to temp file first, then move (atomic operation)
        var tempPath = filePath + ".tmp";
        try
        {
            // Use SaveOptions.None to preserve formatting as much as possible
            _document.Save(tempPath, SaveOptions.None);

            // Backup original if it exists
            if (File.Exists(filePath))
            {
                var backupPath = filePath + ".bak";
                File.Copy(filePath, backupPath, overwrite: true);
            }

            // Move temp to actual file
            File.Move(tempPath, filePath, overwrite: true);

            // Clear modified flags
            foreach (var settlement in Settlements)
            {
                settlement.IsModified = false;
            }
        }
        catch
        {
            // Clean up temp file if it exists
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
    }

    /// <summary>
    /// Gets whether there are any unsaved changes.
    /// </summary>
    public bool HasUnsavedChanges => Settlements.Any(s => s.IsModified);
}
