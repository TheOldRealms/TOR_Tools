using SixLabors.ImageSharp;

namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// Walks <c>GUI/SpriteParts/</c> and returns every PNG file path (relative
/// to <paramref name="root"/>) in the order the Modding Kit would have
/// produced them: depth-first, a directory's files before its subdirectories,
/// NTFS sort. Port of SpriteForge's <c>internal/sprite/scan.go::scanPNGs</c>.
/// </summary>
public static class SpriteScanner
{
    /// <summary>
    /// Returns every <c>.png</c> file in <paramref name="root"/> as a path
    /// relative to it, using OS directory separators. Non-existent root
    /// returns an empty list.
    /// </summary>
    public static List<string> ScanPngs(string root)
    {
        var results = new List<string>();
        if (!Directory.Exists(root)) return results;
        Walk(root, string.Empty, results);
        return results;
    }

    /// <summary>
    /// Returns the width + height of a PNG file via ImageSharp's identify
    /// path — reads only the IHDR chunk. Matches Go's
    /// <c>png.DecodeConfig</c> which also reads only the header.
    /// </summary>
    public static (int Width, int Height) GetPngSize(string path)
    {
        using var stream = File.OpenRead(path);
        var info = Image.Identify(stream);
        if (info is null)
            throw new InvalidDataException($"'{path}': not a decodable image.");
        return (info.Width, info.Height);
    }

    /// <summary>
    /// Splits a sprite-part name like <c>CareerSystem\icon_sword</c> into
    /// its category and pure name. Backslash is the hard-coded separator
    /// that matches the Modding Kit (Windows-authored even when we read on
    /// Linux). A name without a backslash uses the <c>Default</c> category
    /// per the Modding Kit's convention.
    /// </summary>
    public static (string Category, string PureName) SplitCategory(string name)
    {
        var slash = name.IndexOf('\\');
        return slash > 0
            ? (name[..slash], name[(slash + 1)..])
            : ("Default", name);
    }

    static void Walk(string dir, string relative, List<string> results)
    {
        // Enumerate once, then process files before subdirectories (both
        // in NTFS order).
        var entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList();
        entries.Sort((a, b) => NtfsComparer.Instance.Compare(a.Name, b.Name));

        foreach (var entry in entries)
        {
            if (entry is FileInfo file &&
                string.Equals(file.Extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(string.IsNullOrEmpty(relative) ? file.Name : Path.Combine(relative, file.Name));
            }
        }

        foreach (var entry in entries)
        {
            if (entry is DirectoryInfo subdir)
            {
                var nextRel = string.IsNullOrEmpty(relative) ? subdir.Name : Path.Combine(relative, subdir.Name);
                Walk(subdir.FullName, nextRel, results);
            }
        }
    }
}
