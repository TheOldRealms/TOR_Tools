namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// One <c>&lt;SpritePart&gt;</c> entry — a single PNG placed at a specific
/// pixel offset on a specific sheet of a specific category. Equivalent of
/// SpriteForge's <c>internal/sprite/packer.go::part</c>.
/// </summary>
public sealed class SpritePart
{
    /// <summary>XML name — may contain '\' for nested folders.</summary>
    public required string Name { get; init; }

    public required string Category { get; init; }
    public required string Path { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int SheetId { get; set; }
    public int SheetX { get; set; }
    public int SheetY { get; set; }
    public NineRegion? Nine { get; set; }
}

/// <summary>
/// Nine-region metadata attached to a sprite part (border-slice sprite).
/// Mirrors SpriteForge's <c>internal/sprite/scan.go::nineRegion</c>.
/// </summary>
public sealed class NineRegion
{
    public required string Name { get; init; }
    public int LeftWidth { get; init; }
    public int RightWidth { get; init; }
    public int TopHeight { get; init; }
    public int BottomHeight { get; init; }
}

/// <summary>
/// One sprite-category output with its resolved sheets.
/// </summary>
public sealed class SpriteCategory
{
    public required string Name { get; init; }
    public required CategoryConfig Config { get; init; }
    public List<SpritePart> Parts { get; } = new();
    public List<Sheet> Sheets { get; } = new();

    /// <summary>
    /// Sheet count written into the <c>&lt;SpriteSheetCount&gt;</c> field
    /// of <c>SpriteData.xml</c>. Usually equals <c>Sheets.Count</c> but is
    /// kept distinct so the XML writer can reproduce the Modding Kit's
    /// behaviour of reporting a lower-than-actual count for empty final
    /// sheets.
    /// </summary>
    public int SpriteSheetCount { get; set; }
}

/// <summary>
/// One packed sheet (a rectangle of pixels big enough to hold its sprites).
/// Equivalent of SpriteForge's <c>internal/sprite/packer.go::sheet</c>.
/// </summary>
public sealed class Sheet
{
    public required int Width { get; set; }
    public required int Height { get; set; }
    public int EdgeSize { get; init; }
    public int Id { get; set; }
    public List<SpritePart> Parts { get; } = new();
}

/// <summary>
/// One entry in the <c>&lt;Sprites&gt;</c> block of <c>SpriteData.xml</c>.
/// Nine-region sprites carry extra border fields; everything else is a
/// <c>&lt;GenericSprite&gt;</c> with just a name and a part reference.
/// </summary>
public sealed class SpriteEntry
{
    public required string Name { get; init; }
    public required SpritePart Part { get; init; }

    /// <summary>Non-null means write a <c>&lt;NineRegionSprite&gt;</c>.</summary>
    public NineRegion? Nine { get; init; }
}
