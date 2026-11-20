using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Rhino;
using Rhino.Display;
using RhinoSpellCheck.Core;

namespace RhinoSpellCheck.Documents
{
    /// <summary>What one sheet contributed to a sweep.</summary>
    public sealed class SheetResult
    {
        public Guid SheetId { get; internal set; }
        public string SheetName { get; internal set; }
        public int TextItems { get; internal set; }

        /// <summary>Misspellings reviewed on this sheet.</summary>
        public int Issues { get; internal set; }

        /// <summary>
        /// Misspellings that also show on this sheet but are reviewed on an
        /// earlier one - model text seen through details on both.
        /// </summary>
        public int AlsoOnEarlierSheet { get; internal set; }

        /// <summary>The distinct misspelled words reviewed on this sheet, most frequent first.</summary>
        public IReadOnlyList<string> Words { get; internal set; }
    }

    /// <summary>The whole sweep, ready to print and to publish to the panel.</summary>
    public sealed class LayoutSweepResult
    {
        /// <summary>Every issue, sheet by sheet, each occurrence on the first sheet it appears.</summary>
        public List<SpellIssue> Issues { get; internal set; }

        public List<SheetResult> Sheets { get; internal set; }

        public int TextItems { get; internal set; }

        public ScanCoverage Coverage { get; internal set; }
    }

    /// <summary>
    /// Checks a list of layout sheets in order and lays the result out for a
    /// sheet-by-sheet review.
    /// </summary>
    public static class LayoutSweep
    {
        /// <summary>
        /// The layouts in the order they appear in the set - Rhino's page
        /// number, which is the tab order - rather than the order Rhino happens
        /// to hold them in.
        /// </summary>
        public static List<RhinoPageView> InSetOrder(IEnumerable<RhinoPageView> pages)
        {
            if (pages == null)
                return new List<RhinoPageView>();

            return pages
                .Where(p => p != null && p.MainViewport != null)
                .Select((p, index) => new { Page = p, Index = index })
                .OrderBy(x => SafePageNumber(x.Page))
                .ThenBy(x => x.Index)
                .Select(x => x.Page)
                .ToList();
        }

        private static int SafePageNumber(RhinoPageView page)
        {
            try
            {
                return page.PageNumber;
            }
            catch (Exception)
            {
                return int.MaxValue;
            }
        }

        /// <param name="progress">Optional; told the 1-based sheet number and name before each sheet is checked.</param>
        public static LayoutSweepResult Run(
            RhinoDoc doc,
            IList<RhinoPageView> pages,
            SpellEngine engine,
            TokenizerOptions options,
            Action<int, string> progress = null)
        {
            var perSheet = new List<IReadOnlyList<SpellIssue>>();
            var sheets = new List<SheetResult>();
            var coverage = new ScanCoverage();
            int textItems = 0;

            for (int i = 0; i < pages.Count; i++)
            {
                RhinoPageView page = pages[i];

                if (progress != null)
                    progress(i + 1, page.PageName);

                IReadOnlyList<TextRecord> records = DocumentScanner.Harvest(doc, page);

                var sheetCoverage = new ScanCoverage();
                IReadOnlyList<SpellIssue> issues = DocumentScanner.FindIssues(
                    records, engine, options, default(CancellationToken), sheetCoverage);

                perSheet.Add(issues);
                coverage.Merge(sheetCoverage);
                textItems += records.Count;

                sheets.Add(new SheetResult
                {
                    SheetId = page.MainViewport.Id,
                    SheetName = page.PageName,
                    TextItems = records.Count,
                    AlsoOnEarlierSheet = issues.Count // corrected below, once duplicates are known
                });
            }

            int dropped;
            List<SpellIssue> combined = SheetWalk.Combine(perSheet, out dropped);

            foreach (SheetResult sheet in sheets)
            {
                List<SpellIssue> mine = combined.Where(x => x.SheetId == sheet.SheetId).ToList();

                sheet.Issues = mine.Count;
                sheet.AlsoOnEarlierSheet -= mine.Count;
                sheet.Words = mine
                    .GroupBy(x => x.Word, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.Count() > 1 ? g.Key + " x" + g.Count() : g.Key)
                    .ToList();
            }

            return new LayoutSweepResult
            {
                Issues = combined,
                Sheets = sheets,
                TextItems = textItems,
                Coverage = coverage
            };
        }
    }
}
