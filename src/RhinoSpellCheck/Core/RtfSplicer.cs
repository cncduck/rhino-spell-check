using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RhinoSpellCheck.Core
{
    /// <summary>
    /// Minimal RTF reader that maps every plain-text character back to the byte
    /// range that produced it, so a single word can be swapped out without
    /// disturbing the surrounding formatting.
    ///
    /// Rhino stores annotation text as RTF and exposes the flattened form through
    /// <c>PlainTextWithFields</c>. Rewriting that flattened string and assigning it
    /// back would discard every bold/italic/font run in the annotation, so instead
    /// we splice the replacement directly into the RTF at the mapped offsets.
    /// </summary>
    public static class RtfSplicer
    {
        /// <summary>Where a single plain-text character came from in the RTF source.</summary>
        public struct CharSpan
        {
            public int RtfStart;
            public int RtfEnd; // exclusive
        }

        public sealed class PlainMap
        {
            public string Plain;
            public List<CharSpan> Spans;
        }

        /// <summary>Control words whose entire group carries no displayable text.</summary>
        private static readonly HashSet<string> DestinationGroups = new HashSet<string>(StringComparer.Ordinal)
        {
            "fonttbl", "colortbl", "stylesheet", "info", "pict", "filetbl",
            "listtable", "listoverridetable", "generator", "header", "footer",
            "headerl", "headerr", "headerf", "footerl", "footerr", "footerf",
            "footnote", "xmlnstbl", "themedata", "colorschememapping",
            "datastore", "latentstyles", "rsidtbl", "upr", "revtbl"
        };

        /// <summary>Control words that emit a literal character.</summary>
        private static readonly Dictionary<string, char> LiteralControlWords = new Dictionary<string, char>(StringComparer.Ordinal)
        {
            { "par", '\n' }, { "line", '\n' }, { "sect", '\n' }, { "page", '\n' },
            { "tab", '\t' },
            { "emdash", '—' }, { "endash", '–' },
            { "lquote", '‘' }, { "rquote", '’' },
            { "ldblquote", '“' }, { "rdblquote", '”' },
            { "bullet", '•' }, { "enspace", ' ' }, { "emspace", ' ' }
        };

        /// <summary>
        /// Replaces the plain-text range [<paramref name="plainStart"/>,
        /// <paramref name="plainLength"/>) with <paramref name="replacement"/>.
        /// Fails rather than guessing if the text at that offset is not
        /// <paramref name="expectedWord"/>, which keeps a stale scan from
        /// corrupting an annotation the user edited in the meantime.
        /// </summary>
        public static bool TryReplaceRange(
            string rtf,
            int plainStart,
            int plainLength,
            string expectedWord,
            string replacement,
            out string result)
        {
            result = null;

            if (string.IsNullOrEmpty(rtf) || plainStart < 0 || plainLength <= 0)
                return false;

            PlainMap map = Parse(rtf);
            if (plainStart + plainLength > map.Plain.Length)
                return false;

            if (!string.Equals(map.Plain.Substring(plainStart, plainLength), expectedWord, StringComparison.Ordinal))
                return false;

            int last = plainStart + plainLength - 1;
            var sb = new StringBuilder(rtf.Length + replacement.Length);

            sb.Append(rtf, 0, map.Spans[plainStart].RtfStart);
            sb.Append(Escape(replacement));

            // A word can straddle two formatting runs. Copy whatever RTF sat
            // between the characters we are deleting so the run structure and
            // brace balance survive.
            for (int i = plainStart; i < last; i++)
            {
                int gapStart = map.Spans[i].RtfEnd;
                int gapEnd = map.Spans[i + 1].RtfStart;
                if (gapEnd > gapStart)
                    sb.Append(rtf, gapStart, gapEnd - gapStart);
            }

            int tail = map.Spans[last].RtfEnd;
            sb.Append(rtf, tail, rtf.Length - tail);

            result = sb.ToString();
            return true;
        }

        /// <summary>Flattens RTF to plain text using the same rules as <see cref="Parse"/>.</summary>
        public static string GetPlainText(string rtf)
        {
            return string.IsNullOrEmpty(rtf) ? string.Empty : Parse(rtf).Plain;
        }

        /// <summary>
        /// Translates an offset measured against Rhino's own flattened text into an
        /// offset in <paramref name="plain"/>.
        ///
        /// Our reader and Rhino's need not agree character-for-character on every
        /// exotic control word, and a silent one-character drift would rewrite the
        /// wrong word. When the two flattenings differ we re-locate the word by
        /// searching for the whole-word occurrence closest to the expected offset,
        /// and give up rather than guess if there is none.
        /// </summary>
        /// <returns>The aligned offset, or -1 if the word cannot be located.</returns>
        public static int AlignOffset(string plain, string referencePlain, int referenceStart, string word)
        {
            if (string.IsNullOrEmpty(plain) || string.IsNullOrEmpty(word))
                return -1;

            // Fast path: the two readers agree, so the offset transfers directly.
            if (referencePlain != null &&
                string.Equals(plain, referencePlain, StringComparison.Ordinal) &&
                referenceStart >= 0 &&
                referenceStart + word.Length <= plain.Length &&
                string.Equals(plain.Substring(referenceStart, word.Length), word, StringComparison.Ordinal))
            {
                return referenceStart;
            }

            int best = -1;
            int bestDistance = int.MaxValue;

            for (int i = plain.IndexOf(word, StringComparison.Ordinal); i >= 0;
                 i = i + 1 <= plain.Length - word.Length ? plain.IndexOf(word, i + 1, StringComparison.Ordinal) : -1)
            {
                if (!IsWholeWordAt(plain, i, word.Length))
                    continue;

                int distance = Math.Abs(i - referenceStart);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        private static bool IsWholeWordAt(string text, int start, int length)
        {
            if (start > 0 && char.IsLetter(text[start - 1]))
                return false;

            int end = start + length;
            return end >= text.Length || !char.IsLetter(text[end]);
        }

        public static PlainMap Parse(string rtf)
        {
            var plain = new StringBuilder();
            var spans = new List<CharSpan>();

            int i = 0;
            int depth = 0;
            int skipDepth = -1;   // group depth whose contents are being discarded
            int unicodeSkip = 1;  // current \ucN value

            while (i < rtf.Length)
            {
                char c = rtf[i];

                if (c == '{')
                {
                    depth++;
                    i++;
                    continue;
                }

                if (c == '}')
                {
                    if (skipDepth >= 0 && depth <= skipDepth)
                        skipDepth = -1;
                    depth--;
                    i++;
                    continue;
                }

                if (c != '\\')
                {
                    // Literal newlines in the RTF source are formatting, not text.
                    if (skipDepth < 0 && c != '\r' && c != '\n')
                        Emit(plain, spans, c, i, i + 1);
                    i++;
                    continue;
                }

                if (i + 1 >= rtf.Length)
                    break;

                char next = rtf[i + 1];

                if (IsAsciiLetter(next))
                {
                    i = ReadControlWord(rtf, i, plain, spans, ref skipDepth, ref unicodeSkip, depth);
                    continue;
                }

                i = ReadControlSymbol(rtf, i, plain, spans, ref skipDepth, depth);
            }

            return new PlainMap { Plain = plain.ToString(), Spans = spans };
        }

        private static int ReadControlWord(
            string rtf,
            int start,
            StringBuilder plain,
            List<CharSpan> spans,
            ref int skipDepth,
            ref int unicodeSkip,
            int depth)
        {
            int j = start + 1;
            while (j < rtf.Length && IsAsciiLetter(rtf[j]))
                j++;

            string word = rtf.Substring(start + 1, j - start - 1);

            bool negative = false;
            if (j < rtf.Length && rtf[j] == '-')
            {
                negative = true;
                j++;
            }

            int digitsStart = j;
            while (j < rtf.Length && rtf[j] >= '0' && rtf[j] <= '9')
                j++;

            int? parameter = null;
            if (j > digitsStart)
            {
                int value;
                if (int.TryParse(rtf.Substring(digitsStart, j - digitsStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    parameter = negative ? -value : value;
            }

            // A single trailing space delimits the control word and is not text.
            int end = j;
            if (end < rtf.Length && rtf[end] == ' ')
                end++;

            if (DestinationGroups.Contains(word))
            {
                if (skipDepth < 0)
                    skipDepth = depth;
                return end;
            }

            if (skipDepth >= 0)
                return end;

            if (word == "uc")
            {
                unicodeSkip = parameter.HasValue && parameter.Value >= 0 ? parameter.Value : 1;
                return end;
            }

            if (word == "u")
            {
                int code = parameter.GetValueOrDefault();
                if (code < 0)
                    code += 0x10000; // writers emit >32767 as a negative signed short

                // The escape plus its ANSI fallback characters together represent
                // one plain character, so the span must cover all of it.
                int afterFallback = SkipUnicodeFallback(rtf, end, unicodeSkip);
                if (code > 0 && code <= 0xFFFF)
                    Emit(plain, spans, (char)code, start, afterFallback);
                return afterFallback;
            }

            char literal;
            if (LiteralControlWords.TryGetValue(word, out literal))
            {
                Emit(plain, spans, literal, start, end);
                return end;
            }

            return end;
        }

        private static int ReadControlSymbol(
            string rtf,
            int start,
            StringBuilder plain,
            List<CharSpan> spans,
            ref int skipDepth,
            int depth)
        {
            char next = rtf[start + 1];

            switch (next)
            {
                case '\\':
                case '{':
                case '}':
                    if (skipDepth < 0)
                        Emit(plain, spans, next, start, start + 2);
                    return start + 2;

                case '\'':
                    {
                        int end = Math.Min(start + 4, rtf.Length);
                        if (end == start + 4)
                        {
                            int value;
                            if (int.TryParse(rtf.Substring(start + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                            {
                                if (skipDepth < 0)
                                    Emit(plain, spans, DecodeCp1252((byte)value), start, end);
                                return end;
                            }
                        }
                        return end;
                    }

                case '~':
                    if (skipDepth < 0)
                        Emit(plain, spans, ' ', start, start + 2);
                    return start + 2;

                case '_':
                    if (skipDepth < 0)
                        Emit(plain, spans, '‑', start, start + 2);
                    return start + 2;

                case '*':
                    // \* marks an ignorable destination: drop the whole group.
                    if (skipDepth < 0)
                        skipDepth = depth;
                    return start + 2;

                case '\r':
                case '\n':
                    if (skipDepth < 0)
                        Emit(plain, spans, '\n', start, start + 2);
                    return start + 2;

                default:
                    // \- (optional hyphen) and anything else contribute no text.
                    return start + 2;
            }
        }

        /// <summary>
        /// Steps over the ANSI fallback that follows a <c>\uN</c> escape. Each unit
        /// is either a <c>\'hh</c> escape, another control sequence, or one literal
        /// character.
        /// </summary>
        private static int SkipUnicodeFallback(string rtf, int index, int count)
        {
            for (int n = 0; n < count && index < rtf.Length; n++)
            {
                if (rtf[index] == '{' || rtf[index] == '}')
                    break;

                if (rtf[index] == '\\')
                {
                    if (index + 1 < rtf.Length && rtf[index + 1] == '\'')
                        index = Math.Min(index + 4, rtf.Length);
                    else
                        index = Math.Min(index + 2, rtf.Length);
                }
                else
                {
                    index++;
                }
            }
            return index;
        }

        private static void Emit(StringBuilder plain, List<CharSpan> spans, char c, int rtfStart, int rtfEnd)
        {
            plain.Append(c);
            spans.Add(new CharSpan { RtfStart = rtfStart, RtfEnd = rtfEnd });
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '{': sb.Append("\\{"); break;
                    case '}': sb.Append("\\}"); break;
                    case '\n': sb.Append("\\par "); break;
                    case '\t': sb.Append("\\tab "); break;
                    case '\r': break;
                    default:
                        if (c < 0x80)
                            sb.Append(c);
                        else
                            sb.Append("\\u").Append(((int)c).ToString(CultureInfo.InvariantCulture)).Append('?');
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Windows-1252 differs from Latin-1 only in 0x80-0x9F. Hard-coding that
        /// window avoids depending on System.Text.Encoding.CodePages, which is not
        /// registered by default outside .NET Framework.
        /// </summary>
        private static readonly char[] Cp1252High =
        {
            '€', '', '‚', 'ƒ', '„', '…', '†', '‡',
            'ˆ', '‰', 'Š', '‹', 'Œ', '', 'Ž', '',
            '', '‘', '’', '“', '”', '•', '–', '—',
            '˜', '™', 'š', '›', 'œ', '', 'ž', 'Ÿ'
        };

        private static char DecodeCp1252(byte value)
        {
            if (value >= 0x80 && value <= 0x9F)
                return Cp1252High[value - 0x80];
            return (char)value;
        }
    }
}
