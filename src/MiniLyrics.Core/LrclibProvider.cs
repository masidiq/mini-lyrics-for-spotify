using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MiniLyrics.Core;

public sealed class LrclibProvider(HttpClient http, string cacheDirectory) : ILyricsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<LyricsDocument?> GetAsync(Track track, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(track.Key)));
        var cachePath = Path.Combine(cacheDirectory, hash + ".json");
        LyricsDocument? document = null;
        try
        {
            if (File.Exists(cachePath))
            {
                document = JsonSerializer.Deserialize<LyricsDocument>(await File.ReadAllTextAsync(cachePath, cancellationToken), JsonOptions);
                if (document is not null && (document.Instrumental || HasTimedLyrics(document)
                    || document.SyncedSearchCheckedAt > DateTimeOffset.UtcNow.AddDays(-1))) return document;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

        if (document is null)
        {
            var query = $"api/get?track_name={Uri.EscapeDataString(track.Title)}&artist_name={Uri.EscapeDataString(track.Artist)}";
            if (!string.IsNullOrWhiteSpace(track.Album)) query += "&album_name=" + Uri.EscapeDataString(track.Album);
            if (track.DurationSeconds > 0) query += "&duration=" + track.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            using var response = await http.GetAsync(query, cancellationToken);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                CheckResponse(response);
                document = JsonSerializer.Deserialize<LyricsDocument>(await response.Content.ReadAsStringAsync(cancellationToken), JsonOptions);
            }
        }
        if (document?.Instrumental != true && (document is null || !HasTimedLyrics(document)) && track.DurationSeconds > 0)
        {
            try
            {
                using var search = await http.GetAsync($"api/search?track_name={Uri.EscapeDataString(track.Title)}&artist_name={Uri.EscapeDataString(track.Artist)}", cancellationToken);
                SearchRecord[] records = [];
                if (search.StatusCode != HttpStatusCode.NotFound)
                {
                    CheckResponse(search);
                    records = JsonSerializer.Deserialize<SearchRecord[]>(await search.Content.ReadAsStringAsync(cancellationToken), JsonOptions) ?? [];
                }
                var match = records.Where(record => Normalize(record.TrackName) == Normalize(track.Title)
                        && Normalize(record.ArtistName) == Normalize(track.Artist)
                        && double.IsFinite(record.Duration) && Math.Abs(record.Duration - track.DurationSeconds) <= 2
                        && !record.Instrumental && HasTimedLyrics(new(record.PlainLyrics, record.SyncedLyrics, false))
                        && LrcParser.Parse(record.SyncedLyrics)[^1].At.TotalSeconds <= track.DurationSeconds + 5)
                    .OrderByDescending(record => Normalize(record.AlbumName) == Normalize(track.Album))
                    .ThenBy(record => Math.Abs(record.Duration - track.DurationSeconds)).ThenBy(record => record.Id).FirstOrDefault();
                if (match is not null) document = new(match.PlainLyrics ?? document?.PlainLyrics, match.SyncedLyrics, false);
                if (document is not null) document = document with { SyncedSearchCheckedAt = DateTimeOffset.UtcNow };
            }
            catch (Exception ex) when (document is not null && !cancellationToken.IsCancellationRequested
                && ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
            {
                // Keep usable plain lyrics offline; retry the timed search on the next load.
            }
        }
        if (document is not null)
        {
            try
            {
                Directory.CreateDirectory(cacheDirectory);
                var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(document), cancellationToken);
                    File.Move(temporary, cachePath, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return document;
    }

    private static bool HasTimedLyrics(LyricsDocument document) => LrcParser.Parse(document.SyncedLyrics).Any(line => LrcParser.IsSungLine(line.Text));
    private static string Normalize(string? text) => string.Join(" ", (text ?? "").Normalize(NormalizationForm.FormKC)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static void CheckResponse(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var retryAfter = response.Headers.RetryAfter;
        var wait = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
        throw new LyricsRequestException(response.StatusCode, wait);
    }
    private sealed record SearchRecord(long Id, string TrackName, string ArtistName, string AlbumName, double Duration,
        bool Instrumental, string? PlainLyrics, string? SyncedLyrics);
}
