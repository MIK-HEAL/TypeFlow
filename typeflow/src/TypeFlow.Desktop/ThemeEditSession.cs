using System.IO;
using System.Text.Json;
using TypeFlow.Core;
using TypeFlow.Themes;
using TypeFlow.Rendering.Wpf;

namespace TypeFlow.Desktop;

// A data-only editing transaction. It never reads keys or owns an input engine.
public sealed class ThemeEditSession : IDisposable
{
    private readonly ThemeLibrary library;
    private readonly Theme original;
    private Theme current;
    private readonly List<Theme> history = [];
    private int index;
    private bool disposed;
    private readonly string sourceName;
    private readonly string? sourceReport;
    public string Directory { get; }
    public Theme Current => Clone(current);
    public bool CanUndo => index > 0;
    public bool CanRedo => index + 1 < history.Count;
    public bool IsDirty => JsonSerializer.Serialize(current, ThemeFiles.Json) != JsonSerializer.Serialize(original, ThemeFiles.Json);
    public event Action? Changed;
    public ThemeEditSession(ThemeLibrary library, string sourceDirectory)
    {
        this.library = library;
        original = ThemeFiles.Load(Path.Combine(sourceDirectory, "theme.json"));
        sourceName = original.Name;
        var reportPath = ThemeFiles.AssetPath(sourceDirectory, "import-report.json");
        if (File.Exists(reportPath) && new FileInfo(reportPath).Length <= 1024 * 1024) sourceReport = File.ReadAllText(reportPath);
        Directory = Path.GetFullPath(Path.Combine(library.Root, "drafts", Guid.NewGuid().ToString("N")));
        System.IO.Directory.CreateDirectory(Directory);
        try
        {
            foreach (var relative in ThemeFiles.ReferencedImages(original))
            {
                var target = ThemeFiles.AssetPath(Directory, relative);
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(ThemeFiles.AssetPath(sourceDirectory, relative), target);
            }
            current = Clone(original); ValidateRender(current); history.Add(Clone(current));
        }
        catch { System.IO.Directory.Delete(Directory, true); throw; }
    }
    private static Theme Clone(Theme theme) => JsonSerializer.Deserialize<Theme>(JsonSerializer.Serialize(theme, ThemeFiles.Json), ThemeFiles.Json)!;
    private void Check() => ObjectDisposedException.ThrowIf(disposed, this);
    private void ValidateRender(Theme theme)
    {
        var validator = new CandidateSurface(); validator.SetTheme(theme, Directory);
    }
    public void Update(Func<Theme, Theme> change)
    {
        Check(); var candidate = ThemeFiles.DeclareCapabilities(change(Clone(current)));
        ValidateRender(candidate);
        if (JsonSerializer.Serialize(candidate, ThemeFiles.Json) == JsonSerializer.Serialize(current, ThemeFiles.Json)) return;
        if (index + 1 < history.Count) history.RemoveRange(index + 1, history.Count - index - 1);
        current = candidate; history.Add(Clone(current));
        if (history.Count > 101) history.RemoveAt(0);
        index = history.Count - 1; Changed?.Invoke();
    }
    public void Undo() { Check(); if (CanUndo) { current = Clone(history[--index]); Changed?.Invoke(); } }
    public void Redo() { Check(); if (CanRedo) { current = Clone(history[++index]); Changed?.Invoke(); } }
    public void Reset() => Update(_ => Clone(original));
    public string CopyImage(string source)
    {
        Check(); var image = ThemeImages.Load(source);
        // Normalize every new decoration to a static PNG. GIF animation is never silently enabled.
        var relative = "assets/edited-" + Guid.NewGuid().ToString("N") + ".png";
        var path = ThemeFiles.AssetPath(Directory, relative);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var output = File.Create(path); encoder.Save(output); return relative;
    }
    public string AddLayer(string imagePath)
    {
        var relative = CopyImage(imagePath);
        var id = Guid.NewGuid().ToString("N");
        Update(t => t with { Layers = [.. t.Layers, new(id, relative, Path.GetFileNameWithoutExtension(imagePath), Width: 96, Height: 96)] });
        return "layer:" + id;
    }
    public void EditLayer(string id, Func<ThemeLayer, ThemeLayer> change) => Update(t => t with { Layers = t.Layers.Select(x => x.Id == id ? change(x) : x).ToArray() });
    public void RemoveLayer(string id) => Update(t => t with { Layers = t.Layers.Where(x => x.Id != id).ToArray() });
    public void ReorderLayer(string id, int delta) => Update(t =>
    {
        var layers = t.Layers.ToList(); var from = layers.FindIndex(x => x.Id == id);
        if (from < 0) return t;
        var to = Math.Clamp(from + delta, 0, layers.Count - 1); var item = layers[from]; layers.RemoveAt(from); layers.Insert(to, item);
        return t with { Layers = layers.ToArray() };
    });
    public void Move(string element, double dx, double dy, IReadOnlyList<CanvasElement> elements)
    {
        var bounds = elements.Single(x => x.Id == element).Bounds;
        if (element.StartsWith("layer:", StringComparison.Ordinal))
            EditLayer(element[6..], x => x with { X = Math.Clamp(x.X + dx, -2048, 2048), Y = Math.Clamp(x.Y + dy, -1024, 1024) });
        else Update(t => t with { Layout = element == "preedit"
            ? t.Layout with { PreeditPosition = new(Math.Clamp(bounds.X + dx, 0, 2048), Math.Clamp(bounds.Y + dy, 0, 1024)) }
            : t.Layout with { CandidatePosition = new(Math.Clamp(bounds.X + dx, 0, 2048), Math.Clamp(bounds.Y + dy, 0, 1024)) } });
    }
    public string SaveCopy(string name)
    {
        Check(); if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) throw new InvalidDataException("请填写 1–100 字的皮肤名称。");
        var id = Guid.NewGuid().ToString("N");
        var theme = ThemeFiles.DeclareCapabilities(current with { Id = "user." + id, Name = name.Trim(), Source = $"基于 {sourceName} 自定义；{original.Source}" });
        var staging = Path.Combine(Directory, "publish-" + id);
        System.IO.Directory.CreateDirectory(staging);
        try
        {
            foreach (var relative in ThemeFiles.ReferencedImages(theme))
            {
                var target = ThemeFiles.AssetPath(staging, relative); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(ThemeFiles.AssetPath(Directory, relative), target);
            }
            ThemeFiles.Save(theme, staging);
            // Keep conversion limitations alongside user edits; editing does not restore discarded SSF effects.
            var report = new { sourceThemeId = original.Id, sourceThemeName = sourceName, edited = true, originalLicense = original.License };
            File.WriteAllText(Path.Combine(staging, "customization.json"), JsonSerializer.Serialize(report, ThemeFiles.Json));
            if (sourceReport != null) File.WriteAllText(Path.Combine(staging, "import-report.json"), sourceReport);
            var targetDirectory = Path.Combine(library.Themes, "user-" + id);
            System.IO.Directory.Move(staging, targetDirectory); return targetDirectory;
        }
        catch { System.IO.Directory.Delete(staging, true); throw; }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var prefix = Path.GetFullPath(Path.Combine(library.Root, "drafts")) + Path.DirectorySeparatorChar;
        if (!Directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("草稿清理路径越界。");
        if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
    }
}
