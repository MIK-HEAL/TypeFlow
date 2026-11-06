using System.IO.Compression;
using System.Text;
using System.Text.Json;
using TypeFlow.Core;
using TypeFlow.Importers.Ssf;

var repo = Path.GetFullPath(args.Length > 0 ? args[0] : "../../..");
var temp = Path.Combine(repo, "typeflow/build/tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
var passed = 0;
try
{
    var importer = new SsfImporter();
    var sample = Path.Combine(repo, "test/ssf/尼古喵喵！.ssf");
    var imported = await importer.ImportAsync(sample, Path.Combine(temp, "sample"));
    Check(imported.Theme.Name == "尼古喵喵！" && imported.Theme.Author == "忧郁游戏", "UTF-16 metadata");
    Check(imported.Report.Sha256 == "7d7ed80e39481bf754839ef9ea802798751c0e550c3a54ee4e2055cad3a0347d", "real fixture provenance");
    Check(imported.Theme.Text.Color == "#FFADB101" && imported.Theme.Selection.TextColor == "#FF71961B", "BGR to ARGB");
    Check(imported.Theme.Layout.PreeditMargin!.Left == 160 && imported.Theme.Layout.PreeditMargin.Top == 21, "SSF margin order");
    Check(imported.Report.Compatibility == "partial" && imported.Report.Diagnostics.Any(x => x.Code == "animation") && imported.Report.Diagnostics.Any(x => x.Code == "invalid-slice"), "honest compatibility report");
    var loaded = ThemeFiles.Load(Path.Combine(imported.ThemeDirectory, "theme.json"));
    Check(loaded.Id == imported.Theme.Id && File.Exists(Path.Combine(imported.ThemeDirectory, "source/Skin.ini")), "standalone native theme");
    await Reject<IOException>(() => importer.ImportAsync(sample, imported.ThemeDirectory), "no overwrite");
    var registry = new ImporterRegistry(); registry.Register(importer); registry.Register(new NativeThemeImporter());
    Check(registry.Resolve(sample).Info.Id == "typeflow.ssf" && registry.Resolve(imported.ThemeDirectory).Info.Id == "typeflow.native", "two import plugins");
    var native = await registry.Resolve(imported.ThemeDirectory).ImportAsync(imported.ThemeDirectory, Path.Combine(temp, "native"));
    Check(native.Theme.Id == imported.Theme.Id && File.Exists(Path.Combine(native.ThemeDirectory, "assets/background.png")), "native importer copies referenced image");

    foreach (var entry in new[] { "../escape.png", "C:/escape.png", "assets\\..\\escape.png", "assets/name:stream", "assets/./image.png" })
    {
        var archive = MakeZip((entry, [1]));
        await Reject<InvalidDataException>(() => importer.ImportAsync(archive, Path.Combine(temp, Guid.NewGuid().ToString("N"))), "reject unsafe archive path " + entry);
    }
    await Reject<InvalidDataException>(() => importer.ImportAsync(MakeZip(("Skin.ini", [1]), ("skin.ini", [2])), Path.Combine(temp, "duplicate")), "case collision");
    await Reject<InvalidDataException>(() => importer.ImportAsync(MakeZip(("other.txt", [1])), Path.Combine(temp, "missing")), "missing config");
    using (var ct = new CancellationTokenSource())
    {
        ct.Cancel();
        await Reject<OperationCanceledException>(() => importer.ImportAsync(sample, Path.Combine(temp, "cancel"), ct.Token), "cancel rolls back staging");
    }
    Check(!Directory.EnumerateDirectories(temp).Any(x => x.Contains(".import-")), "no failed import debris");
    foreach (var theme in new[] {
        new Theme { FormatVersion = 99 }, new Theme { RequiredCapabilities = ["arbitrary-script"] },
        new Theme { Text = new(FontSize: double.NaN) }, new Theme { Background = new("../escape.png", "stretch") },
        new Theme { Layout = new(Direction: "vertical") }, new Theme { Text = new(Color: "red") } })
    {
        try { ThemeFiles.Validate(theme, temp); throw new Exception("Invalid theme accepted"); }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException) { passed++; }
    }
    // Same fixture configuration in three encodings, mixed image case, and arbitrary extension.
    using (var zip = ZipFile.OpenRead(sample))
    {
        var config = Encoding.Unicode.GetString(zip.GetEntry("skin.ini")!.Open().ReadAll()).TrimStart('\uFEFF');
        var image = zip.GetEntry("skin1.png")!.Open().ReadAll();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var encoding in new[] { Encoding.UTF8, Encoding.GetEncoding("GB18030"), Encoding.BigEndianUnicode })
        {
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(config)).ToArray();
            var path = MakeZip(("SKIN.INI", bytes), ("SKIN1.PNG", image));
            var result = await importer.ImportAsync(path, Path.Combine(temp, Guid.NewGuid().ToString("N")));
            Check(result.Theme.Name == "尼古喵喵！", "encoding and case " + encoding.WebName);
        }
    }
    var large = MakeZip(("huge.bin", new byte[17 * 1024 * 1024]));
    await Reject<InvalidDataException>(() => importer.ImportAsync(large, Path.Combine(temp, "large")), "expanded entry size limit");
    var invalidJson = Path.Combine(temp, "invalid.json");
    await File.WriteAllTextAsync(invalidJson, "{\"layout\":null}");
    try { ThemeFiles.Load(invalidJson); throw new Exception("null accepted"); } catch (InvalidDataException) { passed++; }
    Console.WriteLine($"PASS: {passed} checks, including supplied SSF, malicious archives, encoding variants and rollback.");
}
finally { Directory.Delete(temp, true); }

void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); passed++; }
async Task Reject<T>(Func<Task> action, string label) where T : Exception
{
    try { await action(); } catch (T) { passed++; return; }
    throw new Exception("FAIL: " + label);
}
string MakeZip(params (string Name, byte[] Content)[] files)
{
    var path = Path.Combine(temp, Guid.NewGuid().ToString("N") + ".data");
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var file in files) { using var stream = zip.CreateEntry(file.Name).Open(); stream.Write(file.Content); }
    return path;
}
static class StreamExtensions
{
    public static byte[] ReadAll(this Stream stream) { using (stream) { using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray(); } }
}
