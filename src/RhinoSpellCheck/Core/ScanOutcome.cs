namespace RhinoSpellCheck.Core
{
    /// <summary>
    /// The result of one check, in a form a command can publish and the panel
    /// can adopt.
    ///
    /// The commands scan; the panel only reviews. This is what passes between
    /// them, through <c>SpellCheckPlugin.PublishScan</c>.
    /// </summary>
    public sealed class ScanOutcome
    {
        public ScanOutcome(SpellSession session, int textItemCount, ScanCoverage coverage, string scopeLabel)
            : this(session, textItemCount, coverage, scopeLabel, 0)
        {
        }

        public ScanOutcome(
            SpellSession session, int textItemCount, ScanCoverage coverage, string scopeLabel, int sheetsChecked)
        {
            Session = session;
            TextItemCount = textItemCount;
            Coverage = coverage;
            ScopeLabel = scopeLabel;
            SheetsChecked = sheetsChecked;
        }

        public SpellSession Session { get; private set; }

        /// <summary>How many pieces of text were harvested, for the status line.</summary>
        public int TextItemCount { get; private set; }

        /// <summary>How much of that text was actually checked. May be null.</summary>
        public ScanCoverage Coverage { get; private set; }

        /// <summary>What was checked, e.g. "Selection" or "Layout 'A-101'".</summary>
        public string ScopeLabel { get; private set; }

        /// <summary>
        /// How many layout sheets a sheet walk covered, clean ones included.
        /// Zero when the check was not a sheet walk.
        /// </summary>
        public int SheetsChecked { get; private set; }

        /// <summary>True when the issues are grouped by sheet and reviewed one sheet at a time.</summary>
        public bool IsSheetWalk
        {
            get { return SheetsChecked > 0; }
        }
    }
}
