# Privacy Policy — Mini Lyrics for Spotify

Last updated: October 29, 2026

Mini Lyrics for Spotify is a Windows desktop application published by Sidiq. This policy describes information handled by the application.

## Information accessed and sent

The application reads the title, artist, album, and playback duration exposed by the Spotify desktop app through Windows Media Sessions so it can find and display matching lyrics. When a lyrics lookup is needed, those track details are sent over HTTPS to LRCLIB (`lrclib.net`). The application may also request a matching timed-lyrics result from that service. LRCLIB is a separate service and its own privacy practices apply to information it receives. See [LRCLIB](https://lrclib.net/) for information about that service.

The application does not request a Spotify password, access token, or account login. It does not record or transmit audio, and it does not send playback position. Spotify track metadata is sent only to perform lyrics lookups.

## Information stored on your device

Display preferences and window position are stored in `%LocalAppData%\MiniLyricsForSpotify\settings.json`. Retrieved lyrics are cached under `%LocalAppData%\MiniLyricsForSpotify\cache`. Cache filenames are hashes derived from track identity. If you enable “Start automatically with Spotify,” the application stores a startup entry in the current Windows user's Run registry key. These items remain on your device until you remove them or delete the application data. The application does not provide a built-in cache deletion control.

## Sharing and analytics

The application does not include its own analytics, advertising, or account system and does not sell personal information. Track details for lyrics lookup are shared with LRCLIB as described above. Network providers may process connection data according to their own policies.

## Children's privacy

The application is a general-purpose desktop utility and does not knowingly collect personal information from children.

## Changes and contact

This policy may be updated when the application changes. For privacy questions, contact [ke.sidiq@gmail.com](mailto:ke.sidiq@gmail.com).

Project: https://github.com/masidiq/mini-lyrics-for-spotify
