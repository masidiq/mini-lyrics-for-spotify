using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
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
    private readonly SpotifyProcessMonitor spotifyProcess = new();
    private readonly SpotifyVisibility spotifyVisibility = new();
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
    private readonly Forms.ToolStripMenuItem topmostMenu;
    private readonly Forms.ToolStripMenuItem startupMenu;
    private readonly Forms.ToolStripMenuItem fontMenu, opacityMenu, offsetMenu;
    private readonly Forms.NumericUpDown trayFont, trayOpacity, trayOffset;
    private readonly Forms.ToolStripMenuItem textModeMenu, languageMenu;
    private readonly Forms.ToolStripMenuItem[] textModeOptions, languageOptions;
    private readonly Forms.ToolStripMenuItem scrollModeMenu;
    private readonly Forms.ToolStripMenuItem[] scrollModeOptions;
    private readonly LyricsRomanizer romanizer = new();
    private LyricsState displayedState = new(null, LyricsStatus.Waiting);
    private CancellationTokenSource? romanizationCancellation;
    private long renderVersion;
    private Task romanizationTask = Task.CompletedTask;
    private bool syncingSettings;
    private IReadOnlyList<LyricLine> lines = [];
    private readonly List<TextBlock> lyricBlocks = [];
    private readonly List<TextBlock> textOnlyBlocks = [];
    private readonly List<string> originalLyricTexts = [];
    private Track? currentTrack;
    private bool busy, ready, exiting, smokeTesting;
    private bool fontSizeAnimating;
    private long fontSizeAnimationUntil;
    private int activeIndex = -2;
    private int displayIndex = -1;
    private double? heightBeforeSettings;
    private static readonly DependencyProperty AnimatedTextOffsetProperty = DependencyProperty.Register(
        "AnimatedTextOffset", typeof(double), typeof(MainWindow),
        new PropertyMetadata(0d, (owner, args) => ((MainWindow)owner).TextOnlyScroll.ScrollToVerticalOffset((double)args.NewValue)));

    public MainWindow(bool smokeTest = false)
    {
        smokeTesting = smokeTest;
        InitializeComponent();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MiniLyricsForSpotify/1.0 (personal Windows desktop app)");
        lyrics = new LyricsController(new LrclibProvider(http, Path.Combine(UserSettings.DataDirectory, "cache")));
        lyrics.Changed += RenderLyrics;
        Left = settings.Left; Top = settings.Top; Width = settings.Width; Height = settings.Height;
        FontSlider.Value = settings.FontSize; OpacitySlider.Value = settings.Opacity;
        OffsetSlider.Value = settings.CorrectionSeconds; TopmostCheck.IsChecked = settings.AlwaysOnTop;
        StartWithSpotifyCheck.IsChecked = settings.StartWithSpotify;
        TextOnlyCheck.IsChecked = settings.TextOnly; IgnoreMouseCheck.IsChecked = settings.IgnoreMouseClicks;
        TextModeCombo.SelectedIndex = (int)settings.TextMode;
        LanguageCombo.SelectedIndex = (int)settings.RomanizationLanguage;
        ScrollModeCombo.SelectedIndex = (int)settings.ScrollMode;
        trayIcon = CreateTrayIcon();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Mini Lyrics for Spotify", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem($"Mini Lyrics for Spotify · v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}") { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Tampilkan / sembunyikan", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        textOnlyMenu = new Forms.ToolStripMenuItem("Mode teks saja") { CheckOnClick = true, Checked = settings.TextOnly };
        textOnlyMenu.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (!syncingSettings) TextOnlyCheck.IsChecked = textOnlyMenu.Checked;
        });
        menu.Items.Add(textOnlyMenu);
        ignoreMouseMenu = new Forms.ToolStripMenuItem("Tembus klik (abaikan mouse)") { CheckOnClick = true, Checked = settings.IgnoreMouseClicks };
        ignoreMouseMenu.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (!syncingSettings) IgnoreMouseCheck.IsChecked = ignoreMouseMenu.Checked;
        });
        menu.Items.Add(ignoreMouseMenu);
        topmostMenu = new Forms.ToolStripMenuItem("Selalu di atas aplikasi lain") { CheckOnClick = true };
        topmostMenu.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (!syncingSettings) TopmostCheck.IsChecked = topmostMenu.Checked;
        });
        menu.Items.Add(topmostMenu);
        startupMenu = new Forms.ToolStripMenuItem("Mulai otomatis bersama Spotify") { CheckOnClick = true };
        startupMenu.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (!syncingSettings) StartWithSpotifyCheck.IsChecked = startupMenu.Checked;
        });
        menu.Items.Add(startupMenu);
        (fontMenu, trayFont) = CreateTrayNumber(14, 36, 0, 1, value => FontSlider.Value = (double)value);
        (opacityMenu, trayOpacity) = CreateTrayNumber(35, 100, 0, 5, value => OpacitySlider.Value = (double)value / 100);
        (offsetMenu, trayOffset) = CreateTrayNumber(-10, 10, 2, 0.25m, value => OffsetSlider.Value = (double)value);
        menu.Items.Add(fontMenu); menu.Items.Add(opacityMenu); menu.Items.Add(offsetMenu);
        (textModeMenu, textModeOptions) = CreateTrayChoice(["Asli", "Latin", "Latin + Asli"], value => TextModeCombo.SelectedIndex = value);
        (languageMenu, languageOptions) = CreateTrayChoice(["Otomatis (Korea / Jepang)", "Korea", "Jepang"], value => LanguageCombo.SelectedIndex = value);
        menu.Items.Add(textModeMenu); menu.Items.Add(languageMenu);
        (scrollModeMenu, scrollModeOptions) = CreateTrayChoice(["Per baris", "Smooth kontinu (sesuai timing)"], value => ScrollModeCombo.SelectedIndex = value);
        menu.Items.Add(scrollModeMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
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
        CompositionTarget.Rendering += OnAnimationFrame;
        ready = true;
        ApplySettings();
        ApplyDisplayMode();
        mediaTimer.Start();
        lyricTimer.Start();
        if (!smokeTesting && settings.StartWithSpotify && !StartupRegistration.TrySetEnabled(true, out var error))
            SaveStatus.Text = error;
    }

    public void StartFollowingSpotify()
    {
        bool? visible = spotifyVisibility.Update(!smokeTesting && settings.StartWithSpotify, spotifyProcess.IsRunning(), false);
        if (visible != false) Show();
    }

    private void UpdateSpotifyVisibility(bool running)
    {
        bool? visible = spotifyVisibility.Update(settings.StartWithSpotify, running, SettingsPanel.Visibility == Visibility.Visible);
        if (visible == true) ShowPanel();
        else if (visible == false) { PersistSettings(); Hide(); }
    }

    private void StartupSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || syncingSettings) return;
        bool enabled = StartWithSpotifyCheck.IsChecked == true;
        bool previous = settings.StartWithSpotify;
        string error = "";
        bool registered = smokeTesting || StartupRegistration.TrySetEnabled(enabled, out error);
        if (registered)
        {
            settings.StartWithSpotify = enabled;
            if (!smokeTesting && !settings.Save())
            {
                settings.StartWithSpotify = previous;
                StartupRegistration.TrySetEnabled(previous, out _);
                error = "Pengaturan belum tersimpan. Periksa izin folder data lokal.";
            }
        }
        if (!registered || settings.StartWithSpotify != enabled)
        {
            syncingSettings = true;
            try { StartWithSpotifyCheck.IsChecked = previous; }
            finally { syncingSettings = false; }
            MessageBox.Show(error, "Mini Lyrics", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        SyncTraySettings();
        if (!settings.StartWithSpotify) spotifyVisibility.Update(false, false, false);
        if (!smokeTesting) UpdateSpotifyVisibility(spotifyProcess.IsRunning());
    }

    private async Task PollMedia()
    {
        if (busy || exiting || smokeTesting) return;
        busy = true;
        try
        {
            UpdateSpotifyVisibility(spotifyProcess.IsRunning());
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
        displayedState = state;
        var version = ++renderVersion;
        romanizationCancellation?.Cancel();
        lines = LrcParser.Parse(state.Document?.SyncedLyrics);
        activeIndex = -2;
        displayIndex = -1;
        lyricBlocks.Clear();
        textOnlyBlocks.Clear();
        originalLyricTexts.Clear();
        BeginAnimation(AnimatedTextOffsetProperty, null);
        TextOnlyPanel.Children.Clear();
        LyricsTopSpacer.Height = LyricsBottomSpacer.Height = 0;
        LyricsPanel.Children.Clear();
        LyricsScroll.ScrollToTop();
        RetryButton.Visibility = state.Status is LyricsStatus.Error or LyricsStatus.NotFound or LyricsStatus.Retrying ? Visibility.Visible : Visibility.Collapsed;
        Message.Visibility = state.Status == LyricsStatus.Ready ? Visibility.Collapsed : Visibility.Visible;
        Message.Text = state.Status switch
        {
            LyricsStatus.Loading => "Mencari lirik…",
            LyricsStatus.Retrying => $"Koneksi terganggu. Mencoba lagi otomatis setelah {state.RetryAfterSeconds} detik…",
            LyricsStatus.NotFound => "Lirik belum tersedia di LRCLIB.",
            LyricsStatus.Instrumental => "Instrumental · tanpa lirik",
            LyricsStatus.Error => "Lirik gagal dimuat. Periksa koneksi lalu coba lagi.",
            _ => "Menunggu Spotify…"
        };
        LyricsSource.Text = lines.Count > 0 ? "LRCLIB · SYNC" : "LRCLIB";
        LyricsSource.ToolTip = null;
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
            LyricsSource.Text = "LRCLIB · TEKS · PREVIEW";
            LyricsSource.ToolTip = "Tanpa timestamp: baris pertama ditandai sebagai preview, bukan penanda waktu nyanyian.";
            TextOnlyText.Text = plain ?? "";
        }
        UpdateHighlight();
        CenterTextOnlyLine(false);
        if (settings.TextMode != LyricTextMode.Original)
            romanizationTask = RomanizeVisibleLinesAsync(lyricBlocks.Select(block => block.Text).ToArray(), version);
    }

    private async Task RomanizeVisibleLinesAsync(IReadOnlyList<string> originals, long version)
    {
        using var cancellation = new CancellationTokenSource();
        romanizationCancellation = cancellation;
        try
        {
            var converted = await romanizer.ConvertLinesAsync(originals, settings.TextMode, settings.RomanizationLanguage, cancellation.Token);
            if (exiting || cancellation.IsCancellationRequested || version != renderVersion) return;
            for (int i = 0; i < converted.Count; i++)
            {
                lyricBlocks[i].Text = textOnlyBlocks[i].Text = converted[i];
                lyricBlocks[i].ToolTip = textOnlyBlocks[i].ToolTip = originals[i];
            }
            activeIndex = -2;
            UpdateHighlight();
            CenterTextOnlyLine(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!exiting && version == renderVersion)
            {
                LyricsSource.Text += " · ASLI";
                LyricsSource.ToolTip = "Romanisasi belum tersedia; teks asli tetap ditampilkan.";
            }
        }
        finally { if (ReferenceEquals(romanizationCancellation, cancellation)) romanizationCancellation = null; }
    }

    private void AddLine(string text, bool plain)
    {
        originalLyricTexts.Add(text);
        var block = new TextBlock
        {
            Text = text, FontSize = settings.FontSize, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 0, 8, 10), Foreground = new SolidColorBrush(plain ? Color.FromRgb(220, 231, 224) : Color.FromRgb(137, 157, 145)),
            FontWeight = FontWeights.Medium
        };
        LyricHighlight.Initialize(block, false);
        lyricBlocks.Add(block);
        LyricsPanel.Children.Add(block);
        var overlayBlock = new TextBlock
        {
            Text = text, FontSize = settings.FontSize, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 0, 8, 14),
            Foreground = new SolidColorBrush(Color.FromRgb(255, 224, 102)),
            FontWeight = plain ? FontWeights.SemiBold : FontWeights.Medium, Opacity = plain ? 1 : 0.5,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 1 }
        };
        LyricHighlight.Initialize(overlayBlock, true);
        textOnlyBlocks.Add(overlayBlock);
        TextOnlyPanel.Children.Add(overlayBlock);
    }

    private void UpdateHighlight()
    {
        if (lyricBlocks.Count == 0) return;
        var index = lines.Count > 0 ? LrcParser.ActiveIndex(lines, clock.Position(DateTimeOffset.UtcNow), settings.CorrectionSeconds) : -1;
        var nextDisplayIndex = lines.Count > 0 ? LrcParser.DisplayIndex(lines, index)
            : Math.Max(0, originalLyricTexts.FindIndex(LrcParser.IsSungLine));
        if (index == activeIndex && nextDisplayIndex == displayIndex) return;
        var previousDisplayIndex = displayIndex;
        activeIndex = index;
        displayIndex = nextDisplayIndex;
        TextOnlyText.Text = displayIndex >= 0 ? lyricBlocks[displayIndex].Text : "♪";
        bool animateHighlight = previousDisplayIndex >= 0 && IsVisible;
        for (int i = 0; i < lyricBlocks.Count; i++)
        {
            LyricHighlight.SetActive(lyricBlocks[i], i == displayIndex, animateHighlight);
            LyricHighlight.SetActive(textOnlyBlocks[i], i == displayIndex, animateHighlight);
        }
        if (displayIndex >= 0)
        {
            LyricsPanel.UpdateLayout();
            var offset = lyricBlocks[displayIndex].TranslatePoint(new Point(), LyricsPanel).Y;
            LyricsScroll.ScrollToVerticalOffset(Math.Max(0, offset - LyricsScroll.ViewportHeight * 0.3));
        }
        CenterTextOnlyLine(previousDisplayIndex >= 0 && Math.Abs(displayIndex - previousDisplayIndex) <= 1);
    }

    private void TextViewportChanged(object sender, SizeChangedEventArgs e)
    {
        if (ready) CenterTextOnlyLine(false);
    }

    private void CenterTextOnlyLine(bool animate)
    {
        if (settings.ScrollMode == LyricScrollMode.Continuous && lines.Count > 0)
        {
            ScrollContinuously();
            return;
        }
        if (TextOnlyView.Visibility != Visibility.Visible || textOnlyBlocks.Count == 0) return;
        TextOnlyScroll.UpdateLayout();
        var viewport = TextOnlyScroll.ViewportHeight;
        if (viewport <= 0) return;
        // Real content spacers contribute to scroll extent, including short/plain lyrics.
        // Account for line height so the first line starts exactly at center with offset zero.
        LyricsTopSpacer.Height = Math.Max(0, (viewport - textOnlyBlocks[0].ActualHeight) / 2);
        var last = textOnlyBlocks[^1];
        LyricsBottomSpacer.Height = Math.Max(0, (viewport - last.ActualHeight) / 2 - last.Margin.Bottom);
        TextOnlyScroll.UpdateLayout();
        var block = textOnlyBlocks[Math.Max(0, displayIndex)];
        var target = Math.Clamp(block.TranslatePoint(new Point(0, block.ActualHeight / 2), TextOnlyContent).Y
            - viewport / 2, 0, TextOnlyScroll.ScrollableHeight);
        var from = TextOnlyScroll.VerticalOffset;
        BeginAnimation(AnimatedTextOffsetProperty, null);
        SetValue(AnimatedTextOffsetProperty, animate ? from : target);
        if (!animate) TextOnlyScroll.ScrollToVerticalOffset(target);
        if (animate)
            BeginAnimation(AnimatedTextOffsetProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(400))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void OnAnimationFrame(object? sender, EventArgs e)
    {
        if (smokeTesting || exiting || !IsVisible) return;
        if (fontSizeAnimating)
        {
            CenterTextOnlyLine(false);
            fontSizeAnimating = Environment.TickCount64 < fontSizeAnimationUntil;
        }
        else if (settings.ScrollMode == LyricScrollMode.Continuous && lines.Count > 0)
            ScrollContinuously();
    }

    private void ScrollContinuously()
    {
        if (lines.Count == 0 || textOnlyBlocks.Count != lines.Count) return;
        var anchor = LyricScrolling.GetAnchor(lines, clock.Position(DateTimeOffset.UtcNow), settings.CorrectionSeconds);
        if (anchor.From < 0) return;
        if (TextOnlyView.Visibility == Visibility.Visible)
        {
            TextOnlyScroll.UpdateLayout();
            var viewport = TextOnlyScroll.ViewportHeight;
            if (viewport <= 0) return;
            LyricsTopSpacer.Height = Math.Max(0, (viewport - textOnlyBlocks[0].ActualHeight) / 2);
            var last = textOnlyBlocks[^1];
            LyricsBottomSpacer.Height = Math.Max(0, (viewport - last.ActualHeight) / 2 - last.Margin.Bottom);
            TextOnlyScroll.UpdateLayout();
            double Center(int index) => textOnlyBlocks[index].TranslatePoint(new Point(0, textOnlyBlocks[index].ActualHeight / 2), TextOnlyContent).Y;
            var target = Math.Clamp(LyricScrolling.Interpolate(Center(anchor.From), Center(anchor.To), anchor.Progress) - viewport / 2,
                0, TextOnlyScroll.ScrollableHeight);
            BeginAnimation(AnimatedTextOffsetProperty, null);
            SetValue(AnimatedTextOffsetProperty, target);
            if (Math.Abs(TextOnlyScroll.VerticalOffset - target) > 0.05) TextOnlyScroll.ScrollToVerticalOffset(target);
        }
        else if (PanelSurface.Visibility == Visibility.Visible && SettingsPanel.Visibility != Visibility.Visible)
        {
            LyricsPanel.UpdateLayout();
            double Offset(int index) => lyricBlocks[index].TranslatePoint(new Point(), LyricsPanel).Y;
            LyricsScroll.ScrollToVerticalOffset(Math.Max(0, LyricScrolling.Interpolate(Offset(anchor.From), Offset(anchor.To), anchor.Progress)
                - LyricsScroll.ViewportHeight * 0.3));
        }
    }

    private void ApplySettings()
    {
        if (!ready) return;
        bool romanizationChanged = (int)settings.TextMode != TextModeCombo.SelectedIndex
            || (int)settings.RomanizationLanguage != LanguageCombo.SelectedIndex;
        settings.TextMode = (LyricTextMode)Math.Max(0, TextModeCombo.SelectedIndex);
        settings.RomanizationLanguage = (RomanizationLanguage)Math.Max(0, LanguageCombo.SelectedIndex);
        settings.ScrollMode = (LyricScrollMode)Math.Max(0, ScrollModeCombo.SelectedIndex);
        bool fontSizeChanged = settings.FontSize != FontSlider.Value;
        settings.FontSize = FontSlider.Value;
        settings.Opacity = OpacitySlider.Value;
        settings.CorrectionSeconds = OffsetSlider.Value;
        settings.AlwaysOnTop = TopmostCheck.IsChecked == true;
        settings.TextOnly = TextOnlyCheck.IsChecked == true;
        settings.IgnoreMouseClicks = IgnoreMouseCheck.IsChecked == true;
        Opacity = settings.Opacity;
        Topmost = settings.AlwaysOnTop;
        FontLabel.Text = $"Ukuran lirik · {settings.FontSize:0} px";
        OpacityLabel.Text = $"Opasitas · {settings.Opacity:P0}";
        OffsetLabel.Text = $"Koreksi waktu · {settings.CorrectionSeconds:+0.00;-0.00;0.00} dtk";
        bool animateFontSize = IsVisible;
        foreach (var block in lyricBlocks) LyricHighlight.SetFontSize(block, settings.FontSize, animateFontSize);
        foreach (var block in textOnlyBlocks) LyricHighlight.SetFontSize(block, settings.FontSize, animateFontSize);
        LyricHighlight.SetFontSize(TextOnlyText, settings.FontSize, animateFontSize);
        if (fontSizeChanged && animateFontSize)
        {
            fontSizeAnimating = true;
            fontSizeAnimationUntil = Environment.TickCount64 + LyricHighlight.DurationMilliseconds;
        }
        activeIndex = -2;
        UpdateHighlight();
        CenterTextOnlyLine(false);
        SyncTraySettings();
        ApplyDisplayMode();
        if (romanizationChanged && displayedState.Status == LyricsStatus.Ready) RenderLyrics(displayedState);
    }

    private void RomanizationSettingChanged(object sender, SelectionChangedEventArgs e) { ApplySettings(); ScheduleSave(); }

    private (Forms.ToolStripMenuItem, Forms.ToolStripMenuItem[]) CreateTrayChoice(string[] labels, Action<int> changed)
    {
        var menu = new Forms.ToolStripMenuItem();
        var items = labels.Select((label, index) =>
        {
            var item = new Forms.ToolStripMenuItem(label);
            item.Click += (_, _) => Dispatcher.Invoke(() => { if (!syncingSettings) changed(index); });
            return item;
        }).ToArray();
        menu.DropDownItems.AddRange(items);
        return (menu, items);
    }

    private (Forms.ToolStripMenuItem, Forms.NumericUpDown) CreateTrayNumber(decimal minimum, decimal maximum,
        int decimals, decimal increment, Action<decimal> changed)
    {
        var input = new Forms.NumericUpDown
        {
            Minimum = minimum, Maximum = maximum, DecimalPlaces = decimals, Increment = increment,
            Width = 170, BackColor = Drawing.Color.FromArgb(25, 37, 30), ForeColor = Drawing.Color.White
        };
        var item = new Forms.ToolStripMenuItem();
        item.DropDownItems.Add(new Forms.ToolStripControlHost(input) { Padding = new Forms.Padding(8) });
        input.ValueChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (!syncingSettings) changed(input.Value);
        });
        return (item, input);
    }

    private void SyncTraySettings()
    {
        syncingSettings = true;
        try
        {
            textOnlyMenu.Checked = settings.TextOnly;
            ignoreMouseMenu.Checked = settings.IgnoreMouseClicks;
            topmostMenu.Checked = settings.AlwaysOnTop;
            startupMenu.Checked = settings.StartWithSpotify;
            trayFont.Value = (decimal)settings.FontSize;
            trayOpacity.Value = (decimal)(settings.Opacity * 100);
            trayOffset.Value = (decimal)settings.CorrectionSeconds;
            fontMenu.Text = $"Ukuran lirik · {settings.FontSize:0} px";
            opacityMenu.Text = $"Opasitas · {settings.Opacity:P0}";
            offsetMenu.Text = $"Koreksi waktu · {settings.CorrectionSeconds:+0.00;-0.00;0.00} dtk";
            for (int i = 0; i < textModeOptions.Length; i++) textModeOptions[i].Checked = i == (int)settings.TextMode;
            for (int i = 0; i < languageOptions.Length; i++) languageOptions[i].Checked = i == (int)settings.RomanizationLanguage;
            textModeMenu.Text = "Tampilan lirik · " + textModeOptions[(int)settings.TextMode].Text;
            languageMenu.Text = "Bahasa romanisasi · " + languageOptions[(int)settings.RomanizationLanguage].Text;
            for (int i = 0; i < scrollModeOptions.Length; i++) scrollModeOptions[i].Checked = i == (int)settings.ScrollMode;
            scrollModeMenu.Text = "Mode auto-scroll · " + scrollModeOptions[(int)settings.ScrollMode].Text;
        }
        finally { syncingSettings = false; }
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
            MinHeight = 600;
            Height = Math.Max(Height, 600);
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
        if (!smokeTesting) UpdateSpotifyVisibility(spotifyProcess.IsRunning());
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
    private async void ExitApplication()
    {
        if (exiting) return;
        PersistSettings();
        exiting = true;
        CompositionTarget.Rendering -= OnAnimationFrame;
        mediaTimer.Stop(); lyricTimer.Stop(); saveTimer.Stop();
        lyrics.Dispose();
        romanizationCancellation?.Cancel();
        http.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose(); trayIcon.Dispose();
        await romanizer.DisposeAsync();
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
            ScrollModeCombo.SelectedIndex = 0;
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
            results["panelValuesSyncToTray"] = trayFont.Value == 24 && trayOpacity.Value == 75 && trayOffset.Value == 1.25m
                && topmostMenu.Checked == (TopmostCheck.IsChecked == true);
            trayFont.Value = 28; trayOpacity.Value = 85; trayOffset.Value = -1.5m;
            topmostMenu.Checked = false;
            results["trayValuesSyncToPanel"] = FontSlider.Value == 28 && Math.Abs(OpacitySlider.Value - 0.85) < 0.001
                && OffsetSlider.Value == -1.5 && TopmostCheck.IsChecked == false && !Topmost;
            topmostMenu.Checked = true;
            FontSlider.Value = 20; OpacitySlider.Value = 1; OffsetSlider.Value = 0;
            RenderLyrics(new(new Track("Retry demo", "Mini Lyrics", "", 100), LyricsStatus.Retrying, RetryAfterSeconds: 5));
            results["automaticRetryStatusInPanel"] = Message.Visibility == Visibility.Visible
                && Message.Text.Contains("otomatis") && Message.Text.Contains("5 detik") && RetryButton.Visibility == Visibility.Visible;
            textOnlyMenu.Checked = true;
            results["automaticRetryStatusInOverlay"] = TextOnlyText.Visibility == Visibility.Visible
                && TextOnlyText.Text == Message.Text && TextOnlyScroll.Visibility == Visibility.Collapsed;
            textOnlyMenu.Checked = false;
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
            double lineHeightBeforeHighlight = textOnlyBlocks[2].ActualHeight;
            clock.Update(TimeSpan.FromSeconds(12), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight();
            await Task.Delay(100);
            var growingScale = (ScaleTransform)textOnlyBlocks[2].RenderTransform;
            results["highlightHasIntermediateBrightnessAndSize"] = textOnlyBlocks[2].Opacity > 0.4 && textOnlyBlocks[2].Opacity < 1
                && growingScale.ScaleX > 1 && growingScale.ScaleX < 1.06
                && textOnlyBlocks[1].Opacity > 0.4 && textOnlyBlocks[1].Opacity < 1;
            results["panelHighlightHasIntermediateColor"] = lyricBlocks[2].Foreground is SolidColorBrush animatedBrush
                && animatedBrush.Color != Color.FromRgb(137, 157, 145) && animatedBrush.Color != Color.FromRgb(103, 232, 160);
            results["highlightKeepsLayoutHeightStable"] = Math.Abs(textOnlyBlocks[2].ActualHeight - lineHeightBeforeHighlight) < 0.1;
            Capture(Path.Combine(outputDirectory, "highlight-transition.png"));
            double opacityBeforeReverse = textOnlyBlocks[2].Opacity;
            clock.Update(TimeSpan.FromSeconds(7), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight();
            results["highlightRapidSeekStartsFromCurrentAppearance"] = Math.Abs(textOnlyBlocks[2].Opacity - opacityBeforeReverse) < 0.05;
            await Task.Delay(400);
            results["highlightSettlesAfterRapidSeek"] = textOnlyBlocks[1].Opacity == 1 && textOnlyBlocks[2].Opacity == 0.4
                && Math.Abs(((ScaleTransform)textOnlyBlocks[1].RenderTransform).ScaleX - 1.06) < 0.0001;
            FontSlider.Value = 28;
            await Task.Delay(100);
            results["fontSizeHasIntermediateValue"] = textOnlyBlocks[1].FontSize > 20 && textOnlyBlocks[1].FontSize < 28;
            FontSlider.Value = 24;
            await Task.Delay(400);
            results["fontSizeSettlesAfterRapidChanges"] = textOnlyBlocks[1].FontSize == 24 && lyricBlocks[1].FontSize == 24;
            FontSlider.Value = 20;
            await Task.Delay(400);
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
            clock.Update(TimeSpan.FromSeconds(7), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            TextModeCombo.SelectedIndex = 1; LanguageCombo.SelectedIndex = 0;
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]こんにちは\n[00:05]日本語\n[00:10]ありがとう", false)));
            await romanizationTask.WaitAsync(TimeSpan.FromSeconds(20));
            results["japaneseRomanizationInOverlay"] = textOnlyBlocks[1].Text.Replace(" ", "").ToLowerInvariant() == "nihongo"
                && activeIndex == 1 && lines[1].At == TimeSpan.FromSeconds(5);
            TextModeCombo.SelectedIndex = 2;
            await romanizationTask.WaitAsync(TimeSpan.FromSeconds(10));
            results["latinPlusOriginalInBothViews"] = textOnlyBlocks[1].Text.Contains("\n日本語") && lyricBlocks[1].Text == textOnlyBlocks[1].Text;
            Capture(Path.Combine(outputDirectory, "japanese-romanization.png"));
            languageOptions[1].PerformClick(); textModeOptions[1].PerformClick();
            results["romanizationTraySync"] = LanguageCombo.SelectedIndex == 1 && TextModeCombo.SelectedIndex == 1
                && languageOptions[1].Checked && textModeOptions[1].Checked;
            RenderLyrics(new(sample, LyricsStatus.Ready, new("안녕하세요\n사랑해요", null, false)));
            await romanizationTask.WaitAsync(TimeSpan.FromSeconds(10));
            results["koreanPlainLyricsRomanized"] = textOnlyBlocks[1].Text == "saranghaeyo" && lines.Count == 0;
            results["plainFirstLineHasDistinctPreviewHighlight"] = textOnlyBlocks[0].Opacity == 1 && textOnlyBlocks[1].Opacity < 1
                && textOnlyBlocks[0].FontWeight == FontWeights.Bold && LyricsSource.Text.Contains("PREVIEW");
            Capture(Path.Combine(outputDirectory, "korean-romanization.png"));
            LanguageCombo.SelectedIndex = 0;
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]많아\n[00:05]싫어\n[00:10]HELLO", false)));
            await romanizationTask.WaitAsync(TimeSpan.FromSeconds(10));
            results["koreanCompoundFinalsDoNotCancelEntireSong"] = textOnlyBlocks[0].Text == "mana"
                && textOnlyBlocks[1].Text == "sireo" && textOnlyBlocks[2].Text == "HELLO" && lines.Count == 3;
            LanguageCombo.SelectedIndex = 2;
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]日本語", false)));
            var staleConversion = romanizationTask;
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]Current English line", false)));
            await Task.WhenAll(staleConversion, romanizationTask).WaitAsync(TimeSpan.FromSeconds(10));
            results["staleRomanizationCannotReplaceTrack"] = textOnlyBlocks[0].Text == "Current English line";
            TextModeCombo.SelectedIndex = 0;
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]사랑해요", false)));
            results["originalModePreservesLyrics"] = textOnlyBlocks[0].Text == "사랑해요";
            TextModeCombo.SelectedIndex = 1; LanguageCombo.SelectedIndex = 0;
            results["romanizationPanelSync"] = languageOptions[0].Checked && textModeOptions[1].Checked;
            TextModeCombo.SelectedIndex = 0;
            Width = 1000; Height = 420; FontSlider.Value = 30;
            clock.Reset();
            clock.Update(TimeSpan.Zero, DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]The first line should start in the center\n[00:05]The next line stays below\n[00:10]Then the music carries on", false)));
            await Task.Delay(200);
            var firstLineCenter = textOnlyBlocks[0].TranslatePoint(new Point(0, textOnlyBlocks[0].ActualHeight / 2), TextOnlyScroll).Y;
            results["firstLineCenteredAtSongStart"] = Math.Abs(firstLineCenter - TextOnlyScroll.ActualHeight / 2) < 2;
            Capture(Path.Combine(outputDirectory, "song-start.png"));
            RenderLyrics(new(sample, LyricsStatus.Ready, new("First line without timestamps\nSecond line\nThird line", null, false)));
            await Task.Delay(100);
            var plainFirstCenter = textOnlyBlocks[0].TranslatePoint(new Point(0, textOnlyBlocks[0].ActualHeight / 2), TextOnlyScroll).Y;
            results["plainFirstLineCentered"] = Math.Abs(plainFirstCenter - TextOnlyScroll.ActualHeight / 2) < 2 && LyricsTopSpacer.Height > 0;
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]A single line stays centered", false)));
            await Task.Delay(100);
            var singleCenter = textOnlyBlocks[0].TranslatePoint(new Point(0, textOnlyBlocks[0].ActualHeight / 2), TextOnlyScroll).Y;
            results["singleLineCentered"] = Math.Abs(singleCenter - TextOnlyScroll.ActualHeight / 2) < 2;
            TextModeCombo.SelectedIndex = 2; LanguageCombo.SelectedIndex = 2;
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]日本語", false)));
            await romanizationTask.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(100);
            var bilingualCenter = textOnlyBlocks[0].TranslatePoint(new Point(0, textOnlyBlocks[0].ActualHeight / 2), TextOnlyScroll).Y;
            results["bilingualFirstLineCenteredAfterConversion"] = textOnlyBlocks[0].Text.Contains("\n日本語")
                && Math.Abs(bilingualCenter - TextOnlyScroll.ActualHeight / 2) < 2;
            Width = 420; Height = 300;
            await Task.Delay(100);
            var resizedCenter = textOnlyBlocks[0].TranslatePoint(new Point(0, textOnlyBlocks[0].ActualHeight / 2), TextOnlyScroll).Y;
            results["firstLineRecentersAfterResize"] = Math.Abs(resizedCenter - TextOnlyScroll.ActualHeight / 2) < 2;
            Width = 925; Height = 309; FontSlider.Value = 33;
            TextModeCombo.SelectedIndex = 1; LanguageCombo.SelectedIndex = 0;
            clock.Reset();
            clock.Update(TimeSpan.Zero, DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:05]\n[00:10]♪\n[00:20]こんにちは\n[00:25]日本語", false)));
            await romanizationTask.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(100);
            var introCenter = textOnlyBlocks[2].TranslatePoint(new Point(0, textOnlyBlocks[2].ActualHeight / 2), TextOnlyScroll).Y;
            results["introFirstSungLineCenteredBeforeAnyTimestamp"] = activeIndex == -1 && displayIndex == 2
                && Math.Abs(introCenter - TextOnlyScroll.ActualHeight / 2) < 2 && textOnlyBlocks[2].Opacity == 1;
            clock.Update(TimeSpan.FromSeconds(12), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight();
            await Task.Delay(500);
            var cueCenter = textOnlyBlocks[2].TranslatePoint(new Point(0, textOnlyBlocks[2].ActualHeight / 2), TextOnlyScroll).Y;
            results["introSkipsMusicalCuesAndKeepsFirstSungLineBright"] = activeIndex == 1 && displayIndex == 2
                && Math.Abs(cueCenter - TextOnlyScroll.ActualHeight / 2) < 2 && textOnlyBlocks[2].Opacity == 1;
            Capture(Path.Combine(outputDirectory, "intro-preview.png"));
            var introOffset = TextOnlyScroll.VerticalOffset;
            clock.Update(TimeSpan.FromSeconds(20), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight();
            await Task.Delay(500);
            results["introToFirstSungLineDoesNotJump"] = activeIndex == 2 && displayIndex == 2 && Math.Abs(TextOnlyScroll.VerticalOffset - introOffset) < 2;
            TextModeCombo.SelectedIndex = 0;
            ScrollModeCombo.SelectedIndex = 1;
            clock.Reset();
            clock.Update(TimeSpan.Zero, DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            Hide();
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00][Intro]\n[00:10](Instrumental)\n[00:20]First sung line\n[00:30]Second sung line", false)));
            results["firstHighlightPreparedBeforeWindowVisible"] = displayIndex == 2 && textOnlyBlocks[2].Opacity == 1
                && textOnlyBlocks[2].FontWeight == FontWeights.Bold && textOnlyBlocks[0].Opacity < 1;
            ShowPanel();
            await Task.Delay(100);
            var firstPreviewCenter = textOnlyBlocks[2].TranslatePoint(new Point(0, textOnlyBlocks[2].ActualHeight / 2), TextOnlyScroll).Y;
            results["firstHighlightSkipsIntroLabelsAndCenters"] = displayIndex == 2 && textOnlyBlocks[2].Opacity == 1
                && Math.Abs(firstPreviewCenter - TextOnlyScroll.ActualHeight / 2) < 2;
            Capture(Path.Combine(outputDirectory, "first-line-highlight.png"));
            TextModeCombo.SelectedIndex = 0;
            ScrollModeCombo.SelectedIndex = 1;
            results["continuousScrollPanelSync"] = scrollModeOptions[1].Checked && settings.ScrollMode == LyricScrollMode.Continuous;
            clock.Reset();
            clock.Update(TimeSpan.Zero, DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]First continuous line\n[00:10]Next continuous line\n[00:20]Third continuous line\n[00:30]Final continuous line", false)));
            ScrollContinuously();
            await Task.Delay(100);
            var continuousStart = TextOnlyScroll.VerticalOffset;
            clock.Update(TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight(); ScrollContinuously();
            await Task.Delay(100);
            var continuousMiddle = TextOnlyScroll.VerticalOffset;
            clock.Update(TimeSpan.FromSeconds(10), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight(); ScrollContinuously();
            await Task.Delay(100);
            var continuousEnd = TextOnlyScroll.VerticalOffset;
            results["continuousScrollMovesWithinTimestampInterval"] = continuousStart < continuousMiddle && continuousMiddle < continuousEnd
                && Math.Abs(continuousMiddle - (continuousStart + continuousEnd) / 2) < 2;
            await Task.Delay(150); ScrollContinuously();
            await Task.Delay(100);
            results["continuousScrollStopsWhilePaused"] = Math.Abs(TextOnlyScroll.VerticalOffset - continuousEnd) < 2;
            clock.Update(TimeSpan.Zero, DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight(); ScrollContinuously();
            await Task.Delay(100);
            results["continuousScrollSeeksBackward"] = Math.Abs(TextOnlyScroll.VerticalOffset - continuousStart) < 2;
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]\n[00:05]♪\n[00:20]First sung line\n[00:30]Next sung line", false)));
            ScrollContinuously();
            await Task.Delay(100);
            var continuousIntro = TextOnlyScroll.VerticalOffset;
            clock.Update(TimeSpan.FromSeconds(12), DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            UpdateHighlight(); ScrollContinuously();
            await Task.Delay(100);
            results["continuousScrollKeepsIntroPreviewCentered"] = Math.Abs(TextOnlyScroll.VerticalOffset - continuousIntro) < 2 && displayIndex == 2;
            scrollModeOptions[0].PerformClick();
            results["continuousScrollTraySync"] = ScrollModeCombo.SelectedIndex == 0 && settings.ScrollMode == LyricScrollMode.PerLine;
            Width = 420; Height = 220; FontSlider.Value = 20;
            TextModeCombo.SelectedIndex = 1; LanguageCombo.SelectedIndex = 0;
            clock.Reset();
            clock.Update(TimeSpan.Zero, DateTimeOffset.UtcNow, false, TimeSpan.FromSeconds(180), 1, DateTimeOffset.UtcNow);
            RenderLyrics(new(sample, LyricsStatus.Ready, new(null, "[00:00]Preview starts immediately\n[00:10]Another line", false)));
            await romanizationTask.WaitAsync(TimeSpan.FromSeconds(10));
            results["firstHighlightAlsoWorksAtZeroTimestamp"] = activeIndex == 0 && displayIndex == 0
                && textOnlyBlocks[0].Opacity == 1 && textOnlyBlocks[0].FontWeight == FontWeights.Bold;
            textOnlyMenu.Checked = false;
            results["restoreNormalPanel"] = PanelSurface.Visibility == Visibility.Visible && TextOnlyView.Visibility == Visibility.Collapsed;
            ignoreMouseMenu.Checked = true;
            ToggleSettings(this, new RoutedEventArgs());
            results["settingsTemporarilyReceiveMouse"] = !MousePassthrough.IsEnabled(nativeWindow) && settings.IgnoreMouseClicks;
            TextOnlyCheck.IsChecked = true;
            IgnoreMouseCheck.IsChecked = false;
            results["panelModesSyncToTray"] = textOnlyMenu.Checked && !ignoreMouseMenu.Checked
                && SettingsPanel.Visibility == Visibility.Visible && PanelSurface.Visibility == Visibility.Visible;
            ignoreMouseMenu.Checked = true;
            results["trayModesSyncToPanel"] = IgnoreMouseCheck.IsChecked == true && !MousePassthrough.IsEnabled(nativeWindow);
            StartWithSpotifyCheck.IsChecked = true;
            results["startupPanelSyncsToTray"] = settings.StartWithSpotify && startupMenu.Checked;
            UpdateSpotifyVisibility(false);
            results["startupKeepsSettingsAccessible"] = IsVisible;
            Capture(Path.Combine(outputDirectory, "settings.png"));
            ToggleSettings(this, new RoutedEventArgs());
            results["settingsRestoreClickThrough"] = MousePassthrough.IsEnabled(nativeWindow);
            results["settingsRestoresCompactHeight"] = Height == 220;
            spotifyVisibility.Update(false, false, false);
            UpdateSpotifyVisibility(false);
            results["startupWaitsInTray"] = !IsVisible && tray.Visible;
            UpdateSpotifyVisibility(true);
            results["startupShowsWithSpotify"] = IsVisible;
            Hide();
            UpdateSpotifyVisibility(true);
            results["startupRespectsManualHide"] = !IsVisible;
            UpdateSpotifyVisibility(false);
            UpdateSpotifyVisibility(true);
            results["startupShowsAfterSpotifyReopens"] = IsVisible;
            UpdateSpotifyVisibility(false);
            results["startupHidesAfterSpotifyCloses"] = !IsVisible;
            startupMenu.Checked = false;
            results["startupTraySyncsToPanel"] = !settings.StartWithSpotify && StartWithSpotifyCheck.IsChecked == false;
            ShowPanel();
            results["startupCommandQuotesPath"] = StartupRegistration.Command(@"C:\Apps With Spaces\MiniLyrics.exe") == "\"C:\\Apps With Spaces\\MiniLyrics.exe\" --background";
            var settingsPath = Path.Combine(outputDirectory, "test-settings.json");
            var saved = new UserSettings { FontSize = 28, Opacity = 0.65, AlwaysOnTop = false, TextOnly = true, CorrectionSeconds = -2.5, Width = 480, Height = 260,
                StartWithSpotify = true, TextMode = LyricTextMode.LatinAndOriginal, RomanizationLanguage = RomanizationLanguage.Japanese };
            results["settingsSaved"] = saved.Save(settingsPath);
            var reloaded = UserSettings.Load(settingsPath);
            results["settingsRoundTrip"] = reloaded.FontSize == 28 && reloaded.Opacity == 0.65 && !reloaded.AlwaysOnTop
                && reloaded.TextOnly && reloaded.IgnoreMouseClicks && reloaded.CorrectionSeconds == -2.5 && reloaded.Width == 480 && reloaded.Height == 260;
            results["romanizationSettingsPersist"] = reloaded.TextMode == LyricTextMode.LatinAndOriginal && reloaded.RomanizationLanguage == RomanizationLanguage.Japanese;
            results["startupSettingPersists"] = reloaded.StartWithSpotify;
            results["scrollModePersists"] = reloaded.ScrollMode == LyricScrollMode.Continuous;
            File.WriteAllText(settingsPath, "invalid json");
            results["settingsCorruptionRecovery"] = UserSettings.Load(settingsPath).FontSize == 20;
            results["startupDefaultsOff"] = !UserSettings.Load(settingsPath).StartWithSpotify;
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
