using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniLyrics.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MiniLyrics.App;

public partial class MainWindow : Window
{
    private readonly UserSettings settings = UserSettings.Load();
    private readonly SpotifySessionReader reader = new();
    private readonly PlaybackClock clock = new();
    private readonly HttpClient http = new() { BaseAddress = new Uri("https://lrclib.net/"), Timeout = TimeSpan.FromSeconds(15) };
    private readonly LyricsController lyrics;
    private readonly DispatcherTimer mediaTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer lyricTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Forms.NotifyIcon tray;
    private readonly Drawing.Icon trayIcon;
    private readonly Forms.ToolStripMenuItem textOnlyMenu;
    private readonly Forms.ToolStripMenuItem ignoreMouseMenu;
    private IReadOnlyList<LyricLine> lines = [];
    private readonly List<TextBlock> lyricBlocks = [];
    private readonly List<TextBlock> textOnlyBlocks = [];
    private Track? currentTrack;
    private bool busy, ready, exiting, smokeTesting;
    private int activeIndex = -2;
    private double? heightBeforeSettings;
    private static readonly DependencyProperty AnimatedTextOffsetProperty = DependencyProperty.Register(
        "AnimatedTextOffset", typeof(double), typeof(MainWindow),
        new PropertyMetadata(0d, (owner, args) => ((MainWindow)owner).TextOnlyScroll.ScrollToVerticalOffset((double)args.NewValue)));

