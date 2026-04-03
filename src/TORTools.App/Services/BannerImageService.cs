using Avalonia.Media.Imaging;
using TORTools.Core.Services;

namespace TORTools.App.Services;

/// <summary>
/// Service for loading and caching banner images from the TOR_Armory asset sources.
/// </summary>
public class BannerImageService : IDisposable
{
    private readonly string? _bannerIconsPath;
    private readonly Dictionary<string, Bitmap?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new BannerImageService.
    /// </summary>
    /// <param name="armoryAssetSourcesPath">Path to TOR_Armory/AssetSources</param>
    public BannerImageService(string? armoryAssetSourcesPath)
    {
        if (!string.IsNullOrEmpty(armoryAssetSourcesPath))
        {
            _bannerIconsPath = Path.Combine(armoryAssetSourcesPath, "extra_assets", "ui", "faction_banner_icons");
            if (!Directory.Exists(_bannerIconsPath))
            {
                Console.WriteLine($"[BannerImageService] Banner icons path not found: {_bannerIconsPath}");
                _bannerIconsPath = null;
            }
            else
            {
                Console.WriteLine($"[BannerImageService] Initialized with path: {_bannerIconsPath}");
            }
        }
    }

    /// <summary>
    /// Gets a banner image for the given banner_key attribute value.
    /// </summary>
    /// <param name="bannerKey">The banner_key from XML (e.g., "11.116.149...:kingdom_averland")</param>
    /// <returns>The loaded Bitmap, or null if not found</returns>
    public Bitmap? GetBannerImage(string? bannerKey)
    {
        if (string.IsNullOrEmpty(_bannerIconsPath) || string.IsNullOrEmpty(bannerKey))
            return null;

        // Extract image name from banner_key (part after the colon)
        var imageName = FactionCatalogService.ExtractBannerImageName(bannerKey);
        if (string.IsNullOrEmpty(imageName))
            return null;

        return GetImageByName(imageName);
    }

    /// <summary>
    /// Gets a banner image by its name (without extension).
    /// </summary>
    /// <param name="imageName">The image name (e.g., "kingdom_averland")</param>
    /// <returns>The loaded Bitmap, or null if not found</returns>
    public Bitmap? GetImageByName(string? imageName)
    {
        if (string.IsNullOrEmpty(_bannerIconsPath) || string.IsNullOrEmpty(imageName))
            return null;

        lock (_cacheLock)
        {
            // Check cache first
            if (_imageCache.TryGetValue(imageName, out var cached))
                return cached;

            // Try to load the image
            var imagePath = Path.Combine(_bannerIconsPath, $"{imageName}.png");
            Bitmap? bitmap = null;

            if (File.Exists(imagePath))
            {
                try
                {
                    bitmap = new Bitmap(imagePath);
                    Console.WriteLine($"[BannerImageService] Loaded banner: {imageName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BannerImageService] Error loading {imagePath}: {ex.Message}");
                }
            }

            // Cache the result (even if null, to avoid repeated lookups)
            _imageCache[imageName] = bitmap;
            return bitmap;
        }
    }

    /// <summary>
    /// Gets the full path to a banner image file.
    /// </summary>
    /// <param name="bannerKey">The banner_key from XML</param>
    /// <returns>Full path to the PNG file, or null if not found</returns>
    public string? GetBannerImagePath(string? bannerKey)
    {
        if (string.IsNullOrEmpty(_bannerIconsPath) || string.IsNullOrEmpty(bannerKey))
            return null;

        var imageName = FactionCatalogService.ExtractBannerImageName(bannerKey);
        if (string.IsNullOrEmpty(imageName))
            return null;

        var imagePath = Path.Combine(_bannerIconsPath, $"{imageName}.png");
        return File.Exists(imagePath) ? imagePath : null;
    }

    /// <summary>
    /// Clears the image cache.
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            foreach (var bitmap in _imageCache.Values)
            {
                bitmap?.Dispose();
            }
            _imageCache.Clear();
        }
    }

    /// <summary>
    /// Whether the service is properly initialized with a valid path.
    /// </summary>
    public bool IsInitialized => !string.IsNullOrEmpty(_bannerIconsPath);

    /// <summary>
    /// Gets all available banner image names (without extension).
    /// </summary>
    public IEnumerable<string> GetAvailableBannerNames()
    {
        if (string.IsNullOrEmpty(_bannerIconsPath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(_bannerIconsPath, "*.png"))
        {
            yield return Path.GetFileNameWithoutExtension(file);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ClearCache();
        GC.SuppressFinalize(this);
    }
}
