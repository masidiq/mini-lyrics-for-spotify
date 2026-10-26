using System.Net;
using MiniLyrics.Core;

int count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name); count++;
}

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
    handler.Status = HttpStatusCode.ServiceUnavailable;
    using var controller = new LyricsController(provider);
    await controller.LoadAsync(new("offline", "singer", "", 100));
    Check(controller.State.Status == LyricsStatus.Error, "network/server failure becomes retryable state");
    handler.Status = HttpStatusCode.OK;
    handler.Json = "{\"instrumental\":true}";
    await controller.LoadAsync(new("instrumental", "singer", "", 100));
    Check(controller.State.Status == LyricsStatus.Instrumental, "instrumental state");
    handler.Json = "{\"plainLyrics\":\"plain text\"}";
    await controller.LoadAsync(new("plain", "singer", "", 100));
    Check(controller.State.Status == LyricsStatus.Ready && controller.State.Document?.SyncedLyrics is null, "plain lyric fallback");
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
Console.WriteLine($"All {count} checks passed.");

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
    public string Json { get; set; } = "{\"plainLyrics\":\"hello\",\"syncedLyrics\":\"[00:01.00]hello\",\"instrumental\":false}";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; LastUri = request.RequestUri;
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Json) });
    }
}
