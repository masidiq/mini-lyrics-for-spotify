using System.Net;
using MiniLyrics.Core;

int count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name); count++;
}

var visibility = new SpotifyVisibility();
Check(visibility.Update(false, false, false) is null, "manual mode leaves visibility unchanged");
Check(visibility.Update(true, false, false) == false, "automatic mode starts hidden without Spotify");
Check(visibility.Update(true, false, false) is null, "idle polling preserves manual show");
Check(visibility.Update(true, true, false) == true, "Spotify opening shows lyrics");
Check(visibility.Update(true, true, false) is null, "polling preserves manual hide");
Check(visibility.Update(true, false, true) is null, "Spotify closing keeps settings accessible");
Check(visibility.Update(true, false, false) == false, "closing settings applies pending Spotify state");
Check(visibility.Update(true, true, true) is null, "Spotify opening does not interrupt settings");
Check(visibility.Update(true, true, false) == true, "Spotify reopening restores lyrics");
Check(visibility.Update(false, false, false) is null, "disabling automatic mode preserves visibility");
Check(visibility.Update(true, true, false) == true, "reenabling reevaluates Spotify state");

var lines = LrcParser.Parse("[ar:Example]\n[offset:+100]\n[00:02.50][01:02.005]Second\n[00:01.1]First\n[bad]ignored\n[00:03.000]\n[00:99]invalid");
Check(lines.Count == 4 && lines[0].Text == "First", "metadata ignored, multiple timestamps expanded and sorted");
Check(lines[0].At == TimeSpan.FromSeconds(1.2) && lines[1].At == TimeSpan.FromSeconds(2.6)
    && lines[3].At == TimeSpan.FromSeconds(62.105), "fraction precision and LRC offset");
Check(lines[2].Text == "", "blank timed lines retained for instrumental gaps");
Check(LrcParser.ActiveIndex(lines, TimeSpan.Zero, 0) == -1, "no highlighted line before first timestamp");
Check(LrcParser.ActiveIndex(lines, TimeSpan.FromSeconds(2.6), 0) == 1, "exact timestamp selects new line");
Check(LrcParser.ActiveIndex(lines, TimeSpan.FromSeconds(1.6), 1) == 1, "positive correction advances lyrics");
Check(LrcParser.ActiveIndex(lines, TimeSpan.FromSeconds(3.6), -1) == 1, "negative correction delays lyrics");
Check(LrcParser.ActiveIndex([], TimeSpan.FromSeconds(20), 0) == -1, "empty LRC safe");
var introLines = LrcParser.Parse("[00:00]\n[00:05]♪\n[00:20]First sung line\n[00:25]Second line\n[00:30]");
Check(LrcParser.DisplayIndex(introLines, -1) == 2 && LrcParser.DisplayIndex(introLines, 0) == 2
    && LrcParser.DisplayIndex(introLines, 1) == 2, "intro previews first sung line past empty and musical cues");
Check(LrcParser.DisplayIndex(introLines, 2) == 2 && LrcParser.DisplayIndex(introLines, 3) == 3,
    "sung line remains highlighted until next timestamp");
Check(LrcParser.DisplayIndex(introLines, 4) == 3 && LrcParser.DisplayIndex([], -1) == -1,
    "outro keeps last sung line and empty lyrics remain safe");
var introLabels = LrcParser.Parse("[00:00][Intro]\n[00:10](Instrumental)\n[00:20]First sung line\n[00:30](Hello)");
Check(LrcParser.DisplayIndex(introLabels, -1) == 2 && LrcParser.DisplayIndex(introLabels, 0) == 2
    && LrcParser.DisplayIndex(introLabels, 1) == 2, "intro section labels do not steal first-line highlight");
