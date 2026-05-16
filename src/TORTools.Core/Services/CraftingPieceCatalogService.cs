using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace TORTools.Core.Services;

/// <summary>
/// Represents a crafting piece definition from tor_crafting_pieces.xml.
/// </summary>
public class CraftingPieceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MeshName { get; set; } = string.Empty;
    public string PieceType { get; set; } = string.Empty; // Blade, Handle, Guard, Pommel
    public string Culture { get; set; } = string.Empty;
    public int Tier { get; set; }
    public float Length { get; set; }
    public float Weight { get; set; }
    public bool IsDefault { get; set; }
    public bool IsHidden { get; set; }

    // BuildData offsets for piece assembly
    public float PieceOffset { get; set; }
    public float NextPieceOffset { get; set; }
    public float PreviousPieceOffset { get; set; }

    // BladeData for blade pieces
    public string? ThrustDamageType { get; set; }
    public float ThrustDamageFactor { get; set; }
    public string? SwingDamageType { get; set; }
    public float SwingDamageFactor { get; set; }
}

/// <summary>
/// Represents piece type assembly data from a template.
/// </summary>
public class PieceTypeData
{
    public string PieceType { get; set; } = string.Empty;
    public int BuildOrder { get; set; }
}

/// <summary>
/// Represents a crafting template from tor_crafting_templates.xml.
/// </summary>
public class CraftingTemplateInfo
{
    public string Id { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string ModifierGroup { get; set; } = string.Empty;
    public List<PieceTypeData> PieceDatas { get; set; } = new();
    public List<string> UsablePieceIds { get; set; } = new();
}

/// <summary>
/// Represents offset values for a crafting piece's BuildData.
/// </summary>
public class PieceOffsets
{
    public string PieceId { get; set; } = string.Empty;
    public float PieceOffset { get; set; }
    public float NextPieceOffset { get; set; }
    public float PreviousPieceOffset { get; set; }
}

/// <summary>
/// Service for loading and querying crafting pieces and templates.
/// </summary>
public class CraftingPieceCatalogService
{
    private readonly Dictionary<string, CraftingPieceInfo> _pieces = new();
    private readonly Dictionary<string, CraftingTemplateInfo> _templates = new();
    private string? _piecesFilePath;
    private bool _loaded;

    /// <summary>
    /// Loads crafting pieces and templates from the specified module data path.
    /// </summary>
    /// <param name="moduleDataPath">Path to ModuleData folder containing tor_crafting_pieces.xml and tor_crafting_templates.xml</param>
    public void Load(string moduleDataPath)
    {
        _pieces.Clear();
        _templates.Clear();

        var piecesPath = Path.Combine(moduleDataPath, "tor_crafting_pieces.xml");
        var templatesPath = Path.Combine(moduleDataPath, "tor_crafting_templates.xml");

        if (File.Exists(piecesPath))
        {
            _piecesFilePath = piecesPath;
            LoadPieces(piecesPath);
        }

        if (File.Exists(templatesPath))
        {
            LoadTemplates(templatesPath);
        }

        _loaded = true;
    }

    private void LoadPieces(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root;
        if (root == null) return;

        foreach (var element in root.Elements("CraftingPiece"))
        {
            var piece = new CraftingPieceInfo
            {
                Id = element.Attribute("id")?.Value ?? string.Empty,
                Name = element.Attribute("name")?.Value ?? string.Empty,
                MeshName = element.Attribute("mesh")?.Value ?? string.Empty,
                PieceType = element.Attribute("piece_type")?.Value ?? string.Empty,
                Culture = element.Attribute("culture")?.Value ?? string.Empty,
                Tier = int.TryParse(element.Attribute("tier")?.Value, out var tier) ? tier : 1,
                Length = float.TryParse(element.Attribute("length")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var length) ? length : 0,
                Weight = float.TryParse(element.Attribute("weight")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) ? weight : 0,
                IsDefault = element.Attribute("is_default")?.Value == "true",
                IsHidden = element.Attribute("is_hidden")?.Value == "true"
            };

            // Parse BuildData (use InvariantCulture for decimal parsing)
            var buildData = element.Element("BuildData");
            if (buildData != null)
            {
                piece.PieceOffset = float.TryParse(buildData.Attribute("piece_offset")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var po) ? po : 0;
                piece.NextPieceOffset = float.TryParse(buildData.Attribute("next_piece_offset")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var npo) ? npo : 0;
                piece.PreviousPieceOffset = float.TryParse(buildData.Attribute("previous_piece_offset")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ppo) ? ppo : 0;
            }

            // Parse BladeData (use InvariantCulture for decimal parsing)
            var bladeData = element.Element("BladeData");
            if (bladeData != null)
            {
                var thrust = bladeData.Element("Thrust");
                if (thrust != null)
                {
                    piece.ThrustDamageType = thrust.Attribute("damage_type")?.Value;
                    piece.ThrustDamageFactor = float.TryParse(thrust.Attribute("damage_factor")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tdf) ? tdf : 0;
                }

                var swing = bladeData.Element("Swing");
                if (swing != null)
                {
                    piece.SwingDamageType = swing.Attribute("damage_type")?.Value;
                    piece.SwingDamageFactor = float.TryParse(swing.Attribute("damage_factor")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sdf) ? sdf : 0;
                }
            }

            if (!string.IsNullOrEmpty(piece.Id))
            {
                _pieces[piece.Id] = piece;
            }
        }
    }

