using System.IO;
using System.Text.Json;
using TypeFlow.Core;
using TypeFlow.Importers.Ssf;

namespace TypeFlow.Desktop;

public sealed class ThemeLibrary
{
    public string Root { get; }
    public string Themes => Path.Combine(Root, "themes");
    public string DefaultDirectory => Path.Combine(Themes, "default");
    public ImporterRegistry Importers { get; } = new();
    public ThemeLibrary(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Themes);
        Importers.Register(new SsfImporter());
        Importers.Register(new NativeThemeImporter());
        Directory.CreateDirectory(DefaultDirectory);
        ThemeFiles.Save(new Theme(), DefaultDirectory);
    }
    public string ActiveDirectory
    {
        get
        {
            try
            {
                var path = SelectedDirectory;
                _ = ThemeFiles.Load(Path.Combine(path, "theme.json"));
                return path;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or NotSupportedException) { return DefaultDirectory; }
        }
    }
    // Resolve selection independently of temporary JSON syntax errors; live viewers keep their last good frame.
    public string SelectedDirectory
    {
        get
        {
            try
            {
                var id = JsonSerializer.Deserialize<string>(File.ReadAllText(Path.Combine(Root, "active.json")));
                var path = ThemeFiles.AssetPath(Themes, id!);
                return Directory.Exists(path) ? path : DefaultDirectory;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException) { return DefaultDirectory; }
        }
    }
    public void Activate(string directory)
    {
        var name = Path.GetFileName(Path.GetFullPath(directory));
        var path = ThemeFiles.AssetPath(Themes, name);
        if (!Path.GetFullPath(directory).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("只能应用库内皮肤。");
        _ = ThemeFiles.Load(Path.Combine(path, "theme.json"));
        var temp = Path.Combine(Root, "active.json.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(name));
        File.Move(temp, Path.Combine(Root, "active.json"), true);
    }
    public Task<ImportResult> Import(string source, CancellationToken ct = default) => Importers.Resolve(source)
        .ImportAsync(source, Path.Combine(Themes, "user-" + Guid.NewGuid().ToString("N")), ct);
    public string? LinkedSource(string directory) => LinkedThemes.SourceFile(RequireEntry(directory));
    public LinkedSnapshot RefreshLinked(string directory) => LinkedThemes.Refresh(RequireEntry(directory));
    public string ConfigFile(string directory) => LinkedSource(directory) ?? Path.Combine(RequireEntry(directory), "theme.json");
    public string Link(string sourceFile)
    {
        sourceFile = Path.GetFullPath(sourceFile);
        var target = Path.Combine(Themes, "user-link-" + Guid.NewGuid().ToString("N"));
        var staging = target + ".import-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            File.WriteAllText(Path.Combine(staging, "link.json"), JsonSerializer.Serialize(new ThemeLink(sourceFile), ThemeFiles.Json));
            _ = LinkedThemes.Refresh(staging);
            Directory.Move(staging, target); return target;
        }
        catch { Directory.Delete(staging, true); throw; }
    }
    private string RequireEntry(string directory)
    {
        var path = Path.GetFullPath(directory);
        var contained = ThemeFiles.AssetPath(Themes, Path.GetFileName(path));
        if (!path.Equals(contained, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("只能访问库内的皮肤条目。");
        return path;
    }
    public IEnumerable<(string Directory, Theme Theme)> List()
    {
        foreach (var path in Directory.EnumerateDirectories(Themes).Where(x => !Path.GetFileName(x).Contains(".import-", StringComparison.Ordinal)))
        {
            Theme? theme = null;
            try { theme = ThemeFiles.Load(Path.Combine(path, "theme.json")); }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or NotSupportedException or ArgumentException) { }
            if (theme != null) yield return (path, theme);
        }
    }
    public void Delete(string path)
    {
        var name = Path.GetFileName(Path.GetFullPath(path));
        var contained = ThemeFiles.AssetPath(Themes, name);
        if (!Path.GetFullPath(path).Equals(contained, StringComparison.OrdinalIgnoreCase) || !name.StartsWith("user-", StringComparison.Ordinal))
            throw new InvalidOperationException("只能删除用户导入的皮肤。");
        if (ActiveDirectory.Equals(contained, StringComparison.OrdinalIgnoreCase)) Activate(DefaultDirectory);
        Directory.Delete(contained, true);
    }
}
