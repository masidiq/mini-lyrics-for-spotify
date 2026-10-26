using System.Globalization;
using System.Text.RegularExpressions;

namespace MiniLyrics.Core;

public sealed record Track(string Title, string Artist, string Album, double DurationSeconds)
{
    public string Key => $"{Title.Trim()}\n{Artist.Trim()}\n{Album.Trim()}\n{Math.Round(DurationSeconds)}";
}

public sealed record LyricLine(TimeSpan At, string Text);
public sealed record LyricsDocument(string? PlainLyrics, string? SyncedLyrics, bool Instrumental);
public enum LyricsStatus { Waiting, Loading, Ready, NotFound, Instrumental, Error }
public sealed record LyricsState(Track? Track, LyricsStatus Status, LyricsDocument? Document = null);

public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d+):([0-5]\d)(?:[\.:](\d{1,3}))?\]")]
    private static partial Regex Timestamp();
    [GeneratedRegex(@"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex Offset();

    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return [];
        var offsetMatch = Offset().Match(lrc);
        var offsetMs = offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, out var parsed) ? parsed : 0;
        var result = new List<LyricLine>();
        foreach (var row in lrc.Split('\n'))
        {
            var matches = Timestamp().Matches(row);
            if (matches.Count == 0) continue;
            var text = row[(matches[^1].Index + matches[^1].Length)..].Trim();
            foreach (Match match in matches)
            {
                if (!double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var minutes) || minutes > 1_000_000) continue;
                var seconds = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var fraction = match.Groups[3].Success
                    ? double.Parse("0." + match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                result.Add(new LyricLine(TimeSpan.FromSeconds(minutes * 60 + seconds + fraction)
                    + TimeSpan.FromMilliseconds(offsetMs), text));
            }
        }
        return result.OrderBy(line => line.At).ToArray();
    }

    // Positive correction advances the lyrics; negative correction delays them.
    public static int ActiveIndex(IReadOnlyList<LyricLine> lines, TimeSpan position, double correctionSeconds)
    {
        var target = position + TimeSpan.FromSeconds(Math.Clamp(correctionSeconds, -10, 10));
        int low = 0, high = lines.Count - 1, found = -1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (lines[middle].At <= target) { found = middle; low = middle + 1; }
            else high = middle - 1;
        }
        return found;
    }
}

public interface ILyricsProvider
{
    Task<LyricsDocument?> GetAsync(Track track, CancellationToken cancellationToken);
}

public sealed class LyricsController(ILyricsProvider provider) : IDisposable
{
    private CancellationTokenSource? pending;
    private long generation;
    public LyricsState State { get; private set; } = new(null, LyricsStatus.Waiting);
    public event Action<LyricsState>? Changed;

    private void Set(LyricsState state) { State = state; Changed?.Invoke(state); }

    public async Task LoadAsync(Track? track)
    {
        var request = Interlocked.Increment(ref generation);
        var previous = pending;
        pending = null;
        previous?.Cancel();
        if (track is null) { Set(new(null, LyricsStatus.Waiting)); return; }
        using var cancellation = new CancellationTokenSource();
        pending = cancellation;
        Set(new(track, LyricsStatus.Loading));
        try
        {
            var document = await provider.GetAsync(track, cancellation.Token);
            if (request != generation || cancellation.IsCancellationRequested) return;
            var status = document?.Instrumental == true ? LyricsStatus.Instrumental
                : document is null || (string.IsNullOrWhiteSpace(document.PlainLyrics) && string.IsNullOrWhiteSpace(document.SyncedLyrics))
                    ? LyricsStatus.NotFound : LyricsStatus.Ready;
            Set(new(track, status, document));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException)
        {
            if (request == generation && !cancellation.IsCancellationRequested) Set(new(track, LyricsStatus.Error));
        }
        finally { if (ReferenceEquals(pending, cancellation)) pending = null; }
    }

    public void Dispose()
    {
        Interlocked.Increment(ref generation);
        pending?.Cancel();
        pending = null;
    }
}
