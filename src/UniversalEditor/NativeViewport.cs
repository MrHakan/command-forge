using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace UniversalEditor;

internal sealed record GameWindow(IntPtr Handle, int ProcessId, string Title);

internal sealed class NativeViewport : Panel
{
    private IntPtr gameHandle;
    private IntPtr oldParent;
    private IntPtr oldStyle;
    private NativeMethods.Rect oldBounds;
    public bool IsAttached => gameHandle != IntPtr.Zero && NativeMethods.IsWindow(gameHandle);
    public IntPtr GameHandle => gameHandle;
    public int GameProcessId { get; private set; }
    public NativeViewport() { BackColor = Color.FromArgb(17, 22, 31); Resize += (_, _) => ResizeGame(); }

    public void Attach(GameWindow window)
    {
        if (IsAttached) throw new InvalidOperationException("Detach the current game first.");
        if (!NativeMethods.IsWindow(window.Handle)) throw new IOException("The game window is no longer available.");
        NativeMethods.GetWindowThreadProcessId(window.Handle, out var actualPid);
        if (actualPid != window.ProcessId) throw new IOException("Game window identity changed.");
        gameHandle = window.Handle; GameProcessId = window.ProcessId;
        oldParent = NativeMethods.GetParent(gameHandle);
        oldStyle = NativeMethods.GetWindowLongPtrW(gameHandle, -16);
        NativeMethods.GetWindowRect(gameHandle, out oldBounds);
        try
        {
            var style = (oldStyle.ToInt64() | 0x40000000L) & ~(0x80000000L | 0x00C00000L | 0x00040000L | 0x00080000L | 0x00020000L | 0x00010000L);
            NativeMethods.SetWindowLongPtrW(gameHandle, -16, new IntPtr(style));
            Marshal.SetLastPInvokeError(0);
            var parent = NativeMethods.SetParent(gameHandle, Handle);
            var error = Marshal.GetLastPInvokeError();
            if (parent == IntPtr.Zero && error != 0) throw new Win32Exception(error, "Windows refused to embed this game window.");
            if (NativeMethods.GetParent(gameHandle) != Handle) throw new IOException("The game window was not parented to the viewport.");
            ResizeGame();
        }
        catch { Detach(); throw; }
    }

    public void ResizeGame()
    {
        if (IsAttached && !NativeMethods.SetWindowPos(gameHandle, IntPtr.Zero, 0, 0, Math.Max(1, Width), Math.Max(1, Height), 0x0020 | 0x0004 | 0x0010))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not resize the game viewport.");
    }
    public void FocusGame()
    {
        if (!IsAttached) return;
        var gameThread = NativeMethods.GetWindowThreadProcessId(gameHandle, out _);
        var thisThread = NativeMethods.GetCurrentThreadId();
        var linked = gameThread != thisThread && NativeMethods.AttachThreadInput(thisThread, gameThread, true);
        try { NativeMethods.SetForegroundWindow(FindForm()!.Handle); NativeMethods.SetFocus(gameHandle); }
        finally { if (linked) NativeMethods.AttachThreadInput(thisThread, gameThread, false); }
    }
    public void Detach()
    {
        if (gameHandle != IntPtr.Zero && NativeMethods.IsWindow(gameHandle))
        {
            NativeMethods.SetParent(gameHandle, oldParent);
            NativeMethods.SetWindowLongPtrW(gameHandle, -16, oldStyle);
            NativeMethods.SetWindowPos(gameHandle, IntPtr.Zero, oldBounds.Left, oldBounds.Top,
                oldBounds.Right - oldBounds.Left, oldBounds.Bottom - oldBounds.Top, 0x0020 | 0x0004 | 0x0010);
        }
        gameHandle = IntPtr.Zero; GameProcessId = 0;
    }
    protected override void Dispose(bool disposing) { if (disposing) Detach(); base.Dispose(disposing); }

    public static IReadOnlyList<GameWindow> FindGames()
    {
        var windows = new List<GameWindow>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle)) return true;
            var text = new StringBuilder(512); NativeMethods.GetWindowTextW(handle, text, text.Capacity);
            var title = text.ToString();
            if (!title.StartsWith("Minecraft", StringComparison.OrdinalIgnoreCase)) return true;
            NativeMethods.GetWindowThreadProcessId(handle, out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (process.ProcessName is "java" or "javaw") windows.Add(new GameWindow(handle, (int)pid, title));
            }
            catch (ArgumentException) { }
            catch (Win32Exception) { }
            return true;
        }, IntPtr.Zero);
        return windows;
    }
}

internal static class NativeMethods
{
    internal delegate bool EnumWindowCallback(IntPtr window, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(EnumWindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextW(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AttachThreadInput(uint source, uint target, bool attach);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
}
