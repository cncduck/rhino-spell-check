using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoSpellCheck.Core;
using RhinoSpellCheck.Documents;

namespace RhinoSpellCheck.Commands
{
    /// <summary>
    /// Checks the spelling of the selected text, then hands the misspellings to
    /// the panel to step through.
    ///
    /// Takes a preselection, or asks for one. Only text, leaders, dimensions and
    /// text dots can be picked, so a window over a whole drawing selects just
    /// the things that have words in them.
    /// </summary>
    public class CheckSpellSelectedCommand : Command
    {
        public override string EnglishName
        {
            get { return "CheckSpellSelected"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var plugin = SpellCheckPlugin.Instance;
            if (plugin == null || doc == null)
                return Result.Failure;

            var go = new GetObject();
            go.SetCommandPrompt("Select text, leaders, dimensions or text dots to check");
            go.GeometryFilter = ObjectType.Annotation | ObjectType.TextDot;
            go.EnablePreSelect(true, true);
            go.SubObjectSelect = false;
            go.GroupSelect = true;
            go.GetMultiple(1, 0);

            if (go.CommandResult() != Result.Success)
                return go.CommandResult();

            List<RhinoObject> objects = go.Objects()
                .Select(o => o.Object())
                .Where(o => o != null)
                .ToList();

            SpellEngine engine = CheckRunner.LoadEngine(plugin);
            if (engine == null)
                return Result.Failure;

            IReadOnlyList<TextRecord> records = DocumentScanner.Harvest(doc, objects);

            var coverage = new ScanCoverage();
            List<SpellIssue> issues = DocumentScanner.FindIssues(
                records, engine, plugin.TokenizerOptions,
                default(System.Threading.CancellationToken), coverage).ToList();

            RhinoApp.WriteLine(issues.Count == 0
                ? string.Format("Spell Check: no misspellings in {0} text item(s).", records.Count)
                : string.Format("Spell Check: {0} misspelling(s) in {1} text item(s) - review them in the Spell Check panel.",
                    issues.Count, records.Count));

            CheckRunner.PrintCoverage(coverage);

            CheckRunner.Publish(doc, plugin, engine, issues, records.Count, coverage, "Selection", 0);
            return Result.Success;
        }
    }
}
