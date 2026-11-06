using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TypeFlow.Core;
using TypeFlow.Themes;
using TypeFlow.Rendering.Wpf;
using TypeFlow.Desktop;

internal static class LayoutTests
{
    public static int Run(string repo, string temp, ThemeLibrary library, string source, IInputEngine engine)
    {
        int passed = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("LAYOUT FAIL: " + label); passed++; }
        var evidence = Path.Combine(repo, "typeflow/build/evidence");
        var assets = Path.Combine(temp, "art"); Directory.CreateDirectory(assets);
        var red = MakeImage(Path.Combine(assets, "red.png"), 8, 8, (_, _) => Colors.Red);
        var blue = MakeImage(Path.Combine(assets, "blue.png"), 8, 8, (_, _) => Colors.Blue);
        var tiled = MakeImage(Path.Combine(assets, "tile.png"), 12, 12, (x, y) =>
            x < 2 || y < 2 ? Colors.Lime : x >= 10 || y >= 10 ? Colors.Magenta : (x - 2) % 8 < 4 ? Colors.Red : Colors.Blue);
        var originalHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "theme.json")));
        foreach (var key in "nihao") engine.ProcessKey(key);
        var before = JsonSerializer.Serialize(engine.View, ThemeFiles.Json);
        string saved;
        string draft;
        using (var edit = new ThemeEditSession(library, source))
        {
            draft = edit.Directory;
            var baseline = JsonSerializer.Serialize(edit.Current, ThemeFiles.Json);
            edit.Update(t => t with { Layout = t.Layout with { PreeditPosition = new(170, 18), CandidatePosition = new(165, 48), MinHeight = 190 } });
            var changed = JsonSerializer.Serialize(edit.Current, ThemeFiles.Json);
            edit.Undo(); Check(JsonSerializer.Serialize(edit.Current, ThemeFiles.Json) == baseline, "undo complete snapshot");
            edit.Redo(); Check(JsonSerializer.Serialize(edit.Current, ThemeFiles.Json) == changed, "redo complete snapshot");
            var id = edit.AddLayer(red);
            edit.EditLayer(id[6..], l => l with { Name = "角标", Anchor = "topRight", X = -16, Y = 110, Width = 28, Height = 28, Opacity = .6, Plane = "aboveText" });
            var surface = new CandidateSurface(); surface.SetView(engine.View); surface.SetTheme(edit.Current, edit.Directory);
            surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); surface.Arrange(new Rect(surface.DesiredSize));
            edit.Move("preedit", 7, 5, surface.Elements);
            Check(edit.Current.Layout.PreeditPosition == new TextPosition(177, 23), "move explicit text position");
            Check(edit.Current.Layout.CandidatePosition == new TextPosition(165, 48), "independent candidate position");
            var snapshot = JsonSerializer.Serialize(edit.Current, ThemeFiles.Json);
            try { edit.Update(t => t with { Layers = [.. t.Layers, t.Layers[0]] }); throw new Exception("duplicate accepted"); }
            catch (InvalidDataException) { passed++; }
            Check(JsonSerializer.Serialize(edit.Current, ThemeFiles.Json) == snapshot, "invalid edit is transactional");
            try { edit.Update(t => t with { Background = t.Background with { Mode = "nineSlice", Slice = new(9999, 0, 0, 0) } }); throw new Exception("bad slice accepted"); }
            catch (InvalidDataException) { passed++; }
            edit.Update(t => t with { Background = t.Background with { HorizontalFill = "tile" } });
            saved = edit.SaveCopy("尼古喵喵 · 布局自定义测试");
            var native = ThemeFiles.Load(Path.Combine(saved, "theme.json"));
            Check(native.RequiredCapabilities.Contains("decoration-layers") && native.RequiredCapabilities.Contains("text-position") && native.RequiredCapabilities.Contains("tiled-images"), "declared capabilities");
            Check(native.Id != edit.Current.Id && native.Author == edit.Current.Author && native.License == edit.Current.License, "new identity and retained attribution");
            Check(File.Exists(Path.Combine(saved, "import-report.json")), "original SSF limitations retained");
            var manifest = JsonSerializer.Deserialize<ImportReport>(File.ReadAllText(Path.Combine(saved, "import-report.json")), ThemeFiles.Json)!;
            Check(manifest.Compatibility == "partial", "editing does not claim full conversion");
            surface.SetTheme(native, saved); surface.SavePng(Path.Combine(evidence, "advanced-layout.png"));
            Check(JsonSerializer.Serialize(engine.View, ThemeFiles.Json) == before, "editing and saving preserve real composition");
            Check(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "theme.json"))).SequenceEqual(originalHash), "original theme unchanged");
            edit.Undo(); edit.Update(t => t with { Layout = t.Layout with { CandidateGap = 17 } }); Check(!edit.CanRedo, "editing after undo truncates redo");
        }
        Check(!Directory.Exists(draft), "draft cleanup");
        var standalone = new CandidateSurface(); standalone.SetView(engine.View); standalone.SetTheme(ThemeFiles.Load(Path.Combine(saved, "theme.json")), saved);
        standalone.SavePng(Path.Combine(evidence, "advanced-layout-reloaded.png"));
        Check(File.ReadAllBytes(Path.Combine(evidence, "advanced-layout.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(evidence, "advanced-layout-reloaded.png"))), "saved render survives draft deletion");
        var reimported = new NativeThemeImporter().ImportAsync(saved, Path.Combine(temp, "layer-reimport")).GetAwaiter().GetResult();
        Check(ThemeFiles.ReferencedImages(reimported.Theme).All(x => File.Exists(ThemeFiles.AssetPath(reimported.ThemeDirectory, x))), "native plugin copies layer assets");
        var window = new LayoutEditorWindow(library, saved, engine.View);
        window.SavePreview(Path.Combine(evidence, "layout-editor.png")); window.Close();
        Check(File.Exists(Path.Combine(evidence, "layout-editor.png")), "actual editor controls render without showing a window");

        var canvas = new CandidateSurface(); canvas.SetView(new("", []));
        var theme = new Theme { Layout = new(Padding: new(0, 0, 0, 0), MinWidth: 100, MinHeight: 80), Background = new(Color: "#FFFFFFFF") };
        theme = theme with { Layers = [new("blue", "blue.png", X: 0, Y: 0, Width: 20, Height: 20), new("red", "red.png", X: 0, Y: 0, Width: 20, Height: 20, Plane: "aboveText")] };
        canvas.SetTheme(theme, assets); var frame = Render(canvas);
        Check(Pixel(frame, 10, 10) == Colors.Red, "foreground layer painted after behind layer");
        theme = theme with { Layers = [theme.Layers[0], theme.Layers[1] with { Visible = false }] };
        canvas.SetTheme(theme, assets); frame = Render(canvas); Check(Pixel(frame, 10, 10) == Colors.Blue, "hidden layer omitted");
        theme = theme with { Layers = [new("anchor", "red.png", Anchor: "bottomRight", X: -3, Y: -4, Width: 10, Height: 12)] };
        canvas.SetTheme(theme, assets); frame = Render(canvas);
        Check(canvas.Elements.Single(x => x.Id == "layer:anchor").Bounds == new Rect(87, 64, 10, 12), "bottom-right anchor geometry");
        Check(Pixel(frame, 88, 65) == Colors.Red, "anchored decoration renders");
        theme = theme with { Layers = [theme.Layers[0] with { Anchor = "topLeft", X = 0, Y = 0, Opacity = .5 }] };
        canvas.SetTheme(theme, assets); frame = Render(canvas); var pixel = Pixel(frame, 4, 4);
        Check(pixel.R == 255 && pixel.B is >= 126 and <= 129, "opacity blends against background");
        theme = theme with { Layers = [], Background = new("tile.png", "tile") };
        canvas.SetTheme(theme, assets); frame = Render(canvas);
        Check(Pixel(frame, 4, 4) == Colors.Red && Pixel(frame, 16, 4) == Colors.Red && Pixel(frame, 8, 4) == Colors.Blue, "whole-image tiling repeats source pixels");
        theme = theme with { Background = new("tile.png", "nineSlice", new(2, 2, 2, 2), HorizontalFill: "tile", VerticalFill: "tile") };
        canvas.SetTheme(theme, assets); frame = Render(canvas);
        Check(Pixel(frame, 0, 0) == Colors.Lime && Pixel(frame, 99, 79) == Colors.Magenta, "nine-slice corners retained");
        Check(Pixel(frame, 3, 4) == Pixel(frame, 11, 4) && Pixel(frame, 3, 4) != Pixel(frame, 7, 4), "nine-slice center tiles at source size");
        engine.Clear();
        Console.WriteLine($"PASS: {passed} advanced layout checks (transactional edits, assets, coordinates, anchors, opacity and pixel-level tiling).");
        return passed;
    }
    private static string MakeImage(string path, int width, int height, Func<int, int, Color> color)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { var c = color(x, y); var i = (y * width + x) * 4; pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = c.A; }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); return path;
    }
    private static RenderTargetBitmap Render(CandidateSurface surface)
    {
        surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); surface.Arrange(new Rect(surface.DesiredSize)); surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface); return bitmap;
    }
    private static Color Pixel(BitmapSource image, int x, int y)
    { var bytes = new byte[4]; image.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0); return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]); }
}
