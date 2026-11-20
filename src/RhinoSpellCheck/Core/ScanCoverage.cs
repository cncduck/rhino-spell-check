using System;
using System.Collections.Generic;
using System.Globalization;

namespace RhinoSpellCheck.Core
{
    /// <summary>
    /// How much of what was scanned actually got spell-checked, and why the rest
    /// did not.
    ///
    /// This exists because the tokenizer's defaults are aggressive enough to be
    /// misleading. On a real production drawing, 3,990 of 4,241 annotations
    /// contained no word the tokenizer would emit - 1,277 of them purely because
    /// they are ALL CAPS, which drawing notes almost always are. With the default
    /// options the checker examined 5.9% of the drawing and reported no
    /// misspellings, which reads as "this drawing is clean" rather than "nine
    /// tenths of it was never looked at".
    ///
    /// Silently checking a fraction is a worse failure than misplacing a squiggle,
    /// so the scan reports its own coverage and names the option that would widen
    /// it.
    /// </summary>
    public sealed class ScanCoverage
    {
        /// <summary>Text items harvested from the document (an annotation, or one label of a text dot).</summary>
        public int TextsFound { get; private set; }

        /// <summary>Text items that produced at least one word to check.</summary>
        public int TextsChecked { get; private set; }

        /// <summary>Words actually handed to the dictionary.</summary>
        public int WordsChecked { get; private set; }

        public int SkippedAllCaps { get; private set; }

        public int SkippedMixedCase { get; private set; }

        public int SkippedAdjacentToDigits { get; private set; }

        public int SkippedTooShort { get; private set; }

        /// <summary>Text with nothing word-like in it under any settings - numbers, symbols, field codes.</summary>
        public int NoWordsAtAll { get; private set; }

        /// <summary>The minimum word length in force during the scan, so the report can name it.</summary>
        public int MinimumWordLength { get; private set; }

        public int TextsSkipped { get { return TextsFound - TextsChecked; } }

        /// <summary>
        /// Fraction of harvested text items that were actually checked, 0 to 1.
        /// </summary>
        public double Fraction
        {
            get { return TextsFound == 0 ? 1.0 : (double)TextsChecked / TextsFound; }
        }

        /// <summary>
        /// True when a setting - rather than the text itself - is what kept a
        /// meaningful share of the drawing from being checked. Text that simply
        /// contains no words is not a blind spot; it is nothing to check.
        /// </summary>
        public bool HasBlindSpot
        {
            get { return SkippedAllCaps + SkippedMixedCase + SkippedAdjacentToDigits + SkippedTooShort > 0; }
        }

        /// <summary>
        /// Records one text item, given how many words the tokenizer emitted for it
        /// under the current options.
        ///
        /// When it emitted none, the options are relaxed one at a time to find out
        /// which one was responsible. That only runs for text that produced
        /// nothing, and tokenizing is far cheaper than the dictionary lookups it
        /// replaces, so the cost falls on exactly the items that cost nothing to
        /// check in the first place.
        /// </summary>
        public void Observe(string text, TokenizerOptions options, int wordsEmitted)
        {
            if (string.IsNullOrEmpty(text))
                return;

            TextsFound++;

            if (options != null)
                MinimumWordLength = options.MinimumWordLength;

            if (wordsEmitted > 0)
            {
                TextsChecked++;
                WordsChecked += wordsEmitted;
                return;
            }

            if (options == null)
            {
                NoWordsAtAll++;
                return;
            }

            // A single text can be excluded by several rules at once. It is
            // attributed to the first one that would have let it through, so the
            // buckets stay a partition and the totals still add up - at the cost
            // of under-reporting the rarer rules. The headline number the user
            // acts on is the total skipped, which is exact either way.
            if (options.IgnoreAllCaps && YieldsWords(text, Relax(options, allCaps: true)))
                SkippedAllCaps++;
            else if (options.IgnoreMixedCase && YieldsWords(text, Relax(options, mixedCase: true)))
                SkippedMixedCase++;
            else if (options.IgnoreWordsAdjacentToDigits && YieldsWords(text, Relax(options, digits: true)))
                SkippedAdjacentToDigits++;
            else if (options.MinimumWordLength > 1 && YieldsWords(text, Relax(options, shortWords: true)))
                SkippedTooShort++;
            else
                NoWordsAtAll++;
        }

