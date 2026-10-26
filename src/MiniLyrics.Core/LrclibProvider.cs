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
        try
        {
            if (File.Exists(cachePath))
            {
                var cached = JsonSerializer.Deserialize<LyricsDocument>(await File.ReadAllTextAsync(cachePath, cancellationToken), JsonOptions);
                if (cached is not null) return cached;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

        var query = $"api/get?track_name={Uri.EscapeDataString(track.Title)}&artist_name={Uri.EscapeDataString(track.Artist)}";
        if (!string.IsNullOrWhiteSpace(track.Album)) query += "&album_name=" + Uri.EscapeDataString(track.Album);
        if (track.DurationSeconds > 0) query += "&duration=" + track.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        using var response = await http.GetAsync(query, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var document = JsonSerializer.Deserialize<LyricsDocument>(await response.Content.ReadAsStringAsync(cancellationToken), JsonOptions);
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
}
