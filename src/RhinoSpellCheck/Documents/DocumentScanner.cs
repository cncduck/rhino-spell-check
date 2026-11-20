using System;
using System.Collections.Generic;
using System.Threading;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoSpellCheck.Core;

namespace RhinoSpellCheck.Documents
{
    /// <summary>A single block of text pulled out of the document.</summary>
    public sealed class TextRecord
    {
        public Guid ObjectId { get; set; }
        public TextSlot Slot { get; set; }
        public string Text { get; set; }
        public string ObjectKind { get; set; }
        public string LayerName { get; set; }

        /// <summary>True when the object lives on a layout sheet rather than in the model.</summary>
        public bool IsPageSpace { get; set; }

        /// <summary>For page-space objects, the layout they belong to.</summary>
        public Guid LayoutId { get; set; }

        /// <summary>The sheet this text was harvested from, when harvested by sheet.</summary>
        public Guid SheetId { get; set; }

        /// <summary>The name of <see cref="SheetId"/>.</summary>
        public string SheetName { get; set; }
    }

    /// <summary>
    /// Collects annotation text from a document and turns it into spelling issues.
    ///
    /// The two halves are deliberately separate: <c>Harvest</c> touches the
    /// document and must run on Rhino's main thread, while <see cref="FindIssues"/>
    /// is pure and carries the expensive Hunspell work.
    /// </summary>
    public static class DocumentScanner
    {
        /// <summary>How much text to show around the word in the panel.</summary>
        private const int ContextRadius = 40;

        private const string Ellipsis = "…";

        /// <summary>
        /// Harvests the given objects - normally the user's selection. Anything
        /// that carries no text (curves, blocks, details) is skipped rather than
        /// rejected, so a window selection over a whole drawing just works.
        /// </summary>
        public static IReadOnlyList<TextRecord> Harvest(RhinoDoc doc, IEnumerable<RhinoObject> objects)
        {
            var records = new List<TextRecord>();
            if (doc == null || objects == null)
                return records;

            var seen = new HashSet<Guid>();
            foreach (RhinoObject obj in objects)
            {
                if (obj == null || !seen.Add(obj.Id))
                    continue;

                ObjectType type = obj.ObjectType;
                if (type != ObjectType.Annotation && type != ObjectType.TextDot)
                    continue;

                AddRecordsFor(doc, obj, records, null);
            }

            return records;
        }

        /// <summary>
        /// Harvests everything visible on one layout sheet, tagging each record
        /// with the sheet so the panel can walk the set sheet by sheet.
        /// </summary>
        public static IReadOnlyList<TextRecord> Harvest(RhinoDoc doc, RhinoPageView page)
        {
            var records = new List<TextRecord>();
            if (doc == null || page == null)
                return records;

            foreach (RhinoObject obj in EnumerateLayout(doc, page))
                AddRecordsFor(doc, obj, records, page);

            return records;
        }

        private static void AddRecordsFor(RhinoDoc doc, RhinoObject obj, List<TextRecord> records, RhinoPageView sheet)
        {
            if (obj == null || obj.Geometry == null)
                return;

            var template = new TextRecord
            {
                ObjectId = obj.Id,
                ObjectKind = KindOf(obj),
                LayerName = LayerNameOf(doc, obj),

                // Which space the object lives in decides where navigation takes
                // the user: a page-space word is on its own sheet, a model-space
                // one is either seen through a detail or in a modelling view.
                IsPageSpace = obj.Attributes.Space == ActiveSpace.PageSpace,
                LayoutId = obj.Attributes.Space == ActiveSpace.PageSpace ? obj.Attributes.ViewportId : Guid.Empty,

                SheetId = sheet == null || sheet.MainViewport == null ? Guid.Empty : sheet.MainViewport.Id,
                SheetName = sheet == null ? null : sheet.PageName
            };

            var annotation = obj.Geometry as AnnotationBase;
            if (annotation != null)
            {
                AddRecord(records, template, TextSlot.Annotation, AnnotationText.Read(annotation));
                return;
            }

            var dot = obj.Geometry as TextDot;
            if (dot != null)
            {
                AddRecord(records, template, TextSlot.TextDotPrimary, dot.Text);
                AddRecord(records, template, TextSlot.TextDotSecondary, dot.SecondaryText);
            }
        }

        public static IReadOnlyList<SpellIssue> FindIssues(
            IEnumerable<TextRecord> records,
            SpellEngine engine,
            TokenizerOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return FindIssues(records, engine, options, cancellationToken, null);
        }

