namespace TORTools.Core.SpritePack.Rdc;

/// <summary>
/// An entire RuntimeDataCache file (<c>Modules/&lt;Mod&gt;/RuntimeDataCache/&lt;pkg-guid&gt;.rdc</c>).
/// The on-disk header is just a magic + count + metadata-size prefix;
/// everything meaningful lives on the <see cref="Entries"/> list.
/// </summary>
public sealed class RdcFile
{
    public const uint Magic = 0x30434452; // "RDC0"
    public const int HeaderSize = 20;      // magic(4) + reserved(4) + count(4) + metadata-size(8)
    public const int EntrySize  = 145;     // guids(3*16) + sizes(3*8) + typeVersion(4) + hashBlock(64) + magic(4) + lz4(1)

    public List<RdcEntry> Entries { get; } = new();
}
