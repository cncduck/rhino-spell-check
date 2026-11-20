using System;
using System.Reflection;
using Rhino;
using Rhino.UI;
using RhinoSpellCheck.Core;
using RhinoSpellCheck.UI;

namespace RhinoSpellCheck.PanelTests
{
    /// <summary>
    /// Drives the Eto panel inside a hidden Rhino.
    ///
    /// What makes it possible, measured rather than assumed:
    /// - Rhino started with <c>WindowStyle.Hidden</c> initialises Eto (Wpf
    ///   platform), so panel controls construct normally.
    /// - <c>new SpellCheckPlugin()</c> throws - "this is the job of the plug-in
    ///   manager" - but Rhino has already loaded the plug-in by then, so
    ///   <c>SpellCheckPlugin.Instance</c> is a properly constructed one. Combined
    ///   with the assembly preload, it is *this build's* instance.
    /// - The panel can be constructed directly and its IPanel lifecycle methods
    ///   called, which is exactly how Rhino drives it.
    ///
    /// Private state is read by reflection. That is deliberate: these are tests
    /// of behaviour the user sees, and widening the panel's public surface just
    /// to observe it would be worse than the reflection.
    /// </summary>
    internal static class PanelProbe
    {
        private static readonly Guid SheetOne = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid SheetTwo = new Guid("bbbbbbbb-0000-0000-0000-000000000002");

        private static int _passed;
        private static int _failed;

        public static int Run()
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                Console.WriteLine("No active document - Rhino must be started with WindowStyle.Hidden.");
                return 2;
            }

            SpellCheckPlugin plugin = SpellCheckPlugin.Instance;
            if (plugin == null)
            {
                Console.WriteLine("SpellCheckPlugin.Instance is null - Rhino did not load the plug-in.");
                Console.WriteLine("The panel cannot be tested without it; is the plug-in installed?");
                return 2;
            }

            Console.WriteLine();

            IdlePanelPointsAtTheCommands(doc, plugin);
            PanelHasNoScanControls(doc, plugin);
            ScanPublishedBeforePanelExistsIsAdoptedOnShow(doc, plugin);
            ScanPublishedWhilePanelIsOpenIsAdopted(doc, plugin);
            StatusNamesTheScope(doc, plugin);
            NavigationMovesThroughTheIssues(doc, plugin);
            SheetWalkShowsWhichSheetAndMovesOn(doc, plugin);
            SelectionCheckHasNoSheetLine(doc, plugin);
            CleanCheckReplacesAnOldOne(doc, plugin);
            DigitsOptionReachesThePlugin(doc, plugin);
            ClearingTheScanEmptiesThePanel(doc, plugin);

            Reset(doc, plugin);

