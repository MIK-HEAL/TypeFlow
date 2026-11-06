using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using TypeFlow.Core;
using TypeFlow.Engine.Rime;
using TypeFlow.Importers.Ssf;
using TypeFlow.Rendering.Avalonia;
using TypeFlow.Themes;

namespace TypeFlow.CrossPlatform;

public sealed record LaunchOptions(string Workspace, string? Theme = null, string[]? Rime = null)
{
    public static LaunchOptions Parse(string[] args)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypeFlow", "Avalonia");
        string? theme = null; string[]? rime = null;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--workspace" when i + 1 < args.Length: root = args[++i]; break;
                case "--theme" when i + 1 < args.Length: theme = args[++i]; break;
                case "--rime" when i + 4 < args.Length: rime = args[(i + 1)..(i + 5)]; i += 4; break;
                default: throw new ArgumentException("参数不正确；使用 --help 查看用法。");
            }
        return new(Path.GetFullPath(root), theme, rime);
    }
}

public static class CrossPlatformComponents
{
    public static ThemeLibrary CreateThemeLibrary(string root)
    {
        var imports = new ImporterRegistry();
        imports.Register(new SsfImporter()); imports.Register(new NativeThemeImporter());
        return new(root, imports, new AvaloniaThemeValidator());
    }
}

public sealed class App : Application
{
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
