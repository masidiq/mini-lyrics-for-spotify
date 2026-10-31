namespace MiniLyrics.Core;

// Emit actions only on Spotify transitions, preserving manual tray visibility choices.
public sealed class SpotifyVisibility
{
    private bool? lastRunning;

    public bool? Update(bool enabled, bool running, bool settingsOpen)
    {
        if (!enabled) { lastRunning = null; return null; }
        if (settingsOpen || lastRunning == running) return null;
        lastRunning = running;
        return running;
    }
}
