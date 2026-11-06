namespace TypeFlow.Core;

public sealed record PluginInfo(string Id, int ApiVersion, string Version, string[] Capabilities);
public sealed record Diagnostic(string Code, string Message, string Severity = "warning");
public sealed record ImportReport(string PluginId, string SourceName, string Sha256,
    string Compatibility, IReadOnlyList<Diagnostic> Diagnostics);
public sealed record ImportResult(string ThemeDirectory, Theme Theme, ImportReport Report);
public interface IThemeImporter
{
    PluginInfo Info { get; }
    bool CanImport(string path);
    Task<ImportResult> ImportAsync(string source, string destination, CancellationToken cancellationToken = default);
}

// Explicit registration keeps execution separate from importing a data-only theme.
public sealed class ImporterRegistry
{
    private readonly List<IThemeImporter> importers = [];
    public IReadOnlyList<IThemeImporter> Importers => importers.AsReadOnly();
    public void Register(IThemeImporter importer)
    {
        if (importer.Info.ApiVersion != 1) throw new NotSupportedException("导入器接口版本不兼容。");
        if (importers.Any(x => x.Info.Id == importer.Info.Id)) throw new InvalidOperationException("导入器标识重复。");
        importers.Add(importer);
    }
    public IThemeImporter Resolve(string path) => importers.FirstOrDefault(x => x.CanImport(path))
        ?? throw new NotSupportedException("没有能识别此文件内容的导入器；加密 SSF 暂不支持。");
}

public sealed record Candidate(string Text, string Comment = "", string Label = "");
public sealed record CandidateView(string Preedit, IReadOnlyList<Candidate> Candidates,
    int Highlighted = 0, int Page = 0, bool IsLastPage = true);
public sealed record KeyResult(bool Handled, string Commit, CandidateView View);
public sealed record CandidatePresentation(bool Visible, int Left, int Top, int Right, int Bottom, CandidateView? View);
public interface ICandidateRenderer
{
    void SetTheme(Theme theme, string directory);
    void SetView(CandidateView view);
}

// Hosts provide image decoding / rendering checks before publishing a linked snapshot.
public interface IThemeValidator
{
    void Validate(Theme theme, string directory);
}

public interface IInputEngine : IDisposable
{
    string Id { get; }
    KeyResult ProcessKey(int keycode, int modifiers = 0);
    KeyResult SelectCandidate(int index);
    CandidateView View { get; }
    void Clear();
}
