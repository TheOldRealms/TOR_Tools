namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// Case-insensitive ordinal compare with a case-sensitive tiebreak — mirrors
/// the sort NTFS uses when the Modding Kit calls
/// <c>Directory.GetFiles(dir, "*.png", AllDirectories)</c> on .NET Framework.
/// Port of <c>internal/sprite/scan.go::ntfsLess</c> in the Go fork.
/// </summary>
public sealed class NtfsComparer : IComparer<string>
{
    public static readonly NtfsComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        // Case-insensitive compare first, case-sensitive as tiebreak.
        var upperCompare = string.CompareOrdinal(x.ToUpperInvariant(), y.ToUpperInvariant());
        if (upperCompare != 0) return upperCompare;
        return string.CompareOrdinal(x, y);
    }
}
