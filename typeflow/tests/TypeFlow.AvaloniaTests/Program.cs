using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TypeFlow.Core;
using TypeFlow.CrossPlatform;
using TypeFlow.Engine.Rime;
using TypeFlow.Rendering.Avalonia;
using TypeFlow.Themes;
using Surface = TypeFlow.Rendering.Avalonia.CandidateSurface;
using Images = TypeFlow.Rendering.Avalonia.ThemeImages;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var repo = Path.GetFullPath(args[0]);
        var temp = Path.GetFullPath(Path.Combine(repo, "typeflow/build/avalonia-tests-" + Guid.NewGuid().ToString("N")));
        var evidence = Path.Combine(repo, "typeflow/build/evidence/avalonia");
        Directory.CreateDirectory(temp); Directory.CreateDirectory(evidence);
        var passed = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("AVALONIA FAIL: " + label); passed++; }
        void Reject<T>(Action action) where T : Exception
        { try { action(); } catch (T) { passed++; return; } throw new Exception("Expected " + typeof(T).Name); }
        try
        {
            var library = CrossPlatformComponents.CreateThemeLibrary(Path.Combine(temp, "workspace"));
            var imported = library.Import(Path.Combine(repo, "test/ssf/尼古喵喵！.ssf")).GetAwaiter().GetResult();
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            using var surface = new Surface();
            var window = new Window { Content = surface, SizeToContent = SizeToContent.WidthAndHeight };
            surface.SetTheme(imported.Theme, imported.ThemeDirectory); surface.SetView(WorkbenchWindow.Demo);
            window.Show(); window.UpdateLayout();
            foreach (var scale in new[] { 1.0, 1.5, 2.0 })
            {
                var path = Path.Combine(evidence, $"ssf-{scale:0.0}.png");
                surface.SavePng(path, scale);
                using var bitmap = new Bitmap(path);
                Check(bitmap.PixelSize.Width >= 431 * scale && bitmap.PixelSize.Height >= 160 * scale, "real image output at scale " + scale);
            }
            var red = MakeImage(Path.Combine(temp, "red.png"), 8, 8, (_, _) => Colors.Red);
            var blue = MakeImage(Path.Combine(temp, "blue.png"), 8, 8, (_, _) => Colors.Blue);
            var highDpi = MakeImage(Path.Combine(temp, "high-dpi.png"), 8, 8, (_, _) => Colors.Red, new(192, 192));
            using (var bitmap = Images.Load(highDpi))
                Check(bitmap.Size == new Size(8, 8) && bitmap.Dpi == new Vector(96, 96), "PNG DPI metadata does not change skin geometry");
            var tile = MakeImage(Path.Combine(temp, "tile.png"), 12, 12, (x, y) =>
                x < 2 || y < 2 ? Colors.Lime : x >= 10 || y >= 10 ? Colors.Magenta : (x - 2) % 8 < 4 ? Colors.Red : Colors.Blue);
            var plain = new Theme
            {
                Layout = new(Padding: new(0, 0, 0, 0), MinWidth: 100, MinHeight: 80),
                Background = new(Color: "#FFFFFFFF")
            };
            surface.SetView(new("", []));
            var model = plain with { Layers = [new("blue", "blue.png", Width: 20, Height: 20), new("red", "red.png", Width: 20, Height: 20, Plane: "aboveText")] };
            surface.SetTheme(model, temp);
            using (var bitmap = Render(surface)) Check(Pixel(bitmap, 10, 10) == Colors.Red, "foreground painted after background layers");
            surface.SetTheme(model with { Layers = [model.Layers[0], model.Layers[1] with { Visible = false }] }, temp);
            using (var bitmap = Render(surface)) Check(Pixel(bitmap, 10, 10) == Colors.Blue, "hidden layers omitted");
            model = plain with { Layers = [new("anchor", "red.png", Anchor: "bottomRight", X: -3, Y: -4, Width: 10, Height: 12)] };
            surface.SetTheme(model, temp);
            using (var bitmap = Render(surface))
            {
                Check(surface.Elements.Single(x => x.Id == "layer:anchor").Bounds == new Rect(87, 64, 10, 12), "anchor geometry uses DIP");
                Check(Pixel(bitmap, 88, 65) == Colors.Red, "anchored layer renders");
            }
            surface.SetTheme(plain with { Layers = [new("alpha", "red.png", Width: 20, Height: 20, Opacity: .5)] }, temp);
            using (var bitmap = Render(surface))
            {
                var pixel = Pixel(bitmap, 5, 5);
                Check(pixel.R == 255 && pixel.B is >= 126 and <= 129, "layer opacity blends");
            }
            surface.SetTheme(plain with { Background = new("tile.png", "tile") }, temp);
            using (var bitmap = Render(surface))
                Check(Pixel(bitmap, 4, 4) == Colors.Red && Pixel(bitmap, 16, 4) == Colors.Red && Pixel(bitmap, 8, 4) == Colors.Blue, "tile source repeats");
            var nine = plain with { Background = new("tile.png", "nineSlice", new(2, 2, 2, 2), HorizontalFill: "tile", VerticalFill: "tile") };
            surface.SetTheme(nine, temp);
            using (var bitmap = Render(surface))
            {
                Check(Pixel(bitmap, 0, 0) == Colors.Lime && Pixel(bitmap, 99, 79) == Colors.Magenta, "nine-slice corners preserved");
                Check(Pixel(bitmap, 3, 4) == Pixel(bitmap, 11, 4) && Pixel(bitmap, 3, 4) != Pixel(bitmap, 7, 4), "nine-slice center repeats");
            }
            Reject<InvalidDataException>(() => surface.SetTheme(nine with { Background = nine.Background with { Slice = new(7, 2, 7, 2) } }, temp));
            using (var bitmap = Render(surface)) Check(Pixel(bitmap, 99, 79) == Colors.Magenta, "failed theme retains previous image");
            var invalid = Path.Combine(temp, "invalid.png"); File.WriteAllText(invalid, "bad image");
            Reject<NotSupportedException>(() => surface.SetTheme(plain with { Background = new("invalid.png", "stretch") }, temp));
            var oversized = File.ReadAllBytes(red); BinaryPrimitives.WriteUInt32BigEndian(oversized.AsSpan(16, 4), 9000);
            File.WriteAllBytes(invalid, oversized);
            Reject<InvalidDataException>(() => Images.Load(invalid));
            var animation = File.ReadAllBytes(red); var actl = new byte[20];
            BinaryPrimitives.WriteUInt32BigEndian(actl.AsSpan(0, 4), 8); "acTL"u8.CopyTo(actl.AsSpan(4));
            File.WriteAllBytes(invalid, [.. animation[..33], .. actl, .. animation[33..]]);
            Reject<NotSupportedException>(() => Images.Load(invalid));
            var normalized = Path.Combine(temp, "normalized.png");
            new AvaloniaThemeImageTranscoder().WriteStaticPng(red, normalized);
            using (var bitmap = Images.Load(normalized)) Check(bitmap.PixelSize == new PixelSize(8, 8), "injected image backend writes static PNG");

            surface.SetTheme(plain with { Layout = plain.Layout with { PreeditPosition = new(20, 10) } }, temp);
            surface.SetView(new("abc", [new("one"), new("two")]));
            surface.IsEditing = true;
            TextPosition? moved = null;
            surface.ElementMoved += (id, dx, dy) => { if (id == "preedit") moved = new(20 + dx, 10 + dy); };
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Dispose();
            window.MouseDown(new(21, 11), MouseButton.Left);
            window.MouseMove(new(31, 16), RawInputModifiers.LeftMouseButton); window.MouseUp(new(31, 16), MouseButton.Left);
            Check(moved == new TextPosition(30, 15), $"pointer drag raises movement in DIP (selected={surface.SelectedElement}, moved={moved})");
            surface.IsEditing = false;
            surface.SetView(new("abc", [new("one"), new("two")]));
            using (var bitmap = Render(surface)) { }
            window.CaptureRenderedFrame()?.Dispose();
            var selected = -1; surface.CandidateSelected += index => selected = index;
            var bounds = surface.Elements.Single(x => x.Id == "candidates").Bounds;
            window.MouseDown(new(bounds.X + 4, bounds.Y + 3), MouseButton.Left);
            window.MouseUp(new(bounds.X + 4, bounds.Y + 3), MouseButton.Left);
            Check(selected == 0, "candidate click reports index");
            window.Close();

            var workbench = new WorkbenchWindow(library);
            workbench.OpenTheme(imported.ThemeDirectory); workbench.Show(); workbench.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var saved = workbench.SaveCopy("跨平台布局副本");
            Check(ThemeFiles.Load(Path.Combine(saved, "theme.json")).Name == "跨平台布局副本", "workbench uses shared save transaction");
            Check(workbench.GetVisualDescendants().OfType<Expander>().Single().Header?.ToString()?.Contains("部分兼容") == true,
                "workbench keeps the original partial-compatibility report visible after saving");
            workbench.SavePreview(Path.Combine(evidence, "workbench-candidate.png"));
            using (var frame = workbench.CaptureRenderedFrame() ?? throw new Exception("No workbench frame"))
                frame.Save(Path.Combine(evidence, "workbench.png"), new PngBitmapEncoderOptions());
            Check(File.Exists(Path.Combine(evidence, "workbench.png")), "complete workbench renders with Skia");
            var fontButton = workbench.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "字号 ＋"));
            fontButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var activeBefore = library.ActiveDirectory;
            workbench.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "设为当前皮肤"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(library.ActiveDirectory == activeBefore, "unsaved layout cannot be applied as a different saved model");
            workbench.Close(); Dispatcher.UIThread.RunJobs();
            Check(workbench.IsVisible && workbench.OwnedWindows.Count() == 1, "closing a dirty workbench asks before discarding");
            var confirm = workbench.OwnedWindows.Single();
            confirm.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "继续编辑"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Check(workbench.IsVisible && !workbench.OwnedWindows.Any(), "cancelled close retains the workbench");
            workbench.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "放弃修改"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            workbench.Close();
            Check(!workbench.IsVisible, "reset allows a clean close");
            if (args.Contains("--rime"))
            {
                NativeLibrary.SetDllImportResolver(typeof(RimeEngine).Assembly, (name, _, _) =>
                    name == "typeflow_rime" ? NativeLibrary.Load(Path.Combine(repo, "typeflow/build/native-portable/Release/typeflow_rime.dll")) : 0);
                var engine = new RimeEngine(Path.Combine(repo, "typeflow/runtime/rime.dll"), Path.Combine(repo, "typeflow/rime-data"),
                    Path.Combine(temp, "rime-user"), "luna_pinyin_simp");
                var live = new WorkbenchWindow(library, engine); live.Show(); live.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var input = live.GetVisualDescendants().OfType<TextBox>().Single(x => x.PlaceholderText == "在这里输入拼音");
                input.Focus(); live.KeyTextInput("nihao");
                Check(engine.View.Candidates.Count > 0 && engine.View.Candidates[0].Text == "你好", "workbench routes text to real Rime");
                live.OpenTheme(saved);
                Check(engine.View.Preedit.Replace(" ", "") == "nihao", "theme switch preserves real composition");
                live.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                var output = live.GetVisualDescendants().OfType<TextBox>().Single(x => x.PlaceholderText == "选词结果");
                Check(output.Text == "你好", "space commits real candidate in Avalonia");
                live.Close();
            }
            File.WriteAllText(Path.Combine(evidence, args.Contains("--rime") ? "result-rime.json" : "result.json"), JsonSerializer.Serialize(new { passed, os = RuntimeInformation.OSDescription, backend = "Avalonia 12.1.5 / Skia headless", rime = args.Contains("--rime") }, ThemeFiles.Json));
            Console.WriteLine($"PASS: {passed} Avalonia checks (real pixels, DPI, slices, tiling, pointer events, shared editing and workbench composition).");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            // Only remove the generated test directory within this project's build folder.
            var prefix = Path.GetFullPath(Path.Combine(repo, "typeflow/build")) + Path.DirectorySeparatorChar;
            if (temp.StartsWith(prefix, ThemeFiles.FileSystemComparison) && Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }
    private static RenderTargetBitmap Render(Surface surface)
    {
        surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); surface.Arrange(new Rect(surface.DesiredSize));
        var result = new RenderTargetBitmap(new((int)surface.Bounds.Width, (int)surface.Bounds.Height), new(96, 96));
        result.Render(surface); return result;
    }
    private static string MakeImage(string path, int width, int height, Func<int, int, Color> color, Vector? dpi = null)
    {
        using var bitmap = new WriteableBitmap(new(width, height), dpi ?? new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var frame = bitmap.Lock())
        {
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var c = color(x, y); var address = frame.Address + y * frame.RowBytes + x * 4;
                Marshal.Copy(new byte[] { c.B, c.G, c.R, c.A }, 0, address, 4);
            }
        }
        bitmap.Save(path, new PngBitmapEncoderOptions()); return path;
    }
    private static Color Pixel(Bitmap bitmap, int x, int y)
    {
        using var converted = new WriteableBitmap(bitmap.PixelSize, new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var frame = converted.Lock(); bitmap.CopyPixels(frame);
        var bytes = new byte[4]; Marshal.Copy(frame.Address + y * frame.RowBytes + x * 4, bytes, 0, 4);
        return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
}