Check(LrcParser.IsSungLine("(Hello)") && !LrcParser.IsSungLine("[Intro]"), "parenthesized lyrics remain valid outside known section labels");
Check(LrcParser.ActiveIndex(lines, TimeSpan.FromSeconds(1), 200) == LrcParser.ActiveIndex(lines, TimeSpan.FromSeconds(1), 10), "correction clamped to ten seconds");
Check(LrcParser.Parse("[99999999999999999999999999999999999:01]invalid").Count == 0, "overflowing timestamp ignored");
Check(LrcParser.ActiveIndex(LrcParser.Parse("[00:01]a\n[00:01]b"), TimeSpan.FromSeconds(1), 0) == 1, "duplicate timestamps deterministic");
var now = DateTimeOffset.UtcNow;
var clock = new PlaybackClock();
clock.Update(TimeSpan.FromSeconds(5), now, true, TimeSpan.FromSeconds(100), 1, now);
Check(clock.Position(now.AddSeconds(3)) == TimeSpan.FromSeconds(8), "playback interpolates between polls");
clock.Update(TimeSpan.FromSeconds(5), now, false, TimeSpan.FromSeconds(100), 1, now.AddSeconds(3));
Check(clock.Position(now.AddSeconds(8)) == TimeSpan.FromSeconds(8), "pause freezes interpolated position even if timeline is unchanged");
clock.Update(TimeSpan.FromSeconds(40), now.AddSeconds(8), false, TimeSpan.FromSeconds(100), 1, now.AddSeconds(8));
Check(clock.Position(now.AddSeconds(10)) == TimeSpan.FromSeconds(40), "seek while paused");
clock.Update(TimeSpan.FromSeconds(40), now.AddSeconds(8), true, TimeSpan.FromSeconds(100), 1, now.AddSeconds(10));
Check(clock.Position(now.AddSeconds(12)) == TimeSpan.FromSeconds(42), "resume continues from pause without counting paused time");
Check(clock.Position(now.AddSeconds(200)) == TimeSpan.FromSeconds(100), "position clamped to duration");
clock.Reset();
clock.Update(TimeSpan.FromSeconds(0), now, true, TimeSpan.FromSeconds(100), 1, now.AddSeconds(2));
Check(clock.Position(now.AddSeconds(2)) == TimeSpan.FromSeconds(2), "Windows last-updated timestamp accounted for");

var fake = new DeferredProvider();
using (var controller = new LyricsController(fake))
{
    var first = controller.LoadAsync(new("old", "artist", "album", 100));
    var second = controller.LoadAsync(new("new", "artist", "album", 100));
    Check(fake.Tokens["old"].IsCancellationRequested, "track change cancels previous request");
    fake.Requests["new"].SetResult(new("new lyrics", null, false));
    await second;
    fake.Requests["old"].SetResult(new("old lyrics", null, false));
    await first;
    Check(controller.State.Track?.Title == "new" && controller.State.Document?.PlainLyrics == "new lyrics", "late old response cannot replace current lyrics");
    var pending = controller.LoadAsync(new("pending", "artist", "", 100));
    await controller.LoadAsync(null);
    fake.Requests["pending"].SetResult(new("stale", null, false));
    await pending;
    Check(controller.State.Status == LyricsStatus.Waiting && controller.State.Track is null, "disconnect invalidates in-flight request");
}

var waits = new List<TimeSpan>();
var scrollLines = LrcParser.Parse("[00:00]\n[00:10]First\n[00:30]Second\n[00:40]Third");
var introAnchor = LyricScrolling.GetAnchor(scrollLines, TimeSpan.FromSeconds(5), 0);
Check(introAnchor.From == 1 && introAnchor.To == 1 && introAnchor.Progress == 0, "continuous scroll holds first sung line during intro");
var midAnchor = LyricScrolling.GetAnchor(scrollLines, TimeSpan.FromSeconds(20), 0);
Check(midAnchor.From == 1 && midAnchor.To == 2 && midAnchor.Progress == 0.5
    && LyricScrolling.Interpolate(100, 180, midAnchor.Progress) == 140, "continuous scroll interpolates pixels using timestamp progress");
Check(LyricScrolling.GetAnchor(scrollLines, TimeSpan.FromSeconds(35), 0).Progress == 0.5,
    "different line durations produce corresponding continuous speed");
