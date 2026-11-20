using System;
using System.Collections.Generic;
using System.Linq;

namespace RhinoSpellCheck.Core
{
    /// <summary>
    /// Holds the results of a scan and drives the Word-style review loop:
    /// step through the misspellings, apply a correction, ignore, or accept a word
    /// into the dictionary.
    ///
    /// Applying a correction changes the length of the text it lives in, so every
    /// later issue in the same slot has its offset shifted to match. That is what
    /// keeps a second fix in the same paragraph landing on the right characters.
    /// </summary>
    public sealed class SpellSession
    {
        private readonly List<SpellIssue> _issues;
        private readonly SpellEngine _engine;
        private readonly ITextStore _store;
        private int _current = -1;

        public SpellSession(IEnumerable<SpellIssue> issues, SpellEngine engine, ITextStore store)
        {
            _issues = issues == null ? new List<SpellIssue>() : issues.ToList();
            _engine = engine;
            _store = store;

            if (_issues.Count > 0)
                _current = FindNextPending(-1, true);
        }

        /// <summary>Raised whenever the current issue or any issue's state changes.</summary>
        public event EventHandler Changed;

        public IReadOnlyList<SpellIssue> Issues { get { return _issues; } }

        public SpellEngine Engine { get { return _engine; } }

        public int TotalCount { get { return _issues.Count; } }

        public int PendingCount { get { return _issues.Count(i => i.State == IssueState.Pending); } }

        public int CorrectedCount { get { return _issues.Count(i => i.State == IssueState.Corrected); } }

        public int IgnoredCount { get { return _issues.Count(i => i.State == IssueState.Ignored); } }

        public int CurrentIndex { get { return _current; } }

        public SpellIssue Current
        {
            get { return _current >= 0 && _current < _issues.Count ? _issues[_current] : null; }
        }

        public IReadOnlyList<string> CurrentSuggestions
        {
            get
            {
                var issue = Current;
                return issue == null ? new string[0] : issue.GetSuggestions(_engine);
            }
        }

        public bool MoveNext()
        {
            int next = FindNextPending(_current, true);
            if (next < 0)
                return false;

            _current = next;
            RaiseChanged();
            return true;
        }

        public bool MovePrevious()
        {
            int previous = FindNextPending(_current, false);
            if (previous < 0)
                return false;

            _current = previous;
            RaiseChanged();
            return true;
        }

        public bool MoveTo(int index)
        {
            if (index < 0 || index >= _issues.Count || index == _current)
                return false;

            _current = index;
            RaiseChanged();
            return true;
        }

        /// <summary>Applies <paramref name="replacement"/> to the current issue.</summary>
        public bool ApplyCurrent(string replacement)
        {
            return Apply(Current, replacement);
        }

        /// <summary>
        /// Applies the same replacement to every remaining issue with the same
        /// word, anywhere in the document.
        /// </summary>
        public int ApplyAllMatching(string replacement)
        {
            var issue = Current;
            if (issue == null || string.IsNullOrEmpty(replacement))
                return 0;

            // Snapshot first: Apply mutates offsets on the live list. Ascending
            // order keeps the shift bookkeeping correct for the ones still to come.
            var matches = _issues
                .Where(i => i.State == IssueState.Pending && string.Equals(i.Word, issue.Word, StringComparison.Ordinal))
                .ToList();

            int applied = matches.Count(m => Apply(m, replacement, false));

            AdvanceIfCurrentIsDone();
            RaiseChanged();
            return applied;
        }

        public bool IgnoreCurrent()
        {
            var issue = Current;
            if (issue == null || issue.State != IssueState.Pending)
                return false;

            issue.State = IssueState.Ignored;
            AdvanceIfCurrentIsDone();
            RaiseChanged();
            return true;
        }

        /// <summary>Ignores every remaining occurrence of the current word for this session.</summary>
        public int IgnoreAllMatching()
        {
            var issue = Current;
            if (issue == null)
                return 0;

            if (_engine != null)
                _engine.IgnoreForSession(issue.Word);

            int count = MarkMatchingIgnored(issue.Word);
            AdvanceIfCurrentIsDone();
            RaiseChanged();
            return count;
        }

        /// <summary>
        /// Adds the current word to the personal dictionary and clears every
        /// remaining occurrence of it.
        /// </summary>
        public int AddCurrentToDictionary()
        {
            var issue = Current;
            if (issue == null)
                return 0;

            if (_engine != null)
                _engine.AddToUserDictionary(issue.Word);

            int count = MarkMatchingIgnored(issue.Word);
            AdvanceIfCurrentIsDone();
            RaiseChanged();
            return count;
        }

        private bool Apply(SpellIssue issue, string replacement, bool raise = true)
        {
            if (issue == null || issue.State != IssueState.Pending)
                return false;

            if (string.IsNullOrEmpty(replacement) || replacement == issue.Word)
                return false;

            bool written = _store != null &&
                _store.ReplaceRange(issue.ObjectId, issue.Slot, issue.Start, issue.Length, issue.Word, replacement);

            if (!written)
            {
                // The object was edited or deleted since the scan; do not guess.
                issue.State = IssueState.Stale;
                if (raise)
                {
                    AdvanceIfCurrentIsDone();
                    RaiseChanged();
                }
                return false;
            }

            issue.State = IssueState.Corrected;
            ShiftFollowing(issue, replacement.Length - issue.Length);

            if (raise)
            {
                AdvanceIfCurrentIsDone();
                RaiseChanged();
            }

            return true;
        }

        /// <summary>
        /// Moves later issues in the same slot by <paramref name="delta"/> so their
        /// offsets keep pointing at the same words.
        /// </summary>
        private void ShiftFollowing(SpellIssue applied, int delta)
        {
            if (delta == 0)
                return;

            int appliedEnd = applied.End;

            foreach (var other in _issues)
            {
                if (ReferenceEquals(other, applied))
                    continue;
                if (other.ObjectId != applied.ObjectId || other.Slot != applied.Slot)
                    continue;
                if (other.Start >= appliedEnd)
                    other.Start += delta;
            }
        }

        private int MarkMatchingIgnored(string word)
        {
            int count = 0;
            foreach (var other in _issues)
            {
                if (other.State == IssueState.Pending && string.Equals(other.Word, word, StringComparison.Ordinal))
                {
                    other.State = IssueState.Ignored;
                    count++;
                }
            }
            return count;
        }

        private void AdvanceIfCurrentIsDone()
        {
            var issue = Current;
            if (issue != null && issue.State == IssueState.Pending)
                return;

            _current = FindNextPending(_current, true);
        }

        /// <summary>
        /// Finds the next (or previous) pending issue, wrapping around the list.
        /// Returns -1 when nothing is left to review.
        /// </summary>
        private int FindNextPending(int from, bool forward)
        {
            int count = _issues.Count;
            if (count == 0)
                return -1;

            int start = from < 0 ? (forward ? -1 : count) : from;

            for (int step = 1; step <= count; step++)
            {
                int index = forward
                    ? ((start + step) % count + count) % count
                    : ((start - step) % count + count) % count;

                if (_issues[index].State == IssueState.Pending)
                    return index;
            }

            // Nothing pending; keep pointing at the current issue if it exists.
            return _issues.Any(i => i.State == IssueState.Pending) ? from : -1;
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}
