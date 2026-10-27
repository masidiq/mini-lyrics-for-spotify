namespace MiniLyrics.Core;

internal static class KoreanPronunciation
{
    // Expand compound batchim into simple final/initial consonants supported by Romanization.NET.
    // Hangul syllables encode (initial * 21 + vowel) * 28 + final, starting at U+AC00.
    public static string ExpandCompoundFinals(string word)
    {
        var syllables = word.ToCharArray();
        for (int i = 0; i < syllables.Length; i++)
        {
            int code = syllables[i] - 0xAC00;
            if (code is < 0 or > 11171) continue;
            int final = code % 28;
            if (final is not (3 or 5 or 6 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 18)) continue;
            bool hasNext = i + 1 < syllables.Length && syllables[i + 1] is >= '\uAC00' and <= '\uD7A3';
            int nextInitial = hasNext ? (syllables[i + 1] - 0xAC00) / 588 : -1;
            int retainedFinal;
            if (nextInitial == 11) // Before vowel-initial syllables: liaison, dropping ㅎ from ㄶ/ㅀ.
            {
                (retainedFinal, int initial) = final switch
                {
                    3 => (1, 9), 5 => (4, 12), 6 => (4, 11),
                    9 => (8, 0), 10 => (8, 6), 11 => (8, 7),
                    12 => (8, 9), 13 => (8, 16), 14 => (8, 17),
                    15 => (8, 11), 18 => (17, 9), _ => throw new InvalidOperationException()
                };
                syllables[i + 1] = (char)(0xAC00 + initial * 588 + (syllables[i + 1] - 0xAC00) % 588);
            }
            else
            {
                retainedFinal = final switch
                {
                    3 => 1, 5 or 6 => 4, 9 => nextInitial == 0 ? 8 : 1,
                    10 => 16, 11 => syllables[i] == '밟' ? 17 : 8,
                    12 or 13 or 15 => 8, 14 or 18 => 17, _ => throw new InvalidOperationException()
                };
                if (hasNext && final is 6 or 15)
                {
                    // ㄶ/ㅀ aspirate a following ㄱ/ㄷ/ㅈ to ㅋ/ㅌ/ㅊ.
                    int aspirated = nextInitial switch { 0 => 15, 3 => 16, 12 => 14, _ => nextInitial };
                    syllables[i + 1] = (char)(0xAC00 + aspirated * 588 + (syllables[i + 1] - 0xAC00) % 588);
                }
            }
            syllables[i] = (char)(0xAC00 + code - final + retainedFinal);
        }
        return new string(syllables);
    }
}