Check(LyricScrolling.GetAnchor(scrollLines, TimeSpan.FromSeconds(15), 5).Progress == 0.5,
    "continuous scroll uses lyric time correction");
var gapAnchor = LyricScrolling.GetAnchor(LrcParser.Parse("[00:00]First\n[00:05]\n[00:10]Next"), TimeSpan.FromSeconds(2.5), 0);
Check(gapAnchor.From == 0 && gapAnchor.To == 2 && gapAnchor.Progress == 0.5, "continuous scrolling reaches next lyric before instrumental preview");
Check(LyricScrolling.GetAnchor(scrollLines, TimeSpan.FromSeconds(100), 0).From == 3
    && LyricScrolling.GetAnchor([], TimeSpan.Zero, 0).From == -1, "continuous scroll handles outro and empty timestamps");
var recovering = new ScriptedProvider((_, attempt) => attempt <= 6
    ? throw new HttpRequestException("server unavailable", null, HttpStatusCode.ServiceUnavailable)
    : new("recovered lyrics", null, false));
using (var controller = new LyricsController(recovering, (wait, _) => { waits.Add(wait); return Task.CompletedTask; }))
{
    await controller.LoadAsync(new("recovery", "artist", "", 100));
    Check(waits.Select(wait => wait.TotalSeconds).SequenceEqual(new double[] { 2, 5, 10, 30, 60, 60 }), "automatic retry uses capped exponential backoff");
    Check(controller.State.Status == LyricsStatus.Ready && recovering.Calls == 7, "temporary server failure recovers without manual retry");
}
foreach (var failure in new Exception[]
{
    new HttpRequestException("offline"), new TaskCanceledException("timeout"),
    new HttpRequestException("timeout", null, HttpStatusCode.RequestTimeout),
    new IOException("connection reset")
})
{
    var transient = new ScriptedProvider((_, attempt) => attempt == 1 ? throw failure : new("ok", null, false));
    using var controller = new LyricsController(transient, (_, _) => Task.CompletedTask);
    await controller.LoadAsync(new("temporary", "artist", "", 100));
    Check(transient.Calls == 2 && controller.State.Status == LyricsStatus.Ready, $"retry recovers from {failure.GetType().Name}: {failure.Message}");
}
foreach (bool sameTrack in new[] { false, true })
{
    var waitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationToken waitToken = default;
    var interrupted = new ScriptedProvider((_, attempt) => attempt == 1 ? throw new HttpRequestException("offline") : new("current", null, false));
    using var controller = new LyricsController(interrupted, async (_, token) =>
    {
        waitToken = token;
        waitStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    });
    var previous = controller.LoadAsync(new("old", "artist", "", 100));
    await waitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Check(controller.State.Status == LyricsStatus.Retrying && controller.State.RetryAfterSeconds == 2, "retry wait is exposed to UI");
    await controller.LoadAsync(new(sameTrack ? "old" : "new", "artist", "", 100));
    await previous.WaitAsync(TimeSpan.FromSeconds(2));
    Check(waitToken.IsCancellationRequested && interrupted.Calls == 2 && controller.State.Status == LyricsStatus.Ready,
        sameTrack ? "manual retry cancels pending automatic retry" : "track change cancels pending automatic retry");
}
foreach (bool disposing in new[] { false, true })
{
    var pendingProvider = new ScriptedProvider((_, _) => throw new HttpRequestException("offline"));
    CancellationToken waitToken = default;
    using var controller = new LyricsController(pendingProvider, (_, token) =>
    {
        waitToken = token;
        return Task.Delay(Timeout.InfiniteTimeSpan, token);
    });
    var pending = controller.LoadAsync(new("old", "artist", "", 100));
    if (disposing) controller.Dispose(); else await controller.LoadAsync(null);
    await pending.WaitAsync(TimeSpan.FromSeconds(2));
    Check(waitToken.IsCancellationRequested && pendingProvider.Calls == 1, disposing ? "shutdown cancels automatic retry" : "disconnect cancels automatic retry");
}
var missing = new ScriptedProvider((_, _) => null);
using (var controller = new LyricsController(missing, (_, _) => throw new InvalidOperationException("must not retry")))
{
    await controller.LoadAsync(new("missing", "artist", "", 100));
    Check(missing.Calls == 1 && controller.State.Status == LyricsStatus.NotFound, "missing lyrics are not retried");
}
var malformed = new ScriptedProvider((_, _) => throw new System.Text.Json.JsonException("invalid response"));
using (var controller = new LyricsController(malformed, (_, _) => throw new InvalidOperationException("must not retry")))
{
    await controller.LoadAsync(new("malformed", "artist", "", 100));
    Check(malformed.Calls == 1 && controller.State.Status == LyricsStatus.Error, "malformed response does not loop");
}

