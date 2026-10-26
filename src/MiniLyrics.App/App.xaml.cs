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
        instance = new Mutex(true, "Local\\MiniLyricsForSpotify", out bool first);
        if (!first && !smoke)
        {
            MessageBox.Show("Mini Lyrics sudah berjalan. Buka melalui ikon di system tray.", "Mini Lyrics");
            Shutdown();
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (smoke) window.RunSmokeTest(e.Args[1]);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
