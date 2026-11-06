using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TypeFlow.Core;

public sealed record Insets(double Left = 12, double Top = 8, double Right = 12, double Bottom = 8);
public sealed record TextPosition(double X = 0, double Y = 0);
public sealed record ThemeText(string FontFamily = "Microsoft YaHei UI", double FontSize = 16,
    string Color = "#FF243047", string PreeditColor = "#FF567533");
public sealed record ThemeLayout(string Direction = "horizontal", Insets? Padding = null,
    double CandidateGap = 12, double RowGap = 6, double MinWidth = 260, double MinHeight = 0,
    Insets? PreeditMargin = null, Insets? CandidateMargin = null,
    TextPosition? PreeditPosition = null, TextPosition? CandidatePosition = null);
public sealed record ThemeBackground(string? Image = null, string Mode = "solid",
    Insets? Slice = null, string Color = "#FFF4F6FA", string HorizontalFill = "stretch", string VerticalFill = "stretch");
public sealed record ThemeLayer(string Id, string Image, string Name = "装饰图层", string Anchor = "topLeft",
    double X = 0, double Y = 0, double Width = 0, double Height = 0, double Opacity = 1,
    string Plane = "behindText", string Mode = "stretch", Insets? Slice = null,
    string HorizontalFill = "stretch", string VerticalFill = "stretch", bool Visible = true);
public sealed record ThemeSelection(string TextColor = "#FFFFFFFF", string BackgroundColor = "#FF3B6EE8");
public sealed record Theme
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }
    public int FormatVersion { get; init; } = 1;
    public string Id { get; init; } = "typeflow.default";
    public string Name { get; init; } = "默认";
    public string Author { get; init; } = "TypeFlow";
    public string? Source { get; init; }
    public string? License { get; init; }
    public string Units { get; init; } = "dip";
    public string[] RequiredCapabilities { get; init; } = [];
    public ThemeLayout Layout { get; init; } = new(Padding: new());
    public ThemeText Text { get; init; } = new();
    public ThemeBackground Background { get; init; } = new();
    public ThemeSelection Selection { get; init; } = new();
    public ThemeLayer[] Layers { get; init; } = [];
    public Dictionary<string, JsonElement> Extensions { get; init; } = [];
}

