using Avalonia;
using Avalonia.Media.Imaging;
using NMSE.Data;

namespace NMSE.UI.Avalonia.Services;

/// <summary>
/// Loads item images from the filesystem and owns a bounded-resolution bitmap cache.
/// Files are decoded from memory so the installed resource bundle is never held open.
/// </summary>
public sealed class AvaloniaIconProvider : IDisposable
{
    public const int MaxCacheDimension = 128;

    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();
    private readonly string _iconsDirectory;
    private bool _disposed;

    public AvaloniaIconProvider(string iconsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconsDirectory);
        _iconsDirectory = Path.GetFullPath(iconsDirectory);
    }

    public string IconsDirectory => _iconsDirectory;

    /// <summary>Returns a cached icon, or <see langword="null"/> when it cannot be decoded.</summary>
    public Bitmap? GetIcon(string? iconFilename)
    {
        if (string.IsNullOrWhiteSpace(iconFilename))
            return null;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cache.TryGetValue(iconFilename, out Bitmap? cached))
                return cached;

            Bitmap? loaded = LoadAndScale(GetIconPath(iconFilename));
            _cache.Add(iconFilename, loaded);
            return loaded;
        }
    }

    /// <summary>
    /// Decodes every distinct database icon in parallel, then atomically merges the
    /// results into the live cache. Existing entries always win.
    /// </summary>
    public void PreloadIcons(GameItemDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        HashSet<string> pending = database.Items.Values
            .Select(item => item.Icon)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            pending.ExceptWith(_cache.Keys);
        }

        if (pending.Count == 0)
            return;

        var loaded = new Dictionary<string, Bitmap?>(StringComparer.OrdinalIgnoreCase);
        var loadedSync = new Lock();
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
        };

        Parallel.ForEach(pending, options, iconFilename =>
        {
            Bitmap? bitmap = LoadAndScale(GetIconPath(iconFilename));
            lock (loadedSync)
                loaded.Add(iconFilename, bitmap);
        });

        lock (_sync)
        {
            if (_disposed)
            {
                foreach (Bitmap? bitmap in loaded.Values)
                    bitmap?.Dispose();
                throw new ObjectDisposedException(nameof(AvaloniaIconProvider));
            }

            foreach ((string name, Bitmap? bitmap) in loaded)
            {
                if (_cache.TryAdd(name, bitmap))
                    continue;

                // A foreground lookup populated this key while preload was running.
                bitmap?.Dispose();
            }
        }
    }

    public Bitmap? GetIconForItem(string? itemId, GameItemDatabase? database)
    {
        if (database is null || string.IsNullOrWhiteSpace(itemId))
            return null;

        GameItem? item = database.GetItem(itemId) ?? database.GetItem($"^{itemId}");
        return item is null ? null : GetIcon(item.Icon);
    }

    public Bitmap? GetClassIcon(string? itemClass) =>
        string.IsNullOrWhiteSpace(itemClass)
            ? null
            : GetIcon($"CLASS_{itemClass.Trim().ToUpperInvariant()}.png");

    private string GetIconPath(string iconFilename) => Path.Combine(_iconsDirectory, iconFilename);

    private static Bitmap? LoadAndScale(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            byte[] bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes, writable: false);
            var original = new Bitmap(stream);
            int largestDimension = Math.Max(original.PixelSize.Width, original.PixelSize.Height);
            if (largestDimension <= MaxCacheDimension)
                return original;

            double scale = (double)MaxCacheDimension / largestDimension;
            var targetSize = new PixelSize(
                Math.Max(1, (int)Math.Round(original.PixelSize.Width * scale)),
                Math.Max(1, (int)Math.Round(original.PixelSize.Height * scale)));
            Bitmap scaled = original.CreateScaledBitmap(targetSize, BitmapInterpolationMode.MediumQuality);
            original.Dispose();
            return scaled;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (Bitmap? bitmap in _cache.Values)
                bitmap?.Dispose();
            _cache.Clear();
        }
    }
}
