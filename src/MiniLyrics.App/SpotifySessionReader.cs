using System;
using System.Linq;
using System.Threading.Tasks;
using MiniLyrics.Core;
using Windows.Media.Control;

namespace MiniLyrics.App;

public sealed record PlaybackSnapshot(Track Track, TimeSpan Position, DateTimeOffset UpdatedAt,
    bool IsPlaying, double PlaybackRate, string Source);

public sealed class SpotifySessionReader
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    public static bool IsSpotify(string id) =>
        string.Equals(id, "Spotify.exe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, "Spotify", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("SpotifyAB.SpotifyMusic_", StringComparison.OrdinalIgnoreCase);

    public async Task<PlaybackSnapshot?> ReadAsync()
    {
        manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var sessions = manager.GetSessions().Where(s => IsSpotify(s.SourceAppUserModelId)).ToArray();
        var session = sessions.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            ?? sessions.FirstOrDefault();
        if (session is null) return null;
        var media = await session.TryGetMediaPropertiesAsync();
        if (string.IsNullOrWhiteSpace(media.Title) || string.IsNullOrWhiteSpace(media.Artist)) return null;
        var timeline = session.GetTimelineProperties();
        var playback = session.GetPlaybackInfo();
        return new(new Track(media.Title, media.Artist, media.AlbumTitle, Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds)),
            timeline.Position - timeline.StartTime, timeline.LastUpdatedTime,
            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            playback.PlaybackRate ?? 1, session.SourceAppUserModelId);
    }
}
