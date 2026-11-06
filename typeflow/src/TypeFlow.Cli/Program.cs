using System.Text.Json;
using TypeFlow.Core;
using TypeFlow.Engine.Rime;
using TypeFlow.Importers.Ssf;

try
{
    switch (args)
    {
        case ["--validate", var config]:
            var theme = ThemeFiles.Load(config);
            // A headless structural check does not imply successful image decoding or UI rendering.
            Print(new { theme.Id, theme.Name, validation = "model-and-resource-paths" });
            return 0;
        case ["--import", var source, var destination]:
            var importers = new ImporterRegistry();
            importers.Register(new SsfImporter());
            importers.Register(new NativeThemeImporter());
            var result = await importers.Resolve(source).ImportAsync(source, destination);
            Print(result.Report);
            return 0;
        case ["--engine-smoke", var library, var shared, var user, var schema, var output]:
            using (var engine = new RimeEngine(library, shared, user, schema))
            {
                foreach (var key in "nihao") engine.ProcessKey(key);
                var before = engine.View;
                if (before.Candidates.Count == 0) throw new InvalidOperationException("Rime 没有返回候选。");
                var commit = engine.SelectCandidate(0).Commit;
                if (string.IsNullOrWhiteSpace(commit)) throw new InvalidOperationException("Rime 没有返回上屏文字。");
                File.WriteAllText(output, JsonSerializer.Serialize(new { engine.Id, before, commit }, ThemeFiles.Json));
            }
            return 0;
        case ["--help"] or []:
            Console.WriteLine("""
                TypeFlow.Cli (Windows / Linux / macOS; .NET 10)
                  --validate <theme.json|theme.jsonc>
                  --import <source.ssf|theme-directory> <new-destination>
                  --engine-smoke <absolute-librime-path> <shared-data> <user-data> <schema> <output.json>
                --validate checks the model and resource paths; it does not decode images.
                --engine-smoke requires the native bridge and librime for the current OS/architecture.
                """);
            return 0;
        default:
            Console.Error.WriteLine("参数不正确；使用 --help 查看用法。");
            return 2;
    }
}
catch (Exception e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, ThemeFiles.Json));
