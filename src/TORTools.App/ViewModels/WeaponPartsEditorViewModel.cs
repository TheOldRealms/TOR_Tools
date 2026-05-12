using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TORTools.Core.Services;

namespace TORTools.App.ViewModels;

/// <summary>
/// ViewModel for the 3D weapon parts editor.
/// </summary>
public partial class WeaponPartsEditorViewModel : ObservableObject
{
    private readonly CraftingPieceCatalogService _catalogService;
    private readonly FbxLoaderService _fbxLoaderService;
    private bool _isUpdatingOffsets; // Flag to prevent recursive updates when populating offset fields

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private string _statusMessage = "Not loaded";

    // Template selection
    [ObservableProperty]
    private ObservableCollection<CraftingTemplateInfo> _availableTemplates = new();

    [ObservableProperty]
    private CraftingTemplateInfo? _selectedTemplate;

    // Piece lists by type (filtered views)
    [ObservableProperty]
    private ObservableCollection<CraftingPieceInfo> _availableBlades = new();

    [ObservableProperty]
    private ObservableCollection<CraftingPieceInfo> _availableHandles = new();

    [ObservableProperty]
    private ObservableCollection<CraftingPieceInfo> _availableGuards = new();

    [ObservableProperty]
    private ObservableCollection<CraftingPieceInfo> _availablePommels = new();

    // Full unfiltered lists
    private List<CraftingPieceInfo> _allBlades = new();
    private List<CraftingPieceInfo> _allHandles = new();
    private List<CraftingPieceInfo> _allGuards = new();
    private List<CraftingPieceInfo> _allPommels = new();

    // Filter text for each piece type
    [ObservableProperty]
    private string _bladeFilter = string.Empty;

    [ObservableProperty]
    private string _handleFilter = string.Empty;

    [ObservableProperty]
    private string _guardFilter = string.Empty;

    [ObservableProperty]
    private string _pommelFilter = string.Empty;

    // Selected pieces
    [ObservableProperty]
    private CraftingPieceInfo? _selectedBlade;

    [ObservableProperty]
    private CraftingPieceInfo? _selectedHandle;

    [ObservableProperty]
    private CraftingPieceInfo? _selectedGuard;

    [ObservableProperty]
    private CraftingPieceInfo? _selectedPommel;

    // Scale factors (100 = 1.0x)
    [ObservableProperty]
    private int _bladeScale = 100;

    [ObservableProperty]
    private int _handleScale = 100;

    [ObservableProperty]
    private int _guardScale = 100;

    [ObservableProperty]
    private int _pommelScale = 100;

    // Editable offset values for each piece type
    [ObservableProperty]
    private decimal _bladePieceOffset;

    [ObservableProperty]
    private decimal _bladePrevOffset;

    [ObservableProperty]
    private decimal _bladeNextOffset;

    [ObservableProperty]
    private decimal _handlePieceOffset;

    [ObservableProperty]
    private decimal _handlePrevOffset;

    [ObservableProperty]
    private decimal _handleNextOffset;

    [ObservableProperty]
    private decimal _guardPieceOffset;

    [ObservableProperty]
    private decimal _guardPrevOffset;

    [ObservableProperty]
    private decimal _guardNextOffset;

    [ObservableProperty]
    private decimal _pommelPieceOffset;

    [ObservableProperty]
    private decimal _pommelPrevOffset;

    [ObservableProperty]
    private decimal _pommelNextOffset;

    // Calculated weapon stats
    [ObservableProperty]
    private string _totalLength = "0 cm";

    [ObservableProperty]
    private string _totalWeight = "0 kg";

    // Mesh data for viewport (mesh, offset in cm, scale factor)
    public ObservableCollection<(MeshData mesh, Vector3 offset, float scale)> LoadedMeshes { get; } = new();

    // Events for view to subscribe to
    public event Action? MeshesChanged;
    public event Action<CraftingPieceInfo?>? PieceHighlighted;