var directory = Path.Combine(Path.GetTempPath(), "mini-lyrics-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var handler = new FakeHttpHandler();
    using var http = new HttpClient(handler) { BaseAddress = new Uri("https://lrclib.net/") };
    var provider = new LrclibProvider(http, directory);
    var track = new Track("Song & spaces", "Singer", "Album", 123.45);
    var document = await provider.GetAsync(track, default);
    Check(document?.SyncedLyrics == "[00:01.00]hello", "LRCLIB JSON parsed");
    Check(handler.LastUri!.Query.Contains("track_name=Song%20%26%20spaces") && handler.LastUri.Query.Contains("duration=123.45"), "request metadata encoded with invariant duration");
    await provider.GetAsync(track, default);
    Check(handler.Calls == 1, "cache avoids repeat network request");
    var cachePath = Directory.GetFiles(directory, "*.json").Single();
    await File.WriteAllTextAsync(cachePath, "corrupted");
    await provider.GetAsync(track, default);
    Check(handler.Calls == 2, "corrupt cache recovered through provider");
    handler.Status = HttpStatusCode.NotFound;
    Check(await provider.GetAsync(new("missing", "singer", "", 100), default) is null, "404 gives missing lyrics");
    handler.Status = HttpStatusCode.Forbidden;
    using var controller = new LyricsController(provider);
    await controller.LoadAsync(new("offline", "singer", "", 100));
    Check(controller.State.Status == LyricsStatus.Error, "permanent HTTP failure stops with manual retry available");
    handler.Status = HttpStatusCode.TooManyRequests;
    handler.RetryAfterSeconds = 17;
    var rateWaits = new List<TimeSpan>();
    using (var rateController = new LyricsController(provider, (wait, _) =>
    {
        rateWaits.Add(wait);
        handler.Status = HttpStatusCode.OK;
        return Task.CompletedTask;
    }))
    {
        await rateController.LoadAsync(new("rate limited", "singer", "", 100));
        Check(rateWaits.Count == 1 && rateWaits[0] == TimeSpan.FromSeconds(17) && rateController.State.Status == LyricsStatus.Ready,
            "HTTP 429 honors Retry-After and recovers");
    }
    handler.Status = HttpStatusCode.OK;
    handler.Json = "{\"instrumental\":true}";
    await controller.LoadAsync(new("instrumental", "singer", "", 100));
    Check(controller.State.Status == LyricsStatus.Instrumental, "instrumental state");
    handler.Json = "{\"plainLyrics\":\"plain text\"}";
    await controller.LoadAsync(new("plain", "singer", "", 100));
    Check(controller.State.Status == LyricsStatus.Ready && controller.State.Document?.SyncedLyrics is null, "plain lyric fallback");
    object SearchRecord(long id, string title, string artist, string album, double seconds, string? lrc) =>
        new { id, trackName = title, artistName = artist, albumName = album, duration = seconds, instrumental = false, plainLyrics = "plain", syncedLyrics = lrc };
    handler.SearchJson = System.Text.Json.JsonSerializer.Serialize(new[]
    {
        SearchRecord(1, "Same Song", "Wrong Artist", "Original", 200, "[00:00]wrong artist"),
        SearchRecord(2, "Same Song live", "Same Artist", "Original", 200, "[00:00]wrong version"),
        SearchRecord(3, "Same Song", "Same Artist", "Original", 3, "[00:00]bad duration"),
        SearchRecord(4, "Same Song", "Same Artist", "Original", 200, "[10:00]invalid timing"),
        SearchRecord(5, "Same Song", "Same Artist", "Reissue", 200, "[00:00]reissue"),
        SearchRecord(6, "Same Song", "Same Artist", "Original", 201.5, "[00:00]less close"),
        SearchRecord(7, "Same Song", "Same Artist", "Original", 200.2, "[00:00]best\n[00:10]next")
    });
    var fallbackTrack = new Track("Same Song", "Same Artist", "Original", 200);
    var fallback = await provider.GetAsync(fallbackTrack, default);
    Check(fallback?.SyncedLyrics?.Contains("best") == true, "synced search checks title artist duration and ranks matching album");
    var callsBeforeCached = handler.Calls;
    await provider.GetAsync(fallbackTrack, default);
    Check(handler.Calls == callsBeforeCached, "upgraded timed result is cached without repeat search");
    var legacyTrack = fallbackTrack with { Album = "Old cache" };
    var legacyKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(legacyTrack.Key)));
    await File.WriteAllTextAsync(Path.Combine(directory, legacyKey + ".json"), "{\"PlainLyrics\":\"old plain\",\"SyncedLyrics\":null,\"Instrumental\":false}");
    var legacy = await provider.GetAsync(legacyTrack, default);
    Check(legacy?.SyncedLyrics?.Contains("reissue") == true && handler.LastUri!.AbsolutePath.EndsWith("/search"),
        "legacy plain cache searches other album with same artist title and duration");
    handler.SearchJson = "[]";
    var noSyncTrack = new Track("no sync", "artist", "", 100);
    var noSync = await provider.GetAsync(noSyncTrack, default);
    var callsBeforeNegativeCache = handler.Calls;
    await provider.GetAsync(noSyncTrack, default);
    Check(noSync?.PlainLyrics == "plain text" && noSync.SyncedSearchCheckedAt is not null && handler.Calls == callsBeforeNegativeCache,
        "unsynced search keeps plain lyrics and caches negative lookup for one day");
    handler.SearchStatus = HttpStatusCode.ServiceUnavailable;
    var offlinePlain = await provider.GetAsync(new("offline search", "artist", "", 100), default);
    Check(offlinePlain?.PlainLyrics == "plain text" && offlinePlain.SyncedSearchCheckedAt is null, "search outage keeps usable plain lyrics without negative caching");
    handler.SearchStatus = HttpStatusCode.OK;
}
finally
{
    // This uniquely created test-owned directory contains only generated test cache files.
    if (Directory.Exists(directory))
    {
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }
}
await using (var romanizer = new LyricsRomanizer())
{
    var korean = await romanizer.ConvertLinesAsync(["안녕하세요", "사랑해요", "한국어", "국물"], LyricTextMode.Latin, RomanizationLanguage.Korean);
    Check(korean[0] == "annyeonghaseyo" && korean[1] == "saranghaeyo", "Korean Hangul converts to Latin");
    Check(korean[2].Replace("-", "") == "hangugeo" && korean[3] == "gungmul", "Korean liaison and nasal assimilation");
    var compoundWords = new Dictionary<string, string>
    {
        ["많아"] = "mana", ["않아"] = "ana", ["괜찮아"] = "gwaenchana", ["싫어"] = "sireo",
        ["많다"] = "manta", ["않고"] = "anko", ["앉아"] = "anja", ["읽어"] = "ilgeo",
        ["읽다"] = "ikda", ["삶"] = "sam", ["값"] = "gap", ["값이"] = "gapsi", ["없어"] = "eopseo", ["넓어"] = "neolbeo"
    };
    foreach (var (hangul, expected) in compoundWords)
    {
        var converted = await romanizer.ConvertLinesAsync([hangul], LyricTextMode.Latin, RomanizationLanguage.Automatic);
        Check(converted[0].Replace("-", "") == expected, $"Korean compound batchim converts: {hangul} -> {converted[0]}");
    }
    var allHangul = Enumerable.Range(0xAC00, 11172).Select(code => ((char)code).ToString()).ToArray();
    var allLatin = await romanizer.ConvertLinesAsync(allHangul, LyricTextMode.Latin, RomanizationLanguage.Automatic);
    Check(allLatin.All(text => !text.Any(c => c is >= '\uAC00' and <= '\uD7A3')), "all 11172 modern Hangul syllables convert without dictionary exceptions");
    var allLiaison = await romanizer.ConvertLinesAsync(allHangul.Select(text => text + "아").ToArray(), LyricTextMode.Latin, RomanizationLanguage.Automatic);
    Check(allLiaison.All(text => !text.Any(c => c is >= '\uAC00' and <= '\uD7A3')), "all modern Hangul syllables before vowels convert without dictionary exceptions");
    var japanese = await romanizer.ConvertLinesAsync(["こんにちは", "日本語"], LyricTextMode.Latin, RomanizationLanguage.Automatic);
    Check(japanese.All(text => !text.Any(c => c is >= '\u3040' and <= '\u9FFF'))
        && japanese[1].Replace(" ", "").ToLowerInvariant() == "nihongo", "Japanese kana and kanji convert with dictionary readings");
    Check(japanese[0].Replace(" ", "").ToLowerInvariant() == "konnichiwa", "Japanese uses dictionary pronunciation rather than kana spelling");
    var explicitJapanese = await romanizer.ConvertLinesAsync(["日本語"], LyricTextMode.Latin, RomanizationLanguage.Japanese);
    Check(explicitJapanese[0].Replace(" ", "").ToLowerInvariant() == "nihongo", "manual Japanese handles kanji-only lyrics");
    var ambiguous = await romanizer.ConvertLinesAsync(["日本語"], LyricTextMode.Latin, RomanizationLanguage.Automatic);
    Check(ambiguous[0] == "日本語", "automatic mode preserves ambiguous Han-only text");
    var mixed = await romanizer.ConvertLinesAsync(["HELLO 사랑해요! 123 ♪", "안녕 こんにちは"], LyricTextMode.Latin, RomanizationLanguage.Automatic);
    Check(mixed[0] == "HELLO saranghaeyo! 123 ♪" && !mixed[1].Contains("안녕") && !mixed[1].Contains("こんにちは"), "mixed Korean Japanese and Latin preserve English and punctuation");
    var both = await romanizer.ConvertLinesAsync(["사랑해요", "HELLO"], LyricTextMode.LatinAndOriginal, RomanizationLanguage.Automatic);
    Check(both[0] == "saranghaeyo\n사랑해요" && both[1] == "HELLO", "Latin plus original avoids duplicating unchanged English");
    var original = await romanizer.ConvertLinesAsync(["사랑해요", "日本語"], LyricTextMode.Original, RomanizationLanguage.Japanese);
    Check(original.SequenceEqual(new[] { "사랑해요", "日本語" }), "original text remains intact after conversions and cache use");
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    bool stopped = false;
    try { await romanizer.ConvertLinesAsync(["日本語"], LyricTextMode.Latin, RomanizationLanguage.Japanese, canceled.Token); }
    catch (OperationCanceledException) { stopped = true; }
    Check(stopped, "romanization accepts cancellation for stale lyrics");
    var decomposed = await romanizer.ConvertLinesAsync(["안녕".Normalize(System.Text.NormalizationForm.FormD), "", "♪"], LyricTextMode.Latin, RomanizationLanguage.Korean);
    Check(decomposed[0] == "annyeong" && decomposed[1] == "" && decomposed[2] == "♪", "decomposed Hangul blank lines and instrumental markers remain safe");
}
Console.WriteLine($"All {count} checks passed.");