        /// <param name="coverage">
        /// Optional. Filled in with how much of the harvested text was actually
        /// checked and why the rest was not, so the check can report its own blind
        /// spots rather than letting an aggressive tokenizer default read as "this
        /// drawing is clean". Pass null to skip the bookkeeping.
        /// </param>
        public static IReadOnlyList<SpellIssue> FindIssues(
            IEnumerable<TextRecord> records,
            SpellEngine engine,
            TokenizerOptions options,
            CancellationToken cancellationToken,
            ScanCoverage coverage)
        {
            var issues = new List<SpellIssue>();
            if (records == null || engine == null)
                return issues;

            foreach (TextRecord record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(record.Text))
                    continue;

                int wordsEmitted = 0;

                foreach (WordToken token in Tokenizer.Tokenize(record.Text, options))
                {
                    wordsEmitted++;

                    if (engine.IsCorrect(token.Text))
                        continue;

                    var issue = new SpellIssue(record.ObjectId, record.Slot, token.Text, token.Start)
                    {
                        ObjectKind = record.ObjectKind,
                        LayerName = record.LayerName,
                        IsPageSpace = record.IsPageSpace,
                        LayoutId = record.LayoutId,
                        SheetId = record.SheetId,
                        SheetName = record.SheetName
                    };

                    DescribeContext(record.Text, token, issue);
                    issues.Add(issue);
                }

                if (coverage != null)
                    coverage.Observe(record.Text, options, wordsEmitted);
            }

            return issues;
        }

        /// <summary>
        /// Everything on one layout sheet. That is two different populations, and
        /// taking only the first would make the check feel broken on a real sheet:
        ///
        /// - annotations placed on the sheet itself, which are page space and
        ///   carry this layout's main viewport id;
        /// - annotations in *model* space that show through the sheet's details -
        ///   the dimensions and callouts on the drawing, which are typically not
        ///   page space at all.
        ///
        /// A detail is a window onto model space, so "is this visible on the
        /// page" is exactly <c>RhinoViewport.IsVisible</c> against that detail's
        /// viewport. Probed rather than assumed: it correctly rejects geometry
        /// outside the detail's view.
        /// </summary>
        private static IEnumerable<RhinoObject> EnumerateLayout(RhinoDoc doc, RhinoPageView page)
        {
            var results = new List<RhinoObject>();

            if (doc == null || page == null || page.MainViewport == null)
                return results;

            var seen = new HashSet<Guid>();

            // The sheet's own annotations.
            ObjectEnumeratorSettings onSheet = TextSettings();
            onSheet.ViewportFilter = page.MainViewport;

            foreach (RhinoObject obj in doc.Objects.GetObjectList(onSheet))
            {
                if (obj != null && seen.Add(obj.Id))
                    results.Add(obj);
            }

            DetailViewObject[] details = page.GetDetailViews();
            if (details == null || details.Length == 0)
                return results;

            // Model-space annotations framed by any of this sheet's details.
            foreach (RhinoObject obj in doc.Objects.GetObjectList(TextSettings()))
            {
                if (obj == null || obj.Geometry == null)
                    continue;
                if (obj.Attributes.Space != ActiveSpace.ModelSpace)
                    continue;
                if (seen.Contains(obj.Id))
                    continue;

                BoundingBox box;
                try
                {
                    box = obj.Geometry.GetBoundingBox(true);
                }
                catch (Exception)
                {
                    continue;
                }

                if (box.IsValid && FindDetailShowing(details, box) != null && seen.Add(obj.Id))
                    results.Add(obj);
            }

            return results;
        }

        private static ObjectEnumeratorSettings TextSettings()
        {
            return new ObjectEnumeratorSettings
            {
                ObjectTypeFilter = ObjectType.Annotation | ObjectType.TextDot,
                NormalObjects = true,
                LockedObjects = true,
                HiddenObjects = true,
                ActiveObjects = true,
                ReferenceObjects = false,
                DeletedObjects = false,
                IncludeLights = false,
                IncludeGrips = false,
                // Text inside block definitions is shared by every instance, so
                // editing it would silently change other parts of the model.
                IdefObjects = false
            };
        }

        /// <summary>
        /// The first switched-on detail that shows <paramref name="worldBox"/>,
        /// or null. Shared with navigation, so "this word is on this sheet" and
        /// "zoom the sheet to this word" can never disagree about which detail.
        /// </summary>
        public static DetailViewObject FindDetailShowing(DetailViewObject[] details, BoundingBox worldBox)
        {
            if (details == null || !worldBox.IsValid)
                return null;

            foreach (DetailViewObject detail in details)
            {
                // A detail turned off on the sheet shows nothing.
                if (detail == null || detail.Viewport == null || !detail.Attributes.Visible)
                    continue;

                try
                {
                    if (Shows(detail, worldBox))
                        return detail;
                }
                catch (Exception)
                {
                    // A malformed detail should not abort the whole check.
                }
            }

            return null;
        }

