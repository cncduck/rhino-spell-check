namespace RhinoSpellCheck.Core
{
    /// <summary>
    /// Controls which words the tokenizer bothers to emit. The defaults are tuned
    /// for CAD annotation text, which is dense with part codes, acronyms and
    /// dimensions that a prose-oriented spell checker would flag constantly.
    /// </summary>
    public sealed class TokenizerOptions
    {
        /// <summary>Words shorter than this are never checked. Filters out unit
        /// suffixes and single-letter callouts ("A", "mm", "GA").</summary>
        public int MinimumWordLength { get; set; } = 3;

        /// <summary>Skip words that are entirely upper case ("TYP", "NTS", "SIM").
        /// Drawing notes are full of these and almost none are in a dictionary.</summary>
        public bool IgnoreAllCaps { get; set; } = true;

        /// <summary>Skip words with an interior capital ("RhinoCommon", "McNeel",
        /// "uPVC"). These are nearly always product or proper nouns.</summary>
        public bool IgnoreMixedCase { get; set; } = true;

        /// <summary>Skip letter runs that touch a digit, so "12mm", "3Dprinted"
        /// and "A3" do not produce bogus "mm"/"Dprinted"/"A" tokens.</summary>
        public bool IgnoreWordsAdjacentToDigits { get; set; } = true;

        public static TokenizerOptions Default
        {
            get { return new TokenizerOptions(); }
        }

        public TokenizerOptions Clone()
        {
            return new TokenizerOptions
            {
                MinimumWordLength = MinimumWordLength,
                IgnoreAllCaps = IgnoreAllCaps,
                IgnoreMixedCase = IgnoreMixedCase,
                IgnoreWordsAdjacentToDigits = IgnoreWordsAdjacentToDigits
            };
        }
    }
}
