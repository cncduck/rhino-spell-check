using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoSpellCheck.Core;
using RhinoSpellCheck.Documents;

namespace RhinoSpellCheck.Commands
{
    /// <summary>
    /// Checks one layout sheet or the whole set, then walks the misspellings in
    /// the panel one sheet at a time: the panel switches to each sheet as its
    /// first word comes up, and Next past a sheet's last word moves on to the
    /// next sheet with anything to fix.
    ///
    /// A sheet means everything visible on the page - its own annotations plus
    /// model-space text framed by its details. See
    /// <see cref="DocumentScanner.Harvest(RhinoDoc, RhinoPageView)"/>.
    /// </summary>
    public class CheckSpellLayoutCommand : Command
    {
        public override string EnglishName
        {
            get { return "CheckSpellLayout"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var plugin = SpellCheckPlugin.Instance;
            if (plugin == null || doc == null)
                return Result.Failure;

            RhinoPageView[] all = doc.Views.GetPageViews();
            if (all == null || all.Length == 0)
            {
                RhinoApp.WriteLine("Spell Check: this document has no layouts.");
                return Result.Nothing;
            }

            List<RhinoPageView> chosen;
            Result picked = ChooseLayouts(doc, LayoutSweep.InSetOrder(all), out chosen);
            if (picked != Result.Success || chosen.Count == 0)
                return picked == Result.Success ? Result.Nothing : picked;

            SpellEngine engine = CheckRunner.LoadEngine(plugin);
            if (engine == null)
                return Result.Failure;

            LayoutSweepResult sweep = LayoutSweep.Run(doc, chosen, engine, plugin.TokenizerOptions,
                (n, name) =>
                {
                    if (chosen.Count > 1)
                        RhinoApp.SetCommandPrompt(string.Format("Checking sheet {0} of {1}: {2}", n, chosen.Count, name));
                });

            PrintSweep(sweep);

            string label = chosen.Count == 1
                ? "Layout '" + chosen[0].PageName + "'"
                : chosen.Count + " layouts";

            CheckRunner.Publish(doc, plugin, engine, sweep.Issues, sweep.TextItems, sweep.Coverage,
                label, chosen.Count);

            return Result.Success;
        }

        private static Result ChooseLayouts(RhinoDoc doc, List<RhinoPageView> all, out List<RhinoPageView> chosen)
        {
            chosen = new List<RhinoPageView>();

            var active = doc.Views.ActiveView as RhinoPageView;

            var go = new GetOption();
            go.SetCommandPrompt(active == null
                ? "Check which layouts? Press Enter for all of them"
                : "Check which layouts? Press Enter for this one");

            int current = active == null ? -1 : go.AddOption("Current");
            int everything = go.AddOption("All");
            int named = go.AddOption("Named");

            // Enter takes the obvious default: this sheet if we are on one,
            // otherwise the whole set.
            go.AcceptNothing(true);

            GetResult result = go.Get();

            if (result == GetResult.Nothing)
            {
                if (active != null) chosen.Add(active);
                else chosen = all;
                return Result.Success;
            }

            if (result != GetResult.Option)
                return Result.Cancel;

            int index = go.Option().Index;

            if (index == current && active != null)
            {
                chosen.Add(active);
                return Result.Success;
            }

            if (index == everything)
            {
                chosen = all;
                return Result.Success;
            }

            if (index == named)
                return ChooseByName(all, out chosen);

            return Result.Cancel;
        }

        /// <summary>
        /// Asks for a sheet by name.
        ///
        /// Layout names routinely contain spaces ("A-101 PLANS"), which command
        /// options cannot, so this prints the list and takes a string rather than
        /// offering one option per sheet.
        /// </summary>
        private static Result ChooseByName(List<RhinoPageView> all, out List<RhinoPageView> chosen)
        {
            chosen = new List<RhinoPageView>();

            RhinoApp.WriteLine("Layouts in this document:");
            foreach (RhinoPageView view in all)
                RhinoApp.WriteLine("  " + view.PageName);

            string name = string.Empty;
            Result got = RhinoGet.GetString("Layout name", false, ref name);
            if (got != Result.Success)
                return got;

            name = (name ?? string.Empty).Trim();

            RhinoPageView match = all.FirstOrDefault(
                v => string.Equals(v.PageName, name, StringComparison.CurrentCultureIgnoreCase));

            if (match == null)
            {
                RhinoApp.WriteLine("Spell Check: no layout called '" + name + "'.");
                return Result.Nothing;
            }

            chosen.Add(match);
            return Result.Success;
        }

        /// <summary>
        /// One line per sheet, then the total. The panel is where the words are
        /// fixed; this is the record of which sheets needed it.
        /// </summary>
        private static void PrintSweep(LayoutSweepResult sweep)
        {
            const int wordsShown = 6;

            foreach (SheetResult sheet in sweep.Sheets)
            {
                string line;

                if (sheet.TextItems == 0)
                {
                    line = "nothing to check";
                }
                else if (sheet.Issues == 0)
                {
                    line = "no misspellings in " + sheet.TextItems + " text item(s)";
                }
                else
                {
                    string words = string.Join(", ", sheet.Words.Take(wordsShown));
                    if (sheet.Words.Count > wordsShown)
                        words += ", ...";

                    line = string.Format("{0} misspelling(s): {1}", sheet.Issues, words);
                }

                if (sheet.AlsoOnEarlierSheet > 0)
                    line += string.Format(" (+{0} shown on an earlier sheet)", sheet.AlsoOnEarlierSheet);

                RhinoApp.WriteLine("  " + sheet.SheetName + ": " + line);
            }

            int withIssues = sweep.Sheets.Count(s => s.Issues > 0);

            RhinoApp.WriteLine(sweep.Issues.Count == 0
                ? string.Format("Spell Check: no misspellings on {0} layout(s).", sweep.Sheets.Count)
                : string.Format("Spell Check: {0} misspelling(s) on {1} of {2} layout(s) - review them in the Spell Check panel.",
                    sweep.Issues.Count, withIssues, sweep.Sheets.Count));

            CheckRunner.PrintCoverage(sweep.Coverage);
        }
    }
}
