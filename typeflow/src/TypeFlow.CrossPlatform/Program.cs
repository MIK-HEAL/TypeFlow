using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using TypeFlow.Core;
using TypeFlow.Engine.Rime;
using TypeFlow.Importers.Ssf;
using TypeFlow.Rendering.Avalonia;
using TypeFlow.Themes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;
using System.Reflection;

namespace TypeFlow.CrossPlatform;

public sealed record LaunchOptions(string Workspace, string? Theme = null, string[]? Rime = null, string? SmokeOutput = null)
{
    public static LaunchOptions Parse(string[] args)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypeFlow", "Avalonia");
        string? theme = null; string[]? rime = null; string? smoke = null; var workspaceSpecified = false;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--workspace" when i + 1 < args.Length: root = args[++i]; workspaceSpecified = true; break;
                case "--theme" when i + 1 < args.Length: theme = args[++i]; break;
                case "--rime" when i + 4 < args.Length: rime = args[(i + 1)..(i + 5)]; i += 4; break;
                case "--smoke-test" when i + 1 < args.Length: smoke = args[++i]; break;
                default: throw new ArgumentException("参数不正确；使用 --help 查看用法。");
            }
        if (smoke != null && (!workspaceSpecified || rime != null || theme != null))
            throw new ArgumentException("--smoke-test requires an explicit --workspace and cannot be combined with --rime or --theme.");
        return new(Path.GetFullPath(root), theme, rime, smoke == null ? null : Path.GetFullPath(smoke));
    }
}

public static class CrossPlatformComponents
{
    public static ThemeLibrary CreateThemeLibrary(string root)
    {
        var imports = new ImporterRegistry();
        imports.Register(new SsfImporter()); imports.Register(new NativeThemeImporter());
        var library = new ThemeLibrary(root, imports, new AvaloniaThemeValidator());
        foreach (var name in new[] { "paper", "night" })
        {
            var source = Path.Combine(AppContext.BaseDirectory, "themes", name);
            var destination = Path.Combine(library.Themes, "bundled-" + name);
            // Copy bundled data on first use; user edits are never overwritten on restart/update.
            if (Directory.Exists(source) && !Directory.Exists(destination))
                new NativeThemeImporter().ImportAsync(source, destination).GetAwaiter().GetResult();
        }
        return library;
    }
}

public sealed class App : Application
{
    public static string ProductVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "development";
    public static LaunchOptions Options { get; set; } = LaunchOptions.Parse([]);
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var library = CrossPlatformComponents.CreateThemeLibrary(Options.Workspace);
            IInputEngine? engine = Options.Rime is { } r ? new RimeEngine(r[0], r[1], r[2], r[3]) : null;
            var window = new WorkbenchWindow(library, engine);
            if (Options.Theme is { } source)
            {
                var entry = Directory.Exists(source) ? library.Import(source).GetAwaiter().GetResult().ThemeDirectory : library.Link(source);
                window.OpenTheme(entry);
            }
            desktop.MainWindow = window;
            if (Options.SmokeOutput is { } output)
            {
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var restoredTheme = ThemeFiles.Load(Path.Combine(library.ActiveDirectory, "theme.json")).Name;
                        var samples = library.List().Where(x => Path.GetFileName(x.Directory).StartsWith("bundled-", StringComparison.Ordinal)).ToArray();
                        if (samples.Length != 2) throw new InvalidDataException("Bundled themes are missing.");
                        foreach (var sample in samples) library.Validator.Validate(sample.Theme, sample.Directory);
                        window.OpenTheme(samples.Single(x => x.Theme.Id == "typeflow.paper").Directory);
                        var saved = window.SaveCopy("便携包验证副本");
                        library.Activate(saved);
                        var image = Path.ChangeExtension(output, ".png");
                        window.SavePreview(image);
                        var report = new
                        {
                            success = true, version = ProductVersion,
                            baseDirectory = AppContext.BaseDirectory, runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(),
                            framework = RuntimeInformation.FrameworkDescription, sampleCount = samples.Length,
                            savedTheme = ThemeFiles.Load(Path.Combine(saved, "theme.json")).Name, restoredTheme, preview = image
                        };
                        File.WriteAllText(output, JsonSerializer.Serialize(report, ThemeFiles.Json));
                        desktop.Shutdown(0);
                    }
                    catch (Exception e)
                    {
                        File.WriteAllText(output, JsonSerializer.Serialize(new { success = false, error = e.ToString() }, ThemeFiles.Json));
                        desktop.Shutdown(1);
                    }
                }, DispatcherPriority.Loaded);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args is ["--help"])
            {
                Console.WriteLine("""
                    TypeFlow.CrossPlatform
                      --workspace <theme-library-directory>
                      --theme <native-theme-directory|theme.json|theme.jsonc>
                      --rime <native-librime-path> <shared-data> <user-data> <schema>
                      --smoke-test <report.json> --workspace <temporary-profile>
                    Without --rime, the workbench displays explicitly labeled demonstration candidates.
                    Default library: LocalApplicationData/TypeFlow/Avalonia
                    """);
                return 0;
            }
            App.Options = LaunchOptions.Parse(args);
            return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
