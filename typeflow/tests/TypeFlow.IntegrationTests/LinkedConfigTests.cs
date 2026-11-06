using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using TypeFlow.Core;
using TypeFlow.Desktop;

internal static class LinkedConfigTests
{
    public static int Run(string repo, string temp, ThemeLibrary library, IInputEngine engine)
    {
        var passed = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("CONFIG FAIL: " + label); passed++; }
        var author = Path.Combine(temp, "my-config"); Directory.CreateDirectory(author);
        var config = Path.Combine(author, "my-skin.jsonc");
        var template = Path.Combine(repo, "typeflow/themes/config-example/theme.jsonc");
        File.Copy(template, config);
        var model = ThemeFiles.Load(config);
        Check(model.Name == "我的配置皮肤" && model.Layout.CandidatePosition == new TextPosition(20, 54), "annotated JSONC template loads");
        Check(model.Schema != null && model.Text.FontSize == 18, "schema metadata and trailing commas");
        var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "typeflow/docs/theme.schema.json")));
        Check(schema.RootElement.GetProperty("properties").TryGetProperty("layers", out _), "published schema contains layers");
        var original = File.ReadAllBytes(config);
        var entry = library.Link(config);
        Check(library.ConfigFile(entry).Equals(Path.GetFullPath(config), StringComparison.OrdinalIgnoreCase), "configuration entry resolves original authoring file");
        Check(File.ReadAllBytes(config).SequenceEqual(original), "link never overwrites source");
        Check(File.Exists(Path.Combine(entry, "link.json")), "persistent link record");
        library.Activate(entry);
        var monitor = new ThemeConfigMonitor(library, entry);
        Check(monitor.Poll().Error == null, "initial poll accepts snapshot");
        foreach (var key in "nihao") engine.ProcessKey(key);
        var composition = JsonSerializer.Serialize(engine.View, ThemeFiles.Json);

        var timestamp = File.GetLastWriteTimeUtc(config);
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"fontSize\": 18", "\"fontSize\": 24"));
        File.SetLastWriteTimeUtc(config, timestamp);
        var reload = monitor.Poll();
        Check(reload.Changed && reload.Error == null, "content hash detects same-size same-timestamp edit");
        Check(ThemeFiles.Load(Path.Combine(entry, "theme.json")).Text.FontSize == 24, "hot reload publishes font change");
        Check(JsonSerializer.Serialize(engine.View, ThemeFiles.Json) == composition, "configuration reload preserves real preedit and candidates");
        var good = File.ReadAllBytes(Path.Combine(entry, "theme.json"));
        File.WriteAllText(config, "{\"layout\":");
        Check(monitor.Poll().Error != null, "incomplete editor save is diagnosed");
        Check(File.ReadAllBytes(Path.Combine(entry, "theme.json")).SequenceEqual(good), "invalid JSON does not publish over last good snapshot");
        Check(library.ActiveDirectory == entry, "invalid external JSON keeps active skin");

        model = model with { Text = model.Text with { FontSize = 21 } };
        var atomic = config + ".tmp";
        File.WriteAllText(atomic, JsonSerializer.Serialize(model, ThemeFiles.Json)); File.Move(atomic, config, true);
        Check(monitor.Poll().Changed && ThemeFiles.Load(Path.Combine(entry, "theme.json")).Text.FontSize == 21, "atomic rename-save recovers");

        var image = Path.Combine(author, "background.png");
        File.Copy(Path.Combine(temp, "art/red.png"), image);
        model = model with { Background = new("background.png", "stretch"), Layers = [new("decoration", "background.png", Width: 12, Height: 12)] };
        File.WriteAllText(config, JsonSerializer.Serialize(model, ThemeFiles.Json));
        Check(monitor.Poll(true).Changed, "new image reference is hot-reloaded");
        var first = ThemeFiles.Load(Path.Combine(entry, "theme.json"));
        Check(first.Layers[0].Image == first.Background.Image && first.Background.Image!.StartsWith("assets/", StringComparison.Ordinal), "shared image reference rebased into cache");
        var firstImagePath = ThemeFiles.AssetPath(entry, first.Background.Image!);
        var firstImage = File.ReadAllBytes(firstImagePath);
        File.Copy(Path.Combine(temp, "art/blue.png"), image, true);
        Check(monitor.Poll(true).Changed, "image edit changes snapshot without JSON edit");
        var second = ThemeFiles.Load(Path.Combine(entry, "theme.json"));
        Check(second.Background.Image != first.Background.Image, "image versions are content-addressed");
        Check(File.ReadAllBytes(firstImagePath).SequenceEqual(firstImage), "older valid image remains immutable");
        var secondGood = File.ReadAllBytes(Path.Combine(entry, "theme.json"));
        File.WriteAllText(image, "invalid image bytes");
        Check(monitor.Poll(true).Error != null && File.ReadAllBytes(Path.Combine(entry, "theme.json")).SequenceEqual(secondGood), "bad image retains prior valid configuration");
        File.Delete(image);
        Check(monitor.Poll(true).Error != null, "missing image reported");
        File.Copy(Path.Combine(temp, "art/red.png"), image);
        Check(monitor.Poll(true).Error == null, "restored image recovers");
        File.Delete(config);
        Check(monitor.Poll(true).Error != null, "removed authoring file reported");
        var restarted = new ThemeLibrary(library.Root);
        Check(restarted.ActiveDirectory == entry && ThemeFiles.Load(Path.Combine(entry, "theme.json")).Layers.Length == 1, "restart works from last good snapshot without source");
        File.WriteAllText(config, JsonSerializer.Serialize(model, ThemeFiles.Json));
        monitor.Poll(true);
        Check(JsonSerializer.Serialize(engine.View, ThemeFiles.Json) == composition, "failed and recovered reloads preserve input");
        var sourceHash = SHA256.HashData(File.ReadAllBytes(config));
        library.Delete(entry);
        Check(File.Exists(config) && File.Exists(image) && SHA256.HashData(File.ReadAllBytes(config)).SequenceEqual(sourceHash), "removing association never deletes or modifies external source");
        Check(library.ActiveDirectory == library.DefaultDirectory, "removed active association restores default");

        var local = library.Import(Path.Combine(repo, "typeflow/themes/night")).GetAwaiter().GetResult().ThemeDirectory;
        var localMonitor = new ThemeConfigMonitor(library, local);
        localMonitor.Poll();
        var localFile = Path.Combine(local, "theme.json"); var localTheme = ThemeFiles.Load(localFile);
        File.WriteAllText(localFile, JsonSerializer.Serialize(localTheme with { Layout = localTheme.Layout with { CandidateGap = 23 } }, ThemeFiles.Json));
        Check(localMonitor.Poll().Changed && library.ConfigFile(local) == localFile, "managed JSON configurations also reload");
        var invalidSource = Path.Combine(author, "bad.json"); File.WriteAllText(invalidSource, "{ bad");
        var entriesBefore = Directory.GetDirectories(library.Themes).Length;
        try { library.Link(invalidSource); throw new Exception("invalid association accepted"); } catch (JsonException) { passed++; }
        Check(Directory.GetDirectories(library.Themes).Length == entriesBefore, "failed initial association leaves no entry or staging directory");
        engine.Clear();
        Console.WriteLine($"PASS: {passed} configuration checks (JSONC, associations, atomic reload, image versions, recovery and input preservation).");
        return passed;
    }
}
