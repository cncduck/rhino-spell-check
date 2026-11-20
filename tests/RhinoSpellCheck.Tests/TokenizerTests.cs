using System.Linq;
using RhinoSpellCheck.Core;
using Xunit;

namespace RhinoSpellCheck.Tests
{
    public class TokenizerTests
    {
        private static string[] Words(string text, TokenizerOptions options = null)
        {
            return Tokenizer.Tokenize(text, options).Select(t => t.Text).ToArray();
        }

        [Fact]
        public void Splits_plain_prose_into_words()
        {
            Assert.Equal(new[] { "The", "quick", "brown", "fox" }, Words("The quick brown fox"));
        }

        [Fact]
        public void Reports_offsets_that_index_the_source_string()
        {
            var tokens = Tokenizer.Tokenize("all wrods here");
            var wrods = tokens.Single(t => t.Text == "wrods");

            Assert.Equal(4, wrods.Start);
            Assert.Equal(9, wrods.End);
            Assert.Equal("wrods", "all wrods here".Substring(wrods.Start, wrods.Length));
        }

        [Fact]
        public void Keeps_interior_apostrophes_but_drops_trailing_ones()
        {
            Assert.Equal(new[] { "don't" }, Words("don't"));
            Assert.Equal(new[] { "designers" }, Words("designers'"));
        }

        [Fact]
        public void Normalises_nothing_but_still_tokenises_curly_apostrophes()
        {
            Assert.Equal(new[] { "don\u2019t" }, Words("don\u2019t"));
        }

        [Fact]
        public void Splits_hyphenated_compounds_into_parts()
        {
            Assert.Equal(new[] { "pre", "drilled" }, Words("pre-drilled"));
        }

        [Fact]
        public void Skips_rhino_text_fields_but_keeps_the_prose_around_them()
        {
            var words = Words("Scale %<ModelUnits>% drawing");

            Assert.Equal(new[] { "Scale", "drawing" }, words);
            Assert.DoesNotContain("ModelUnits", words);
        }

        [Fact]
        public void Skips_nested_looking_text_fields()
        {
            Assert.Empty(Words("%<UserText(\"cabinet\", \"finish\")>%"));
        }

        [Fact]
        public void Skips_urls_emails_and_paths()
        {
            Assert.Empty(Words("https://example.com/drawings"));
            Assert.Empty(Words("someone@example.com"));
            Assert.Empty(Words(@"C:\Projects\details.3dm"));
            Assert.Empty(Words("www.example.com"));
        }

        [Fact]
        public void Ignores_all_caps_by_default_but_can_be_told_not_to()
        {
            Assert.Empty(Words("TYP NTS SIM"));

            var options = new TokenizerOptions { IgnoreAllCaps = false };
            Assert.Equal(new[] { "TYP", "NTS", "SIM" }, Words("TYP NTS SIM", options));
        }

        [Fact]
        public void Ignores_mixed_case_product_names_by_default()
        {
            Assert.Empty(Words("RhinoCommon McNeel"));

            var options = new TokenizerOptions { IgnoreMixedCase = false };
            Assert.Equal(new[] { "RhinoCommon", "McNeel" }, Words("RhinoCommon McNeel", options));
        }

        [Fact]
        public void Ignores_letter_runs_that_touch_a_digit()
        {
            Assert.Empty(Words("12mm"));
            Assert.Empty(Words("3Dprinted"));
            Assert.Equal(new[] { "plate" }, Words("plate 12mm"));
        }

        [Fact]
        public void Honours_minimum_word_length()
        {
            Assert.Equal(new[] { "the" }, Words("of the"));
            Assert.Equal(new[] { "of", "the" }, Words("of the", new TokenizerOptions { MinimumWordLength = 2 }));
        }

        [Fact]
        public void Handles_empty_and_null_input()
        {
            Assert.Empty(Words(""));
            Assert.Empty(Tokenizer.Tokenize(null).ToArray());
        }

        [Fact]
        public void Tokenises_across_newlines()
        {
            Assert.Equal(new[] { "first", "second" }, Words("first\nsecond"));
        }
    }
}
