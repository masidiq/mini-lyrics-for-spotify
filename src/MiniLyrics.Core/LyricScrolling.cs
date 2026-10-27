namespace MiniLyrics.Core;

public enum LyricScrollMode { PerLine, Continuous }
public sealed record ScrollAnchor(int From, int To, double Progress);

public static class LyricScrolling
{
    public static ScrollAnchor GetAnchor(IReadOnlyList<LyricLine> lines, TimeSpan position, double correctionSeconds)
    {
        var active = LrcParser.ActiveIndex(lines, position, correctionSeconds);
        var from = LrcParser.DisplayIndex(lines, active);
        if (from < 0 || from != active) return new(from, from, 0); // Hold the upcoming first line during the intro.
        int next = from + 1;
        while (next < lines.Count && (!LrcParser.IsSungLine(lines[next].Text) || lines[next].At <= lines[from].At)) next++;
        if (next == lines.Count) return new(from, from, 0);
        var end = lines.Skip(from + 1).First(line => line.At > lines[from].At).At;
        var adjusted = position + TimeSpan.FromSeconds(Math.Clamp(correctionSeconds, -10, 10));
        var progress = Math.Clamp((adjusted - lines[from].At).TotalMilliseconds / (end - lines[from].At).TotalMilliseconds, 0, 1);
        return new(from, next, progress);
    }

    public static double Interpolate(double from, double to, double progress) => from + (to - from) * Math.Clamp(progress, 0, 1);
}
