using System.Runtime.InteropServices;
using System.Windows;

namespace TypeFlow.Desktop;

internal static class CompanionActions
{
    public static void Execute(string action, Action settings)
    {
        if (action == "settings") { settings(); return; }
        if (action == "none") return;
        try
        {
            // The non-activating companion leaves these shortcuts with the editor.
            // Mode bindings are provided by the bundled Rime numbered_mode_switch.
            ushort[] keys = action switch {
                "voice" => [0x5B, 0x48], // Windows voice typing: Win+H
                "script" => [0x11, 0x10, 0x34],
                "language" => [0x11, 0x10, 0x32],
                _ => throw new ArgumentException("未知挂件操作。")
            };
            if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0))
                throw new InvalidOperationException("请松开 Ctrl、Shift、Alt 和 Win 键后再点击。 ");
            if (GetForegroundWindow() == 0) throw new InvalidOperationException("请先点击要输入文字的编辑框。");
            var inputs = keys.Select(k => Key(k, false)).Concat(keys.Reverse().Select(k => Key(k, true))).ToArray();
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
            if (sent != inputs.Length)
            {
                // Release only keys whose down events were accepted; don't leave modifiers stuck.
                var release = keys.Take((int)Math.Min(sent, (uint)keys.Length)).Reverse().Select(k => Key(k, true)).ToArray();
                if (release.Length != 0) SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
                throw new InvalidOperationException("未能发送切换快捷键。请在普通权限的编辑窗口中重试。");
            }
        }
        catch (Exception e) { MessageBox.Show(e.Message, "TypeFlow 挂件", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private static Input Key(ushort key, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = up ? 2u : 0u } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
