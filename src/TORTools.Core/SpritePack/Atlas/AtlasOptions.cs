namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// Pixel-format override for <see cref="AtlasBuilder"/>. <c>Auto</c>
/// honours each category's <c>&lt;Compress/&gt;</c> flag (DXT5 if set,
/// RGBA8 otherwise); the other values override that per-category choice
/// globally.
/// </summary>
public enum FormatMode { Auto, Rgba8, Dxt5 }

/// <summary>
/// Options passed to <see cref="AtlasBuilder.Build"/>. Zero-value defaults
/// match SpriteForge's own defaults.
/// </summary>
public sealed class AtlasOptions
{
    /// <summary>
    /// When true, the writer emits one <c>.tpac</c> per sprite category
    /// (<c>&lt;Mod&gt;_&lt;category&gt;.tpac</c>) instead of a single
    /// combined <c>&lt;Mod&gt;_ui.tpac</c>. Matches the <c>-per-category</c>
    /// flag we added to the Go sprite-pack fork.
    /// </summary>
    public bool PerCategory { get; init; }

    /// <summary>
    /// Combined-mode package name (without .tpac). Null = <c>&lt;Mod&gt;_ui</c>.
    /// Ignored when <see cref="PerCategory"/> is true.
    /// </summary>
    public string? OutName { get; init; }

    public int SheetSize { get; init; } = 4096;
    public int EdgeSize { get; init; } = 4;
    public FormatMode Format { get; init; } = FormatMode.Auto;

    /// <summary>
    /// Default texture flags written into every texture asset. Mirrors
    /// SpriteForge's <c>DefaultFlags = ["dont_degrade"]</c>.
    /// </summary>
    public IReadOnlyList<string> TextureFlags { get; init; } = new[] { "dont_degrade" };

    public bool WriteSheets { get; init; } = true;
    public bool WriteTpac { get; init; } = true;
    public bool WriteRdc { get; init; } = true;
}

/// <summary>
/// Summary of a single <see cref="AtlasBuilder.Build"/> run.
/// </summary>
public sealed class BuildReport
{
    public required string Module { get; init; }
    public required string ModuleDir { get; init; }
    public List<string> Categories { get; init; } = new();
    public List<SheetInfo> Sheets { get; init; } = new();
    public string? XmlPath { get; set; }
    public string? SheetDir { get; set; }
    public List<string> TpacPaths { get; } = new();
    public List<long> TpacSizes { get; } = new();
    public List<string> RdcPaths { get; } = new();
    public List<string> Warnings { get; } = new();
}

public sealed class SheetInfo
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string Format { get; init; }
}