    private void LoadTemplates(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root;
        if (root == null) return;

        foreach (var element in root.Elements("CraftingTemplate"))
        {
            var template = new CraftingTemplateInfo
            {
                Id = element.Attribute("id")?.Value ?? string.Empty,
                ItemType = element.Attribute("item_type")?.Value ?? string.Empty,
                ModifierGroup = element.Attribute("modifier_group")?.Value ?? string.Empty
            };

            // Parse PieceDatas
            var pieceDatas = element.Element("PieceDatas");
            if (pieceDatas != null)
            {
                foreach (var pd in pieceDatas.Elements("PieceData"))
                {
                    template.PieceDatas.Add(new PieceTypeData
                    {
                        PieceType = pd.Attribute("piece_type")?.Value ?? string.Empty,
                        BuildOrder = int.TryParse(pd.Attribute("build_order")?.Value, out var bo) ? bo : 0
                    });
                }
            }

            // Parse UsablePieces
            var usablePieces = element.Element("UsablePieces");
            if (usablePieces != null)
            {
                foreach (var up in usablePieces.Elements("UsablePiece"))
                {
                    var pieceId = up.Attribute("piece_id")?.Value;
                    if (!string.IsNullOrEmpty(pieceId))
                    {
                        template.UsablePieceIds.Add(pieceId);
                    }
                }
            }

            if (!string.IsNullOrEmpty(template.Id))
            {
                _templates[template.Id] = template;
            }
        }
    }

    /// <summary>
    /// Gets a piece by ID.
    /// </summary>
    public CraftingPieceInfo? GetPiece(string pieceId)
    {
        return _pieces.TryGetValue(pieceId, out var piece) ? piece : null;
    }

    /// <summary>
    /// Gets a template by ID.
    /// </summary>
    public CraftingTemplateInfo? GetTemplate(string templateId)
    {
        return _templates.TryGetValue(templateId, out var template) ? template : null;
    }

