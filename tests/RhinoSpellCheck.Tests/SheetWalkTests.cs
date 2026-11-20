using System;
using System.Collections.Generic;
using System.Linq;
using RhinoSpellCheck.Core;
using Xunit;

namespace RhinoSpellCheck.Tests
{
    public class SheetWalkTests
    {
        private static readonly Guid SheetA = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid SheetB = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
        private static readonly Guid SheetC = new Guid("cccccccc-0000-0000-0000-000000000003");

        private static readonly Guid Note1 = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid Note2 = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid SharedDim = new Guid("33333333-3333-3333-3333-333333333333");

        private static SpellIssue On(Guid sheet, string sheetName, Guid obj, string word, int start = 0)
        {
            return new SpellIssue(obj, TextSlot.Annotation, word, start) { SheetId = sheet, SheetName = sheetName };
        }

        [Fact]
        public void Combine_keeps_sheet_order()
        {
            int dropped;
            List<SpellIssue> combined = SheetWalk.Combine(new[]
            {
                new[] { On(SheetA, "A-101", Note1, "wrold") },
                new[] { On(SheetB, "A-102", Note2, "drawign") }
            }, out dropped);

            Assert.Equal(new[] { "wrold", "drawign" }, combined.Select(i => i.Word));
            Assert.Equal(0, dropped);
        }

        /// <summary>
        /// Model text seen through details on two sheets comes back from both.
        /// Keeping both would have the user fix it on A-101 and then meet a twin
        /// on A-102 that can only go stale.
        /// </summary>
        [Fact]
        public void Combine_keeps_text_seen_on_two_sheets_only_on_the_first()
        {
            int dropped;
            List<SpellIssue> combined = SheetWalk.Combine(new[]
            {
                new[] { On(SheetA, "A-101", SharedDim, "typcal", 4) },
                new[] { On(SheetB, "A-102", SharedDim, "typcal", 4), On(SheetB, "A-102", Note2, "drawign") }
            }, out dropped);

            Assert.Equal(1, dropped);
            Assert.Equal(2, combined.Count);
            Assert.Equal(SheetA, combined.Single(i => i.ObjectId == SharedDim).SheetId);
        }

        [Fact]
        public void Combine_keeps_two_different_words_in_one_object()
        {
            int dropped;
            List<SpellIssue> combined = SheetWalk.Combine(new[]
            {
                new[] { On(SheetA, "A-101", Note1, "teh", 0), On(SheetA, "A-101", Note1, "teh", 8) }
            }, out dropped);

            Assert.Equal(2, combined.Count);
            Assert.Equal(0, dropped);
        }

        [Fact]
        public void Position_counts_within_the_current_sheet()
        {
            var session = new SpellSession(new[]
            {
                On(SheetA, "A-101", Note1, "one"),
                On(SheetB, "A-102", Note2, "two", 0),
                On(SheetB, "A-102", Note2, "three", 10),
                On(SheetC, "A-103", SharedDim, "four")
            }, null, null);

            session.MoveNext(); // "two", first word on A-102

            SheetPosition position = SheetPosition.Of(session);

            Assert.Equal("A-102", position.SheetName);
            Assert.Equal(2, position.SheetNumber);
            Assert.Equal(3, position.SheetCount);
            Assert.Equal(1, position.PositionOnSheet);
            Assert.Equal(2, position.PendingOnSheet);

            session.MoveNext(); // "three"

            position = SheetPosition.Of(session);
            Assert.Equal(2, position.PositionOnSheet);
            Assert.Equal(2, position.PendingOnSheet);
        }

        /// <summary>Next past a sheet's last word goes to the next sheet - the walk.</summary>
        [Fact]
        public void Next_after_the_last_word_on_a_sheet_moves_to_the_next_sheet()
        {
            var session = new SpellSession(new[]
            {
                On(SheetA, "A-101", Note1, "one"),
                On(SheetB, "A-102", Note2, "two")
            }, null, null);

            Assert.Equal("A-101", SheetPosition.Of(session).SheetName);

            session.IgnoreCurrent();

            Assert.Equal("A-102", SheetPosition.Of(session).SheetName);
            Assert.Equal(1, SheetPosition.Of(session).PendingOnSheet);
        }

        [Fact]
        public void Reviewed_words_drop_out_of_the_sheet_count()
        {
            var session = new SpellSession(new[]
            {
                On(SheetA, "A-101", Note1, "one", 0),
                On(SheetA, "A-101", Note1, "two", 5),
                On(SheetA, "A-101", Note1, "three", 10)
            }, null, null);

            session.IgnoreCurrent();

            SheetPosition position = SheetPosition.Of(session);
            Assert.Equal(1, position.PositionOnSheet);
            Assert.Equal(2, position.PendingOnSheet);
        }

        [Fact]
        public void No_position_outside_a_sheet_walk()
        {
            var session = new SpellSession(new[]
            {
                new SpellIssue(Note1, TextSlot.Annotation, "wrold", 0)
            }, null, null);

            Assert.Null(SheetPosition.Of(session));
            Assert.Null(SheetPosition.Of(null));
        }
    }
}
