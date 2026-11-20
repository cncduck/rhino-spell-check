using RhinoSpellCheck.Core;
using Xunit;

namespace RhinoSpellCheck.Tests
{
    public class RtfSplicerTests
    {
        // Shaped like what Rhino writes for a text entity.
        private const string SimpleRtf =
            @"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil Arial;}}\viewkind4\uc1\pard\f0\fs20 The quick brown fox\par}";

        [Fact]
        public void Flattens_rtf_to_plain_text()
        {
            Assert.Equal("The quick brown fox\n", RtfSplicer.GetPlainText(SimpleRtf));
        }

        [Fact]
        public void Ignores_font_table_contents()
        {
            // "Arial" lives in the \fonttbl destination and must not appear.
            Assert.DoesNotContain("Arial", RtfSplicer.GetPlainText(SimpleRtf));
        }

        [Fact]
        public void Replaces_a_word_and_leaves_the_rest_of_the_rtf_alone()
        {
            string rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil Arial;}}\pard\f0\fs20 The quikc fox\par}";

            Assert.True(RtfSplicer.TryReplaceRange(rtf, 4, 5, "quikc", "quick", out var result));
            Assert.Equal("The quick fox\n", RtfSplicer.GetPlainText(result));
            Assert.Contains(@"{\fonttbl{\f0\fnil Arial;}}", result);
            Assert.Contains(@"\fs20 The quick fox", result);
        }

        [Fact]
        public void Preserves_formatting_runs_around_the_replaced_word()
        {
            string rtf = @"{\rtf1\ansi\deff0\pard\f0 Plain \b bold\b0  tail\par}";
            string plain = RtfSplicer.GetPlainText(rtf);
            Assert.Equal("Plain bold tail\n", plain);

            int start = plain.IndexOf("bold");
            Assert.True(RtfSplicer.TryReplaceRange(rtf, start, 4, "bold", "bald", out var result));

            Assert.Equal("Plain bald tail\n", RtfSplicer.GetPlainText(result));
            Assert.Contains(@"\b bald\b0", result);
        }

        [Fact]
        public void Preserves_markup_that_sits_inside_the_replaced_word()
        {
            // "wrods" split across a run boundary: "wr" + bold "ods".
            string rtf = @"{\rtf1\ansi\deff0\pard\f0 wr\b ods\b0  here\par}";
            string plain = RtfSplicer.GetPlainText(rtf);
            Assert.Equal("wrods here\n", plain);

            Assert.True(RtfSplicer.TryReplaceRange(rtf, 0, 5, "wrods", "words", out var result));
            Assert.Equal("words here\n", RtfSplicer.GetPlainText(result));

            // The \b / \b0 pair must survive so the RTF stays well formed.
            Assert.Contains(@"\b", result);
            Assert.Contains(@"\b0", result);
        }

        [Fact]
        public void Refuses_to_splice_when_the_text_at_the_offset_changed()
        {
            Assert.False(RtfSplicer.TryReplaceRange(SimpleRtf, 4, 5, "quikc", "quick", out var result));
            Assert.Null(result);
        }

        [Fact]
        public void Refuses_out_of_range_offsets()
        {
            Assert.False(RtfSplicer.TryReplaceRange(SimpleRtf, 900, 5, "quick", "quick", out _));
            Assert.False(RtfSplicer.TryReplaceRange(SimpleRtf, -1, 5, "quick", "quick", out _));
            Assert.False(RtfSplicer.TryReplaceRange(SimpleRtf, 4, 0, "", "quick", out _));
        }

        [Fact]
        public void Decodes_hex_escapes_and_maps_them_as_one_character()
        {
            // \'e9 is "e-acute" in cp1252.
            string rtf = @"{\rtf1\ansi\pard caf\'e9 time\par}";
            Assert.Equal("café time\n", RtfSplicer.GetPlainText(rtf));

            Assert.True(RtfSplicer.TryReplaceRange(rtf, 0, 4, "café", "cafe", out var result));
            Assert.Equal("cafe time\n", RtfSplicer.GetPlainText(result));
        }

        [Fact]
        public void Decodes_unicode_escapes_with_their_ansi_fallback()
        {
            string rtf = @"{\rtf1\ansi\uc1\pard na\u239?ve text\par}";
            Assert.Equal("naïve text\n", RtfSplicer.GetPlainText(rtf));

            Assert.True(RtfSplicer.TryReplaceRange(rtf, 0, 5, "naïve", "naive", out var result));
            Assert.Equal("naive text\n", RtfSplicer.GetPlainText(result));
        }

        [Fact]
        public void Treats_escaped_braces_and_backslashes_as_literal_text()
        {
            string rtf = @"{\rtf1\ansi\pard a\{b\}c\\d\par}";
            Assert.Equal("a{b}c\\d\n", RtfSplicer.GetPlainText(rtf));
        }

        [Fact]
        public void Escapes_rtf_metacharacters_in_the_replacement()
        {
            Assert.Equal(@"a\\b", RtfSplicer.Escape(@"a\b"));
            Assert.Equal(@"\{x\}", RtfSplicer.Escape("{x}"));
            Assert.Equal(@"caf\u233?", RtfSplicer.Escape("café"));
        }

        [Fact]
        public void Round_trips_a_replacement_that_needs_escaping()
        {
            string rtf = @"{\rtf1\ansi\pard the naive one\par}";
            Assert.True(RtfSplicer.TryReplaceRange(rtf, 4, 5, "naive", "naïve", out var result));
            Assert.Equal("the naïve one\n", RtfSplicer.GetPlainText(result));
        }

        [Fact]
        public void Par_and_line_become_newlines()
        {
            Assert.Equal("one\ntwo\n", RtfSplicer.GetPlainText(@"{\rtf1\ansi\pard one\line two\par}"));
        }

        [Fact]
        public void Align_returns_the_offset_unchanged_when_both_readers_agree()
        {
            const string plain = "the quikc fox";
            Assert.Equal(4, RtfSplicer.AlignOffset(plain, plain, 4, "quikc"));
        }

        [Fact]
        public void Align_recovers_from_a_drifting_offset()
        {
            const string mine = "the quikc fox";
            const string reference = "\nthe quikc fox"; // Rhino counted one extra char

            Assert.Equal(4, RtfSplicer.AlignOffset(mine, reference, 5, "quikc"));
        }

        [Fact]
        public void Align_picks_the_occurrence_nearest_the_expected_offset()
        {
            const string mine = "teh one and teh two and teh three";
            const string reference = "x" + mine;

            // Reference offset 13 is the second "teh" (mine sits at 12).
            Assert.Equal(12, RtfSplicer.AlignOffset(mine, reference, 13, "teh"));
        }

        [Fact]
        public void Align_only_matches_whole_words()
        {
            // "cat" appears inside "catalogue" first; only the standalone one counts.
            Assert.Equal(10, RtfSplicer.AlignOffset("catalogue cat", "different", 0, "cat"));
        }

        [Fact]
        public void Align_gives_up_when_the_word_is_gone()
        {
            Assert.Equal(-1, RtfSplicer.AlignOffset("nothing here", "teh gone", 0, "teh"));
            Assert.Equal(-1, RtfSplicer.AlignOffset("", "x", 0, "teh"));
            Assert.Equal(-1, RtfSplicer.AlignOffset("text", "x", 0, null));
        }

        [Fact]
        public void Ignores_starred_destinations()
        {
            string rtf = @"{\rtf1\ansi{\*\generator Rhino;}\pard visible\par}";
            Assert.Equal("visible\n", RtfSplicer.GetPlainText(rtf));
        }
    }
}