    /// <summary>
    /// Gets all pieces of a specific type.
    /// </summary>
    public IEnumerable<CraftingPieceInfo> GetPiecesByType(string pieceType)
    {
        return _pieces.Values.Where(p =>
            p.PieceType.Equals(pieceType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all pieces of a specific type and culture.
    /// </summary>
    public IEnumerable<CraftingPieceInfo> GetPiecesByTypeAndCulture(string pieceType, string culture)
    {
        return _pieces.Values.Where(p =>
            p.PieceType.Equals(pieceType, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(culture) || p.Culture.Contains(culture, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Gets all pieces usable with a specific template.
    /// </summary>
    public IEnumerable<CraftingPieceInfo> GetPiecesForTemplate(string templateId)
    {
        if (!_templates.TryGetValue(templateId, out var template))
            return Enumerable.Empty<CraftingPieceInfo>();

        return template.UsablePieceIds
            .Where(id => _pieces.ContainsKey(id))
            .Select(id => _pieces[id]);
    }

    /// <summary>
    /// Gets all pieces usable with a specific template, filtered by piece type.
    /// </summary>
    public IEnumerable<CraftingPieceInfo> GetPiecesForTemplate(string templateId, string pieceType)
    {
        return GetPiecesForTemplate(templateId)
            .Where(p => p.PieceType.Equals(pieceType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all visible (non-hidden, non-default) pieces of a type.
    /// </summary>
    public IEnumerable<CraftingPieceInfo> GetVisiblePiecesByType(string pieceType)
    {
        return GetPiecesByType(pieceType).Where(p => !p.IsHidden && !p.IsDefault);
    }

    /// <summary>
    /// Gets all available templates.
    /// </summary>
    public IEnumerable<CraftingTemplateInfo> GetAllTemplates()
    {
        return _templates.Values;
    }

    /// <summary>
    /// Gets all available pieces.
    /// </summary>
    public IEnumerable<CraftingPieceInfo> GetAllPieces()
    {
        return _pieces.Values;
    }

    /// <summary>
    /// Gets distinct cultures from all pieces.
    /// </summary>
    public IEnumerable<string> GetDistinctCultures()
    {
        return _pieces.Values
            .Select(p => p.Culture)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .OrderBy(c => c);
    }

    /// <summary>
    /// Gets distinct piece types.
    /// </summary>
    public IEnumerable<string> GetDistinctPieceTypes()
    {
        return _pieces.Values
            .Select(p => p.PieceType)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .OrderBy(t => t);
    }

    /// <summary>
    /// Calculates the position of a piece in an assembled weapon.
    /// All values (length, offsets) are in centimeters.
    /// </summary>
    /// <param name="piece">The piece to position</param>
    /// <param name="previousPiece">The previous piece in build order (null if first)</param>
    /// <param name="previousPosition">The position of the previous piece</param>
    /// <returns>The calculated position along the weapon axis (Y-axis) in centimeters</returns>
    public Vector3 CalculatePiecePosition(
        CraftingPieceInfo piece,
        CraftingPieceInfo? previousPiece,
        Vector3 previousPosition)
    {
        if (previousPiece == null)
        {
            // First piece (usually Handle) starts at origin + piece_offset
            return new Vector3(0, piece.PieceOffset, 0);
        }

        // Calculate position based on previous piece's next_piece_offset and this piece's previous_piece_offset
        // All values are in centimeters
        var gap = previousPiece.NextPieceOffset + piece.PreviousPieceOffset;
        var newY = previousPosition.Y + previousPiece.Length + gap;

        return new Vector3(0, newY, 0);
    }

    /// <summary>
    /// Calculates positions for all pieces in a weapon assembly.
    /// </summary>
    /// <param name="template">The crafting template defining build order</param>
    /// <param name="selectedPieces">Dictionary of piece type to selected piece</param>
    /// <returns>Dictionary of piece type to calculated position</returns>
    public Dictionary<string, Vector3> CalculateAssemblyPositions(
        CraftingTemplateInfo template,
        Dictionary<string, CraftingPieceInfo> selectedPieces)
    {
        var positions = new Dictionary<string, Vector3>();

        // Sort by build order, but handle special cases:
        // - Negative build order (pommel) is attached to handle's back
        // - Positive build order proceeds forward
        var orderedTypes = template.PieceDatas
            .Where(pd => pd.BuildOrder >= 0)
            .OrderBy(pd => pd.BuildOrder)
            .ToList();

        var negativeTypes = template.PieceDatas
            .Where(pd => pd.BuildOrder < 0)
            .OrderByDescending(pd => pd.BuildOrder)
            .ToList();

        CraftingPieceInfo? previousPiece = null;
        var previousPosition = Vector3.Zero;

        // Process positive build order pieces
        foreach (var pieceType in orderedTypes)
        {
            if (!selectedPieces.TryGetValue(pieceType.PieceType, out var piece))
                continue;

            var position = CalculatePiecePosition(piece, previousPiece, previousPosition);
            positions[pieceType.PieceType] = position;

            previousPiece = piece;
            previousPosition = position;
        }

        // Process negative build order pieces (attach to back of handle)
        if (selectedPieces.TryGetValue("Handle", out var handlePiece) && positions.ContainsKey("Handle"))
        {
            var handlePosition = positions["Handle"];

            foreach (var pieceType in negativeTypes)
            {
                if (!selectedPieces.TryGetValue(pieceType.PieceType, out var piece))
                    continue;

                // Pommel attaches to the back of the handle
                // Position it so its top connects to handle's bottom
                var pommelY = handlePosition.Y - handlePiece.PreviousPieceOffset - piece.NextPieceOffset - piece.Length;
                positions[pieceType.PieceType] = new Vector3(0, pommelY, 0);
            }
        }

        return positions;
    }

    /// <summary>
    /// Checks if the service has been loaded.
    /// </summary>
    public bool IsLoaded => _loaded;

    /// <summary>
    /// Gets the total number of loaded pieces.
    /// </summary>
    public int PieceCount => _pieces.Count;

    /// <summary>
    /// Gets the total number of loaded templates.
    /// </summary>
    public int TemplateCount => _templates.Count;

    /// <summary>
    /// Gets the file path for the crafting pieces XML.
    /// </summary>
    public string? PiecesFilePath => _piecesFilePath;

    /// <summary>
    /// Saves modified piece offsets back to the crafting pieces XML file.
    /// Only updates pieces that have changed offsets.
    /// </summary>
    /// <param name="modifiedOffsets">List of pieces with their new offset values.</param>
    /// <returns>Number of pieces updated.</returns>
    public int SavePieceOffsets(IEnumerable<PieceOffsets> modifiedOffsets)
    {
        if (string.IsNullOrEmpty(_piecesFilePath) || !File.Exists(_piecesFilePath))
        {
            throw new InvalidOperationException("Crafting pieces file path not set or file doesn't exist.");
        }

        var doc = XDocument.Load(_piecesFilePath, LoadOptions.PreserveWhitespace);
        var root = doc.Root;
        if (root == null) return 0;

        int updatedCount = 0;

        foreach (var offsets in modifiedOffsets)
        {
            // Find the piece element by ID
            var pieceElement = root.Elements("CraftingPiece")
                .FirstOrDefault(e => e.Attribute("id")?.Value == offsets.PieceId);

            if (pieceElement == null)
            {
                Console.WriteLine($"[SavePieceOffsets] Piece not found: {offsets.PieceId}");
                continue;
            }

            // Get or create BuildData element
            var buildData = pieceElement.Element("BuildData");
            if (buildData == null)
            {
                // Insert BuildData after the CraftingPiece opening tag attributes
                // Try to insert before other child elements, or as first child
                var firstChild = pieceElement.Elements().FirstOrDefault();
                buildData = new XElement("BuildData");
                if (firstChild != null)
                {
                    firstChild.AddBeforeSelf(buildData);
                }
                else
                {
                    pieceElement.Add(buildData);
                }
            }

            // Update offset attributes
            var pieceOffsetStr = offsets.PieceOffset.ToString("0.##", CultureInfo.InvariantCulture);
            var nextOffsetStr = offsets.NextPieceOffset.ToString("0.##", CultureInfo.InvariantCulture);
            var prevOffsetStr = offsets.PreviousPieceOffset.ToString("0.##", CultureInfo.InvariantCulture);

            // Check if any values actually changed
            var currentPieceOffset = buildData.Attribute("piece_offset")?.Value;
            var currentNextOffset = buildData.Attribute("next_piece_offset")?.Value;
            var currentPrevOffset = buildData.Attribute("previous_piece_offset")?.Value;

            bool changed = false;

            if (currentPieceOffset != pieceOffsetStr && offsets.PieceOffset != 0)
            {
                buildData.SetAttributeValue("piece_offset", pieceOffsetStr);
                changed = true;
            }
            else if (offsets.PieceOffset == 0 && currentPieceOffset != null)
            {
                buildData.Attribute("piece_offset")?.Remove();
                changed = true;
            }

            if (currentNextOffset != nextOffsetStr && offsets.NextPieceOffset != 0)
            {
                buildData.SetAttributeValue("next_piece_offset", nextOffsetStr);
                changed = true;
            }
            else if (offsets.NextPieceOffset == 0 && currentNextOffset != null)
            {
                buildData.Attribute("next_piece_offset")?.Remove();
                changed = true;
            }

            if (currentPrevOffset != prevOffsetStr && offsets.PreviousPieceOffset != 0)
            {
                buildData.SetAttributeValue("previous_piece_offset", prevOffsetStr);
                changed = true;
            }
            else if (offsets.PreviousPieceOffset == 0 && currentPrevOffset != null)
            {
                buildData.Attribute("previous_piece_offset")?.Remove();
                changed = true;
            }

            // Remove BuildData element if it has no attributes
            if (!buildData.HasAttributes)
            {
                buildData.Remove();
            }

            if (changed)
            {
                // Also update the in-memory piece info
                if (_pieces.TryGetValue(offsets.PieceId, out var pieceInfo))
                {
                    pieceInfo.PieceOffset = offsets.PieceOffset;
                    pieceInfo.NextPieceOffset = offsets.NextPieceOffset;
                    pieceInfo.PreviousPieceOffset = offsets.PreviousPieceOffset;
                }

                updatedCount++;
                Console.WriteLine($"[SavePieceOffsets] Updated {offsets.PieceId}: offset={pieceOffsetStr}, next={nextOffsetStr}, prev={prevOffsetStr}");
            }
        }

        if (updatedCount > 0)
        {
            // Save with atomic write (temp file + rename)
            var tempPath = _piecesFilePath + ".tmp";
            using (var writer = new StreamWriter(tempPath, false, System.Text.Encoding.UTF8))
            {
                doc.Save(writer);
            }
            File.Delete(_piecesFilePath);
            File.Move(tempPath, _piecesFilePath);
            Console.WriteLine($"[SavePieceOffsets] Saved {updatedCount} piece(s) to {_piecesFilePath}");
        }

        return updatedCount;
    }
}
