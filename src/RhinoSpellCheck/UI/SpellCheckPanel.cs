using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;
using RhinoSpellCheck.Core;
using RhinoSpellCheck.Documents;

namespace RhinoSpellCheck.UI
{
    /// <summary>
    /// The Spell Check panel: where the misspellings found by
    /// <c>CheckSpellSelected</c> and <c>CheckSpellLayout</c> are reviewed, one
    /// word at a time, the way a word processor's spell checker works.
    ///
    /// It does not scan. The commands are the one way in; this panel adopts
    /// whatever they publish through <see cref="SpellCheckPlugin.PublishScan"/>.
    /// </summary>
    [Guid("8B2D5E17-4A63-49CF-B0D2-7E5C931A6F84")]
    public class SpellCheckPanel : Eto.Forms.Panel, IPanel
    {
        internal const string IdleMessage = "Nothing checked yet. Run CheckSpellSelected or CheckSpellLayout.";

        private readonly Label _status = new Label { Text = IdleMessage, Wrap = WrapMode.Word };
        private readonly Label _sheet = new Label { Font = SystemFonts.Bold(), Wrap = WrapMode.Word, Visible = false };

        private readonly Label _word = new Label { Font = SystemFonts.Bold(11), TextColor = Colors.Firebrick };
        private readonly Label _location = new Label { TextColor = Colors.Gray };
        private readonly Label _context = new Label { Wrap = WrapMode.Word, Height = 46 };

        private readonly DropDown _suggestions = new DropDown();
        private readonly TextBox _replacement = new TextBox();

        private readonly Button _previous = new Button { Text = "< Previous" };
        private readonly Button _next = new Button { Text = "Next >" };
        private readonly Button _change = new Button { Text = "Change" };
        private readonly Button _changeAll = new Button { Text = "Change All" };
        private readonly Button _ignore = new Button { Text = "Ignore" };
        private readonly Button _ignoreAll = new Button { Text = "Ignore All" };
        private readonly Button _addWord = new Button { Text = "Add to Dictionary" };

        private readonly NumericStepper _minimumLength = new NumericStepper { MinValue = 1, MaxValue = 12, Width = 55 };
        private readonly CheckBox _ignoreCaps = new CheckBox { Text = "Ignore ALL CAPS words" };
        private readonly CheckBox _ignoreMixed = new CheckBox { Text = "Ignore MixedCase words" };
        private readonly CheckBox _ignoreDigits = new CheckBox { Text = "Ignore words next to digits" };

        private readonly IssueNavigator _navigator = new IssueNavigator();
        private ScanOutcome _outcome;
        private SpellSession _session;
        private bool _updatingUi;

        public SpellCheckPanel()
        {
            BuildLayout();
            WireEvents();
            LoadOptionsFromPlugin();
            UpdateUi();
        }

        public static Guid PanelId
        {
            get { return typeof(SpellCheckPanel).GUID; }
        }

        private static SpellCheckPlugin Plugin
        {
            get { return SpellCheckPlugin.Instance; }
        }

        private void BuildLayout()
        {
            var layout = new DynamicLayout { Padding = new Padding(8), DefaultSpacing = new Size(6, 6) };

            layout.AddRow(_status);
            layout.AddRow(_sheet);

            layout.BeginVertical(new Padding(0, 6, 0, 0), new Size(6, 3));
            layout.AddRow(new Label { Text = "Not in dictionary:", Font = SystemFonts.Bold() });
            layout.AddRow(_word);
            layout.AddRow(_location);
            layout.AddRow(_context);
            layout.EndVertical();

            layout.BeginVertical(new Padding(0, 6, 0, 0), new Size(6, 3));
            layout.AddRow(new Label { Text = "Suggestions:", Font = SystemFonts.Bold() });
            layout.AddRow(_suggestions);
            layout.AddRow(new Label { Text = "Replace with:" });
            layout.AddRow(_replacement);
            layout.EndVertical();

            layout.AddSeparateRow(_previous, _next, null);
            layout.AddSeparateRow(_change, _changeAll, null);
            layout.AddSeparateRow(_ignore, _ignoreAll, null);
            layout.AddSeparateRow(_addWord, null);

            layout.BeginVertical(new Padding(0, 10, 0, 0), new Size(6, 3));
            layout.AddRow(new Label { Text = "What to check", Font = SystemFonts.Bold() });
            layout.AddRow(new Label { Text = "Applies to the next check.", TextColor = Colors.Gray });
            layout.AddRow(new Label { Text = "Minimum word length:" }, _minimumLength, null);
            layout.AddRow(_ignoreCaps);
            layout.AddRow(_ignoreMixed);
            layout.AddRow(_ignoreDigits);
            layout.EndVertical();

            layout.Add(null); // push everything to the top

            Content = new Scrollable { Content = layout, Border = BorderType.None };
        }

