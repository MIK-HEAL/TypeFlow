using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using TypeFlow.Core;
using TypeFlow.Themes;
using TypeFlow.Rendering.Wpf;
using TypeFlow.Desktop;
using TypeFlow.Engine.Rime;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var repo = Path.GetFullPath(args[0]);
        var evidence = Path.Combine(repo, "typeflow/build/evidence"); Directory.CreateDirectory(evidence);
        var temp = Path.Combine(repo, "typeflow/build/integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        int passed = 0;
        void Check(bool v, string label) { if (!v) throw new Exception("FAIL: " + label); passed++; }
        try
        {
            var library = DesktopComponents.CreateThemeLibrary(Path.Combine(temp, "workspace"));
            var imported = library.Import(Path.Combine(repo, "test/ssf/尼古喵喵！.ssf")).GetAwaiter().GetResult();
            var surface = new CandidateSurface();
            using var engine = new RimeEngine(Path.Combine(repo, "typeflow/runtime/rime.dll"), Path.Combine(repo, "typeflow/rime-data"), Path.Combine(temp, "rime-user"), "luna_pinyin_simp");
            // Reject a second runtime without invalidating the first runtime's native pointers.
            try
            {
                using var second = new RimeEngine(Path.Combine(repo, "typeflow/runtime/rime.dll"), Path.Combine(repo, "typeflow/rime-data"), Path.Combine(temp, "rime-user-2"), "luna_pinyin_simp");
                throw new Exception("Second runtime accepted.");
            }
            catch (InvalidOperationException) { passed++; }
            foreach (var key in "nihao") Check(engine.ProcessKey(key).Handled, "Rime consumes pinyin");
            Check(engine.View.Preedit.Replace(" ", "") == "nihao" && engine.View.Candidates[0].Text == "你好", "real simplified pinyin");
            var before = JsonSerializer.Serialize(engine.View, ThemeFiles.Json);
            surface.SetView(engine.View);
            surface.SetTheme(imported.Theme, imported.ThemeDirectory);
            library.Activate(imported.ThemeDirectory);
            foreach (var scale in new[] { 1.0, 1.5, 2.0 })
            {
                var path = Path.Combine(evidence, $"sample-rime-{scale:0.0}.png");
                surface.SavePng(path, scale);
                using var f = File.OpenRead(path);
                var bitmap = BitmapDecoder.Create(f, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                Check(bitmap.PixelWidth >= 431 * scale && bitmap.PixelHeight >= 160 * scale, "DPI render " + scale);
            }
            surface.SetTheme(new Theme(), library.DefaultDirectory);
            Check(JsonSerializer.Serialize(engine.View, ThemeFiles.Json) == before, "skin switch preserves preedit and candidates");
            surface.SetTheme(imported.Theme, imported.ThemeDirectory);
            Check(engine.ProcessKey(0xff56).View.Page > 0, "PageDown");
            Check(engine.ProcessKey(0xff55).View.Page == 0, "PageUp");
            Check(engine.ProcessKey(32).Commit == "你好", "space commits first candidate");
            engine.ProcessKey('n'); engine.ProcessKey('i');
            Check(engine.ProcessKey(0xff08).View.Preedit == "n", "backspace edits composition");
            Check(engine.ProcessKey(0xff1b).View.Preedit.Length == 0, "escape cancels");
            foreach (var key in "nihao") engine.ProcessKey(key);
            var secondText = engine.View.Candidates[1].Text;
            Check(engine.ProcessKey('2').Commit == secondText, "numeric selection");
            foreach (var key in "nihao") engine.ProcessKey(key);
            var firstText = engine.View.Candidates[0].Text;
            Check(engine.SelectCandidate(0).Commit == firstText, "candidate click API after learning");
            engine.ProcessKey('n'); engine.Clear(); Check(engine.View.Preedit.Length == 0, "clear composition");
            passed += LayoutTests.Run(repo, temp, library, imported.ThemeDirectory, engine);
            passed += LinkedConfigTests.Run(repo, temp, library, engine);
            library.Delete(imported.ThemeDirectory);
            Check(library.ActiveDirectory == library.DefaultDirectory, "deleting active theme restores default");
            File.WriteAllText(Path.Combine(library.Root, "active.json"), "\"../escape\"");
            Check(library.ActiveDirectory == library.DefaultDirectory, "invalid active config falls back");
            using (var pipe = new NamedPipeServerStream("TypeFlow.Weasel.v1." + Process.GetCurrentProcess().SessionId,
                PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            using (var sender = Process.Start(new ProcessStartInfo(Path.Combine(repo, "typeflow/build/native/Release/typeflow_endpoint_smoke.exe"))
                { UseShellExecute = false, CreateNoWindow = true })!)
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
                using var reader = new StreamReader(pipe);
                CandidatePresentation? message = null;
                while (message?.Visible != true)
                {
                    var line = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                    if (line == null) throw new Exception("Endpoint disconnected without presentation data.");
                    message = JsonSerializer.Deserialize<CandidatePresentation>(line, ThemeFiles.Json);
                    pipe.WriteByte((byte)'1'); pipe.Flush();
                }
                File.WriteAllText(Path.Combine(evidence, "weasel-protocol.json"), JsonSerializer.Serialize(message, ThemeFiles.Json));
                Check(message.View!.Candidates[0].Text == "你好" && message.Left == 400 && message.Bottom == 426, "native Weasel adapter protocol round trip");
                sender.WaitForExit(5000);
                Check(sender.HasExited && sender.ExitCode == 0, "native sender exits cleanly");
            }
            Console.WriteLine($"PASS: {passed} integration checks with real Rime, theme switching, keyboard control, library recovery, WPF DPI rendering and native pipe transport.");
            File.WriteAllText(Path.Combine(evidence, "integration-result.json"), JsonSerializer.Serialize(new { passed, before = JsonSerializer.Deserialize<JsonElement>(before), commit = "你好" }, ThemeFiles.Json));
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            // The native library may briefly finish closing its log/database handles.
            for (var attempt = 0; attempt < 5 && Directory.Exists(temp); attempt++)
            {
                try { Directory.Delete(temp, true); }
                catch (IOException) when (attempt < 4) { Thread.Sleep(100); }
            }
        }
    }
}
