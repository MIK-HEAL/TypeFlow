using System.IO;
using System.Text.Json;
using System.Windows;
using TypeFlow.Core;
using TypeFlow.Engine.Rime;
using TypeFlow.Importers.Ssf;

namespace TypeFlow.Desktop;

internal static class Program
{
    public static readonly CandidateView Demo = new("ni gu miao miao", [new("尼古喵喵"), new("你好"), new("喵喵"), new("输入法")]);
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args is ["--import", var source, var destination])
            {
                var registry = new ImporterRegistry(); registry.Register(new SsfImporter()); registry.Register(new NativeThemeImporter());
                var result = registry.Resolve(source).ImportAsync(source, destination).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(result.Report, ThemeFiles.Json)); return 0;
            }
            if (args.Length >= 3 && args[0] == "--render")
            {
                var surface = new CandidateSurface(); surface.SetTheme(ThemeFiles.Load(args[1]), Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
                surface.SetView(Demo); surface.SavePng(args[2], args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 1);
                return 0;
            }
            if (args is ["--engine-smoke", var dll, var shared, var user, var schema, var output])
            {
                using var engine = new RimeEngine(dll, shared, user, schema);
                foreach (var key in "nihao") engine.ProcessKey(key);
                var before = engine.View;
                if (before.Candidates.Count == 0) throw new InvalidOperationException("真实 Rime 没有返回候选。");
                var commit = engine.SelectCandidate(0).Commit;
                if (string.IsNullOrWhiteSpace(commit)) throw new InvalidOperationException("真实 Rime 没有返回上屏文字。");
                File.WriteAllText(output, JsonSerializer.Serialize(new { engine.Id, before, commit }, ThemeFiles.Json)); return 0;
            }
            var root = FindProductRoot();
            var library = new ThemeLibrary(Path.Combine(root, "workspace"));
            var app = new Application();
            if (args is ["--weasel-host"]) return app.Run(new WeaselHost(library));
            return app.Run(new MainWindow(library, root));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            if (args.Length == 0) MessageBox.Show(e.Message, "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
    public static string FindProductRoot()
    {
        for (var p = new DirectoryInfo(AppContext.BaseDirectory); p != null; p = p.Parent)
            if (Directory.Exists(Path.Combine(p.FullName, "src/TypeFlow.Core"))) return p.FullName;
        return AppContext.BaseDirectory;
    }
}
