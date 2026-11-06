using System.Security.Cryptography;

namespace TypeFlow.Core;

public sealed class NativeThemeImporter : IThemeImporter
{
    public PluginInfo Info => new("typeflow.native", 1, "1.0.0", ["native-directory"]);
    public bool CanImport(string path) => Directory.Exists(path) && File.Exists(Path.Combine(path, "theme.json"));
    public Task<ImportResult> ImportAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var theme = ThemeFiles.Load(Path.Combine(source, "theme.json"));
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("目标已存在，拒绝覆盖。");
        var staging = destination + ".import-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            // Copy only model-referenced resources. A data package never installs executable code.
            foreach (var relative in ThemeFiles.ReferencedImages(theme))
            {
                var target = ThemeFiles.AssetPath(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(ThemeFiles.AssetPath(source, relative), target);
            }
            cancellationToken.ThrowIfCancellationRequested();
            ThemeFiles.Save(theme, staging);
            var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "theme.json"))));
            var report = new ImportReport(Info.Id, Path.GetFileName(Path.GetFullPath(source)), hash, "complete", []);
            File.WriteAllText(Path.Combine(staging, "import-report.json"), System.Text.Json.JsonSerializer.Serialize(report, ThemeFiles.Json));
            Directory.Move(staging, destination);
            return Task.FromResult(new ImportResult(destination, theme, report));
        }
        catch { Directory.Delete(staging, true); throw; }
    }
}