if (args.Contains("--live-lrclib"))
{
    using var liveHttp = new HttpClient { BaseAddress = new Uri("https://lrclib.net/"), Timeout = TimeSpan.FromSeconds(20) };
    liveHttp.DefaultRequestHeaders.UserAgent.ParseAdd("MiniLyricsForSpotify/0.1.2");
    var liveDirectory = Path.Combine(Environment.CurrentDirectory, "artifacts", "verification-v0.1.2", "live-cache");
    var liveProvider = new LrclibProvider(liveHttp, liveDirectory);
    var liveDocument = await liveProvider.GetAsync(new("Manusia Paling Bahagia", "Ghea Indrawari", "Berdamai", 213), default);
    Check(liveDocument is not null && LrcParser.Parse(liveDocument.SyncedLyrics).Count > 0,
        "live LRCLIB lookup upgrades Manusia Paling Bahagia to timed lyrics (text not logged)");
}

int fileArgument = Array.IndexOf(args, "--check-romanization-file");
if (fileArgument >= 0 && fileArgument + 1 < args.Length)
{
    var document = System.Text.Json.JsonSerializer.Deserialize<LyricsDocument>(await File.ReadAllTextAsync(args[fileArgument + 1]),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    var timed = LrcParser.Parse(document.SyncedLyrics);
    var sourceLines = timed.Count > 0 ? timed.Select(line => line.Text).ToArray() : (document.PlainLyrics ?? "").Split('\n');
    await using var converter = new LyricsRomanizer();
    int failures = 0;
    for (int i = 0; i < sourceLines.Length; i++)
    {
        try { await converter.ConvertLinesAsync([sourceLines[i]], LyricTextMode.Latin, RomanizationLanguage.Automatic); }
        catch (Exception ex) { failures++; Console.WriteLine($"Romanization failure at line {i}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
    }
    Check(failures == 0, $"real cached lyrics: {sourceLines.Length} lines convert without exceptions (lyrics not logged)");
    var batch = await converter.ConvertLinesAsync(sourceLines, LyricTextMode.Latin, RomanizationLanguage.Automatic);
    Check(batch.All(text => !text.Any(c => c is >= '\uAC00' and <= '\uD7A3')), "real cached song converts as one batch with no remaining Hangul (lyrics not logged)");
}

sealed class DeferredProvider : ILyricsProvider
{
    public Dictionary<string, TaskCompletionSource<LyricsDocument?>> Requests { get; } = [];
    public Dictionary<string, CancellationToken> Tokens { get; } = [];
    public Task<LyricsDocument?> GetAsync(Track track, CancellationToken cancellationToken)
    {
        Tokens[track.Title] = cancellationToken;
        var source = new TaskCompletionSource<LyricsDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Requests[track.Title] = source;
        return source.Task; // Intentionally ignores cancellation, to simulate an uncooperative provider.
    }
}

sealed class FakeHttpHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public Uri? LastUri { get; private set; }
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public int? RetryAfterSeconds { get; set; }
    public string SearchJson { get; set; } = "[]";
    public HttpStatusCode SearchStatus { get; set; } = HttpStatusCode.OK;
    public string Json { get; set; } = "{\"plainLyrics\":\"hello\",\"syncedLyrics\":\"[00:01.00]hello\",\"instrumental\":false}";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; LastUri = request.RequestUri;
        bool search = request.RequestUri!.AbsolutePath.EndsWith("/search");
        var response = new HttpResponseMessage(search ? SearchStatus : Status) { Content = new StringContent(search ? SearchJson : Json) };
        if (RetryAfterSeconds is { } seconds) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(seconds));
        return Task.FromResult(response);
    }
}

sealed class ScriptedProvider(Func<Track, int, LyricsDocument?> get) : ILyricsProvider
{
    public int Calls { get; private set; }
    public Task<LyricsDocument?> GetAsync(Track track, CancellationToken cancellationToken) => Task.FromResult(get(track, ++Calls));
}
