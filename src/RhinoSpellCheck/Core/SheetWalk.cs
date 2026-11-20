using System;
using System.Collections.Generic;
using System.Linq;

namespace RhinoSpellCheck.Core
{
    /// <summary>
    /// Bookkeeping for reviewing a drawing set one layout sheet at a time.
    ///
    /// The walk itself needs no special machinery: issues are laid out sheet by
    /// sheet, so stepping past the last word on one sheet lands on the first word
    /// of the next. What this adds is the two things that ordering alone gets
    /// wrong - text that appears on more than one sheet, and telling the user
    /// where in the set they are.
    /// </summary>
    public static class SheetWalk
    {
        /// <summary>
        /// Joins per-sheet results, in sheet order, into one review list.
        ///
        /// Model-space text can show through details on several sheets, so the
        /// same word in the same object comes back once per sheet. Keeping every
        /// copy would make the user fix it on the first sheet and then meet a
        /// twin on the next that can only go stale - the text under it has
        /// already changed. Each occurrence is kept on the first sheet it appears.
        /// </summary>
        /// <param name="sheets">Each sheet's issues, in the order the sheets are walked.</param>
        /// <param name="duplicatesDropped">How many repeats were removed.</param>
        public static List<SpellIssue> Combine(IEnumerable<IEnumerable<SpellIssue>> sheets, out int duplicatesDropped)
        {
            var combined = new List<SpellIssue>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            duplicatesDropped = 0;

            if (sheets == null)
                return combined;

            foreach (IEnumerable<SpellIssue> sheet in sheets)
            {
                if (sheet == null)
                    continue;

                foreach (SpellIssue issue in sheet)
                {
                    if (issue == null)
                        continue;

                    if (seen.Add(KeyOf(issue)))
                        combined.Add(issue);
                    else
                        duplicatesDropped++;
                }
            }

            return combined;
        }

        private static string KeyOf(SpellIssue issue)
        {
            return issue.ObjectId.ToString("N") + "|" + (int)issue.Slot + "|" + issue.Start;
        }
    }

    /// <summary>
    /// Where the current word sits within a sheet walk: which sheet, and how far
    /// through that sheet.
    /// </summary>
    public sealed class SheetPosition
    {
        /// <summary>Name of the sheet the current word is on.</summary>
        public string SheetName { get; private set; }

        /// <summary>1-based: this is the Nth sheet that had anything to fix.</summary>
        public int SheetNumber { get; private set; }

        /// <summary>How many sheets had anything to fix.</summary>
        public int SheetCount { get; private set; }

        /// <summary>1-based position of the current word among this sheet's remaining words.</summary>
        public int PositionOnSheet { get; private set; }

        /// <summary>Words still to review on this sheet, the current one included.</summary>
        public int PendingOnSheet { get; private set; }

        /// <summary>
        /// Describes the session's current word, or returns null when there is
        /// none or it was not found on a sheet.
        /// </summary>
        public static SheetPosition Of(SpellSession session)
        {
            if (session == null)
                return null;

            SpellIssue current = session.Current;
            if (current == null || current.SheetId == Guid.Empty)
                return null;

            IReadOnlyList<SpellIssue> issues = session.Issues;

            // Sheets in the order they are walked, i.e. first appearance.
            var order = new List<Guid>();
            foreach (SpellIssue issue in issues)
            {
                if (issue.SheetId != Guid.Empty && !order.Contains(issue.SheetId))
                    order.Add(issue.SheetId);
            }

            int before = 0;
            int pending = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                SpellIssue issue = issues[i];
                if (issue.SheetId != current.SheetId || issue.State != IssueState.Pending)
                    continue;

                pending++;
                if (i < session.CurrentIndex)
                    before++;
            }

            return new SheetPosition
            {
                SheetName = string.IsNullOrEmpty(current.SheetName) ? "(unnamed)" : current.SheetName,
                SheetNumber = order.IndexOf(current.SheetId) + 1,
                SheetCount = order.Count,
                PositionOnSheet = before + 1,
                PendingOnSheet = pending
            };
        }
    }
}