    public WeaponPartsEditorViewModel(CraftingPieceCatalogService catalogService, FbxLoaderService fbxLoaderService)
    {
        _catalogService = catalogService;
        _fbxLoaderService = fbxLoaderService;
    }

    /// <summary>
    /// Initializes the editor with paths to the module data.
    /// </summary>
    public void Initialize(string moduleDataPath, string assetSourcesPath)
    {
        try
        {
            Console.WriteLine($"[WeaponPartsEditor] Initialize called");
            Console.WriteLine($"[WeaponPartsEditor] moduleDataPath: {moduleDataPath}");
            Console.WriteLine($"[WeaponPartsEditor] assetSourcesPath: {assetSourcesPath}");

            StatusMessage = "Loading crafting data...";

            // Check if paths exist
            var piecesPath = Path.Combine(moduleDataPath, "tor_crafting_pieces.xml");
            var templatesPath = Path.Combine(moduleDataPath, "tor_crafting_templates.xml");
            Console.WriteLine($"[WeaponPartsEditor] Looking for pieces at: {piecesPath}");
            Console.WriteLine($"[WeaponPartsEditor] Pieces file exists: {File.Exists(piecesPath)}");
            Console.WriteLine($"[WeaponPartsEditor] Looking for templates at: {templatesPath}");
            Console.WriteLine($"[WeaponPartsEditor] Templates file exists: {File.Exists(templatesPath)}");

            // Load crafting catalog
            _catalogService.Load(moduleDataPath);

            Console.WriteLine($"[WeaponPartsEditor] Catalog loaded: {_catalogService.PieceCount} pieces, {_catalogService.TemplateCount} templates");

            // Initialize FBX loader
            _fbxLoaderService.Initialize(assetSourcesPath);

            // Populate templates
            AvailableTemplates.Clear();
            foreach (var template in _catalogService.GetAllTemplates()
                .OrderBy(t => t.Id))
            {
                AvailableTemplates.Add(template);
            }

            Console.WriteLine($"[WeaponPartsEditor] Added {AvailableTemplates.Count} templates to dropdown");

            IsLoaded = true;
            StatusMessage = $"Loaded {_catalogService.PieceCount} pieces, {_catalogService.TemplateCount} templates";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WeaponPartsEditor] ERROR: {ex}");
            StatusMessage = $"Load error: {ex.Message}";
        }
    }

    /// <summary>
    /// Sets initial piece selections (for editing existing weapon).
    /// If a piece is hidden but part of the weapon, it will be added to the available list.
    /// </summary>
    public void SetInitialPieces(string? bladeId, string? handleId, string? guardId, string? pommelId,
        int bladeScale = 100, int handleScale = 100, int guardScale = 100, int pommelScale = 100)
    {
        Console.WriteLine($"[WeaponPartsEditor] SetInitialPieces: blade={bladeId}, handle={handleId}, guard={guardId}, pommel={pommelId}");

        // Helper to find or add a piece (handles hidden pieces that are part of the weapon)
        CraftingPieceInfo? FindOrAddPiece(string? pieceId, ObservableCollection<CraftingPieceInfo> availableList, List<CraftingPieceInfo> allList)
        {
            if (string.IsNullOrEmpty(pieceId)) return null;

            // First try the available list
            var piece = availableList.FirstOrDefault(p => p.Id == pieceId);
            if (piece != null)
            {
                Console.WriteLine($"[WeaponPartsEditor] Found {pieceId} in available list");
                return piece;
            }

            // Try the full list (includes all non-hidden pieces for current template)
            piece = allList.FirstOrDefault(p => p.Id == pieceId);
            if (piece != null)
            {
                Console.WriteLine($"[WeaponPartsEditor] Found {pieceId} in full list, adding to available");
                availableList.Add(piece);
                return piece;
            }

            // Not in template's pieces - try to get from catalog (handles hidden pieces)
            piece = _catalogService.GetPiece(pieceId);
            if (piece != null)
            {
                Console.WriteLine($"[WeaponPartsEditor] Found {pieceId} in catalog (hidden={piece.IsHidden}), adding to available");
                allList.Add(piece);
                availableList.Add(piece);
                return piece;
            }

            Console.WriteLine($"[WeaponPartsEditor] Could not find {pieceId} anywhere");
            return null;
        }

        SelectedBlade = FindOrAddPiece(bladeId, AvailableBlades, _allBlades);
        SelectedHandle = FindOrAddPiece(handleId, AvailableHandles, _allHandles);
        SelectedGuard = FindOrAddPiece(guardId, AvailableGuards, _allGuards);
        SelectedPommel = FindOrAddPiece(pommelId, AvailablePommels, _allPommels);

        BladeScale = bladeScale;
        HandleScale = handleScale;
        GuardScale = guardScale;
        PommelScale = pommelScale;

        UpdateAssembly();
    }

    partial void OnSelectedTemplateChanged(CraftingTemplateInfo? value)
    {
        if (value == null)
        {
            ClearPieceLists();
            return;
        }

        // Populate piece lists based on template
        PopulatePieceLists(value);
    }

    private void ClearPieceLists()
    {
        _allBlades.Clear();
        _allHandles.Clear();
        _allGuards.Clear();
        _allPommels.Clear();
        AvailableBlades.Clear();
        AvailableHandles.Clear();
        AvailableGuards.Clear();
        AvailablePommels.Clear();
        BladeFilter = string.Empty;
        HandleFilter = string.Empty;
        GuardFilter = string.Empty;
        PommelFilter = string.Empty;
        SelectedBlade = null;
        SelectedHandle = null;
        SelectedGuard = null;
        SelectedPommel = null;
    }

    private void PopulatePieceLists(CraftingTemplateInfo template)
    {
        Console.WriteLine($"[WeaponPartsEditor] PopulatePieceLists called for template: {template.Id}");
        Console.WriteLine($"[WeaponPartsEditor] Template has {template.UsablePieceIds.Count} usable piece IDs");

        ClearPieceLists();

        var pieces = _catalogService.GetPiecesForTemplate(template.Id).ToList();
        Console.WriteLine($"[WeaponPartsEditor] Found {pieces.Count} pieces for template");

        // Note: is_hidden is for in-game smithing UI, not for dev tools - show all pieces
        foreach (var piece in pieces)
        {
            switch (piece.PieceType)
            {
                case "Blade":
                    _allBlades.Add(piece);
                    break;
                case "Handle":
                    _allHandles.Add(piece);
                    break;
                case "Guard":
                    _allGuards.Add(piece);
                    break;
                case "Pommel":
                    _allPommels.Add(piece);
                    break;
            }
        }

        // Sort by ID
        _allBlades = _allBlades.OrderBy(p => p.Id).ToList();
        _allHandles = _allHandles.OrderBy(p => p.Id).ToList();
        _allGuards = _allGuards.OrderBy(p => p.Id).ToList();
        _allPommels = _allPommels.OrderBy(p => p.Id).ToList();

        Console.WriteLine($"[WeaponPartsEditor] After populate: Blades={_allBlades.Count}, Handles={_allHandles.Count}, Guards={_allGuards.Count}, Pommels={_allPommels.Count}");

        // Apply filters (initially empty, so shows all)
        ApplyBladeFilter();
        ApplyHandleFilter();
        ApplyGuardFilter();
        ApplyPommelFilter();
    }

    // Filter change handlers
    partial void OnBladeFilterChanged(string value) => ApplyBladeFilter();
    partial void OnHandleFilterChanged(string value) => ApplyHandleFilter();
    partial void OnGuardFilterChanged(string value) => ApplyGuardFilter();
    partial void OnPommelFilterChanged(string value) => ApplyPommelFilter();

    private void ApplyBladeFilter()
    {
        var selected = SelectedBlade;
        AvailableBlades.Clear();
        var filtered = string.IsNullOrWhiteSpace(BladeFilter)
            ? _allBlades
            : _allBlades.Where(p => p.Id.Contains(BladeFilter, StringComparison.OrdinalIgnoreCase));
        foreach (var item in filtered)
            AvailableBlades.Add(item);
        // Restore selection if still in filtered list
        if (selected != null && AvailableBlades.Contains(selected))
            SelectedBlade = selected;
    }

    private void ApplyHandleFilter()
    {
        var selected = SelectedHandle;
        AvailableHandles.Clear();
        var filtered = string.IsNullOrWhiteSpace(HandleFilter)
            ? _allHandles
            : _allHandles.Where(p => p.Id.Contains(HandleFilter, StringComparison.OrdinalIgnoreCase));
        foreach (var item in filtered)
            AvailableHandles.Add(item);
        if (selected != null && AvailableHandles.Contains(selected))
            SelectedHandle = selected;
    }

    private void ApplyGuardFilter()
    {
        var selected = SelectedGuard;
        AvailableGuards.Clear();
        var filtered = string.IsNullOrWhiteSpace(GuardFilter)
            ? _allGuards
            : _allGuards.Where(p => p.Id.Contains(GuardFilter, StringComparison.OrdinalIgnoreCase));
        foreach (var item in filtered)
            AvailableGuards.Add(item);
        if (selected != null && AvailableGuards.Contains(selected))
            SelectedGuard = selected;
    }

    private void ApplyPommelFilter()
    {
        var selected = SelectedPommel;
        AvailablePommels.Clear();
        var filtered = string.IsNullOrWhiteSpace(PommelFilter)
            ? _allPommels
            : _allPommels.Where(p => p.Id.Contains(PommelFilter, StringComparison.OrdinalIgnoreCase));
        foreach (var item in filtered)
            AvailablePommels.Add(item);
        if (selected != null && AvailablePommels.Contains(selected))
            SelectedPommel = selected;
    }

    partial void OnSelectedBladeChanged(CraftingPieceInfo? value)
    {
        // Populate offset values from the selected piece
        if (value != null)
        {
            Console.WriteLine($"[WeaponPartsEditor] Blade selected: {value.Id} - Offset={value.PieceOffset}, Prev={value.PreviousPieceOffset}, Next={value.NextPieceOffset}");
            _isUpdatingOffsets = true;
            BladePieceOffset = (decimal)value.PieceOffset;
            BladePrevOffset = (decimal)value.PreviousPieceOffset;
            BladeNextOffset = (decimal)value.NextPieceOffset;
            _isUpdatingOffsets = false;
        }
        UpdateAssembly();
    }

    partial void OnSelectedHandleChanged(CraftingPieceInfo? value)
    {
        if (value != null)
        {
            Console.WriteLine($"[WeaponPartsEditor] Handle selected: {value.Id} - Offset={value.PieceOffset}, Prev={value.PreviousPieceOffset}, Next={value.NextPieceOffset}");
            _isUpdatingOffsets = true;
            HandlePieceOffset = (decimal)value.PieceOffset;
            HandlePrevOffset = (decimal)value.PreviousPieceOffset;
            HandleNextOffset = (decimal)value.NextPieceOffset;
            _isUpdatingOffsets = false;
        }
        UpdateAssembly();
    }

    partial void OnSelectedGuardChanged(CraftingPieceInfo? value)
    {
        if (value != null)
        {
            _isUpdatingOffsets = true;
            GuardPieceOffset = (decimal)value.PieceOffset;
            GuardPrevOffset = (decimal)value.PreviousPieceOffset;
            GuardNextOffset = (decimal)value.NextPieceOffset;
            _isUpdatingOffsets = false;
        }
        UpdateAssembly();
    }

    partial void OnSelectedPommelChanged(CraftingPieceInfo? value)
    {
        if (value != null)
        {
            _isUpdatingOffsets = true;
            PommelPieceOffset = (decimal)value.PieceOffset;
            PommelPrevOffset = (decimal)value.PreviousPieceOffset;
            PommelNextOffset = (decimal)value.NextPieceOffset;
            _isUpdatingOffsets = false;
        }
        UpdateAssembly();
    }

    partial void OnBladeScaleChanged(int value) => UpdateAssembly();
    partial void OnHandleScaleChanged(int value) => UpdateAssembly();
    partial void OnGuardScaleChanged(int value) => UpdateAssembly();
    partial void OnPommelScaleChanged(int value) => UpdateAssembly();

    // Offset change handlers - trigger assembly update when user edits offsets
    partial void OnBladePieceOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnBladePrevOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnBladeNextOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnHandlePieceOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnHandlePrevOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnHandleNextOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnGuardPieceOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnGuardPrevOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnGuardNextOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnPommelPieceOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnPommelPrevOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }
    partial void OnPommelNextOffsetChanged(decimal value) { if (!_isUpdatingOffsets) UpdateAssembly(); }

    private void UpdateAssembly()
    {
        if (SelectedTemplate == null) return;

        LoadedMeshes.Clear();

        // Build selected pieces with their scales
        var selectedPieces = new Dictionary<string, (CraftingPieceInfo piece, float scale)>();
        if (SelectedHandle != null) selectedPieces["Handle"] = (SelectedHandle, HandleScale / 100f);
        if (SelectedGuard != null) selectedPieces["Guard"] = (SelectedGuard, GuardScale / 100f);
        if (SelectedBlade != null) selectedPieces["Blade"] = (SelectedBlade, BladeScale / 100f);
        if (SelectedPommel != null) selectedPieces["Pommel"] = (SelectedPommel, PommelScale / 100f);

        if (selectedPieces.Count == 0)
        {
            MeshesChanged?.Invoke();
            UpdateStats();
            return;
        }

        // Calculate positions accounting for scale factors
        var positions = CalculateAssemblyPositionsWithScale(SelectedTemplate, selectedPieces);

        // Load meshes with their positions and scales
        foreach (var (pieceType, position, scale) in positions)
        {
            if (!selectedPieces.TryGetValue(pieceType, out var pieceData))
                continue;

            var piece = pieceData.piece;
            if (string.IsNullOrEmpty(piece.MeshName))
                continue;

            var mesh = _fbxLoaderService.LoadMesh(piece.MeshName);
            if (mesh != null)
            {
                LoadedMeshes.Add((mesh, position, scale));
            }
        }

        MeshesChanged?.Invoke();
        UpdateStats();
    }

    /// <summary>
    /// Calculates positions for all pieces accounting for individual scale factors.
    /// Uses the editable offset values from the UI instead of original piece values.
    /// </summary>
    private List<(string pieceType, Vector3 position, float scale)> CalculateAssemblyPositionsWithScale(
        CraftingTemplateInfo template,
        Dictionary<string, (CraftingPieceInfo piece, float scale)> selectedPieces)
    {
        var result = new List<(string pieceType, Vector3 position, float scale)>();

        // Sort by build order
        var orderedTypes = template.PieceDatas
            .Where(pd => pd.BuildOrder >= 0)
            .OrderBy(pd => pd.BuildOrder)
            .ToList();

        var negativeTypes = template.PieceDatas
            .Where(pd => pd.BuildOrder < 0)
            .OrderByDescending(pd => pd.BuildOrder)
            .ToList();

        // Track current position along the weapon axis
        float currentY = 0;

        // Process positive build order pieces (Handle -> Guard -> Blade)
        string? previousPieceType = null;
        float previousScale = 1f;
        float previousLength = 0f;

        foreach (var pieceTypeData in orderedTypes)
        {
            var pieceType = pieceTypeData.PieceType;
            if (!selectedPieces.TryGetValue(pieceType, out var pieceData))
                continue;

            var piece = pieceData.piece;
            var scale = pieceData.scale;

            // Get editable offset values for this piece type
            var (pieceOffset, prevOffset, nextOffset) = GetEditableOffsets(pieceType);

            if (previousPieceType == null)
            {
                // First piece starts at origin + its piece_offset
                currentY = pieceOffset;
            }
            else
            {
                // Get previous piece's next offset from editable values
                var (_, _, prevNextOffset) = GetEditableOffsets(previousPieceType);
                // Calculate gap: previous piece's next_offset + current piece's previous_offset
                var gap = prevNextOffset + prevOffset;
                // Add previous piece's scaled length + gap
                currentY += (previousLength * previousScale) + gap;
            }

            result.Add((pieceType, new Vector3(0, currentY, 0), scale));

            previousPieceType = pieceType;
            previousScale = scale;
            previousLength = piece.Length;
        }

        // Process negative build order pieces (Pommel attaches to back of Handle)
        if (selectedPieces.TryGetValue("Handle", out var handleData))
        {
            var handlePiece = handleData.piece;
            var handlePosition = result.FirstOrDefault(r => r.pieceType == "Handle").position;
            var (_, handlePrevOffset, _) = GetEditableOffsets("Handle");

            foreach (var pieceTypeData in negativeTypes)
            {
                var pieceType = pieceTypeData.PieceType;
                if (!selectedPieces.TryGetValue(pieceType, out var pieceData))
                    continue;

                var piece = pieceData.piece;
                var scale = pieceData.scale;
                var (_, _, pommelNextOffset) = GetEditableOffsets(pieceType);

                // Pommel attaches to back of handle
                // Position = handle position - handle's previous_offset - pommel's next_offset - pommel's scaled length
                var pommelY = handlePosition.Y - handlePrevOffset - pommelNextOffset - (piece.Length * scale);
                result.Add((pieceType, new Vector3(0, pommelY, 0), scale));
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the editable offset values for a given piece type.
    /// </summary>
    private (float pieceOffset, float prevOffset, float nextOffset) GetEditableOffsets(string pieceType)
    {
        return pieceType switch
        {
            "Blade" => ((float)BladePieceOffset, (float)BladePrevOffset, (float)BladeNextOffset),
            "Handle" => ((float)HandlePieceOffset, (float)HandlePrevOffset, (float)HandleNextOffset),
            "Guard" => ((float)GuardPieceOffset, (float)GuardPrevOffset, (float)GuardNextOffset),
            "Pommel" => ((float)PommelPieceOffset, (float)PommelPrevOffset, (float)PommelNextOffset),
            _ => (0f, 0f, 0f)
        };
    }

    private void UpdateStats()
    {
        float totalLength = 0;
        float totalWeight = 0;

        if (SelectedBlade != null)
        {
            totalLength += SelectedBlade.Length * (BladeScale / 100f);
            totalWeight += SelectedBlade.Weight;
        }
        if (SelectedHandle != null)
        {
            totalLength += SelectedHandle.Length * (HandleScale / 100f);
            totalWeight += SelectedHandle.Weight;
        }
        if (SelectedGuard != null)
        {
            totalLength += SelectedGuard.Length * (GuardScale / 100f);
            totalWeight += SelectedGuard.Weight;
        }
        if (SelectedPommel != null)
        {
            totalLength += SelectedPommel.Length * (PommelScale / 100f);
            totalWeight += SelectedPommel.Weight;
        }

        TotalLength = $"{totalLength:F1} cm";
        TotalWeight = $"{totalWeight:F2} kg";
    }

    [RelayCommand]
    private void HighlightPiece(CraftingPieceInfo? piece)
    {
        PieceHighlighted?.Invoke(piece);
    }

    [RelayCommand]
    private void ResetScales()
    {
        BladeScale = 100;
        HandleScale = 100;
        GuardScale = 100;
        PommelScale = 100;
    }

    /// <summary>
    /// Gets the current selection as piece IDs and scales.
    /// </summary>
    public (string? bladeId, string? handleId, string? guardId, string? pommelId,
        int bladeScale, int handleScale, int guardScale, int pommelScale) GetSelection()
    {
        return (
            SelectedBlade?.Id,
            SelectedHandle?.Id,
            SelectedGuard?.Id,
            SelectedPommel?.Id,
            BladeScale,
            HandleScale,
            GuardScale,
            PommelScale
        );
    }
}
