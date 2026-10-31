using System;
using System.Threading;
using System.Windows;

namespace MiniLyrics.App;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool smoke = e.Args.Length == 2 && e.Args[0] == "--smoke-test";
        bool background = Array.IndexOf(e.Args, "--background") >= 0;
        if (background && !UserSettings.Load().StartWithSpotify) { Shutdown(); return; }
        instance = new Mutex(true, "Local\\MiniLyricsForSpotify", out bool first);
        if (!first && !smoke)
        {
            if (!background) MessageBox.Show("Mini Lyrics sudah berjalan. Buka melalui ikon di system tray.", "Mini Lyrics");
            Shutdown();
            return;
        }
        var window = new MainWindow(smoke);
        MainWindow = window;
        window.StartFollowingSpotify();
        if (smoke) window.RunSmokeTest(e.Args[1]);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