            Console.WriteLine();
            Console.WriteLine("passed: " + _passed + "   failed: " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------- tests

        /// <summary>
        /// The panel no longer scans, so an empty one has to say how to start
        /// - otherwise it is a dead end.
        /// </summary>
        private static void IdlePanelPointsAtTheCommands(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "idle panel";

            Reset(doc, plugin);
            SpellCheckPanel panel = ShowPanel(doc);

            string status = StatusText(panel) ?? string.Empty;

            Check(name + ": names CheckSpellSelected", status.Contains("CheckSpellSelected"), "status was: " + status);
            Check(name + ": names CheckSpellLayout", status.Contains("CheckSpellLayout"), "status was: " + status);
            Check(name + ": Change is disabled", ButtonEnabled(panel, "_change") == false);
        }

        /// <summary>
        /// One way in: the commands. A Scan button or scope list left in the
        /// panel would be a second, competing entry point.
        /// </summary>
        private static void PanelHasNoScanControls(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "review-only panel";

            SpellCheckPanel panel = ShowPanel(doc);

            Check(name + ": no Scan button", Field(panel, "_scan") == null);
            Check(name + ": no scope list", Field(panel, "_scope") == null);
            Check(name + ": no underline switch", Field(panel, "_showUnderlines") == null);
        }

        /// <summary>
        /// The bug that shipped once: a command checks, publishes, and only then
        /// opens the panel. Rhino does not create the panel until it is first
        /// opened, so the event is announced to nobody and the panel has to pick
        /// the check up when it is shown.
        /// </summary>
        private static void ScanPublishedBeforePanelExistsIsAdoptedOnShow(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "check published before the panel exists";

            Reset(doc, plugin);
            SpellSession session = Publish(doc, plugin, "Selection", 0, Words("wrold", "mispelt"));

            SpellCheckPanel panel = ShowPanel(doc);

            Check(name + ": panel adopted the session",
                ReferenceEquals(Field(panel, "_session"), session));

            Check(name + ": status is not idle",
                !(StatusText(panel) ?? string.Empty).Contains("Nothing checked yet"),
                "status was: " + StatusText(panel));

            Check(name + ": the current word is displayed",
                (LabelText(panel, "_word") ?? string.Empty).Length > 0,
                "word label was: '" + LabelText(panel, "_word") + "'");
        }

        /// <summary>The panel is already open when a command checks.</summary>
        private static void ScanPublishedWhilePanelIsOpenIsAdopted(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "check published while the panel is open";

            Reset(doc, plugin);
            SpellCheckPanel panel = ShowPanel(doc);

            SpellSession session = Publish(doc, plugin, "Selection", 0, Words("drawign"));

            // The panel adopts on the Eto main loop, so let it turn over.
            Pump();

            Check(name + ": panel adopted the session",
                ReferenceEquals(Field(panel, "_session"), session),
                "status was: " + StatusText(panel));
        }

        private static void StatusNamesTheScope(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "status names the scope";

            Reset(doc, plugin);
            Publish(doc, plugin, "Layout 'A-103'", 1, OnSheet(SheetOne, "A-103", "wrold"));

            SpellCheckPanel panel = ShowPanel(doc);
            string status = StatusText(panel) ?? string.Empty;

            Check(name + ": the sheet is named", status.Contains("A-103"), "status was: " + status);
        }

        /// <summary>
        /// The reported symptom once was "the panel does not cycle through the
        /// misspelled words", so cycling is asserted directly.
        /// </summary>
        private static void NavigationMovesThroughTheIssues(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "navigation cycles the words";

            Reset(doc, plugin);
            Publish(doc, plugin, "Selection", 0, Words("wrold", "mispelt", "drawign"));

            SpellCheckPanel panel = ShowPanel(doc);

            string first = LabelText(panel, "_word");
            Invoke(panel, "Navigate", true);
            string second = LabelText(panel, "_word");
            Invoke(panel, "Navigate", true);
            string third = LabelText(panel, "_word");

            Check(name + ": first word shown", !string.IsNullOrEmpty(first), "got '" + first + "'");
            Check(name + ": Next moves to a different word", first != second,
                "first '" + first + "' then '" + second + "'");
            Check(name + ": Next moves again", second != third,
                "second '" + second + "' then '" + third + "'");

            Invoke(panel, "Navigate", false);

            Check(name + ": Previous goes back", LabelText(panel, "_word") == second,
                "expected '" + second + "', got '" + LabelText(panel, "_word") + "'");
        }

        /// <summary>
        /// The point of CheckSpellLayout: the panel says which sheet the word is
        /// on and how far through it you are, and Next past the last word on a
        /// sheet moves to the next sheet.
        /// </summary>
        private static void SheetWalkShowsWhichSheetAndMovesOn(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "sheet walk";

            Reset(doc, plugin);

            var issues = new[]
            {
                OnSheet(SheetOne, "A-201", "wrold")[0],
                OnSheet(SheetOne, "A-201", "mispelt")[0],
                OnSheet(SheetTwo, "A-202", "drawign")[0]
            };
            Publish(doc, plugin, "5 layouts", 5, issues);

            SpellCheckPanel panel = ShowPanel(doc);

            string sheet = LabelText(panel, "_sheet") ?? string.Empty;
            Check(name + ": sheet line is shown", LabelVisible(panel, "_sheet") == true);
            Check(name + ": names the first sheet", sheet.Contains("A-201"), "sheet line: " + sheet);
            Check(name + ": position on the sheet", sheet.Contains("word 1 of 2"), "sheet line: " + sheet);
            Check(name + ": position in the set", sheet.Contains("sheet 1 of 2"), "sheet line: " + sheet);

            Invoke(panel, "Navigate", true);
            sheet = LabelText(panel, "_sheet") ?? string.Empty;
            Check(name + ": second word on the same sheet", sheet.Contains("A-201") && sheet.Contains("word 2 of 2"),
                "sheet line: " + sheet);

            Invoke(panel, "Navigate", true);
            sheet = LabelText(panel, "_sheet") ?? string.Empty;
            Check(name + ": Next past the last word moves to the next sheet",
                sheet.Contains("A-202") && sheet.Contains("sheet 2 of 2"), "sheet line: " + sheet);

            string status = StatusText(panel) ?? string.Empty;
            Check(name + ": status counts what is left across the set", status.Contains("3 left"),
                "status was: " + status);
        }

        private static void SelectionCheckHasNoSheetLine(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "selection check";

            Reset(doc, plugin);
            Publish(doc, plugin, "Selection", 0, Words("wrold"));

            SpellCheckPanel panel = ShowPanel(doc);

            Check(name + ": no sheet line", LabelVisible(panel, "_sheet") == false);
            Check(name + ": status names the selection",
                (StatusText(panel) ?? string.Empty).StartsWith("Selection"), "status was: " + StatusText(panel));
        }

        /// <summary>
        /// A clean result is published too, so the panel cannot go on offering
        /// words from an older check.
        /// </summary>
        private static void CleanCheckReplacesAnOldOne(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "clean check";

            Reset(doc, plugin);
            SpellCheckPanel panel = ShowPanel(doc);

            Publish(doc, plugin, "Selection", 0, Words("wrold"));
            Pump();
            Publish(doc, plugin, "Selection", 0, new SpellIssue[0]);
            Pump();

            Check(name + ": says no misspellings",
                (StatusText(panel) ?? string.Empty).Contains("No misspellings"), "status was: " + StatusText(panel));
            Check(name + ": no word left on show", string.IsNullOrEmpty(LabelText(panel, "_word")),
                "word was: " + LabelText(panel, "_word"));
        }

        /// <summary>
        /// The coverage report tells the user to untick "Ignore words next to
        /// digits", so that checkbox has to exist and do something.
        /// </summary>
        private static void DigitsOptionReachesThePlugin(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "digits option";

            bool original = plugin.TokenizerOptions.IgnoreWordsAdjacentToDigits;
            SpellCheckPanel panel = ShowPanel(doc);

            var box = Field(panel, "_ignoreDigits") as Eto.Forms.CheckBox;
            if (!Check(name + ": checkbox exists", box != null))
                return;

            Check(name + ": label matches the coverage report",
                box.Text == "Ignore words next to digits", "label was: " + box.Text);

            try
            {
                box.Checked = !original;
                Check(name + ": unticking reaches the plug-in",
                    plugin.TokenizerOptions.IgnoreWordsAdjacentToDigits == !original);
            }
            finally
            {
                box.Checked = original;
                plugin.TokenizerOptions.IgnoreWordsAdjacentToDigits = original;
                plugin.SavePreferences();
            }
        }

        private static void ClearingTheScanEmptiesThePanel(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            const string name = "clearing the check";

            Reset(doc, plugin);
            Publish(doc, plugin, "Selection", 0, Words("wrold"));

            SpellCheckPanel panel = ShowPanel(doc);

            plugin.PublishScan(doc, null);
            Pump();

            Check(name + ": panel drops the session", Field(panel, "_session") == null,
                "session was " + (Field(panel, "_session") == null ? "null" : "still set"));
            Check(name + ": back to the idle message",
                (StatusText(panel) ?? string.Empty).Contains("Nothing checked yet"), "status was: " + StatusText(panel));
        }

        // ------------------------------------------------------------ helpers

        private static SpellIssue[] Words(params string[] words)
        {
            var issues = new SpellIssue[words.Length];
            for (int i = 0; i < words.Length; i++)
                issues[i] = new SpellIssue(Guid.NewGuid(), TextSlot.Annotation, words[i], 0) { ObjectKind = "Text" };

            return issues;
        }

        private static SpellIssue[] OnSheet(Guid sheet, string sheetName, params string[] words)
        {
            SpellIssue[] issues = Words(words);
            foreach (SpellIssue issue in issues)
            {
                issue.SheetId = sheet;
                issue.SheetName = sheetName;
            }

            return issues;
        }

        private static SpellSession Publish(
            RhinoDoc doc, SpellCheckPlugin plugin, string label, int sheetsChecked, SpellIssue[] issues)
        {
            var session = new SpellSession(issues, null, null);
            plugin.PublishScan(doc, new ScanOutcome(session, issues.Length, null, label, sheetsChecked));
            return session;
        }

        private static SpellCheckPanel ShowPanel(RhinoDoc doc)
        {
            var panel = new SpellCheckPanel();
            panel.PanelShown(doc.RuntimeSerialNumber, ShowPanelReason.Show);
            return panel;
        }

        private static void Reset(RhinoDoc doc, SpellCheckPlugin plugin)
        {
            plugin.PublishScan(doc, null);
            Pump();
        }

        /// <summary>
        /// Turns the Eto main loop over so queued AsyncInvoke work runs. The panel
        /// adopts checks on that loop, so without this the event-driven paths
        /// would look broken when they are merely pending.
        /// </summary>
        private static void Pump()
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    Eto.Forms.Application.Instance.RunIteration();
                }
                catch (Exception)
                {
                    return;
                }
            }
        }

        private static object Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);

            return field == null ? null : field.GetValue(target);
        }

        private static void Invoke(object target, string method, params object[] args)
        {
            MethodInfo info = target.GetType().GetMethod(
                method, BindingFlags.Instance | BindingFlags.NonPublic);

            if (info == null)
                throw new MissingMethodException(target.GetType().Name + "." + method);

            info.Invoke(target, args);
        }

        private static string StatusText(object panel)
        {
            return LabelText(panel, "_status");
        }

        private static string LabelText(object panel, string field)
        {
            var label = Field(panel, field) as Eto.Forms.Label;
            return label == null ? null : label.Text;
        }

        private static bool? LabelVisible(object panel, string field)
        {
            var label = Field(panel, field) as Eto.Forms.Label;
            return label == null ? (bool?)null : label.Visible;
        }

        private static bool? ButtonEnabled(object panel, string field)
        {
            var button = Field(panel, field) as Eto.Forms.Button;
            return button == null ? (bool?)null : button.Enabled;
        }

        private static bool Check(string label, bool condition, string detail = null)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("  PASS  " + label);
            }
            else
            {
                _failed++;
                Console.WriteLine("  FAIL  " + label + (detail == null ? "" : "   [" + detail + "]"));
            }

            return condition;
        }
    }
}
