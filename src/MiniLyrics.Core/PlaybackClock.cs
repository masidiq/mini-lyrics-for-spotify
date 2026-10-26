namespace MiniLyrics.Core;

public sealed class PlaybackClock
{
    private TimeSpan anchor;
    private DateTimeOffset anchoredAt;
    private DateTimeOffset? sourceUpdatedAt;
    private TimeSpan sourcePosition;
    private bool playing;
    private double rate = 1;
    private TimeSpan duration;

    public void Reset() { sourceUpdatedAt = null; anchor = TimeSpan.Zero; playing = false; }

    public void Update(TimeSpan position, DateTimeOffset updatedAt, bool isPlaying, TimeSpan length,
        double playbackRate, DateTimeOffset now)
    {
        var changed = sourceUpdatedAt != updatedAt || sourcePosition != position;
        var current = Position(now);
        duration = length;
        rate = double.IsFinite(playbackRate) && playbackRate > 0 ? playbackRate : 1;
        if (changed)
        {
            // Windows reports Position as of LastUpdatedTime, not as of this poll.
            var elapsed = isPlaying && updatedAt <= now ? now - updatedAt : TimeSpan.Zero;
            anchor = position + elapsed * rate;
        }
        else anchor = current;
        anchoredAt = now;
        sourcePosition = position;
        sourceUpdatedAt = updatedAt;
        playing = isPlaying;
    }

    public TimeSpan Position(DateTimeOffset now)
    {
        var value = anchor + (playing ? (now - anchoredAt) * rate : TimeSpan.Zero);
        return TimeSpan.FromTicks(Math.Clamp(value.Ticks, 0, duration > TimeSpan.Zero ? duration.Ticks : TimeSpan.MaxValue.Ticks));
    }
}
