namespace TORTools.Core.SpritePack.Tpac;

/// <summary>
/// Texture-asset metadata as written by the Modding Kit, field by field.
/// Field names match the szszss/TpacTool decomp (lots of Unknown* because
/// the Modding Kit doesn't publish semantics); the layout was additionally
/// verified against TOR_Armory's ui_abilityicons_1_tex.tpac.
/// </summary>
public sealed class TextureMetadata
{
    /// <summary>Version of the metadata record itself (observed: 2 or 3).</summary>
    public uint MetadataVersion { get; set; } = 3;

    public Guid BillboardMaterialGuid { get; set; }
    public uint UnknownUint1 { get; set; }

    /// <summary>Source path like "$BASE/Modules/&lt;Mod&gt;/AssetSources/GauntletUI/&lt;name&gt;.png".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>XXH64 of the source PNG bytes.</summary>
    public ulong SourceHash { get; set; }

    public bool UnknownBool { get; set; }
    public uint UnknownUint2 { get; set; }

    /// <summary>Texture flags (e.g. "dont_degrade", "dont_delay_loading").</summary>
    public List<string> Flags { get; set; } = new();

    /// <summary>Observed values: 0 before 1.7.0, 1 since 1.7.0, 2 on newer Armory TPACs.</summary>
    public uint UnknownUint3 { get; set; }

    public byte UnknownByte { get; set; }
    public uint Width { get; set; }
    public uint Height { get; set; }

    /// <summary>Observed: always 1.</summary>
    public uint UnknownUint4 { get; set; } = 1;

    public byte MipmapCount { get; set; }
    public ushort ArrayCount { get; set; } = 1;

    /// <summary>Format string the game parses — "DXT5", "R8G8B8A8_UNORM", "BC7", etc.</summary>
    public string Format { get; set; } = string.Empty;

    public uint UnknownUint5 { get; set; }
    public List<string> SystemFlags { get; set; } = new();

    // Present when UnknownUint3 > 0. Observed on 1.7.0+ textures.
    public uint UnknownUint6 { get; set; }
    public uint UnknownUint7 { get; set; }

    /// <summary>
    /// Each tuple is (asset type guid, asset guid). Observed empty on UI
    /// atlases; TaleWorlds uses it for SDF textures generated from other
    /// textures.
    /// </summary>
    public List<(Guid TypeGuid, Guid AssetGuid)> GeneratedAssets { get; set; } = new();

    /// <summary>
    /// Random-looking 64-bit value since 1.5.7 — likely a Granite id or
    /// checksum. Preserved verbatim.
    /// </summary>
    public ulong UnknownUlong2 { get; set; }

    /// <summary>
    /// Any bytes at the end of the metadata block our parser doesn't yet
    /// understand (newer Bannerlord releases have added trailing fields).
    /// Preserved verbatim so round-trip is byte-identical.
    /// </summary>
    public byte[] TrailingMetadata { get; set; } = Array.Empty<byte>();
}
