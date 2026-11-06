using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TypeFlow.Core;

namespace TypeFlow.Importers.Ssf;

public sealed class SsfImporter : IThemeImporter
{
    public PluginInfo Info => new("typeflow.ssf", 1, "0.1.0", ["zip", "utf16", "utf8", "gb18030", "static-horizontal", "png"]);
    public bool CanImport(string path)
    {
        if (!File.Exists(path)) return false;
        using var f = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        return f.Read(header) == 4 && header.SequenceEqual(new byte[] { 80, 75, 3, 4 });
    }

    public async Task<ImportResult> ImportAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("目标已存在，拒绝覆盖。");
        if (new FileInfo(source).Length > 32 * 1024 * 1024) throw new InvalidDataException("SSF 文件超过 32 MiB。");
        destination = Path.GetFullPath(destination);
        var staging = destination + ".import-" + Guid.NewGuid().ToString("N");
        var diagnostics = new List<Diagnostic>();
        Directory.CreateDirectory(staging);
        try
        {
            await using var input = File.OpenRead(source);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken));
            input.Position = 0;
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, true);
            if (zip.Entries.Count > 512) throw new InvalidDataException("SSF 条目过多。");
            long total = 0;
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.FullName.Replace('\\', '/');
                ThemeFiles.AssetPath(staging, name.TrimEnd('/'));
                if (name.EndsWith('/')) continue;
                if (!entries.TryAdd(name, entry)) throw new InvalidDataException("SSF 存在大小写重名条目。");
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("SSF 不能包含符号链接。");
                total = checked(total + entry.Length);
                if (entry.Length > 16 * 1024 * 1024 || total > 64 * 1024 * 1024)
                    throw new InvalidDataException("SSF 展开尺寸超出限制。");
            }
            var configs = entries.Where(x => Path.GetFileName(x.Key).Equals("skin.ini", StringComparison.OrdinalIgnoreCase)).ToList();
            if (configs.Count != 1) throw new InvalidDataException("必须且只能包含一个 Skin.ini。");
            var configEntry = configs[0];
            var configBytes = await Read(configEntry.Value, 1024 * 1024, cancellationToken);
            var (configText, encoding) = Decode(configBytes);
            var ini = ParseIni(configText);
            string? Get(string section, string key) => ini.GetValueOrDefault(section)?.GetValueOrDefault(key);
            string Need(string section, string key) => Get(section, key) ?? throw new InvalidDataException($"缺少 {section}/{key}。");
            var pictureName = Need("Scheme_H1", "pic").Trim();
            var configFolder = configEntry.Key.Contains('/') ? configEntry.Key[..(configEntry.Key.LastIndexOf('/') + 1)] : "";
            var imageKey = configFolder + pictureName.Replace('\\', '/');
            ThemeFiles.AssetPath(staging, imageKey);
            if (!entries.TryGetValue(imageKey, out var imageEntry)) throw new InvalidDataException("候选背景图片缺失。");
            var image = await Read(imageEntry, 16 * 1024 * 1024, cancellationToken);
            var (width, height) = PngSize(image);
            Directory.CreateDirectory(Path.Combine(staging, "assets"));
            await File.WriteAllBytesAsync(Path.Combine(staging, "assets/background.png"), image, cancellationToken);
            // Preserve all original metadata without making SSF names part of the native rendering contract.
            Directory.CreateDirectory(Path.Combine(staging, "source"));
            await File.WriteAllBytesAsync(Path.Combine(staging, "source/Skin.ini"), configBytes, cancellationToken);
            var horizontal = Integers(Get("Scheme_H1", "layout_horizontal") ?? "0,0,0", 3);
            var vertical = Integers(Get("Scheme_H1", "layout_vertical") ?? "0,0,0", 3);
            var slices = new Insets(horizontal[1], vertical[1], horizontal[2], vertical[2]);
            if (slices.Left + slices.Right >= width || slices.Top + slices.Bottom >= height)
            {
                diagnostics.Add(new("invalid-slice", $"原始拉伸边界超出 {width}×{height} 图片；保留有效横向分区，纵向改为整体拉伸。"));
                slices = new(horizontal[1] + horizontal[2] < width ? horizontal[1] : 0,
                    0, horizontal[1] + horizontal[2] < width ? horizontal[2] : 0, 0);
            }
            if (horizontal[0] != 0 || vertical[0] != 0)
                diagnostics.Add(new("fill-mode", "原皮肤含非默认填充模式，首版使用九宫格拉伸；不承诺平铺或定位模式完全一致。"));
            var p = Integers(Need("Scheme_H1", "pinyin_marge"), 4);
            var c = Integers(Need("Scheme_H1", "zhongwen_marge"), 4);
            // SSF order is top,bottom,left,right (not CSS order).
            var preedit = new Insets(p[2], p[0], p[3], p[1]);
            var candidate = new Insets(c[2], c[0], c[3], c[1]);
            if (ini.ContainsKey("StatusBar")) diagnostics.Add(new("status-bar", "未映射搜狗状态栏和自定义按钮；候选窗不受影响。"));
            if (entries.Keys.Any(x => x.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)))
                diagnostics.Add(new("animation", "包内 GIF 动画未导入，原始 SSF 保持不变。"));
            if (ini.Keys.Any(x => x.Equals("Scheme_H2", StringComparison.OrdinalIgnoreCase) || x.StartsWith("Scheme_V", StringComparison.OrdinalIgnoreCase)))
                diagnostics.Add(new("alternate-layouts", "仅映射 Scheme_H1 横排合窗口；分窗口与竖排未映射。"));
            if (Get("Display", "glow") == "1" || Get("Display", "aero") == "1")
                diagnostics.Add(new("text-effects", "文字发光和 Aero 效果未映射。"));
            diagnostics.Add(new("scope", "兼容范围：静态 PNG 背景、横排合窗口、字体、三种文字颜色、独立文字边距。其他配置保留于 source/Skin.ini。", "info"));
            var theme = new Theme
            {
                Id = "ssf." + hash[..16], Name = Get("General", "skin_name") ?? Path.GetFileNameWithoutExtension(source),
                Author = Get("General", "skin_author") ?? "未知", Source = Path.GetFileName(source),
                License = "原作者素材，授权未声明；仅用于本地导入测试。",
                RequiredCapabilities = ["static-image", "nine-slice", "horizontal"],
                Text = new(Get("Display", "font_ch") ?? "Microsoft YaHei UI", ParseNumber(Get("Display", "font_size") ?? "16"),
                    Color(Need("Display", "zhongwen_color")), Color(Need("Display", "pinyin_color"))),
                Selection = new(Color(Need("Display", "zhongwen_first_color")), "#00000000"),
                Layout = new(Padding: new(0, 0, 0, 0), CandidateGap: 12, RowGap: 0,
                    MinWidth: width, MinHeight: height, PreeditMargin: preedit, CandidateMargin: candidate),
                Background = new("assets/background.png", "nineSlice", slices, "#00000000"),
                Extensions = new() { ["typeflow.ssf"] = JsonSerializer.SerializeToElement(new { encoding, original = ini }, ThemeFiles.Json) }
            };
            ThemeFiles.Save(theme, staging);
            var report = new ImportReport(Info.Id, Path.GetFileName(source), hash, diagnostics.Any(d => d.Severity == "warning") ? "partial" : "complete", diagnostics);
            await File.WriteAllTextAsync(Path.Combine(staging, "import-report.json"), JsonSerializer.Serialize(report, ThemeFiles.Json), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return new(destination, theme, report);
        }
        catch { Directory.Delete(staging, true); throw; }
    }

    private static async Task<byte[]> Read(ZipArchiveEntry entry, int limit, CancellationToken ct)
    {
        if (entry.Length > limit) throw new InvalidDataException("SSF 资源超过限制。");
        await using var stream = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > limit || output.Length + count > entry.Length) throw new InvalidDataException("SSF 资源展开尺寸异常。");
            output.Write(buffer, 0, count);
        }
        if (output.Length != entry.Length) throw new InvalidDataException("SSF 资源不完整。");
        return output.ToArray();
    }
    private static (string, string) Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) return (new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2), "UTF-16LE");
        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) return (new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2), "UTF-16BE");
        try { return (new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'), "UTF-8"); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return (Encoding.GetEncoding("gb18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes), "GB18030");
        }
    }
    private static Dictionary<string, Dictionary<string, string>> ParseIni(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? section = null;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1];
                if (result.ContainsKey(name)) throw new InvalidDataException("SSF 配置包含重复分节。");
                result[name] = section = new(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var eq = line.IndexOf('=');
                if (section == null || eq < 1 || !section.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim()))
                    throw new InvalidDataException("SSF 配置格式错误或键重复。");
            }
        }
        return result;
    }
    private static int[] Integers(string value, int length)
    {
        var values = value.Split(',').Select(x => int.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray();
        if (values.Length != length || values.Any(x => x < 0 || x > 8192)) throw new InvalidDataException("SSF 布局参数不合法。");
        return values;
    }
    private static double ParseNumber(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    public static string Color(string value)
    {
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        var n = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (n > 0xffffff) throw new InvalidDataException("SSF 首版只支持 24 位 BGR 颜色。");
        return $"#FF{n & 255:X2}{(n >> 8) & 255:X2}{(n >> 16) & 255:X2}";
    }
    private static (int Width, int Height) PngSize(byte[] data)
    {
        if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !data.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw new NotSupportedException("首版候选背景仅支持 PNG。");
        var w = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4));
        var h = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4));
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096 || (long)w * h > 8_000_000) throw new InvalidDataException("背景图尺寸超出限制。");
        return (w, h);
    }
}
