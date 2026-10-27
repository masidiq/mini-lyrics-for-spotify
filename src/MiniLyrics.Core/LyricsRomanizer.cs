using System.Text;
using System.Text.RegularExpressions;
using Kawazu;

namespace MiniLyrics.Core;

public enum LyricTextMode { Original, Latin, LatinAndOriginal }
public enum RomanizationLanguage { Automatic, Korean, Japanese }

public sealed partial class LyricsRomanizer : IAsyncDisposable
{
    // The Korean library also caches syllables statically; serialize its calls across instances.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Dictionary<(string, RomanizationLanguage, bool), string> cache = [];
    private readonly Romanization.Korean.RevisedRomanization korean = new();
    private KawazuConverter? japanese;
    private bool disposed;

    [GeneratedRegex(@"[\uAC00-\uD7A3]+")]
    private static partial Regex Hangul();
    [GeneratedRegex(@"[\u3040-\u30FF\u31F0-\u31FF\u3400-\u4DBF\u4E00-\u9FFF\uFF66-\uFF9F\u3000-\u303F]+")]
    private static partial Regex JapaneseText();

    public static bool HasKana(string text) => text.Any(c => c is >= '\u3040' and <= '\u30FF' or >= '\uFF66' and <= '\uFF9F');

    public Task<IReadOnlyList<string>> ConvertLinesAsync(IReadOnlyList<string> originals, LyricTextMode mode,
        RomanizationLanguage language, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mode == LyricTextMode.Original) return (IReadOnlyList<string>)originals.ToArray();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bool japaneseContext = originals.Any(HasKana);
            var result = new string[originals.Count];
            for (int i = 0; i < originals.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var original = originals[i];
                var key = (original, language, japaneseContext);
                if (!cache.TryGetValue(key, out var latin))
                {
                    latin = original.Normalize(NormalizationForm.FormC);
                    if (language is RomanizationLanguage.Automatic or RomanizationLanguage.Korean)
                        latin = Hangul().Replace(latin, match => korean.Process(KoreanPronunciation.ExpandCompoundFinals(match.Value)));
                    if (language == RomanizationLanguage.Japanese || language == RomanizationLanguage.Automatic && japaneseContext)
                    {
                        var builder = new StringBuilder();
                        int end = 0;
                        foreach (Match match in JapaneseText().Matches(latin))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            builder.Append(latin.AsSpan(end, match.Index - end));
                            // IncludeAllContentForSelfExtract keeps this dictionary beside the extracted DLL in single-file builds.
                            var assemblyDirectory = Path.GetDirectoryName(typeof(KawazuConverter).Assembly.Location);
                            japanese ??= new KawazuConverter(Path.Combine(string.IsNullOrEmpty(assemblyDirectory) ? AppContext.BaseDirectory : assemblyDirectory, "IpaDic"));
                            var divisions = await japanese.GetDivisions(match.Value, To.Romaji, Mode.Spaced, RomajiSystem.Hepburn);
                            var words = new List<string>();
                            bool doubledConsonant = false;
                            foreach (var division in divisions)
                            {
                                bool endsInTsu = division.IsEndsInTsu;
                                if (endsInTsu) division.RemoveAt(division.Count - 1);
                                // Pronunciation gives wa/e for the particles は/へ, rather than their written ha/he.
                                var word = division.RomaPronunciation;
                                if (doubledConsonant && word.Length > 0 && words.Count > 0) words[^1] += word[0] + word;
                                else if (word.Length > 0 || endsInTsu) words.Add(word);
                                doubledConsonant = endsInTsu;
                            }
                            builder.Append(string.Join(" ", words).Trim());
                            end = match.Index + match.Length;
                        }
                        builder.Append(latin.AsSpan(end));
                        latin = builder.ToString();
                    }
                    if (cache.Count >= 1024) cache.Clear();
                    cache[key] = latin;
                }
                result[i] = mode == LyricTextMode.LatinAndOriginal && latin != original ? $"{latin}\n{original}" : latin;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return (IReadOnlyList<string>)result;
        }
        finally { Gate.Release(); }
    }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Gate.WaitAsync();
        try { if (!disposed) { disposed = true; japanese?.Dispose(); cache.Clear(); } }
        finally { Gate.Release(); }
    }
}
