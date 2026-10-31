using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace MiniLyrics.App;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MiniLyricsForSpotify";

    internal static string Command(string executable) => $"\"{executable}\" --background";

    public static bool TrySetEnabled(bool enabled, out string error)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled)
            {
                string executable = Environment.ProcessPath ?? throw new IOException("Lokasi aplikasi tidak tersedia.");
                if (!File.Exists(executable) || string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Jalankan EXE Mini Lyrics terlebih dahulu.");
                key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
            }
            else key.DeleteValue(ValueName, false);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            error = "Tidak dapat mengubah startup Windows. " + ex.Message;
            return false;
        }
    }
}

internal sealed class SpotifyProcessMonitor
{
    private long nextCheck;
    private bool running;

    public bool IsRunning()
    {
        long now = Environment.TickCount64;
        if (now < nextCheck) return running;
        nextCheck = now + 2000;
        try
        {
            var processes = Process.GetProcessesByName("Spotify");
            running = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Keep the previous state when Windows temporarily cannot enumerate processes.
        }
        return running;
    }
}
