using System.Collections.Generic;
using Rhino;
using Rhino.UI;
using RhinoSpellCheck.Core;
using RhinoSpellCheck.Documents;
using RhinoSpellCheck.UI;

namespace RhinoSpellCheck.Commands
{
    /// <summary>
    /// What both commands do around the scan itself: load the dictionary, hand
    /// the result to the panel, and print how much was really checked.
    /// </summary>
    internal static class CheckRunner
    {
        /// <summary>The dictionary, or null after printing why it could not load.</summary>
        public static SpellEngine LoadEngine(SpellCheckPlugin plugin)
        {
            if (!plugin.IsEngineLoaded)
            {
                RhinoApp.WriteLine("Spell Check: loading the dictionary...");
                RhinoApp.Wait();
            }

            string error;
            SpellEngine engine = plugin.GetEngine(out error);
            if (engine == null)
                RhinoApp.WriteLine("Spell Check: " + error);

            return engine;
        }

        /// <summary>
        /// Publishes the check and brings the panel up on the first word.
        ///
        /// A clean result is still published, so a panel left open on an older
        /// check is replaced by "no misspellings" rather than going on offering
        /// words that belong to something else. It just does not open the panel
        /// to say so - the command line already has.
        /// </summary>
        public static void Publish(
            RhinoDoc doc,
            SpellCheckPlugin plugin,
            SpellEngine engine,
            IList<SpellIssue> issues,
            int textItemCount,
            ScanCoverage coverage,
            string label,
            int sheetsChecked)
        {
            var store = new RhinoTextStore(doc);
            store.FormattingFlattened += (s, id) =>
                RhinoApp.WriteLine(
                    "Spell Check: mixed formatting in one annotation was simplified when applying the correction.");

            var session = new SpellSession(issues, engine, store);
            plugin.PublishScan(doc, new ScanOutcome(session, textItemCount, coverage, label, sheetsChecked));

            if (issues.Count > 0 && !Panels.IsPanelVisible(SpellCheckPanel.PanelId))
                Panels.OpenPanel(SpellCheckPanel.PanelId);

            doc.Views.Redraw();
        }

        /// <summary>
        /// Prints the coverage breakdown. Always worth printing: "no
        /// misspellings" over a small fraction of the text is not a clean bill of
        /// health, and on a typical drawing most text is skipped as ALL CAPS.
        /// </summary>
        public static void PrintCoverage(ScanCoverage coverage)
        {
            if (coverage == null)
                return;

            foreach (string line in coverage.Describe())
                RhinoApp.WriteLine("  " + line);
        }
    }
}
