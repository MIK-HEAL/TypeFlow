using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using TypeFlow.Core;
using TypeFlow.Themes;

namespace TypeFlow.Desktop;

// One optional renderer/settings process per user/session. Input and commits
// remain in the native frontend; shutting this process down restores its UI.
public static class InstalledSkinApp
{
    private sealed record Request(string Command, string? Source = null);
    private sealed record SkinMode(bool Enabled = false);
    public static string Workspace => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypeFlow", "Skins");
    private static string ControlPipe => "TypeFlow.Settings.v1." + Process.GetCurrentProcess().SessionId;

    public static int Run(string command, string? source = null)
    {
        var name = @"Local\TypeFlow.SkinHost." + Process.GetCurrentProcess().SessionId + "." + WindowsIdentity.GetCurrent().User!.Value;
        using var mutex = new Mutex(true, name, out var owner);
        if (!owner)
        {
            var response = Send(new(command, source)).GetAwaiter().GetResult();
            Console.WriteLine(response);
            return 0;
        }
        try
        {
            if (command is "stop" or "status") { Console.WriteLine("{\"running\":false}"); return 0; }
            var library = DesktopComponents.CreateThemeLibrary(Workspace);
            foreach (var bundled in new[] { "paper", "night" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, "themes", bundled);
                var destination = Path.Combine(library.Themes, "bundled-" + bundled);
                if (Directory.Exists(path) && !Directory.Exists(destination))
                    new NativeThemeImporter().ImportAsync(path, destination).GetAwaiter().GetResult();
            }
            var modeFile = Path.Combine(Workspace, "system-skin.json");
            bool Enabled()
            {
                try { return JsonSerializer.Deserialize<SkinMode>(File.ReadAllText(modeFile), ThemeFiles.Json)?.Enabled == true; }
                catch (Exception e) when (e is IOException or JsonException or ArgumentException) { return false; }
            }
            void SetEnabled(bool enabled)
            {
                var temporary = modeFile + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(new SkinMode(enabled), ThemeFiles.Json));
                File.Move(temporary, modeFile, true);
                if (enabled) ActivateTypeFlow();
            }
            async void ActivateTypeFlow()
            {
                var launcher = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "TypeFlowImeLauncher.exe"));
                if (!File.Exists(launcher)) return;
                try
                {
                    using var process = Process.Start(new ProcessStartInfo(launcher, "--activate-only") { UseShellExecute = false, CreateNoWindow = true });
                    if (process == null) throw new IOException("未能切换输入法。");
                    await process.WaitForExitAsync();
                    if (process.ExitCode != 0) throw new IOException("皮肤已保存，但输入法切换未完成，请使用 Win + 空格选择 TypeFlow。");
                }
                catch (Exception e) { MessageBox.Show(e.Message, "TypeFlow", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var host = new WeaselHost(library, Enabled);
            MainWindow? settings = null;
            using var shutdown = new CancellationTokenSource();
            void ShowSettings()
            {
                if (settings == null)
                {
                    settings = new MainWindow(library, AppContext.BaseDirectory, SetEnabled, Enabled);
                    settings.Closed += (_, _) => settings = null;
                }
                settings.Show();
                if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
                ShowWindow(new WindowInteropHelper(settings).EnsureHandle(), 5);
                settings.Activate();
            }
            async Task<string> Handle(Request request)
            {
                switch (request.Command)
                {
                    case "settings": ShowSettings(); break;
                    case "apply":
                        if (string.IsNullOrWhiteSpace(request.Source)) throw new ArgumentException("缺少皮肤来源。");
                        string entry;
                        var full = Path.GetFullPath(request.Source);
                        if (Directory.Exists(full) && Directory.GetParent(full)?.FullName.Equals(library.Themes, ThemeFiles.FileSystemComparison) == true)
                            entry = full;
                        else entry = (await library.Import(full)).ThemeDirectory;
                        library.Validator.Validate(ThemeFiles.Load(Path.Combine(entry, "theme.json")), entry);
                        library.Activate(entry); SetEnabled(true);
                        break;
                    case "native": SetEnabled(false); host.Hide(); break;
                    case "background": case "status": break;
                    case "stop":
                        break;
                    default: throw new ArgumentException("不支持的设置命令。");
                }
                return JsonSerializer.Serialize(new {
                    running = true, pid = Environment.ProcessId, customThemeEnabled = Enabled(),
                    selectedTheme = ThemeFiles.Load(Path.Combine(library.ActiveDirectory, "theme.json")).Name,
                    selectedDirectory = library.ActiveDirectory, presentations = host.PresentationCount,
                    tsfPresentations = host.NativeTsfPresentationCount, sourcePid = host.LastSourcePid,
                    presentedTheme = host.LastPresentedTheme, visible = host.NativeVisible,
                    rendererActivated = host.IsActive,
                    settingsVisible = settings != null && IsWindowVisible(new WindowInteropHelper(settings).Handle)
                }, ThemeFiles.Json);
            }
            async Task ControlLoop()
            {
                while (!shutdown.IsCancellationRequested)
                {
                    try
                    {
                        await using var pipe = new NamedPipeServerStream(ControlPipe, PipeDirection.InOut, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        await pipe.WaitForConnectionAsync(shutdown.Token);
                        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                        var line = await reader.ReadLineAsync(shutdown.Token);
                        if (line == null || line.Length > 8192) continue;
                        string response;
                        Request? request = null;
                        try { request = JsonSerializer.Deserialize<Request>(line, ThemeFiles.Json) ?? throw new InvalidDataException(); response = await Handle(request); }
                        catch (Exception e) { response = JsonSerializer.Serialize(new { error = e.Message }); }
                        await writer.WriteLineAsync(response.Replace("\r", "").Replace("\n", ""));
                        if (request?.Command == "stop") { shutdown.Cancel(); _ = app.Dispatcher.BeginInvoke(() => app.Shutdown()); break; }
                    }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { break; }
                    catch (IOException) { }
                }
            }
            app.Startup += async (_, _) =>
            {
                host.Start();
                _ = ControlLoop();
                try { _ = await Handle(new(command, source)); }
                catch (Exception e) { MessageBox.Show(e.Message, "TypeFlow 皮肤", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            app.Exit += (_, _) => { shutdown.Cancel(); host.Close(); };
            return app.Run();
        }
        finally { mutex.ReleaseMutex(); }
    }
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);

    private static async Task<string> Send(Request request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var pipe = new NamedPipeClientStream(".", ControlPipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000, timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        var response = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("设置进程没有响应。");
        using var result = JsonDocument.Parse(response);
        if (result.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
        return response;
    }
}
