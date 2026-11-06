using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TypeFlow.Core;

namespace TypeFlow.Desktop;

public sealed record ThemeLink(string SourceFile);
public sealed record LinkedSnapshot(Theme Theme, bool Changed, string[] SourceFiles);

// An external configuration is data, never an executable plugin or a filesystem symlink.
public static class LinkedThemes
{
    public static string? SourceFile(string entry)
    {
        var path = ThemeFiles.AssetPath(entry, "link.json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16 * 1024) throw new InvalidDataException("配置关联记录过大。");
        var link = JsonSerializer.Deserialize<ThemeLink>(File.ReadAllText(path), ThemeFiles.Json) ?? throw new InvalidDataException("配置关联记录为空。");
        if (string.IsNullOrWhiteSpace(link.SourceFile) || !Path.IsPathFullyQualified(link.SourceFile)) throw new InvalidDataException("关联配置必须使用完整本地路径。");
        return Path.GetFullPath(link.SourceFile);
    }
    public static LinkedSnapshot Refresh(string entry)
    {
        var source = SourceFile(entry) ?? throw new InvalidOperationException("该皮肤没有关联外部配置。");
        var sourceDirectory = Path.GetDirectoryName(source)!;
        var theme = ThemeFiles.Load(source);
        var rebased = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceFiles = new List<string> { source };
        long decodedPixels = 0;
        var decoded = new Dictionary<string, System.Windows.Media.Imaging.BitmapSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in ThemeFiles.ReferencedImages(theme))
        {
            var inputPath = ThemeFiles.AssetPath(sourceDirectory, relative);
            sourceFiles.Add(inputPath);
            var bytes = File.ReadAllBytes(inputPath);
            if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("图片超过 16 MiB。");
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            // The cache is content-addressed. An editor replacing an image cannot mutate the last good version.
            var name = "assets/" + hash + Path.GetExtension(relative).ToLowerInvariant();
            var destination = ThemeFiles.AssetPath(entry, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination))
            {
                var temp = destination + ".write-" + Guid.NewGuid().ToString("N");
                try { File.WriteAllBytes(temp, bytes); File.Move(temp, destination, false); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            var image = ThemeImages.Load(destination);
            decodedPixels += (long)image.PixelWidth * image.PixelHeight;
            if (decodedPixels > 16_000_000) throw new InvalidDataException("皮肤图片解码总量超过 1600 万像素。");
            rebased[relative] = name; decoded[relative] = image;
        }
        if (theme.Background is { Mode: "nineSlice", Image: { } background }) ThemeImages.ValidateSlice(decoded[background], theme.Background.Slice);
        foreach (var layer in theme.Layers.Where(x => x.Mode == "nineSlice")) ThemeImages.ValidateSlice(decoded[layer.Image], layer.Slice);
        theme = ThemeFiles.DeclareCapabilities(theme with
        {
            Schema = theme.Schema is { } schema && !Uri.TryCreate(schema, UriKind.Absolute, out _)
                ? new Uri(Path.GetFullPath(Path.Combine(sourceDirectory, schema))).AbsoluteUri : theme.Schema,
            Background = theme.Background.Image is { } sourceBackground ? theme.Background with { Image = rebased[sourceBackground] } : theme.Background,
            Layers = theme.Layers.Select(x => x with { Image = rebased[x.Image] }).ToArray()
        });
        ThemeFiles.Validate(theme, entry);
        var json = JsonSerializer.Serialize(theme, ThemeFiles.Json);
        var config = ThemeFiles.AssetPath(entry, "theme.json");
        var changed = !File.Exists(config) || File.ReadAllText(config) != json;
        if (changed)
        {
            var temporary = config + ".write-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllText(temporary, json); File.Move(temporary, config, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return new(theme, changed, sourceFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

public sealed record ConfigReloadResult(bool Changed, string? Error);

// Polled on the owning UI dispatcher; it handles rename-and-replace saves without file watcher races.
// A stamp taken before a refresh means a concurrent save is seen on the next poll.
public sealed class ThemeConfigMonitor(ThemeLibrary library, string entry)
{
    private string? lastStamp;
    private string[] sourceFiles = [];
    private string? lastSource;
    private DateTime retryAfter;
    private string? lastError;
    public ConfigReloadResult Poll(bool force = false)
    {
        try
        {
            var linked = library.LinkedSource(entry);
            var source = linked ?? Path.Combine(entry, "theme.json");
            if (source != lastSource) { lastStamp = null; sourceFiles = [source]; lastSource = source; }
            var stamp = Stamp(source, sourceFiles);
            if (!force && stamp == lastStamp && (lastError == null || DateTime.UtcNow < retryAfter)) return new(false, lastError);
            lastStamp = stamp;
            bool changed;
            if (linked != null)
            {
                var result = library.RefreshLinked(entry); changed = result.Changed; sourceFiles = result.SourceFiles;
            }
            else
            {
                var model = ThemeFiles.Load(source);
                var validate = new CandidateSurface(); validate.SetTheme(model, entry);
                sourceFiles = [source, .. ThemeFiles.ReferencedImages(model).Select(x => ThemeFiles.AssetPath(entry, x))];
                changed = true;
            }
            lastError = null; return new(changed, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or FormatException or ArgumentException or NotSupportedException)
        { lastError = e.Message; retryAfter = DateTime.UtcNow.AddSeconds(2); return new(false, lastError); }
    }
    private static string Stamp(string source, IEnumerable<string> files)
    {
        var stamp = new StringBuilder();
        foreach (var path in files)
        {
            var file = new FileInfo(path);
            stamp.Append(path).Append('|').Append(file.Exists ? file.Length : -1).Append('|').Append(file.Exists ? file.LastWriteTimeUtc.Ticks : 0).Append(';');
        }
        // JSON is small; also hash it so tools preserving its timestamp cannot hide a configuration edit.
        if (File.Exists(source) && new FileInfo(source).Length <= 1024 * 1024)
            stamp.Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))));
        return stamp.ToString();
    }
}
