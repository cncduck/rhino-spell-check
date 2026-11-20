using System.Linq;
using RhinoSpellCheck.Core;
using Xunit;

namespace RhinoSpellCheck.Tests
{
    /// <summary>
    /// Coverage reporting exists so that "no misspellings" cannot quietly mean
    /// "almost nothing was checked". These pin the attribution, because a wrong
    /// reason is worse than no reason - it would send the user to the wrong
    /// option.
    /// </summary>
    public class ScanCoverageTests
    {
        private static int WordCount(string text, TokenizerOptions options)
        {
            return Tokenizer.Tokenize(text, options).Count();
        }

        private static ScanCoverage Measure(TokenizerOptions options, params string[] texts)
        {
            var coverage = new ScanCoverage();
            foreach (string text in texts)
                coverage.Observe(text, options, WordCount(text, options));

            return coverage;
        }

        [Fact]
        public void CountsCheckedTextAndWords()
        {
            ScanCoverage coverage = Measure(TokenizerOptions.Default, "the quick brown fox");

            Assert.Equal(1, coverage.TextsFound);
            Assert.Equal(1, coverage.TextsChecked);
            Assert.Equal(4, coverage.WordsChecked);
            Assert.False(coverage.HasBlindSpot);
        }

        [Fact]
        public void AllCapsTextIsAttributedToTheCapsOption()
        {
            ScanCoverage coverage = Measure(TokenizerOptions.Default, "APPROVED BY SHOP FOREMAN");

            Assert.Equal(0, coverage.TextsChecked);
            Assert.Equal(1, coverage.SkippedAllCaps);
            Assert.Equal(0, coverage.NoWordsAtAll);
            Assert.True(coverage.HasBlindSpot);
        }

        [Fact]
        public void TextWithNoLettersIsNotBlamedOnAnOption()
        {
            // This is nothing to check, not a blind spot. Reporting it as one
            // would send the user to change a setting that changes nothing.
            ScanCoverage coverage = Measure(TokenizerOptions.Default, "1/2\" = 1'-0\"");

            Assert.Equal(1, coverage.NoWordsAtAll);
            Assert.Equal(0, coverage.SkippedAllCaps);
            Assert.False(coverage.HasBlindSpot);
        }

        [Fact]
        public void CapsAreCountedAsCheckedOnceTheOptionIsOff()
        {
            var options = TokenizerOptions.Default;
            options.IgnoreAllCaps = false;

            ScanCoverage coverage = Measure(options, "APPROVED BY SHOP FOREMAN");

            Assert.Equal(1, coverage.TextsChecked);
            Assert.Equal(0, coverage.SkippedAllCaps);
            Assert.False(coverage.HasBlindSpot);
        }

        [Fact]
        public void FractionReportsTheShareActuallyChecked()
        {
            ScanCoverage coverage = Measure(
                TokenizerOptions.Default,
                "a real sentence here",
                "ALL CAPS NOTE",
                "MORE CAPS",
                "AND AGAIN");

            Assert.Equal(4, coverage.TextsFound);
            Assert.Equal(1, coverage.TextsChecked);
            Assert.Equal(3, coverage.SkippedAllCaps);
            Assert.Equal(0.25, coverage.Fraction, 3);
        }

        [Fact]
        public void SummaryIsSilentWhenNothingWasHeldBackByAnOption()
        {
            ScanCoverage coverage = Measure(TokenizerOptions.Default, "the quick brown fox");

            // Nothing useful to say, so the panel keeps its ordinary message.
            Assert.Null(coverage.ShortSummary());
        }

        [Fact]
        public void SummaryNamesTheShortfallWhenAnOptionHidTheText()
        {
            ScanCoverage coverage = Measure(TokenizerOptions.Default, "real words here", "ALL CAPS NOTE");

            string summary = coverage.ShortSummary();

            Assert.NotNull(summary);
            Assert.Contains("1 of 2", summary);
        }

        [Fact]
        public void DescribeNamesTheOptionThatWouldIncludeSkippedText()
        {
            ScanCoverage coverage = Measure(TokenizerOptions.Default, "ALL CAPS NOTE");

            string report = string.Join("\n", coverage.Describe());

            Assert.Contains("ALL CAPS", report);
            Assert.Contains("Ignore ALL CAPS words", report);
        }

        [Fact]
        public void BucketsPartitionEveryTextExactlyOnce()
        {
            // The buckets are reported as a breakdown, so they have to add up.
            ScanCoverage coverage = Measure(
                TokenizerOptions.Default,
                "a real sentence",
                "ALL CAPS NOTE",
                "RhinoCommon McNeel",
                "12mm 3Dprinted",
                "1/2\" = 1'-0\"");

            int accounted = coverage.TextsChecked
                            + coverage.SkippedAllCaps
                            + coverage.SkippedMixedCase
                            + coverage.SkippedAdjacentToDigits
                            + coverage.SkippedTooShort
                            + coverage.NoWordsAtAll;

            Assert.Equal(coverage.TextsFound, accounted);
        }

        [Fact]
        public void MergeAddsBucketsAndKeepsThemAPartition()
        {
            // A layout sweep scans each sheet separately and reports one total,
            // so the totals still have to reconcile after folding them together.
            ScanCoverage first = Measure(TokenizerOptions.Default, "real words here", "ALL CAPS NOTE");
            ScanCoverage second = Measure(TokenizerOptions.Default, "more real words", "1/2\" = 1'-0\"");

            first.Merge(second);

            Assert.Equal(4, first.TextsFound);
            Assert.Equal(2, first.TextsChecked);
            Assert.Equal(1, first.SkippedAllCaps);
            Assert.Equal(1, first.NoWordsAtAll);

            int accounted = first.TextsChecked
                            + first.SkippedAllCaps
                            + first.SkippedMixedCase
                            + first.SkippedAdjacentToDigits
                            + first.SkippedTooShort
                            + first.NoWordsAtAll;

            Assert.Equal(first.TextsFound, accounted);
        }

        [Fact]
        public void MergingNullChangesNothing()
        {
            ScanCoverage coverage = Measure(TokenizerOptions.Default, "real words here");
            coverage.Merge(null);

            Assert.Equal(1, coverage.TextsFound);
            Assert.Equal(1, coverage.TextsChecked);
        }

        [Fact]
        public void EmptyTextIsNotCounted()
        {
            ScanCoverage coverage = Measure(TokenizerOptions.Default, "", null, "real words here");

            Assert.Equal(1, coverage.TextsFound);
        }
    }
}
