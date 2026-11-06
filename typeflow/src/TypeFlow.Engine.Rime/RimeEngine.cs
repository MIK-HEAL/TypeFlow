using System.Runtime.InteropServices;
using System.Text.Json;
using TypeFlow.Core;

namespace TypeFlow.Engine.Rime;

public sealed class RimeEngine : IInputEngine
{
    private nint handle;
    public string Id => "typeflow.rime";
    public CandidateView View { get; private set; } = new("", []);
    public RimeEngine(string dll, string shared, string user, string schema)
    {
        Directory.CreateDirectory(user);
        handle = Native.tf_open(Path.GetFullPath(dll), Path.GetFullPath(shared), Path.GetFullPath(user), schema);
        if (handle == 0) throw new InvalidOperationException(Error());
    }
    public KeyResult ProcessKey(int keycode, int modifiers = 0) { Check(); return Read(Native.tf_key(handle, keycode, modifiers)); }
    public KeyResult SelectCandidate(int index)
    {
        Check();
        if (index < 0 || index >= View.Candidates.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return Read(Native.tf_select(handle, index));
    }
    private KeyResult Read(nint p)
    {
        if (p == 0) throw new InvalidOperationException(Error());
        var result = JsonSerializer.Deserialize<KeyResult>(Marshal.PtrToStringUTF8(p)!, ThemeFiles.Json)!;
        View = result.View;
        return result;
    }
    public void Clear() { Check(); Native.tf_clear(handle); View = new("", []); }
    private void Check() => ObjectDisposedException.ThrowIf(handle == 0, this);
    private static string Error() => Marshal.PtrToStringUTF8(Native.tf_error()) ?? "Rime bridge error.";
    public void Dispose() { if (handle != 0) { Native.tf_close(handle); handle = 0; } }
    private static class Native
    {
        private const string Dll = "typeflow_rime";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint tf_open([MarshalAs(UnmanagedType.LPUTF8Str)] string dll,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string shared, [MarshalAs(UnmanagedType.LPUTF8Str)] string user,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string schema);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern nint tf_error();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern nint tf_key(nint handle, int key, int modifiers);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern nint tf_select(nint handle, int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void tf_clear(nint handle);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void tf_close(nint handle);
    }
}
