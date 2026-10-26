using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MiniLyrics.App;

internal static class MousePassthrough
{
    private const int ExtendedStyle = -20;
    private const long Transparent = 0x00000020;
    private const long NoActivate = 0x08000000;

    public static bool IsEnabled(IntPtr window) => (GetWindowLongPtr(window, ExtendedStyle).ToInt64() & Transparent) != 0;

    public static void Apply(IntPtr window, bool enabled)
    {
        if (window == IntPtr.Zero) return;
        var existing = GetWindowLongPtr(window, ExtendedStyle).ToInt64();
        var updated = enabled ? existing | Transparent | NoActivate : existing & ~(Transparent | NoActivate);
        if (updated == existing) return;
        // WPF already owns WS_EX_LAYERED (AllowsTransparency); preserve it and all other style bits.
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtr(window, ExtendedStyle, new IntPtr(updated)) == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(System.Drawing.Point point);
}