public static partial class ThemeFiles
{
    // Linux paths are case-sensitive. Never grant access to a sibling differing only in case.
    public static StringComparison FileSystemComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static StringComparer FileSystemComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
        PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public static string AssetPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) throw new InvalidDataException("资源路径不能为空。");
        var normalized = relative.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Split('/').Any(x => x is ".." or "." or "") ||
            normalized.Any(c => c < 32 || ":*?\"<>|".Contains(c)) || Path.IsPathRooted(normalized))
            throw new InvalidDataException($"资源路径不合法：{relative}");
        foreach (var segment in normalized.Split('/'))
        {
            var stem = segment.Split('.')[0].ToUpperInvariant();
            if (segment.EndsWith('.') || segment.EndsWith(' ') || stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3])))
                throw new InvalidDataException("资源路径包含 Windows 保留名称。");
        }
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(prefix, normalized));
        if (!result.StartsWith(prefix, FileSystemComparison)) throw new InvalidDataException("资源路径越界。");
        // Reject reparse points, including ancestors, so a junction cannot redirect file access.
        for (var p = new DirectoryInfo(Path.GetDirectoryName(result)!); p != null; p = p.Parent)
            if (p.Exists && (p.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("资源目录不能包含链接。");
        if (File.Exists(result) && (File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("资源不能是链接。");
        return result;
    }
    public static Theme Load(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("皮肤配置过大。");
        var theme = JsonSerializer.Deserialize<Theme>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("皮肤配置为空。");
        Validate(theme, Path.GetDirectoryName(Path.GetFullPath(path))!);
        return theme;
    }
    public static void Save(Theme theme, string root)
    {
        Validate(theme, root);
        theme = DeclareCapabilities(theme);
        File.WriteAllText(Path.Combine(root, "theme.json"), JsonSerializer.Serialize(theme, Json));
    }
    public static void Validate(Theme theme, string root)
    {
        if (theme.FormatVersion != 1 || theme.Units != "dip") throw new NotSupportedException("不支持此皮肤版本或单位。");
        if (string.IsNullOrWhiteSpace(theme.Id) || string.IsNullOrWhiteSpace(theme.Name) || theme.Id.Length > 128)
            throw new InvalidDataException("皮肤必须有标识和名称。");
        if (theme.RequiredCapabilities is null || theme.Layout is null || theme.Text is null || theme.Background is null || theme.Selection is null || theme.Layers is null)
            throw new InvalidDataException("皮肤字段不能为 null。");
        if (theme.RequiredCapabilities.Except(new[] { "static-image", "nine-slice", "horizontal", "decoration-layers", "text-position", "tiled-images" }).Any())
            throw new NotSupportedException("皮肤要求当前渲染器未提供的能力。");
        if (theme.Layout.Direction != "horizontal") throw new NotSupportedException("当前仅支持横排皮肤。");
        Number(theme.Text.FontSize, 6, 96); Number(theme.Layout.CandidateGap, 0, 256); Number(theme.Layout.RowGap, 0, 128);
        Number(theme.Layout.MinWidth, 0, 2048); Number(theme.Layout.MinHeight, 0, 1024);
        foreach (var position in new[] { theme.Layout.PreeditPosition, theme.Layout.CandidatePosition })
            if (position != null) { Number(position.X, 0, 2048); Number(position.Y, 0, 1024); }
        foreach (var inset in new[] { theme.Layout.Padding, theme.Layout.PreeditMargin, theme.Layout.CandidateMargin, theme.Background.Slice })
            if (inset != null) foreach (var v in new[] { inset.Left, inset.Top, inset.Right, inset.Bottom }) Number(v, 0, 2048);
        foreach (var c in new[] { theme.Text.Color, theme.Text.PreeditColor, theme.Background.Color, theme.Selection.TextColor, theme.Selection.BackgroundColor })
            if (c == null || !ColorPattern().IsMatch(c)) throw new InvalidDataException("颜色须为 #AARRGGBB。");
        if (theme.Background.Mode is not ("solid" or "stretch" or "nineSlice" or "tile")) throw new NotSupportedException("不支持此背景模式。");
        Fill(theme.Background.HorizontalFill); Fill(theme.Background.VerticalFill);
        if (theme.Background.Mode != "solid" && theme.Background.Image == null) throw new InvalidDataException("图片背景缺少资源。");
        if (theme.Layers.Length > 32 || theme.Layers.Any(x => x == null) || theme.Layers.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != theme.Layers.Length)
            throw new InvalidDataException("图层最多 32 个，标识必须唯一。");
        foreach (var layer in theme.Layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Id) || layer.Id.Length > 100 || string.IsNullOrWhiteSpace(layer.Name) || string.IsNullOrWhiteSpace(layer.Image)) throw new InvalidDataException("图层缺少标识、名称或图片。");
            if (layer.Anchor is not ("topLeft" or "topCenter" or "topRight" or "centerLeft" or "center" or "centerRight" or "bottomLeft" or "bottomCenter" or "bottomRight"))
                throw new NotSupportedException("不支持此图层锚点。");
            if (layer.Plane is not ("behindText" or "aboveText") || layer.Mode is not ("stretch" or "tile" or "nineSlice")) throw new NotSupportedException("不支持此图层绘制方式。");
            Number(layer.X, -2048, 2048); Number(layer.Y, -1024, 1024); Number(layer.Width, 0, 2048); Number(layer.Height, 0, 1024); Number(layer.Opacity, 0, 1);
            Fill(layer.HorizontalFill); Fill(layer.VerticalFill);
            if (layer.Slice is { } s) foreach (var v in new[] { s.Left, s.Top, s.Right, s.Bottom }) Number(v, 0, 2048);
        }
        long bytes = 0;
        foreach (var image in ReferencedImages(theme))
        {
            var p = AssetPath(root, image);
            if (!File.Exists(p)) throw new InvalidDataException($"皮肤缺少图片资源：{image}");
            if (new FileInfo(p).Length > 16 * 1024 * 1024) throw new InvalidDataException("背景图片过大。");
            bytes += new FileInfo(p).Length;
        }
        if (bytes > 64 * 1024 * 1024) throw new InvalidDataException("皮肤图片资源总量超过 64 MiB。");
    }
    private static void Fill(string fill) { if (fill is not ("stretch" or "tile")) throw new NotSupportedException("填充方式必须是 stretch 或 tile。"); }
    public static IEnumerable<string> ReferencedImages(Theme theme) =>
        (theme.Background.Image is { } image ? new[] { image } : []).Concat(theme.Layers.Select(x => x.Image)).Distinct(StringComparer.OrdinalIgnoreCase);
    public static Theme DeclareCapabilities(Theme theme)
    {
        var capabilities = new HashSet<string>(theme.RequiredCapabilities, StringComparer.Ordinal);
        if (theme.Layers.Length > 0) capabilities.Add("decoration-layers");
        if (theme.Layout.PreeditPosition != null || theme.Layout.CandidatePosition != null) capabilities.Add("text-position");
        if (theme.Background.Mode == "tile" || theme.Background.HorizontalFill == "tile" || theme.Background.VerticalFill == "tile" ||
            theme.Layers.Any(x => x.Mode == "tile" || x.HorizontalFill == "tile" || x.VerticalFill == "tile")) capabilities.Add("tiled-images");
        return theme with { RequiredCapabilities = capabilities.Order(StringComparer.Ordinal).ToArray() };
    }
    private static void Number(double v, double min, double max)
    {
        if (!double.IsFinite(v) || v < min || v > max) throw new InvalidDataException("皮肤尺寸超出支持范围。");
    }
    [GeneratedRegex("^#[0-9A-Fa-f]{8}$")]
    private static partial Regex ColorPattern();
}