        private void WireEvents()
        {
            _previous.Click += (s, e) => Navigate(false);
            _next.Click += (s, e) => Navigate(true);
            _change.Click += (s, e) => ApplyChange(false);
            _changeAll.Click += (s, e) => ApplyChange(true);
            _ignore.Click += (s, e) => RunAndRefresh(() => _session.IgnoreCurrent());
            _ignoreAll.Click += (s, e) => RunAndRefresh(() => _session.IgnoreAllMatching());
            _addWord.Click += (s, e) => RunAndRefresh(() => _session.AddCurrentToDictionary());

            _suggestions.SelectedIndexChanged += (s, e) =>
            {
                if (_updatingUi)
                    return;

                var value = _suggestions.SelectedValue as string;
                if (!string.IsNullOrEmpty(value))
                    _replacement.Text = value;
            };

            _minimumLength.ValueChanged += (s, e) => SaveOptionsToPlugin();
            _ignoreCaps.CheckedChanged += (s, e) => SaveOptionsToPlugin();
            _ignoreMixed.CheckedChanged += (s, e) => SaveOptionsToPlugin();
            _ignoreDigits.CheckedChanged += (s, e) => SaveOptionsToPlugin();

            var plugin = Plugin;
            if (plugin != null)
                plugin.ScanChanged += OnScanPublished;
        }

