namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// One <c>&lt;SpriteCategory&gt;</c> entry from a module's
/// <c>GUI/SpriteParts/Config.xml</c>. Port of SpriteForge's
/// <c>internal/sprite/scan.go::catConfig</c>; the sheet size itself comes
/// from <see cref="AtlasOptions"/> (which Config.xml can't override).
/// </summary>
public sealed class CategoryConfig
{
    public bool PackAllSpritesToUniqueTextures { get; init; }
    public bool SingleChannel { get; init; }
    public bool NoAlphaChannel { get; init; }
    public bool AlwaysLoad { get; init; }

    /// <summary>
    /// <c>&lt;Compress/&gt;</c> presence — these categories end up stored
    /// as DXT5 in the TPAC. Non-compressed categories ship RGBA8.
    /// </summary>
    public bool Compress { get; init; }

    public int EdgeSize { get; init; }
    public bool HasEdge { get; init; }
}
