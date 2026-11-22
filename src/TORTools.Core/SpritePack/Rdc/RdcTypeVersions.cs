using TORTools.Core.SpritePack.Tpac;

namespace TORTools.Core.SpritePack.Rdc;

/// <summary>
/// The engine writes a different <c>type_version</c> int32 per data-type
/// GUID. From <c>bannerlord-editor-free-sdk/sdk/tpac.py:60-90</c>:
/// textures 0, mesh vertex stream 1, clips 2.
/// </summary>
public static class RdcTypeVersions
{
    /// <summary>
    /// Type GUID of the "Optimized animation" clip cache. Note: the
    /// editor-free-sdk comment writes this as "6f131e6c" in file-byte
    /// order; the .NET-parseable string reverses the first chunk's
    /// endianness.
    /// </summary>
    public static readonly Guid ClipCache = new("6c1e136f-d0b7-41ff-bb7a-275f3630db25");

    // Mesh vertex-stream GUID intentionally not catalogued yet — the first
    // mesh fixture will nail it down. Until then, the reader just preserves
    // whatever type_version the file had on round-trip (see RdcReader).

    public static uint ForType(Guid typeGuid)
    {
        if (typeGuid == TpacGuids.TexPixels) return 0;
        if (typeGuid == ClipCache) return 2;
        // Any unknown type: default to 0. The reader round-trips unknown
        // type versions verbatim via RdcEntry.TypeVersionOverride, so this
        // default only applies when a caller builds a new entry from
        // scratch.
        return 0;
    }
}