        private void LoadOptionsFromPlugin()
        {
            var plugin = Plugin;
            if (plugin == null)
                return;

            _updatingUi = true;
            try
            {
                _minimumLength.Value = plugin.TokenizerOptions.MinimumWordLength;
                _ignoreCaps.Checked = plugin.TokenizerOptions.IgnoreAllCaps;
                _ignoreMixed.Checked = plugin.TokenizerOptions.IgnoreMixedCase;
                _ignoreDigits.Checked = plugin.TokenizerOptions.IgnoreWordsAdjacentToDigits;
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void SaveOptionsToPlugin()
        {
            var plugin = Plugin;
            if (plugin == null || _updatingUi)
                return;

            plugin.TokenizerOptions.MinimumWordLength = (int)_minimumLength.Value;
            plugin.TokenizerOptions.IgnoreAllCaps = _ignoreCaps.Checked.GetValueOrDefault(true);
            plugin.TokenizerOptions.IgnoreMixedCase = _ignoreMixed.Checked.GetValueOrDefault(true);
            plugin.TokenizerOptions.IgnoreWordsAdjacentToDigits = _ignoreDigits.Checked.GetValueOrDefault(true);
            plugin.SavePreferences();
        }

        // ---------------------------------------------------------------- adopting a check

        private void OnScanPublished(object sender, EventArgs e)
        {
            Application.Instance.AsyncInvoke(AdoptCurrentScan);
        }

        /// <summary>
        /// Takes on whatever check the plug-in currently holds.
        ///
        /// Called both when a check is announced and whenever the panel is shown.
        /// The event alone is not enough: Rhino does not create the panel until
        /// it is first opened, so a command that checks and then opens the panel
        /// announces to nobody. Pulling on show is what makes "run the command,
        /// panel appears on the first word" work at all.
        /// </summary>
        private void AdoptCurrentScan()
        {
            var plugin = Plugin;
            if (plugin == null)
                return;

            ScanOutcome outcome = plugin.CurrentScan;

            // Already showing it.
            if (ReferenceEquals(outcome, _outcome))
                return;

            _outcome = outcome;
            _session = outcome == null ? null : outcome.Session;

            UpdateUi();
            FollowCurrentIssue();
        }

        private RhinoDoc Document
        {
            get
            {
                var plugin = Plugin;
                RhinoDoc doc = plugin == null ? null : plugin.ScanDocument;
                return doc ?? RhinoDoc.ActiveDoc;
            }
        }

        // ---------------------------------------------------------------- actions

        private void Navigate(bool forward)
        {
            if (_session == null)
                return;

            if (forward)
                _session.MoveNext();
            else
                _session.MovePrevious();

            UpdateUi();
            FollowCurrentIssue();
        }

        private void ApplyChange(bool all)
        {
            if (_session == null || _session.Current == null)
                return;

            string replacement = (_replacement.Text ?? string.Empty).Trim();
            if (replacement.Length == 0)
            {
                _status.Text = "Enter a replacement, or choose one from the suggestions.";
                return;
            }

            RhinoDoc doc = Document;
            if (doc == null)
                return;

            uint undo = doc.BeginUndoRecord(all ? "Spell Check: Change All" : "Spell Check: Change");
            int applied;
            try
            {
                applied = all
                    ? _session.ApplyAllMatching(replacement)
                    : (_session.ApplyCurrent(replacement) ? 1 : 0);
            }
            finally
            {
                doc.EndUndoRecord(undo);
            }

            UpdateUi();

            if (applied == 0)
                _status.Text = "That word is no longer where the check found it. Run the check again to refresh.";

            FollowCurrentIssue();
        }

        private void RunAndRefresh(Action action)
        {
            if (_session == null || _session.Current == null)
                return;

            action();
            UpdateUi();
            FollowCurrentIssue();
        }

        /// <summary>
        /// Selects the object holding the current word and zooms to it - on a
        /// sheet walk, switching to the word's sheet first. With no underlines,
        /// this is how the user sees which word the panel is talking about.
        /// </summary>
        private void FollowCurrentIssue()
        {
            SpellIssue issue = _session == null ? null : _session.Current;
            RhinoDoc doc = Document;

            if (issue != null && doc != null)
                _navigator.Follow(doc, issue);

            if (doc != null)
                doc.Views.Redraw();
        }

        // ---------------------------------------------------------------- display

        private void UpdateUi()
        {
            _updatingUi = true;
            try
            {
                SpellIssue issue = _session == null ? null : _session.Current;
                bool hasIssue = issue != null;

                _previous.Enabled = _next.Enabled = _session != null && _session.PendingCount > 1;
                _change.Enabled = _changeAll.Enabled = hasIssue;
                _ignore.Enabled = _ignoreAll.Enabled = _addWord.Enabled = hasIssue;

                _status.Text = BuildStatus();
                UpdateSheetLine();

                if (!hasIssue)
                {
                    _word.Text = string.Empty;
                    _location.Text = string.Empty;
                    _context.Text = _session == null
                        ? string.Empty
                        : _session.TotalCount == 0 ? "Nothing to correct." : "All misspellings have been reviewed.";
                    _suggestions.DataStore = new object[0];
                    _replacement.Text = string.Empty;
                    return;
                }

                _word.Text = issue.Word;
                _location.Text = DescribeLocation(issue);
                _context.Text = issue.Context ?? string.Empty;

                IReadOnlyList<string> suggestions = _session.CurrentSuggestions;
                _suggestions.DataStore = suggestions.Cast<object>().ToArray();

                if (suggestions.Count > 0)
                {
                    _suggestions.SelectedIndex = 0;
                    _replacement.Text = suggestions[0];
                }
                else
                {
                    _suggestions.SelectedIndex = -1;
                    _replacement.Text = issue.Word;
                }
            }
            finally
            {
                _updatingUi = false;
            }
        }

        /// <summary>
        /// On a sheet walk, says which sheet the word is on and how far through
        /// it the user is - the answer to "how much of this sheet is left" that
        /// a single count across the whole set cannot give.
        /// </summary>
        private void UpdateSheetLine()
        {
            SheetPosition position = _outcome != null && _outcome.IsSheetWalk
                ? SheetPosition.Of(_session)
                : null;

            _sheet.Visible = position != null;
            _sheet.Text = position == null
                ? string.Empty
                : string.Format("Sheet '{0}': word {1} of {2} - sheet {3} of {4} to fix",
                    position.SheetName, position.PositionOnSheet, position.PendingOnSheet,
                    position.SheetNumber, position.SheetCount);
        }

        private string BuildStatus()
        {
            if (_session == null)
                return IdleMessage;

            string label = _outcome == null ? null : _outcome.ScopeLabel;
            string prefix = string.IsNullOrEmpty(label) ? string.Empty : label + ": ";

            if (_session.TotalCount == 0)
            {
                // "No misspellings" is only good news if most of the drawing was
                // actually read. CAD annotation is largely ALL CAPS, which the
                // default options skip, so say how much was checked.
                string coverage = _outcome == null || _outcome.Coverage == null ? null : _outcome.Coverage.ShortSummary();
                int items = _outcome == null ? 0 : _outcome.TextItemCount;

                return coverage != null
                    ? prefix + "No misspellings found. " + coverage
                    : prefix + "No misspellings found in " + items + " text item(s).";
            }

            int pending = _session.PendingCount;
            if (pending == 0)
            {
                return prefix + string.Format("Done - {0} changed, {1} ignored.",
                    _session.CorrectedCount, _session.IgnoredCount);
            }

            if (_outcome != null && _outcome.IsSheetWalk)
            {
                return prefix + string.Format("{0} left to review ({1} changed, {2} ignored).",
                    pending, _session.CorrectedCount, _session.IgnoredCount);
            }

            int position = _session.Issues
                .Take(Math.Max(_session.CurrentIndex, 0))
                .Count(i => i.State == IssueState.Pending) + 1;

            return prefix + string.Format("{0} of {1} remaining ({2} changed, {3} ignored).",
                position, pending, _session.CorrectedCount, _session.IgnoredCount);
        }

        private static string DescribeLocation(SpellIssue issue)
        {
            string kind = string.IsNullOrEmpty(issue.ObjectKind) ? "Annotation" : issue.ObjectKind;

            if (issue.Slot == TextSlot.TextDotSecondary)
                kind += " (secondary text)";

            return string.IsNullOrEmpty(issue.LayerName)
                ? kind
                : kind + "  -  layer: " + issue.LayerName;
        }

        // ---------------------------------------------------------------- IPanel

        public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
        {
            // Pick up anything checked while this panel did not exist or was
            // closed - the normal case, since a command checks and then opens it.
            LoadOptionsFromPlugin();
            AdoptCurrentScan();
        }

        public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason)
        {
        }

        public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
        {
            var plugin = Plugin;
            if (plugin != null)
            {
                plugin.ScanChanged -= OnScanPublished;

                // Clear the published check too. Leaving one behind would have
                // the panel restore a dead check the next time it opened.
                plugin.PublishScan(null, null);
            }

            _outcome = null;
            _session = null;
        }
    }
}
