using System.Buffers.Binary;
using System.Globalization;
using TypeFlow.Core;

namespace TypeFlow.Importers.Ssf;

// This is a one-way translator. The host only receives the native status-bar model.
internal static class SsfStatusBarTranslator
{
    internal static async Task<ThemeStatusBar?> Translate(Dictionary<string, string> source,
        Func<string, Task<byte[]?>> read, string destination, List<Diagnostic> diagnostics, CancellationToken ct)
    {
        var elements = new List<StatusElement>();
        var saved = new Dictionary<string, (string Path, int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
        async Task<(string Path, int Width, int Height)?> Asset(string name)
        {
            if (saved.TryGetValue(name, out var existing)) return existing;
            var bytes = await read(name);
            if (bytes == null) { diagnostics.Add(new("status-asset", $"状态栏图片缺失：{name}")); return null; }
            var extension = Path.GetExtension(name).ToLowerInvariant();
            int width, height;
            if (extension == ".gif") { var info = GifAnimationInfo.Read(bytes); width = info.Width; height = info.Height; }
            else if (extension == ".png" && bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
            { width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)); height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)); }
            else if (extension == ".bmp" && bytes.Length >= 26 && bytes[0] == 'B' && bytes[1] == 'M')
            { width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18)); height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22))); }
            else { diagnostics.Add(new("status-format", $"跳过未支持的状态栏图片：{name}")); return null; }
            if (width is < 1 or > 2048 || height is < 1 or > 1024) throw new InvalidDataException("状态栏图片尺寸超出限制。");
            var target = $"assets/status-{saved.Count}{extension}";
            await File.WriteAllBytesAsync(ThemeFiles.AssetPath(destination, target), bytes, ct);
            var value = (target, width, height); saved[name] = value; return value;
        }
        async Task Add(string key, string id, string label, string action, bool background = false)
        {
            if (!source.TryGetValue(key, out var images) || string.IsNullOrWhiteSpace(images)) return;
            if (!background && source.GetValueOrDefault(key + "_display") != "1") return;
            var pos = background ? new[] {0,0} : (source.GetValueOrDefault(key + "_pos") ?? "0,0").Split(',')
                .Select(v => int.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (pos.Length != 2 || pos.Any(x => x < 0 || x > 2048)) throw new InvalidDataException("状态栏坐标无效。");
            var names = images.Split(',', StringSplitOptions.TrimEntries);
            var hovers = (source.GetValueOrDefault(key + "_hover") ?? "").Split(',', StringSplitOptions.TrimEntries);
            var pressed = (source.GetValueOrDefault(key + "_down") ?? "").Split(',', StringSplitOptions.TrimEntries);
            var states = new List<StatusVisual>();
            int width = 0, height = 0;
            for (var i = 0; i < Math.Min(2, names.Length); i++)
            {
                if (await Asset(names[i]) is not { } normal) continue;
                async Task<string?> Optional(string[] variants)
                {
                    if (i >= variants.Length || variants[i].Length == 0 || await Asset(variants[i]) is not { } asset) return null;
                    width = Math.Max(width, asset.Width); height = Math.Max(height, asset.Height); return asset.Path;
                }
                width = Math.Max(width, normal.Width); height = Math.Max(height, normal.Height);
                states.Add(new(normal.Path, await Optional(hovers), await Optional(pressed)));
            }
            if (states.Count != 0) elements.Add(new(id, label, pos[0], pos[1], width, height, states.ToArray(), action));
        }
        await Add("pic", "background", "状态栏背景", "none", true);
        // Draw decorations before action images. Transparent image rectangles are not click regions.
        if (int.TryParse(source.GetValueOrDefault("custom_cnt"), out var count))
            for (var i = 0; i < Math.Clamp(count, 0, 16); i++)
                await Add("custom" + i, "decoration-" + i, "挂件动画", "none");
        foreach (var (key, id, label, action) in new[] {
            ("cn_en", "language", "切换中英文", "language"), ("fan_jian", "script", "切换简繁体", "script"),
            ("biaodian", "punctuation", "切换中英文标点", "punctuation"), ("quan_ban", "width", "切换全半角", "width"),
            ("softkeyboard", "keyboard", "屏幕键盘", "keyboard"), ("menu", "settings", "TypeFlow 设置", "settings"),
            ("skinmanager", "skins", "皮肤设置", "skins"), ("voice", "voice", "语音输入", "voice") })
            await Add(key, id, label, action);
        foreach (var key in new[] { "quan_shuang", "sogousearch", "passport" })
            if (source.GetValueOrDefault(key + "_display") == "1") diagnostics.Add(new("status-action", $"未转换搜狗专用动作：{key}。"));
        if (elements.Count == 0) return null;
        diagnostics.Add(new("status-bar", $"已转换 {elements.Count} 个状态栏元素，保留坐标、普通 / 悬停 / 按下图片和通用动作。", "info"));
        return new(elements.Max(x => x.X + x.Width), elements.Max(x => x.Y + x.Height), elements.ToArray());
    }
}