    public MainWindow()
    {
        InitializeComponent();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MiniLyricsForSpotify/1.0 (personal Windows desktop app)");
        lyrics = new LyricsController(new LrclibProvider(http, Path.Combine(UserSettings.DataDirectory, "cache")));
        lyrics.Changed += RenderLyrics;
        Left = settings.Left; Top = settings.Top; Width = settings.Width; Height = settings.Height;
        FontSlider.Value = settings.FontSize; OpacitySlider.Value = settings.Opacity;
        OffsetSlider.Value = settings.CorrectionSeconds; TopmostCheck.IsChecked = settings.AlwaysOnTop;
        ready = true;
        ApplySettings();
        trayIcon = CreateTrayIcon();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Mini Lyrics for Spotify", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Tampilkan / sembunyikan", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        textOnlyMenu = new Forms.ToolStripMenuItem("Mode teks saja") { CheckOnClick = true, Checked = settings.TextOnly };
        textOnlyMenu.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            settings.TextOnly = textOnlyMenu.Checked;
            ApplyDisplayMode();
            PersistSettings();
        });
        menu.Items.Add(textOnlyMenu);
        ignoreMouseMenu = new Forms.ToolStripMenuItem("Tembus klik (abaikan mouse)") { CheckOnClick = true, Checked = settings.IgnoreMouseClicks };
        ignoreMouseMenu.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            settings.IgnoreMouseClicks = ignoreMouseMenu.Checked;
            ApplyMouseMode();
            PersistSettings();
        });
        menu.Items.Add(ignoreMouseMenu);
        menu.Items.Add("Pengaturan", null, (_, _) => Dispatcher.Invoke(() =>
        {
            ShowPanel();
            if (SettingsPanel.Visibility != Visibility.Visible) ToggleSettings(this, new RoutedEventArgs());
        }));
        menu.Items.Add("Coba ambil lirik lagi", null, (_, _) => Dispatcher.Invoke(() => RetryLyrics(this, new RoutedEventArgs())));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Keluar", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowPanel);
        mediaTimer.Tick += async (_, _) => await PollMedia();
        lyricTimer.Tick += (_, _) => UpdateHighlight();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); PersistSettings(); };
        LocationChanged += (_, _) => ScheduleSave();
        SizeChanged += (_, _) => ScheduleSave();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        Closing += OnClosing;
        SourceInitialized += (_, _) => ApplyMouseMode();
        ApplyDisplayMode();
        mediaTimer.Start();
        lyricTimer.Start();
    }

    private async Task PollMedia()
    {
        if (busy || exiting || smokeTesting) return;
        busy = true;
        try
        {
            var snapshot = await reader.ReadAsync();
            if (exiting) return;
            if (snapshot is null)
            {
                if (currentTrack is not null)
                {
                    currentTrack = null;
                    clock.Reset();
                    await lyrics.LoadAsync(null);
                }
                TrackTitle.Text = "Putar lagu di Spotify";
                TrackArtist.Text = "Spotify desktop · komputer ini";
                PlaybackStatus.Text = "BELUM TERHUBUNG";
                Message.Text = "Menunggu Spotify…";
                if (lines.Count == 0) TextOnlyText.Text = Message.Text;
                return;
            }
            bool changed = currentTrack?.Key != snapshot.Track.Key;
            if (changed)
            {
                currentTrack = snapshot.Track;
                clock.Reset();
                TrackTitle.Text = currentTrack.Title;
                TrackTitle.ToolTip = currentTrack.Title;
                TrackArtist.Text = currentTrack.Artist;
                TrackArtist.ToolTip = $"{currentTrack.Artist}\n{currentTrack.Album}";
            }
            clock.Update(snapshot.Position, snapshot.UpdatedAt, snapshot.IsPlaying,
                TimeSpan.FromSeconds(snapshot.Track.DurationSeconds), snapshot.PlaybackRate, DateTimeOffset.UtcNow);
            PlaybackStatus.Text = snapshot.IsPlaying ? "● SEDANG DIPUTAR" : "Ⅱ DIJEDA";
            if (changed) _ = lyrics.LoadAsync(currentTrack);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            currentTrack = null;
            clock.Reset();
            await lyrics.LoadAsync(null);
            PlaybackStatus.Text = "SESI MEDIA TIDAK TERSEDIA";
            Message.Text = "Tidak dapat membaca sesi Spotify. Coba buka ulang Spotify.";
            TextOnlyText.Text = Message.Text;
        }
        finally { busy = false; }
    }

    private void RenderLyrics(LyricsState state)
    {
        if (exiting) return;
        lines = LrcParser.Parse(state.Document?.SyncedLyrics);
        activeIndex = -2;
        lyricBlocks.Clear();
        textOnlyBlocks.Clear();
        BeginAnimation(AnimatedTextOffsetProperty, null);
        TextOnlyPanel.Children.Clear();
        TextOnlyPanel.Margin = new Thickness(12, 0, 12, 0);
        LyricsPanel.Children.Clear();
        LyricsScroll.ScrollToTop();
        RetryButton.Visibility = state.Status is LyricsStatus.Error or LyricsStatus.NotFound ? Visibility.Visible : Visibility.Collapsed;
        Message.Visibility = state.Status == LyricsStatus.Ready ? Visibility.Collapsed : Visibility.Visible;
        Message.Text = state.Status switch
        {
            LyricsStatus.Loading => "Mencari lirik…",
            LyricsStatus.NotFound => "Lirik belum tersedia di LRCLIB.",
            LyricsStatus.Instrumental => "Instrumental · tanpa lirik",
            LyricsStatus.Error => "Lirik gagal dimuat. Periksa koneksi lalu coba lagi.",
            _ => "Menunggu Spotify…"
        };
        LyricsSource.Text = lines.Count > 0 ? "LRCLIB · SYNC" : "LRCLIB";
        TextOnlyText.Text = Message.Text;
        TextOnlyScroll.ScrollToTop();
        TextOnlyText.Visibility = state.Status == LyricsStatus.Ready ? Visibility.Collapsed : Visibility.Visible;
        TextOnlyScroll.Visibility = state.Status == LyricsStatus.Ready ? Visibility.Visible : Visibility.Collapsed;
        if (state.Status != LyricsStatus.Ready) return;
        if (lines.Count > 0)
        {
            foreach (var line in lines) AddLine(string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text, false);
        }
        else
        {
            var plain = state.Document?.PlainLyrics;
            if (string.IsNullOrWhiteSpace(plain)) plain = state.Document?.SyncedLyrics;
            foreach (var row in (plain ?? "").Split('\n')) AddLine(row.TrimEnd('\r'), true);
            LyricsSource.Text = "LRCLIB · TEKS";
            TextOnlyText.Text = plain ?? "";
        }
        UpdateHighlight();
        CenterTextOnlyLine(false);
    }

    private void AddLine(string text, bool plain)
    {
        var block = new TextBlock
        {
            Text = text, FontSize = settings.FontSize, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10), Foreground = new SolidColorBrush(plain ? Color.FromRgb(220, 231, 224) : Color.FromRgb(137, 157, 145)),
            FontWeight = FontWeights.Medium
        };
        lyricBlocks.Add(block);
        LyricsPanel.Children.Add(block);
        var overlayBlock = new TextBlock
        {
            Text = text, FontSize = settings.FontSize, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 14),
            Foreground = new SolidColorBrush(Color.FromRgb(255, 224, 102)),
            FontWeight = plain ? FontWeights.SemiBold : FontWeights.Medium, Opacity = plain ? 1 : 0.5,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 1 }
        };
        textOnlyBlocks.Add(overlayBlock);
        TextOnlyPanel.Children.Add(overlayBlock);
    }

    private void UpdateHighlight()
    {
        if (lines.Count == 0 || !IsVisible) return;
        var index = LrcParser.ActiveIndex(lines, clock.Position(DateTimeOffset.UtcNow), settings.CorrectionSeconds);
        if (index == activeIndex) return;
        var previousIndex = activeIndex;
        activeIndex = index;
        TextOnlyText.Text = index >= 0 && !string.IsNullOrWhiteSpace(lines[index].Text) ? lines[index].Text : "♪";
        for (int i = 0; i < lyricBlocks.Count; i++)
        {
            lyricBlocks[i].Foreground = new SolidColorBrush(i == index ? Color.FromRgb(103, 232, 160) : Color.FromRgb(137, 157, 145));
            lyricBlocks[i].FontWeight = i == index ? FontWeights.SemiBold : FontWeights.Medium;
            textOnlyBlocks[i].Opacity = i == index ? 1 : 0.5;
            textOnlyBlocks[i].FontWeight = i == index ? FontWeights.SemiBold : FontWeights.Medium;
        }
        if (index >= 0)
        {
            LyricsPanel.UpdateLayout();
            var offset = lyricBlocks[index].TranslatePoint(new Point(), LyricsPanel).Y;
            LyricsScroll.ScrollToVerticalOffset(Math.Max(0, offset - LyricsScroll.ViewportHeight * 0.3));
        }
        CenterTextOnlyLine(previousIndex >= -1 && Math.Abs(index - previousIndex) <= 1);
    }

    private void TextViewportChanged(object sender, SizeChangedEventArgs e)
    {
        if (ready) CenterTextOnlyLine(false);
    }

    private void CenterTextOnlyLine(bool animate)
    {
        if (TextOnlyView.Visibility != Visibility.Visible || lines.Count == 0 || textOnlyBlocks.Count == 0) return;
        TextOnlyScroll.UpdateLayout();
        var viewport = TextOnlyScroll.ViewportHeight;
        if (viewport <= 0) return;
        // Padding lets both the first and last lyric reach the center of the viewport.
        TextOnlyPanel.Margin = new Thickness(12, viewport / 2, 12, viewport / 2);
        TextOnlyPanel.UpdateLayout();
        var block = textOnlyBlocks[Math.Max(0, activeIndex)];
        var target = Math.Max(0, TextOnlyPanel.Margin.Top + block.TranslatePoint(new Point(), TextOnlyPanel).Y
            + block.ActualHeight / 2 - viewport / 2);
        var from = TextOnlyScroll.VerticalOffset;
        BeginAnimation(AnimatedTextOffsetProperty, null);
        SetValue(AnimatedTextOffsetProperty, animate ? from : target);
        if (animate)
            BeginAnimation(AnimatedTextOffsetProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(400))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void ApplySettings()
    {
        if (!ready) return;
        settings.FontSize = FontSlider.Value;
        settings.Opacity = OpacitySlider.Value;
        settings.CorrectionSeconds = OffsetSlider.Value;
        settings.AlwaysOnTop = TopmostCheck.IsChecked == true;
        Opacity = settings.Opacity;
        Topmost = settings.AlwaysOnTop;
        FontLabel.Text = $"Ukuran lirik · {settings.FontSize:0} px";
        OpacityLabel.Text = $"Opasitas · {settings.Opacity:P0}";
        OffsetLabel.Text = $"Koreksi waktu · {settings.CorrectionSeconds:+0.00;-0.00;0.00} dtk";
        foreach (var block in lyricBlocks) block.FontSize = settings.FontSize;
        foreach (var block in textOnlyBlocks) block.FontSize = settings.FontSize;
        TextOnlyText.FontSize = settings.FontSize;
        activeIndex = -2;
        UpdateHighlight();
        CenterTextOnlyLine(false);
    }

    private void ApplyMouseMode()
    {
        MousePassthrough.Apply(new WindowInteropHelper(this).Handle,
            settings.IgnoreMouseClicks && SettingsPanel.Visibility != Visibility.Visible);
    }

    private void ScheduleSave()
    {
        if (!ready || exiting || smokeTesting) return;
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void PersistSettings()
    {
        if (smokeTesting) return;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        settings.Left = bounds.Left; settings.Top = bounds.Top;
        settings.Width = bounds.Width; settings.Height = heightBeforeSettings ?? bounds.Height;
        SaveStatus.Text = settings.Save() ? "" : "Pengaturan belum tersimpan. Periksa izin folder data lokal.";
    }

    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { ApplySettings(); ScheduleSave(); }
    private void SettingChanged(object sender, RoutedEventArgs e) { ApplySettings(); ScheduleSave(); }
    private void ApplyDisplayMode()
    {
        bool textOnly = settings.TextOnly && SettingsPanel.Visibility != Visibility.Visible;
        PanelSurface.Visibility = textOnly ? Visibility.Collapsed : Visibility.Visible;
        TextOnlyView.Visibility = textOnly ? Visibility.Visible : Visibility.Collapsed;
        ResizeMode = textOnly ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        ApplyMouseMode();
        activeIndex = -2;
        UpdateHighlight();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => CenterTextOnlyLine(false)));
    }
    private void ToggleSettings(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Collapsed)
        {
            heightBeforeSettings = Height;
            MinHeight = 410;
            Height = Math.Max(Height, 410);
            SettingsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            MinHeight = 180;
            Height = heightBeforeSettings ?? Height;
            heightBeforeSettings = null;
            PersistSettings();
        }
        ApplyDisplayMode();
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        // Buttons keep their own clicks instead of starting a window drag.
        if (e.OriginalSource is DependencyObject source)
        {
            for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
                if (element is Button) return;
        }
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void HidePanel(object sender, RoutedEventArgs e) { PersistSettings(); Hide(); }
    private void ShowPanel()
    {
        Show(); WindowState = WindowState.Normal;
        if (!settings.IgnoreMouseClicks || SettingsPanel.Visibility == Visibility.Visible) Activate();
        activeIndex = -2; UpdateHighlight();
    }
    private void ToggleVisibility() { if (IsVisible) { PersistSettings(); Hide(); } else ShowPanel(); }
    private async void RetryLyrics(object sender, RoutedEventArgs e)
    {
        if (currentTrack is not null) await lyrics.LoadAsync(currentTrack);
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exiting) { e.Cancel = true; PersistSettings(); Hide(); }
    }
    private void ExitApplication()
    {
        if (exiting) return;
        PersistSettings();
        exiting = true;
        mediaTimer.Stop(); lyricTimer.Stop(); saveTimer.Stop();
        lyrics.Dispose();
        http.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose(); trayIcon.Dispose();
        Application.Current.Shutdown();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
    private static Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Drawing.Color.Transparent);
        using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(24, 40, 30));
        using var pen = new Drawing.Pen(Drawing.Color.FromArgb(103, 232, 160), 3);
        graphics.FillEllipse(background, 1, 1, 30, 30);
        graphics.DrawLine(pen, 8, 10, 24, 10); graphics.DrawLine(pen, 8, 16, 22, 16); graphics.DrawLine(pen, 8, 22, 18, 22);
        var handle = bitmap.GetHicon();
        try { using var icon = Drawing.Icon.FromHandle(handle); return (Drawing.Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }

    // Deterministic UI verification; uses original sample text, never fetches lyrics or saves user settings.
    public async void RunSmokeTest(string outputDirectory)
    {
        smokeTesting = true;
        mediaTimer.Stop(); lyricTimer.Stop(); saveTimer.Stop();
        var results = new Dictionary<string, object>();
        try
        {
            Directory.CreateDirectory(outputDirectory);
            textOnlyMenu.Checked = false;
            ignoreMouseMenu.Checked = false;
            Width = 420; Height = 220; Left = 80; Top = 80;
            FontSlider.Value = 20; OpacitySlider.Value = 0.95; TopmostCheck.IsChecked = true;
            await Task.Delay(300);
            results["windowSize"] = Width == 420 && Height == 220;
            results["topmost"] = Topmost;
            results["trayVisible"] = tray.Visible;
            var nativeWindow = new WindowInteropHelper(this).Handle;
            var screenPoint = PointToScreen(new Point(180, 20));
            var testPoint = new Drawing.Point((int)screenPoint.X, (int)screenPoint.Y);
            results["receivesMouseWhenUnlocked"] = MousePassthrough.WindowFromPoint(testPoint) == nativeWindow;
            ignoreMouseMenu.Checked = true;
            results["nativeClickThroughEnabled"] = MousePassthrough.IsEnabled(nativeWindow);
            results["mouseTargetPassesToBackground"] = MousePassthrough.WindowFromPoint(testPoint) != nativeWindow;
            ignoreMouseMenu.Checked = false;
            results["clickThroughCanBeDisabledFromTray"] = !MousePassthrough.IsEnabled(nativeWindow)
                && MousePassthrough.WindowFromPoint(testPoint) == nativeWindow;
            // Hit-test empty space, where a null Background previously swallowed drag attempts.
            var headerPoint = DragHeaderArea.TranslatePoint(new Point(DragHeaderArea.ActualWidth / 2, DragHeaderArea.ActualHeight / 2), this);
            results["emptyHeaderReceivesDrag"] = ReferenceEquals(InputHitTest(headerPoint), DragHeaderArea);
            var titlePoint = TrackHeaderArea.TranslatePoint(new Point(TrackHeaderArea.ActualWidth - 4, TrackHeaderArea.ActualHeight - 1), this);
            results["trackHeaderReceivesDrag"] = InputHitTest(titlePoint) is not null && TrackHeaderArea.Background is not null;
            Capture(Path.Combine(outputDirectory, "waiting.png"));
            Close();
            results["closeHidesToTray"] = !IsVisible && tray.Visible;
            ShowPanel();
            results["restoreFromTray"] = IsVisible && WindowState == WindowState.Normal;
            TopmostCheck.IsChecked = false;
            results["topmostToggle"] = !Topmost;
            TopmostCheck.IsChecked = true;
            FontSlider.Value = 24; OpacitySlider.Value = 0.75; OffsetSlider.Value = 1.25;
            results["settingsControls"] = settings.FontSize == 24 && Math.Abs(Opacity - 0.75) < 0.001 && settings.CorrectionSeconds == 1.25;
            FontSlider.Value = 20; OpacitySlider.Value = 1; OffsetSlider.Value = 0;
            TrackTitle.Text = "A small moment"; TrackArtist.Text = "Mini Lyrics · original test text";
            PlaybackStatus.Text = "● DEMO PEMERIKSAAN UI";
            var sample = new Track("A small moment", "Mini Lyrics", "", 180);
            clock.Reset();
            clock.Update(TimeSpan.FromSeconds(7), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00.00]A quiet window by the light\n[00:05.00]A little room for every line\n[00:10.00]The music carries on", false)));
            await Task.Delay(100);
            results["highlight"] = activeIndex == 1;
            Capture(Path.Combine(outputDirectory, "lyrics.png"));
            textOnlyMenu.Checked = true;
            results["textOnlyLayout"] = PanelSurface.Visibility == Visibility.Collapsed && TextOnlyView.Visibility == Visibility.Visible
                && ResizeMode == ResizeMode.NoResize && AllowsTransparency;
            results["textOnlyActiveLine"] = textOnlyBlocks[1].Text == "A little room for every line" && textOnlyBlocks[1].Opacity == 1;
            results["textOnlyYellowShadow"] = textOnlyBlocks[1].Foreground is SolidColorBrush textBrush && textBrush.Color == Color.FromRgb(255, 224, 102)
                && textOnlyBlocks[1].Effect is DropShadowEffect;
            CenterTextOnlyLine(false);
            await Task.Delay(100);
            var initialOffset = TextOnlyScroll.VerticalOffset;
            var activeCenter = textOnlyBlocks[1].TranslatePoint(new Point(0, textOnlyBlocks[1].ActualHeight / 2), TextOnlyScroll).Y;
            results["activeLineCentered"] = Math.Abs(activeCenter - TextOnlyScroll.ActualHeight / 2) < 2;
            results["topBottomFade"] = TextOnlyView.OpacityMask is LinearGradientBrush fade
                && fade.GradientStops[0].Color.A == 0 && fade.GradientStops[^1].Color.A == 0;
            clock.Update(TimeSpan.FromSeconds(12), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight();
            await Task.Delay(500);
            results["autoScrollAdvances"] = activeIndex == 2 && TextOnlyScroll.VerticalOffset > initialOffset + 5;
            clock.Update(TimeSpan.Zero, DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight();
            await Task.Delay(100);
            results["backwardSeekRecenters"] = activeIndex == 0 && TextOnlyScroll.VerticalOffset < initialOffset;
            clock.Update(TimeSpan.FromSeconds(7), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight();
            await Task.Delay(500);
            Capture(Path.Combine(outputDirectory, "text-only.png"));
            var transparentBitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            transparentBitmap.Render(this);
            var corner = new byte[4];
            transparentBitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
            results["textOnlyTransparentBackground"] = corner[3] == 0;
            clock.Update(TimeSpan.FromSeconds(32), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null,
                "[00:00]A quiet window by the light\n[00:10]The city settles into night\n[00:20]A little room for every line\n[00:30]The music carries on\n[00:40]Another moment passing by\n[00:50]A gentle echo in the sky\n[01:00]And every word moves on", false)));
            await Task.Delay(100);
            Capture(Path.Combine(outputDirectory, "auto-scroll-fade.png"));
            textOnlyMenu.Checked = false;
            results["restoreNormalPanel"] = PanelSurface.Visibility == Visibility.Visible && TextOnlyView.Visibility == Visibility.Collapsed;
            ignoreMouseMenu.Checked = true;
            ToggleSettings(this, new RoutedEventArgs());
            results["settingsTemporarilyReceiveMouse"] = !MousePassthrough.IsEnabled(nativeWindow) && settings.IgnoreMouseClicks;
            Capture(Path.Combine(outputDirectory, "settings.png"));
            ToggleSettings(this, new RoutedEventArgs());
            results["settingsRestoreClickThrough"] = MousePassthrough.IsEnabled(nativeWindow);
            results["settingsRestoresCompactHeight"] = Height == 220;
            var settingsPath = Path.Combine(outputDirectory, "test-settings.json");
            var saved = new UserSettings { FontSize = 28, Opacity = 0.65, AlwaysOnTop = false, TextOnly = true, CorrectionSeconds = -2.5, Width = 480, Height = 260 };
            results["settingsSaved"] = saved.Save(settingsPath);
            var reloaded = UserSettings.Load(settingsPath);
            results["settingsRoundTrip"] = reloaded.FontSize == 28 && reloaded.Opacity == 0.65 && !reloaded.AlwaysOnTop
                && reloaded.TextOnly && reloaded.IgnoreMouseClicks && reloaded.CorrectionSeconds == -2.5 && reloaded.Width == 480 && reloaded.Height == 260;
            File.WriteAllText(settingsPath, "invalid json");
            results["settingsCorruptionRecovery"] = UserSettings.Load(settingsPath).FontSize == 20;
            File.WriteAllText(settingsPath, "{\"TextOnly\":false,\"FontSize\":26}");
            var upgraded = UserSettings.Load(settingsPath);
            results["oldSettingsStartWithoutPanel"] = upgraded.TextOnly && upgraded.TextOnlyDefaultApplied && upgraded.FontSize == 26;
            upgraded.TextOnly = false;
            upgraded.IgnoreMouseClicks = false;
            upgraded.Save(settingsPath);
            results["explicitPanelChoiceRemembered"] = !UserSettings.Load(settingsPath).TextOnly;
            results["explicitMouseChoiceRemembered"] = !UserSettings.Load(settingsPath).IgnoreMouseClicks;
            File.Delete(settingsPath);
            results["spotifyOnlyFilter"] = SpotifySessionReader.IsSpotify("Spotify.exe")
                && SpotifySessionReader.IsSpotify("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")
                && !SpotifySessionReader.IsSpotify("chrome.exe") && !SpotifySessionReader.IsSpotify("OtherSpotifyApp.exe");
            results["passed"] = results.Values.OfType<bool>().All(value => value);
            var playback = await reader.ReadAsync();
            results["spotifyDetected"] = playback is not null;
            results["spotifyIntegration"] = playback is null ? "UNVERIFIED: Spotify desktop is not publishing a music session" : "Media session detected; real lyric synchronization still requires manual playback validation";
        }
        catch (Exception ex) { results["error"] = ex.ToString(); results["passed"] = false; }
        finally
        {
            try { File.WriteAllText(Path.Combine(outputDirectory, "smoke-test.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); }
            finally { ExitApplication(); }
        }
    }

    private void Capture(string path)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