        /// <summary>
        /// Whether the detail shows the box.
        ///
        /// Decided by mapping the box through the detail onto the page and
        /// testing it against the detail's own frame - which is the definition
        /// of "appears in this detail". The detail viewport's
        /// <c>IsVisible</c> was used before, and measured headless it is wrong
        /// in both directions: a detail framing -20..20 called text at x = 206
        /// visible (it maps to x = 24 on an 11-inch sheet whose detail spans 1
        /// to 6), and after re-framing the same detail elsewhere it called text
        /// squarely in the middle of it invisible, with a page-to-model ratio of
        /// zero. Its frustum evidently does not track the window it draws
        /// through. The mapping does.
        ///
        /// A perspective detail still needs <c>IsVisible</c> as well: a
        /// projective mapping cannot tell what is in front of the camera from
        /// what is behind it. Drawing sheets are parallel projections, so that
        /// path is the exception.
        /// </summary>
        private static bool Shows(DetailViewObject detail, BoundingBox worldBox)
        {
            bool? inFrame = LandsInsideFrame(detail, worldBox);

            if (detail.Viewport.IsParallelProjection && inFrame.HasValue)
                return inFrame.Value;

            return detail.Viewport.IsVisible(worldBox) && inFrame.GetValueOrDefault(true);
        }

        /// <summary>
        /// Whether the box, mapped onto the page, overlaps the detail's frame.
        /// Null when that cannot be worked out.
        /// </summary>
        private static bool? LandsInsideFrame(DetailViewObject detail, BoundingBox worldBox)
        {
            if (detail.Geometry == null)
                return null;

            BoundingBox frame = detail.Geometry.GetBoundingBox(true);
            if (!frame.IsValid)
                return null;

            BoundingBox onPage = worldBox;
            if (!onPage.Transform(detail.WorldToPageTransform) || !onPage.IsValid)
                return null;

            return onPage.Max.X >= frame.Min.X && onPage.Min.X <= frame.Max.X &&
                   onPage.Max.Y >= frame.Min.Y && onPage.Min.Y <= frame.Max.Y;
        }

        private static void AddRecord(List<TextRecord> records, TextRecord template, TextSlot slot, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            records.Add(new TextRecord
            {
                ObjectId = template.ObjectId,
                Slot = slot,
                Text = text,
                ObjectKind = template.ObjectKind,
                LayerName = template.LayerName,
                IsPageSpace = template.IsPageSpace,
                LayoutId = template.LayoutId,
                SheetId = template.SheetId,
                SheetName = template.SheetName
            });
        }

        private static string LayerNameOf(RhinoDoc doc, RhinoObject obj)
        {
            try
            {
                int index = obj.Attributes.LayerIndex;
                if (index >= 0 && index < doc.Layers.Count)
                    return doc.Layers[index].FullPath;
            }
            catch (Exception)
            {
                // A malformed attribute table should not abort the whole check.
            }

            return string.Empty;
        }

        private static string KindOf(RhinoObject obj)
        {
            if (obj.Geometry is TextDot)
                return "Text dot";
            if (obj.Geometry is Leader)
                return "Leader";
            if (obj.Geometry is TextEntity)
                return "Text";
            if (obj.Geometry is Dimension)
                return "Dimension";

            try
            {
                return obj.ShortDescription(false);
            }
            catch (Exception)
            {
                return "Annotation";
            }
        }

        /// <summary>
        /// A short excerpt of the word's own line, for the panel. Trimmed ends are
        /// marked with an ellipsis so a cut-off sentence does not read as the
        /// whole note.
        /// </summary>
        private static void DescribeContext(string text, WordToken token, SpellIssue issue)
        {
            int lineStart = token.Start == 0
                ? 0
                : text.LastIndexOf('\n', Math.Min(token.Start - 1, text.Length - 1)) + 1;

            int lineEnd = text.IndexOf('\n', token.Start);
            if (lineEnd < 0)
                lineEnd = text.Length;

            int from = Math.Max(lineStart, token.Start - ContextRadius);
            int to = Math.Min(lineEnd, token.End + ContextRadius);

            string context = text.Substring(from, to - from);
            int offsetInContext = token.Start - from;

            if (from > lineStart)
            {
                context = Ellipsis + context;
                offsetInContext += Ellipsis.Length;
            }
            if (to < lineEnd)
                context = context + Ellipsis;

            issue.Context = context.Replace('\r', ' ').Replace('\t', ' ');
            issue.ContextOffset = offsetInContext;
        }
    }
}
