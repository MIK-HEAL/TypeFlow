using System.Text.Json;
using TypeFlow.Core;
using TypeFlow.Themes;

internal static class ThemeServiceTests
{
    public static int Run(string temp)
    {
        var passed = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("SERVICE FAIL: " + label); passed++; }
        var importers = new ImporterRegistry();
        importers.Register(new NativeThemeImporter());
        var validator = new TestValidator();
        var library = new ThemeLibrary(Path.Combine(temp, "services"), importers, validator);
        Check(library.Importers.Importers.Count == 1, "host controls importer registration");
        var author = Path.Combine(temp, "author"); Directory.CreateDirectory(author);
        var source = Path.Combine(author, "skin.jsonc");
        File.WriteAllText(source, JsonSerializer.Serialize(new Theme { Name = "Portable" }, ThemeFiles.Json));
        var entry = library.Link(source);
        library.Activate(entry);
        var monitor = new ThemeConfigMonitor(library, entry);
        Check(monitor.Poll().Error == null && validator.Calls > 0, "host validator runs without a GUI");
        var lastGood = File.ReadAllBytes(Path.Combine(entry, "theme.json"));
        File.WriteAllText(source, JsonSerializer.Serialize(new Theme { Name = "Updated" }, ThemeFiles.Json));
        validator.Reject = true;
        Check(monitor.Poll(true).Error != null, "host validation failure is reported");
        Check(lastGood.SequenceEqual(File.ReadAllBytes(Path.Combine(entry, "theme.json"))), "validation failure preserves published snapshot");
        validator.Reject = false;
        Check(monitor.Poll(true).Changed && ThemeFiles.Load(Path.Combine(entry, "theme.json")).Name == "Updated", "reload recovers");
        File.WriteAllText(source, "{ bad");
        Check(monitor.Poll(true).Error != null, "invalid JSON is reported");
        var restarted = new ThemeLibrary(library.Root, importers, validator);
        Check(restarted.ActiveDirectory == entry, "restart uses last valid snapshot");
        var siblingRoot = Path.Combine(temp, "SERVICES");
        if (!OperatingSystem.IsWindows())
        {
            var sibling = Path.Combine(siblingRoot, "themes", Path.GetFileName(entry));
            Directory.CreateDirectory(sibling);
            ThemeFiles.Save(new Theme(), sibling);
            try { library.Activate(sibling); throw new Exception("case-distinct sibling accepted"); }
            catch (InvalidDataException) { passed++; }
            try { library.Delete(sibling); throw new Exception("case-distinct sibling deleted"); }
            catch (InvalidOperationException) { passed++; }
            Check(Directory.Exists(sibling), "case-distinct sibling remains intact");
        }
        library.Delete(entry);
        Check(File.Exists(source) && library.ActiveDirectory == library.DefaultDirectory, "unlink preserves external source and restores default");
        return passed;
    }

    // Failure injection verifies the publication boundary; real image decoding stays in renderer tests.
    private sealed class TestValidator : IThemeValidator
    {
        public bool Reject { get; set; }
        public int Calls { get; private set; }
        public void Validate(Theme theme, string directory)
        {
            Calls++;
            ThemeFiles.Validate(theme, directory);
            if (Reject) throw new InvalidDataException("Test renderer rejected the theme.");
        }
    }
}
