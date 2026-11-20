using System;
using System.Collections.Generic;
using System.Linq;
using RhinoSpellCheck.Core;
using Xunit;

namespace RhinoSpellCheck.Tests
{
    public class SpellSessionTests
    {
        private static readonly Guid ObjectA = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ObjectB = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>Builds issues for every listed word by locating it in the text.</summary>
        private static List<SpellIssue> IssuesFor(string text, Guid id, params string[] words)
        {
            var issues = new List<SpellIssue>();
            int cursor = 0;
            foreach (string word in words)
            {
                int index = text.IndexOf(word, cursor, StringComparison.Ordinal);
                Assert.True(index >= 0, "'" + word + "' not present in '" + text + "'");
                issues.Add(new SpellIssue(id, TextSlot.Annotation, word, index));
                cursor = index + word.Length;
            }
            return issues;
        }

        [Fact]
        public void Starts_on_the_first_pending_issue()
        {
            const string text = "teh wrods";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "teh", "wrods"), null, store);

            Assert.Equal(0, session.CurrentIndex);
            Assert.Equal("teh", session.Current.Word);
            Assert.Equal(2, session.PendingCount);
        }

        [Fact]
        public void Applies_a_correction_to_the_document()
        {
            const string text = "teh cat";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "teh"), null, store);

            Assert.True(session.ApplyCurrent("the"));
            Assert.Equal("the cat", store.Get(ObjectA));
            Assert.Equal(1, session.CorrectedCount);
            Assert.Equal(0, session.PendingCount);
        }

        [Fact]
        public void Second_fix_in_the_same_text_lands_correctly_after_a_longer_replacement()
        {
            // "teh" -> "the" is same length; "wrds" -> "words" grows by one, which
            // would push the third issue off by one without offset shifting.
            const string text = "teh wrds hme";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "teh", "wrds", "hme"), null, store);

            Assert.True(session.ApplyCurrent("the"));
            Assert.True(session.ApplyCurrent("words"));
            Assert.True(session.ApplyCurrent("home"));

            Assert.Equal("the words home", store.Get(ObjectA));
        }

        [Fact]
        public void Shifting_also_works_when_the_replacement_is_shorter()
        {
            const string text = "occassion adn end";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "occassion", "adn"), null, store);

            Assert.True(session.ApplyCurrent("occasion"));
            Assert.True(session.ApplyCurrent("and"));

            Assert.Equal("occasion and end", store.Get(ObjectA));
        }

        [Fact]
        public void Change_all_replaces_every_occurrence_across_objects()
        {
            const string a = "teh first teh second";
            const string b = "and teh third";

            var store = new FakeTextStore();
            store.Set(ObjectA, a);
            store.Set(ObjectB, b);

            var issues = IssuesFor(a, ObjectA, "teh", "teh");
            issues.AddRange(IssuesFor(b, ObjectB, "teh"));

            var session = new SpellSession(issues, null, store);

            Assert.Equal(3, session.ApplyAllMatching("the"));
            Assert.Equal("the first the second", store.Get(ObjectA));
            Assert.Equal("and the third", store.Get(ObjectB));
            Assert.Equal(0, session.PendingCount);
        }

        [Fact]
        public void Change_all_keeps_offsets_correct_when_the_word_grows()
        {
            const string text = "wrd here and wrd there";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "wrd", "wrd"), null, store);

            Assert.Equal(2, session.ApplyAllMatching("word"));
            Assert.Equal("word here and word there", store.Get(ObjectA));
        }

        [Fact]
        public void Change_all_leaves_other_words_pending()
        {
            const string text = "teh wrods teh";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var issues = IssuesFor(text, ObjectA, "teh", "wrods", "teh");
            var session = new SpellSession(issues, null, store);

            Assert.Equal(2, session.ApplyAllMatching("the"));
            Assert.Equal(1, session.PendingCount);
            Assert.Equal("wrods", session.Current.Word);
        }

        [Fact]
        public void A_stale_issue_is_flagged_and_the_document_is_untouched()
        {
            const string text = "teh cat";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var issues = IssuesFor(text, ObjectA, "teh");
            var session = new SpellSession(issues, null, store);

            // Someone edits the annotation behind our back.
            store.Set(ObjectA, "a completely different note");

            Assert.False(session.ApplyCurrent("the"));
            Assert.Equal(IssueState.Stale, issues[0].State);
            Assert.Equal("a completely different note", store.Get(ObjectA));
        }

        [Fact]
        public void A_deleted_object_is_flagged_stale()
        {
            const string text = "teh cat";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var issues = IssuesFor(text, ObjectA, "teh");
            var session = new SpellSession(issues, null, store);

            store.Delete(ObjectA);

            Assert.False(session.ApplyCurrent("the"));
            Assert.Equal(IssueState.Stale, issues[0].State);
        }

        [Fact]
        public void Ignoring_advances_without_touching_the_document()
        {
            const string text = "teh wrods";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "teh", "wrods"), null, store);

            Assert.True(session.IgnoreCurrent());
            Assert.Equal(0, store.WriteCount);
            Assert.Equal("wrods", session.Current.Word);
            Assert.Equal(1, session.IgnoredCount);
        }

        [Fact]
        public void Ignore_all_clears_every_matching_occurrence()
        {
            const string text = "Kerf and Kerf and teh";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "Kerf", "Kerf", "teh"), null, store);

            Assert.Equal(2, session.IgnoreAllMatching());
            Assert.Equal(1, session.PendingCount);
            Assert.Equal("teh", session.Current.Word);
        }

        [Fact]
        public void Navigation_wraps_around_and_skips_resolved_issues()
        {
            const string text = "teh wrods hme";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "teh", "wrods", "hme"), null, store);

            Assert.Equal("teh", session.Current.Word);
            Assert.True(session.MoveNext());
            Assert.Equal("wrods", session.Current.Word);
            Assert.True(session.MoveNext());
            Assert.Equal("hme", session.Current.Word);

            // Wraps back to the start.
            Assert.True(session.MoveNext());
            Assert.Equal("teh", session.Current.Word);

            Assert.True(session.MovePrevious());
            Assert.Equal("hme", session.Current.Word);
        }

        [Fact]
        public void Navigation_skips_corrected_issues()
        {
            const string text = "teh wrods hme";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "teh", "wrods", "hme"), null, store);

            session.MoveNext();                 // wrods
            Assert.True(session.ApplyCurrent("words"));

            // "wrods" is resolved, so cycling never returns to it.
            var visited = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                visited.Add(session.Current.Word);
                session.MoveNext();
            }

            Assert.DoesNotContain("wrods", visited);
        }

        [Fact]
        public void Replacing_with_the_same_word_is_rejected()
        {
            const string text = "teh cat";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "teh"), null, store);

            Assert.False(session.ApplyCurrent("teh"));
            Assert.False(session.ApplyCurrent(""));
            Assert.False(session.ApplyCurrent(null));
            Assert.Equal(0, store.WriteCount);
        }

        [Fact]
        public void An_empty_session_reports_nothing_to_do()
        {
            var session = new SpellSession(new SpellIssue[0], null, new FakeTextStore());

            Assert.Null(session.Current);
            Assert.Equal(-1, session.CurrentIndex);
            Assert.Equal(0, session.TotalCount);
            Assert.False(session.MoveNext());
            Assert.False(session.MovePrevious());
            Assert.False(session.ApplyCurrent("x"));
        }

        [Fact]
        public void Raises_changed_when_an_issue_is_resolved()
        {
            const string text = "teh cat";
            var store = new FakeTextStore();
            store.Set(ObjectA, text);

            var session = new SpellSession(IssuesFor(text, ObjectA, "teh"), null, store);

            int raised = 0;
            session.Changed += (s, e) => raised++;

            session.ApplyCurrent("the");
            Assert.True(raised > 0);
        }

        [Fact]
        public void Offsets_in_a_different_slot_are_not_shifted()
        {
            var store = new FakeTextStore();
            store.Set(ObjectA, "wrd one", TextSlot.TextDotPrimary);
            store.Set(ObjectA, "wrd two", TextSlot.TextDotSecondary);

            var issues = new List<SpellIssue>
            {
                new SpellIssue(ObjectA, TextSlot.TextDotPrimary, "wrd", 0),
                new SpellIssue(ObjectA, TextSlot.TextDotSecondary, "wrd", 0)
            };

            var session = new SpellSession(issues, null, store);

            Assert.True(session.ApplyCurrent("word"));
            Assert.True(session.ApplyCurrent("word"));

            Assert.Equal("word one", store.Get(ObjectA, TextSlot.TextDotPrimary));
            Assert.Equal("word two", store.Get(ObjectA, TextSlot.TextDotSecondary));
        }
    }
}