        /// <summary>
        /// Folds another coverage into this one, for a scan made of several
        /// passes - sweeping a set of layouts, for instance. Adding the buckets
        /// keeps them a partition, so the totals still reconcile.
        /// </summary>
        public void Merge(ScanCoverage other)
        {
            if (other == null)
                return;

            TextsFound += other.TextsFound;
            TextsChecked += other.TextsChecked;
            WordsChecked += other.WordsChecked;
            SkippedAllCaps += other.SkippedAllCaps;
            SkippedMixedCase += other.SkippedMixedCase;
            SkippedAdjacentToDigits += other.SkippedAdjacentToDigits;
            SkippedTooShort += other.SkippedTooShort;
            NoWordsAtAll += other.NoWordsAtAll;

            if (other.MinimumWordLength > 0)
                MinimumWordLength = other.MinimumWordLength;
        }

        private static TokenizerOptions Relax(
            TokenizerOptions options,
            bool allCaps = false,
            bool mixedCase = false,
            bool digits = false,
            bool shortWords = false)
        {
            TokenizerOptions relaxed = options.Clone();

            if (allCaps) relaxed.IgnoreAllCaps = false;
            if (mixedCase) relaxed.IgnoreMixedCase = false;
            if (digits) relaxed.IgnoreWordsAdjacentToDigits = false;
            if (shortWords) relaxed.MinimumWordLength = 1;

            return relaxed;
        }

        private static bool YieldsWords(string text, TokenizerOptions options)
        {
            foreach (WordToken unused in Tokenizer.Tokenize(text, options))
                return true;

            return false;
        }

        /// <summary>
        /// A one-line summary for the panel, or null when everything with words in
        /// it was checked and there is nothing worth saying.
        /// </summary>
        public string ShortSummary()
        {
            if (TextsFound == 0)
                return "No text found to check.";

            if (!HasBlindSpot)
                return null;

            return string.Format(
                CultureInfo.CurrentCulture,
                "Checked {0} of {1} text items ({2:0.#}%) - {3} skipped by the current options.",
                TextsChecked, TextsFound, Fraction * 100.0,
                SkippedAllCaps + SkippedMixedCase + SkippedAdjacentToDigits + SkippedTooShort);
        }

        /// <summary>
        /// The full breakdown, one line per bucket, naming the option that would
        /// include each. Used by the report command.
        /// </summary>
        public IEnumerable<string> Describe()
        {
            yield return string.Format(
                CultureInfo.CurrentCulture,
                "Checked {0} of {1} text items ({2:0.#}%), {3} words.",
                TextsChecked, TextsFound, Fraction * 100.0, WordsChecked);

            if (SkippedAllCaps > 0)
                yield return string.Format(CultureInfo.CurrentCulture,
                    "  {0} skipped as ALL CAPS - untick 'Ignore ALL CAPS words' to include them.", SkippedAllCaps);

            if (SkippedMixedCase > 0)
                yield return string.Format(CultureInfo.CurrentCulture,
                    "  {0} skipped as MixedCase - untick 'Ignore MixedCase words' to include them.", SkippedMixedCase);

            if (SkippedAdjacentToDigits > 0)
                yield return string.Format(CultureInfo.CurrentCulture,
                    "  {0} skipped for touching a digit - untick 'Ignore words next to digits' to include them.",
                    SkippedAdjacentToDigits);

            if (SkippedTooShort > 0)
                yield return string.Format(CultureInfo.CurrentCulture,
                    "  {0} skipped as shorter than {1} letters - lower the minimum word length to include them.",
                    SkippedTooShort, MinimumWordLength);

            if (NoWordsAtAll > 0)
                yield return string.Format(CultureInfo.CurrentCulture,
                    "  {0} contain nothing word-like (numbers, symbols, field codes).", NoWordsAtAll);
        }
    }
}
