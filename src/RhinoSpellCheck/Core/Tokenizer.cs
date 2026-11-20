using System;
using System.Collections.Generic;

namespace RhinoSpellCheck.Core
{
    /// <summary>
    /// Splits annotation text into checkable words while stepping around the
    /// things that appear in CAD text but are not prose: Rhino text fields,
    /// URLs, e-mail addresses and file paths.
    /// </summary>
    public static class Tokenizer
    {
        private struct Range
        {
            public int Start;
            public int End; // exclusive
        }

        public static IReadOnlyList<WordToken> Tokenize(string text, TokenizerOptions options = null)
        {
            var tokens = new List<WordToken>();
            if (string.IsNullOrEmpty(text))
                return tokens;

            if (options == null)
                options = TokenizerOptions.Default;

            var skips = BuildSkipRanges(text);

            int i = 0;
            while (i < text.Length)
            {
                int skipEnd = SkipRangeEndAt(skips, i);
                if (skipEnd > i)
                {
                    i = skipEnd;
                    continue;
                }

                if (!char.IsLetter(text[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                i++;
                while (i < text.Length && IsWordContinuation(text, i))
                    i++;

                // An apostrophe is only part of the word when letters follow it,
                // so a possessive plural ("designers'") loses its trailing mark.
                int end = i;
                while (end > start && IsApostrophe(text[end - 1]))
                    end--;

                if (end <= start)
                    continue;

                if (OverlapsSkipRange(skips, start, end))
                    continue;

                string word = text.Substring(start, end - start);
                if (ShouldCheck(text, start, end, word, options))
                    tokens.Add(new WordToken(word, start));
            }

            return tokens;
        }

        private static bool IsWordContinuation(string text, int index)
        {
            char c = text[index];
            if (char.IsLetter(c))
                return true;

            // Keep an apostrophe only when it sits between two letters, so that
            // "don't" survives but a quoted 'word' does not absorb its quotes.
            if (IsApostrophe(c))
                return index + 1 < text.Length && char.IsLetter(text[index + 1]);

            return false;
        }

        private static bool IsApostrophe(char c)
        {
            // U+2019 is what most editors auto-insert; the en_US affix file maps
            // it to a plain apostrophe via ICONV, and so do we.
            return c == '\'' || c == '’' || c == 'ʼ';
        }

        private static bool ShouldCheck(string text, int start, int end, string word, TokenizerOptions options)
        {
            if (word.Length < options.MinimumWordLength)
                return false;

            if (options.IgnoreWordsAdjacentToDigits)
            {
                if (start > 0 && char.IsDigit(text[start - 1]))
                    return false;
                if (end < text.Length && char.IsDigit(text[end]))
                    return false;
            }

            bool hasLower = false;
            bool hasInteriorUpper = false;
            for (int i = 0; i < word.Length; i++)
            {
                char c = word[i];
                if (char.IsLower(c))
                    hasLower = true;
                else if (char.IsUpper(c) && i > 0)
                    hasInteriorUpper = true;
            }

            if (options.IgnoreAllCaps && !hasLower)
                return false;

            if (options.IgnoreMixedCase && hasInteriorUpper && hasLower)
                return false;

            return true;
        }

        /// <summary>
        /// Regions that must never be reported: Rhino text fields such as
        /// <c>%&lt;ModelUnits&gt;%</c>, plus whitespace-delimited chunks that look
        /// like a URL, an e-mail address or a file path.
        /// </summary>
        private static List<Range> BuildSkipRanges(string text)
        {
            var ranges = new List<Range>();

            // Rhino text fields. These survive in PlainTextWithFields and editing
            // inside one would corrupt the field expression.
            int search = 0;
            while (search < text.Length)
            {
                int open = text.IndexOf("%<", search, StringComparison.Ordinal);
                if (open < 0)
                    break;

                int close = text.IndexOf(">%", open + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    // Unterminated field: skip to end rather than risk editing it.
                    ranges.Add(new Range { Start = open, End = text.Length });
                    break;
                }

                ranges.Add(new Range { Start = open, End = close + 2 });
                search = close + 2;
            }

            // Whitespace-delimited chunks that are clearly not prose.
            int chunkStart = -1;
            for (int i = 0; i <= text.Length; i++)
            {
                bool atEnd = i == text.Length;
                bool isSpace = atEnd || char.IsWhiteSpace(text[i]);

                if (!isSpace && chunkStart < 0)
                {
                    chunkStart = i;
                }
                else if (isSpace && chunkStart >= 0)
                {
                    string chunk = text.Substring(chunkStart, i - chunkStart);
                    if (LooksLikeNonProse(chunk))
                        ranges.Add(new Range { Start = chunkStart, End = i });
                    chunkStart = -1;
                }
            }

            ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
            return ranges;
        }

        private static bool LooksLikeNonProse(string chunk)
        {
            if (chunk.IndexOf("://", StringComparison.Ordinal) >= 0)
                return true;

            if (chunk.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                return true;

            // Path-ish: a drive letter, a UNC prefix, or any backslash separator.
            if (chunk.IndexOf(":\\", StringComparison.Ordinal) >= 0 || chunk.StartsWith("\\\\", StringComparison.Ordinal))
                return true;

            // E-mail: an "@" with a dotted domain after it.
            int at = chunk.IndexOf('@');
            if (at > 0 && at < chunk.Length - 1 && chunk.IndexOf('.', at) > at)
                return true;

            return false;
        }

        private static int SkipRangeEndAt(List<Range> ranges, int index)
        {
            for (int i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].Start > index)
                    break;
                if (index >= ranges[i].Start && index < ranges[i].End)
                    return ranges[i].End;
            }
            return index;
        }

        private static bool OverlapsSkipRange(List<Range> ranges, int start, int end)
        {
            for (int i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].Start >= end)
                    break;
                if (ranges[i].End > start)
                    return true;
            }
            return false;
        }
    }
}
