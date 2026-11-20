using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoSpellCheck.Core;
using RhinoSpellCheck.Documents;

namespace RhinoSpellCheck.Harness
{
    internal static class IntegrationTests
    {
        private static int _passed;
        private static int _failed;

        private static RhinoDoc _doc;
        private static SpellEngine _engine;
        private static string _userDictionary;

        public static int RunAll()
        {
            _userDictionary = Path.Combine(Path.GetTempPath(), "rsc-harness-" + Guid.NewGuid().ToString("N") + ".dic");

            try
            {
                _doc = RhinoDoc.CreateHeadless(null);
                if (_doc == null)
                {
                    Console.WriteLine("Could not create a headless document.");
                    return 2;
                }

                _engine = SpellEngine.Create(FindDictionaryDirectory(), "en_US", _userDictionary);
                Console.WriteLine("Rhino " + RhinoApp.Version + " running headless; dictionary loaded.");
                Console.WriteLine();

                PlainTextIsScannedAndCorrected();
                RichTextKeepsItsFormatting();
                LeaderIsScannedAndCorrected();
                TextDotBothSlotsAreCorrected();
                TextFieldsAreSkipped();
                MultiLineOffsetsAreCorrect();
                ContextIsTrimmedWithARealEllipsis();
                DimensionOverrideKeepsItsMeasurement();
                ModelSpaceTextIsTaggedAsModelSpace();
                PageSpaceTextCarriesItsLayout();
                NavigationNeverZoomsADetail();
                StaleEditIsRefused();
                MultipleFixesInOneAnnotation();
                SelectionHarvestTakesOnlyText();
                ActiveLayoutScopeCollectsTheWholeSheet();
                PerLayoutScanAttributesIssuesToTheRightSheet();
                DetailTextIsFramedOnItsSheet();
                SweepWalksSheetsInSetOrderWithoutTwins();
            }
            catch (Exception ex)
            {
                Console.WriteLine("HARNESS ERROR: " + ex);
                _failed++;
            }
            finally
            {
                try { if (File.Exists(_userDictionary)) File.Delete(_userDictionary); } catch (Exception) { }
            }

            Console.WriteLine();
            Console.WriteLine("passed: " + _passed + "   failed: " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// "Current layout" has to mean what the user sees on the page, which is
        /// two populations: the sheet's own page-space annotations, and
        /// model-space annotations framed by the sheet's details. Collecting only
        /// the first would return a title block and miss every dimension on the
        /// drawing.
        ///
        /// The exclusions matter as much as the inclusions - model text outside
        /// every detail's view, and text belonging to a different sheet, must not
        /// come back.
        /// </summary>
        private static void ActiveLayoutScopeCollectsTheWholeSheet()
        {
            const string name = "active layout scope";

            Rhino.Display.RhinoPageView page;
            Rhino.Display.RhinoPageView other;
            try
            {
                page = _doc.Views.AddPageView("Scope Sheet", 11.0, 8.5);
                other = _doc.Views.AddPageView("Other Sheet", 11.0, 8.5);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  " + name + " (page views unavailable headless: " + ex.Message + ")");
                return;
            }

            if (page == null || page.MainViewport == null || other == null)
            {
                Console.WriteLine("  SKIP  " + name + " (headless Rhino created no page view)");
                return;
            }

            DetailViewObject detail;
            try
            {
                detail = page.AddDetailView("scope detail",
                    new Point2d(1, 1), new Point2d(6, 5), Rhino.Display.DefinedViewportProjection.Top);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  " + name + " (details unavailable headless: " + ex.Message + ")");
                return;
            }

            if (detail == null || detail.Viewport == null)
            {
                Console.WriteLine("  SKIP  " + name + " (headless Rhino created no detail)");
                return;
            }

            // Frame a small region of model space, so "outside the detail" is a
            // position we control rather than a guess.
            detail.Viewport.SetCameraTarget(new Point3d(0, 0, 0), false);
            detail.Viewport.ZoomBoundingBox(new BoundingBox(-20, -20, -1, 20, 20, 1));
            detail.CommitViewportChanges();

            Guid onSheet = AddPageText(page, "sheet noet here", new Point3d(1, 7, 0));
            Guid otherSheet = AddPageText(other, "othr sheet text", new Point3d(1, 7, 0));
            Guid insideDetail = AddText("inside detial view", new Point3d(0, 0, 0));
            Guid outsideDetail = AddText("far away textt", new Point3d(5000, 5000, 0));

            // A headless document has no active view to set, so the layout is
            // named explicitly. That is the same code path the ActiveLayout scope
            // takes once it has resolved which page the user is looking at.
            IReadOnlyList<TextRecord> records = DocumentScanner.Harvest(_doc, page);
            var ids = new HashSet<Guid>(records.Select(r => r.ObjectId));

            Check(name + ": sheet annotation collected", ids.Contains(onSheet));

            Check(name + ": model text inside a detail collected", ids.Contains(insideDetail),
                "a detail is a window onto model space, so its contents are on the page");

            Check(name + ": model text outside every detail excluded", !ids.Contains(outsideDetail),
                "text at 5000,5000 is not visible on this sheet");

            Check(name + ": another sheet's annotation excluded", !ids.Contains(otherSheet));

            // Not on a layout must yield nothing, never a silent fall back to
            // checking the whole document.
            Check(name + ": no layout yields nothing",
                DocumentScanner.Harvest(_doc, (Rhino.Display.RhinoPageView)null).Count == 0);

            // The walk groups by these tags, and navigation uses them to find
            // the sheet for model text - which otherwise has no link to a sheet.
            Check(name + ": every record is tagged with this sheet",
                records.Count > 0 && records.All(r => r.SheetId == page.MainViewport.Id && r.SheetName == "Scope Sheet"),
                string.Join(", ", records.Select(r => r.SheetName + "/" + r.SheetId)));

            _doc.Objects.Delete(onSheet, true);
            _doc.Objects.Delete(otherSheet, true);
            _doc.Objects.Delete(insideDetail, true);
            _doc.Objects.Delete(outsideDetail, true);
        }

        /// <summary>
        /// The whole point of SpellCheckLayout's sweep is telling you *which*
        /// sheet to open, so an issue appearing under the wrong heading would make
        /// the command worse than a whole-document scan rather than better.
        /// </summary>
        private static void PerLayoutScanAttributesIssuesToTheRightSheet()
        {
            const string name = "per-layout scan";

            Rhino.Display.RhinoPageView first;
            Rhino.Display.RhinoPageView second;
            try
            {
                first = _doc.Views.AddPageView("Sweep A", 11.0, 8.5);
                second = _doc.Views.AddPageView("Sweep B", 11.0, 8.5);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  " + name + " (page views unavailable headless: " + ex.Message + ")");
                return;
            }

            if (first == null || second == null || first.MainViewport == null || second.MainViewport == null)
            {
                Console.WriteLine("  SKIP  " + name + " (headless Rhino created no page view)");
                return;
            }

            Guid a = AddPageText(first, "sheet aaa mispelt", new Point3d(1, 7, 0));
            Guid b = AddPageText(second, "sheet bbb drawign", new Point3d(1, 7, 0));

            // The command enumerates sheets this way, so the test should too.
            Rhino.Display.RhinoPageView[] pages = _doc.Views.GetPageViews();
            Check(name + ": GetPageViews finds both sheets",
                pages != null && pages.Any(p => p.PageName == "Sweep A") && pages.Any(p => p.PageName == "Sweep B"),
                "got " + (pages == null ? "null" : string.Join(", ", pages.Select(p => p.PageName))));

            IReadOnlyList<SpellIssue> onFirst = DocumentScanner.FindIssues(
                DocumentScanner.Harvest(_doc, first), _engine, TokenizerOptions.Default);

            IReadOnlyList<SpellIssue> onSecond = DocumentScanner.FindIssues(
                DocumentScanner.Harvest(_doc, second), _engine, TokenizerOptions.Default);

            Check(name + ": first sheet reports its own misspelling",
                onFirst.Any(i => i.Word == "mispelt"),
                "got [" + string.Join(", ", onFirst.Select(i => i.Word)) + "]");

            Check(name + ": first sheet does not report the other's",
                !onFirst.Any(i => i.Word == "drawign"));

            Check(name + ": second sheet reports its own misspelling",
                onSecond.Any(i => i.Word == "drawign"),
                "got [" + string.Join(", ", onSecond.Select(i => i.Word)) + "]");

            Check(name + ": second sheet does not report the other's",
                !onSecond.Any(i => i.Word == "mispelt"));

            Check(name + ": every issue points at an object on its own sheet",
                onFirst.All(i => i.ObjectId == a) && onSecond.All(i => i.ObjectId == b));

            _doc.Objects.Delete(a, true);
            _doc.Objects.Delete(b, true);
        }

        private static Guid AddPageText(Rhino.Display.RhinoPageView page, string text, Point3d at)
        {
            var plane = new Plane(at, Vector3d.ZAxis);
            TextEntity entity = TextEntity.Create(text, plane, _doc.DimStyles.Current, false, 0, 0);
            entity.TextHeight = 0.25;

            var attributes = new ObjectAttributes
            {
                Space = ActiveSpace.PageSpace,
                ViewportId = page.MainViewport.Id
            };

            return _doc.Objects.AddText(entity, attributes);
        }

        // ------------------------------------------------------------- tests

        private static void PlainTextIsScannedAndCorrected()
        {
            const string name = "plain text scan + fix";
            Guid id = AddText("This drawign has a mispelled word", new Point3d(0, 0, 0));

            var issues = Scan();
            var words = WordsFor(issues, id);
            Check(name + ": finds both misspellings",
                words.SequenceEqual(new[] { "drawign", "mispelled" }),
                "got [" + string.Join(", ", words) + "]");

            var session = NewSession(issues);
            Check(name + ": applies first fix", session.ApplyCurrent("drawing"));
            Check(name + ": applies second fix", session.ApplyCurrent("misspelled"));

            string result = ReadAnnotation(id);
            Check(name + ": document updated",
                result == "This drawing has a misspelled word", "got '" + result + "'");
        }

        private static void RichTextKeepsItsFormatting()
        {
            const string name = "rich text fix preserves runs";

            var style = _doc.DimStyles.Current;
            TextEntity entity = TextEntity.Create("placeholder", Plane.WorldXY, style, false, 0, 0);
            entity.TextHeight = 1.0;
            entity.SetRichText(
                @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil Arial;}}\pard\f0\fs20 Sample \b bold drawign\b0  tail\par}",
                style);

            Guid id = _doc.Objects.AddText(entity);
            Check(name + ": object added", id != Guid.Empty);

            var annotation = (AnnotationBase)_doc.Objects.FindId(id).Geometry;
            Check(name + ": rhino kept rtf formatting", annotation.TextHasRtfFormatting,
                "plain='" + annotation.PlainTextWithFields + "'");

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            Check(name + ": finds the misspelling inside the bold run",
                issues.Count == 1 && issues[0].Word == "drawign",
                "got [" + string.Join(", ", issues.Select(i => i.Word)) + "]");

            if (issues.Count != 1)
                return;

            var session = NewSession(issues);
            Check(name + ": applies fix", session.ApplyCurrent("drawing"));

            var updated = (AnnotationBase)_doc.Objects.FindId(id).Geometry;
            Check(name + ": text corrected",
                updated.PlainTextWithFields.Contains("bold drawing"),
                "got '" + updated.PlainTextWithFields + "'");

            Check(name + ": still rtf after the edit", updated.TextHasRtfFormatting);
            Check(name + ": bold run survived",
                updated.RichText.IndexOf("\\b", StringComparison.Ordinal) >= 0,
                "rtf='" + Trim(updated.RichText) + "'");
        }

        private static void LeaderIsScannedAndCorrected()
        {
            const string name = "leader scan + fix";

            // Leader points are expressed in the leader plane's 2-D coordinates.
            var points = new[] { new Point2d(20, 0), new Point2d(25, 5) };
            Guid id = _doc.Objects.AddLeader("mesurement note", Plane.WorldXY, points);
            Check(name + ": leader added", id != Guid.Empty);
            if (id == Guid.Empty)
                return;

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            Check(name + ": finds misspelling",
                issues.Count == 1 && issues[0].Word == "mesurement",
                "got [" + string.Join(", ", issues.Select(i => i.Word)) + "]");

            Check(name + ": reported as Leader",
                issues.Count == 1 && issues[0].ObjectKind == "Leader",
                issues.Count == 1 ? issues[0].ObjectKind : "n/a");

            if (issues.Count != 1)
                return;

            var session = NewSession(issues);
            Check(name + ": applies fix", session.ApplyCurrent("measurement"));
            Check(name + ": document updated",
                ReadAnnotation(id) == "measurement note", "got '" + ReadAnnotation(id) + "'");
        }

        private static void TextDotBothSlotsAreCorrected()
        {
            const string name = "text dot both slots";

            var dot = new TextDot("primry label", new Point3d(40, 0, 0));
            dot.SecondaryText = "secondry label";
            Guid id = _doc.Objects.AddTextDot(dot);

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            Check(name + ": finds both slots",
                issues.Count == 2 &&
                issues.Any(i => i.Slot == TextSlot.TextDotPrimary && i.Word == "primry") &&
                issues.Any(i => i.Slot == TextSlot.TextDotSecondary && i.Word == "secondry"),
                "got [" + string.Join(", ", issues.Select(i => i.Slot + ":" + i.Word)) + "]");

            if (issues.Count != 2)
                return;

            var session = NewSession(issues);
            session.ApplyCurrent("primary");
            session.ApplyCurrent("secondary");

            var updated = (TextDot)_doc.Objects.FindId(id).Geometry;
            Check(name + ": primary updated", updated.Text == "primary label", "got '" + updated.Text + "'");
            Check(name + ": secondary updated", updated.SecondaryText == "secondary label",
                "got '" + updated.SecondaryText + "'");
        }

        private static void TextFieldsAreSkipped()
        {
            const string name = "text fields skipped";
            Guid id = AddText("Scale %<ModelUnits>% drawign", new Point3d(60, 0, 0));

            var words = WordsFor(Scan(), id);
            Check(name + ": field contents not reported",
                !words.Contains("ModelUnits"), "got [" + string.Join(", ", words) + "]");
            Check(name + ": real misspelling still reported",
                words.Contains("drawign"), "got [" + string.Join(", ", words) + "]");
        }

        private static void MultiLineOffsetsAreCorrect()
        {
            const string name = "multi-line offsets";
            Guid id = AddText("First line is fine\nSecond line has drawign here", new Point3d(80, 0, 0));

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            Check(name + ": one misspelling", issues.Count == 1,
                "got [" + string.Join(", ", issues.Select(i => i.Word)) + "]");
            if (issues.Count != 1)
                return;

            SpellIssue issue = issues[0];

            string text = ReadAnnotation(id);
            bool offsetOk = issue.Start >= 0 && issue.End <= text.Length &&
                            text.Substring(issue.Start, issue.Length) == issue.Word;

            Check(name + ": offset points at the word", offsetOk, "start=" + issue.Start);

            Check(name + ": context is the word's own line",
                issue.Context != null && issue.Context.StartsWith("Second line") && !issue.Context.Contains("First"),
                "context='" + issue.Context + "'");

            var session = NewSession(issues);
            Check(name + ": applies fix", session.ApplyCurrent("drawing"));
            Check(name + ": second line corrected",
                ReadAnnotation(id).Contains("has drawing here"), "got '" + ReadAnnotation(id) + "'");
        }

        /// <summary>
        /// A dimension's text carries a "&lt;&gt;" placeholder that Rhino substitutes
        /// with the measured value. Correcting a word in the surrounding note must
        /// leave that placeholder intact, or the dimension stops reporting its
        /// measurement.
        /// </summary>
        private static void DimensionOverrideKeepsItsMeasurement()
        {
            const string name = "dimension keeps its measurement placeholder";

            var style = _doc.DimStyles.Current;
            LinearDimension dimension = LinearDimension.Create(
                AnnotationType.Aligned,
                style,
                Plane.WorldXY,
                Vector3d.XAxis,
                new Point3d(160, 0, 0),
                new Point3d(180, 0, 0),
                new Point3d(170, 6, 0),
                0.0);

            if (!Check(name + ": dimension created", dimension != null))
                return;

            // Append a note around the measurement placeholder.
            dimension.PlainText = "<> aproximate length";

            Guid id = _doc.Objects.AddLinearDimension(dimension);
            if (!Check(name + ": dimension added", id != Guid.Empty))
                return;

            string before = ReadAnnotation(id);
            Check(name + ": placeholder present before the fix",
                before != null && before.Contains("<>"), "got '" + before + "'");

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            Check(name + ": finds the misspelling",
                issues.Count == 1 && issues[0].Word == "aproximate",
                "got [" + string.Join(", ", issues.Select(i => i.Word)) + "]");

            Check(name + ": reported as Dimension",
                issues.Count == 1 && issues[0].ObjectKind == "Dimension",
                issues.Count == 1 ? issues[0].ObjectKind : "n/a");

            if (issues.Count != 1)
                return;

            var session = NewSession(issues);
            Check(name + ": applies fix", session.ApplyCurrent("approximate"));

            string after = ReadAnnotation(id);
            Check(name + ": word corrected",
                after != null && after.Contains("approximate"), "got '" + after + "'");
            Check(name + ": placeholder survived",
                after != null && after.Contains("<>"), "got '" + after + "'");
        }

        private static void ModelSpaceTextIsTaggedAsModelSpace()
        {
            const string name = "model space tagging";
            Guid id = AddText("a modle space note", new Point3d(200, 0, 0));

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            if (!Check(name + ": misspelling found", issues.Count == 1,
                "got [" + string.Join(", ", issues.Select(i => i.Word)) + "]"))
                return;

            SpellIssue issue = issues[0];
            Check(name + ": not tagged as page space", !issue.IsPageSpace);
            Check(name + ": no layout id", issue.LayoutId == Guid.Empty, "got " + issue.LayoutId);
            Check(name + ": no sheet outside a sheet walk", issue.SheetId == Guid.Empty, "got " + issue.SheetId);
        }

        private static void PageSpaceTextCarriesItsLayout()
        {
            const string name = "page space tagging";

            Rhino.Display.RhinoPageView page;
            try
            {
                page = _doc.Views.AddPageView("Layout 1", 11.0, 8.5);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  " + name + " (page views unavailable headless: " + ex.Message + ")");
                return;
            }

            if (page == null || page.MainViewport == null)
            {
                Console.WriteLine("  SKIP  " + name + " (headless Rhino created no page view)");
                return;
            }

            Guid layoutId = page.MainViewport.Id;

            var plane = new Plane(new Point3d(1, 1, 0), Vector3d.ZAxis);
            TextEntity entity = TextEntity.Create("sheet noet here", plane, _doc.DimStyles.Current, false, 0, 0);
            entity.TextHeight = 0.25;

            var attributes = new ObjectAttributes
            {
                Space = ActiveSpace.PageSpace,
                ViewportId = layoutId
            };

            Guid id = _doc.Objects.AddText(entity, attributes);
            if (!Check(name + ": page space text added", id != Guid.Empty))
                return;

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            if (!Check(name + ": misspelling found", issues.Count == 1,
                "got [" + string.Join(", ", issues.Select(i => i.Word)) + "]"))
                return;

            SpellIssue issue = issues[0];
            Check(name + ": tagged as page space", issue.IsPageSpace);
            Check(name + ": carries its layout id", issue.LayoutId == layoutId,
                "got " + issue.LayoutId + " expected " + layoutId);

            var navigator = new IssueNavigator();
            BoundingBox frame;
            Rhino.Display.RhinoViewport target = navigator.ActivateViewportFor(_doc, issue, out frame);

            Check(name + ": navigation goes to its own sheet",
                target != null && target.Id == layoutId,
                "got " + (target == null ? "null" : target.Id.ToString()) + " expected " + layoutId);
        }

        /// <summary>
        /// Zooming a detail viewport changes its scale, which edits the layout
        /// rather than moving the camera. Navigation must return the sheet's main
        /// viewport even when a detail is the active one.
        /// </summary>
        private static void NavigationNeverZoomsADetail()
        {
            const string name = "navigation never zooms a detail";

            Rhino.Display.RhinoPageView page;
            try
            {
                page = _doc.Views.AddPageView("Layout 2", 11.0, 8.5);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  " + name + " (page views unavailable headless: " + ex.Message + ")");
                return;
            }

            if (page == null || page.MainViewport == null)
            {
                Console.WriteLine("  SKIP  " + name + " (headless Rhino created no page view)");
                return;
            }

            Guid layoutId = page.MainViewport.Id;

            DetailViewObject detail = page.AddDetailView(
                "Detail 1",
                new Point2d(1, 1),
                new Point2d(9, 7),
                Rhino.Display.DefinedViewportProjection.Top);

            if (detail == null)
            {
                Console.WriteLine("  SKIP  " + name + " (headless Rhino created no detail view)");
                return;
            }

            Guid detailViewportId = detail.Viewport.Id;

            // Make the detail the active viewport - the hazardous state.
            page.SetActiveDetail(detail.Id);

            var plane = new Plane(new Point3d(2, 6, 0), Vector3d.ZAxis);
            TextEntity entity = TextEntity.Create("sheet titel block", plane, _doc.DimStyles.Current, false, 0, 0);
            entity.TextHeight = 0.25;

            var attributes = new ObjectAttributes { Space = ActiveSpace.PageSpace, ViewportId = layoutId };
            Guid id = _doc.Objects.AddText(entity, attributes);

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            if (!Check(name + ": misspelling found", issues.Count == 1,
                "got [" + string.Join(", ", issues.Select(i => i.Word)) + "]"))
                return;

            var navigator = new IssueNavigator();
            BoundingBox frame;
            Rhino.Display.RhinoViewport target = navigator.ActivateViewportFor(_doc, issues[0], out frame);

            if (!Check(name + ": a viewport was chosen", target != null))
                return;

            Check(name + ": chose the sheet, not the detail",
                target.Id == layoutId, "got " + target.Id + " expected " + layoutId);

            Check(name + ": did NOT choose the detail viewport",
                target.Id != detailViewportId, "returned the detail viewport " + detailViewportId);

            Check(name + ": stepped out of the active detail", page.PageIsActive,
                "PageIsActive=" + page.PageIsActive);
        }

        private static void StaleEditIsRefused()
        {
            const string name = "stale edit refused";
            Guid id = AddText("a drawign here", new Point3d(120, 0, 0));

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            if (!Check(name + ": misspelling found", issues.Count == 1))
                return;

            // Rewrite the annotation behind the session's back.
            var annotation = (AnnotationBase)_doc.Objects.FindId(id).Geometry.Duplicate();
            annotation.PlainText = "completely different content";
            _doc.Objects.Replace(id, annotation, false);

            var session = NewSession(issues);
            Check(name + ": apply is refused", !session.ApplyCurrent("drawing"));
            Check(name + ": issue marked stale", issues[0].State == IssueState.Stale, "state=" + issues[0].State);
            Check(name + ": document untouched",
                ReadAnnotation(id) == "completely different content", "got '" + ReadAnnotation(id) + "'");
        }

        private static void MultipleFixesInOneAnnotation()
        {
            const string name = "change all in one annotation";
            Guid id = AddText("teh cat and teh dog and teh bird", new Point3d(140, 0, 0));

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            if (!Check(name + ": three occurrences found", issues.Count == 3,
                "got [" + string.Join(", ", issues.Select(i => i.Word)) + "]"))
                return;

            var session = NewSession(issues);
            int applied = session.ApplyAllMatching("the");

            Check(name + ": all three applied", applied == 3, "applied=" + applied);
            Check(name + ": document correct",
                ReadAnnotation(id) == "the cat and the dog and the bird",
                "got '" + ReadAnnotation(id) + "'");
        }

        /// <summary>
        /// The "…" marking a trimmed excerpt was stored as mojibake in the source
        /// and showed in the panel as "â€¦". Asserted on the character itself.
        /// </summary>
        private static void ContextIsTrimmedWithARealEllipsis()
        {
            const string name = "context ellipsis";
            Guid id = AddText(
                "this opening stretch of the note is long enough to be trimmed drawign " +
                "and so is the closing stretch after the misspelled word in it",
                new Point3d(160, 0, 0));

            var issues = Scan().Where(i => i.ObjectId == id).ToList();
            if (!Check(name + ": misspelling found", issues.Count == 1))
                return;

            string context = issues[0].Context ?? string.Empty;

            Check(name + ": starts with an ellipsis", context.StartsWith("…"), "context='" + context + "'");
            Check(name + ": ends with an ellipsis", context.EndsWith("…"), "context='" + context + "'");
            Check(name + ": no mojibake", !context.Contains("â"), "context='" + context + "'");
            Check(name + ": offset lands on the word",
                issues[0].ContextOffset >= 0 &&
                context.Substring(issues[0].ContextOffset).StartsWith("drawign"),
                "offset=" + issues[0].ContextOffset);

            _doc.Objects.Delete(id, true);
        }

        /// <summary>
        /// CheckSpellSelected harvests whatever was picked. A window selection
        /// over a drawing brings in curves too; they must be skipped, and an
        /// object picked twice must not be checked twice.
        /// </summary>
        private static void SelectionHarvestTakesOnlyText()
        {
            const string name = "selection harvest";

            Guid text = AddText("a selcted note", new Point3d(180, 0, 0));
            Guid curve = _doc.Objects.AddLine(new Point3d(180, -5, 0), new Point3d(190, -5, 0));

            RhinoObject textObject = _doc.Objects.FindId(text);
            RhinoObject curveObject = _doc.Objects.FindId(curve);

            IReadOnlyList<TextRecord> records = DocumentScanner.Harvest(
                _doc, new[] { textObject, curveObject, textObject, null });

            Check(name + ": one record for the text, curve skipped, duplicate ignored",
                records.Count == 1 && records[0].ObjectId == text,
                "got " + records.Count + " record(s)");

            Check(name + ": not tagged with a sheet",
                records.All(r => r.SheetId == Guid.Empty));

            _doc.Objects.Delete(text, true);
            _doc.Objects.Delete(curve, true);
        }

        /// <summary>
        /// The reason CheckSpellLayout can walk a sheet without bouncing the user
        /// to a modelling view: a dimension or note in model space, seen through
        /// a detail, is framed where it appears on the page - and the zoom moves
        /// the sheet, never the detail, whose scale is part of the drawing.
        /// </summary>
        private static void DetailTextIsFramedOnItsSheet()
        {
            const string name = "detail text framed on its sheet";

            Rhino.Display.RhinoPageView page;
            DetailViewObject detail;
            if (!TryAddSheetWithDetail("Frame Sheet", name, out page, out detail))
                return;

            Guid inside = AddText("a framd note", new Point3d(5, 5, 0));
            Guid outside = AddText("far awy note", new Point3d(5000, 5000, 0));

            // Beside the detail rather than far away: this maps to x = 13.5 on
            // an 11-inch sheet, yet the detail's viewport reported it visible.
            Guid beside = AddText("besid the detail", new Point3d(100, 10, 0));

            IReadOnlyList<SpellIssue> issues = DocumentScanner.FindIssues(
                DocumentScanner.Harvest(_doc, page), _engine, TokenizerOptions.Default);

            Check(name + ": text beside the detail is not on the sheet",
                !issues.Any(i => i.ObjectId == beside),
                "harvested '" + string.Join(", ", issues.Where(i => i.ObjectId == beside).Select(i => i.Word)) + "'");

            BoundingBox besideFrame;
            Check(name + ": text beside the detail cannot be framed on the sheet",
                !IssueNavigator.TryFrameOnSheet(page,
                    _doc.Objects.FindId(beside).Geometry.GetBoundingBox(true), out besideFrame));

            _doc.Objects.Delete(beside, true);

            SpellIssue issue = issues.FirstOrDefault(i => i.ObjectId == inside);
            if (!Check(name + ": misspelling found on the sheet", issue != null,
                "got [" + string.Join(", ", issues.Select(i => i.Word)) + "]"))
            {
                Cleanup(inside, outside);
                return;
            }

            Check(name + ": tagged with its sheet",
                issue.SheetId == page.MainViewport.Id && issue.SheetName == "Frame Sheet",
                issue.SheetName + "/" + issue.SheetId);

            // Scale and position are both captured by where model points land
            // on the page. PageToModelRatio would be the obvious measure, but it
            // reads 0 headless, so comparing it would pass vacuously.
            Point3d probeA = new Point3d(0, 0, 0), probeB = new Point3d(10, 10, 0);
            Point3d mappedA = probeA, mappedB = probeB;
            mappedA.Transform(detail.WorldToPageTransform);
            mappedB.Transform(detail.WorldToPageTransform);
            Point3d cameraBefore = detail.Viewport.CameraLocation;

            var navigator = new IssueNavigator();
            BoundingBox frame;
            Rhino.Display.RhinoViewport target = navigator.ActivateViewportFor(_doc, issue, out frame);

            Check(name + ": zooms the sheet's own view",
                target != null && target.Id == page.MainViewport.Id,
                "got " + (target == null ? "null" : target.Id.ToString()));

            // Where the detail sits on the page, in page units.
            BoundingBox rect = detail.Geometry.GetBoundingBox(true);
            rect.Inflate(0.01);

            Check(name + ": framed inside the detail's rectangle on the page",
                frame.IsValid && rect.Contains(frame.Center),
                "frame centre " + frame.Center + ", detail " + rect.Min + " to " + rect.Max);

            Check(name + ": framed at page size, not model size",
                frame.IsValid && frame.Diagonal.Length < rect.Diagonal.Length,
                "frame diagonal " + frame.Diagonal.Length.ToString("F3") +
                " vs detail diagonal " + rect.Diagonal.Length.ToString("F3"));

            navigator.Follow(_doc, issue);

            Point3d afterA = probeA, afterB = probeB;
            afterA.Transform(detail.WorldToPageTransform);
            afterB.Transform(detail.WorldToPageTransform);

            Check(name + ": the detail's scale and framing are untouched",
                afterA.DistanceTo(mappedA) < 1e-9 && afterB.DistanceTo(mappedB) < 1e-9 &&
                mappedA.DistanceTo(mappedB) > 1e-6,
                "before " + mappedA + " / " + mappedB + ", after " + afterA + " / " + afterB);

            Check(name + ": the detail's camera is untouched",
                detail.Viewport.CameraLocation.DistanceTo(cameraBefore) < 1e-9,
                "moved " + detail.Viewport.CameraLocation.DistanceTo(cameraBefore));

            RhinoObject selected = _doc.Objects.FindId(inside);
            Check(name + ": the word's object is selected",
                selected != null && selected.IsSelected(false) > 0,
                "IsSelected=" + (selected == null ? -1 : selected.IsSelected(false)));

            BoundingBox ignored;
            BoundingBox farBox = _doc.Objects.FindId(outside).Geometry.GetBoundingBox(true);
            Check(name + ": text outside every detail cannot be framed on the sheet",
                !IssueNavigator.TryFrameOnSheet(page, farBox, out ignored));

            Cleanup(inside, outside);
        }

        /// <summary>
        /// The walk itself: sheets in the order of the set, each word on the
        /// first sheet it appears on. A dimension visible through details on
        /// two sheets must be reviewed once, or fixing it on the first leaves a
        /// twin on the second that can only go stale.
        /// </summary>
        private static void SweepWalksSheetsInSetOrderWithoutTwins()
        {
            const string name = "layout sweep";

            Rhino.Display.RhinoPageView first, second;
            DetailViewObject firstDetail, secondDetail;
            // A region of model space no other test writes to, so the sheets
            // see only what this test put there.
            var region = new Point3d(-5000, 3000, 0);

            if (!TryAddSheetWithDetail("Walk 1", name, out first, out firstDetail, region) ||
                !TryAddSheetWithDetail("Walk 2", name, out second, out secondDetail, region))
                return;

            Guid onFirst = AddPageText(first, "first sheet noet", new Point3d(1, 7, 0));
            Guid onSecond = AddPageText(second, "second sheet titel", new Point3d(1, 7, 0));
            Guid shared = AddText("a shared dimenshun", region + new Vector3d(2, 2, 0));

            Check(name + ": page numbers put the sheets in tab order",
                first.PageNumber < second.PageNumber,
                "Walk 1 is page " + first.PageNumber + ", Walk 2 is page " + second.PageNumber);

            // Handed over backwards, to prove the sweep reorders them.
            List<Rhino.Display.RhinoPageView> ordered = LayoutSweep.InSetOrder(new[] { second, first });

            Check(name + ": set order puts Walk 1 first",
                ordered.Count == 2 && ordered[0].PageName == "Walk 1" && ordered[1].PageName == "Walk 2",
                string.Join(", ", ordered.Select(p => p.PageName + "#" + p.PageNumber)));

            var progress = new List<string>();
            LayoutSweepResult sweep = LayoutSweep.Run(_doc, ordered, _engine, TokenizerOptions.Default,
                (n, sheet) => progress.Add(n + ":" + sheet));

            Check(name + ": progress reported per sheet",
                progress.SequenceEqual(new[] { "1:Walk 1", "2:Walk 2" }),
                string.Join(", ", progress));

            Check(name + ": issues come sheet by sheet",
                sweep.Issues.Select(i => i.Word).SequenceEqual(new[] { "noet", "dimenshun", "titel" }),
                "got [" + string.Join(", ", sweep.Issues.Select(i => i.Word + "@" + i.SheetName)) + "]");

            Check(name + ": the shared dimension is reviewed once, on the first sheet",
                sweep.Issues.Count(i => i.ObjectId == shared) == 1 &&
                sweep.Issues.Single(i => i.ObjectId == shared).SheetName == "Walk 1",
                "got " + sweep.Issues.Count(i => i.ObjectId == shared) + " cop(ies)");

            Check(name + ": the second sheet reports it as shown earlier",
                sweep.Sheets.Count == 2 && sweep.Sheets[1].Issues == 1 && sweep.Sheets[1].AlsoOnEarlierSheet == 1,
                sweep.Sheets.Count == 2
                    ? "Walk 2: " + sweep.Sheets[1].Issues + " issue(s), " + sweep.Sheets[1].AlsoOnEarlierSheet + " earlier"
                    : "sheets=" + sweep.Sheets.Count);

            // Fixing the shared word goes through once and leaves nothing stale.
            var session = NewSession(sweep.Issues);
            while (session.Current != null && session.Current.ObjectId != shared)
                session.IgnoreCurrent();

            Check(name + ": the shared word can be fixed", session.ApplyCurrent("dimension"));
            Check(name + ": nothing left stale",
                sweep.Issues.All(i => i.State != IssueState.Stale));

            Cleanup(onFirst, onSecond, shared);
        }

        // ------------------------------------------------------------- helpers

        /// <summary>
        /// An 11 x 8.5 sheet with one detail, from (1,1) to (6,5) on the page,
        /// framing model space 20 units either side of <paramref name="centre"/>.
        /// </summary>
        private static bool TryAddSheetWithDetail(
            string sheetName, string testName,
            out Rhino.Display.RhinoPageView page, out DetailViewObject detail,
            Point3d centre = default(Point3d))
        {
            page = null;
            detail = null;

            try
            {
                page = _doc.Views.AddPageView(sheetName, 11.0, 8.5);
                detail = page == null ? null : page.AddDetailView(sheetName + " detail",
                    new Point2d(1, 1), new Point2d(6, 5), Rhino.Display.DefinedViewportProjection.Top);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  SKIP  " + testName + " (layouts unavailable headless: " + ex.Message + ")");
                return false;
            }

            if (page == null || page.MainViewport == null || detail == null || detail.Viewport == null)
            {
                Console.WriteLine("  SKIP  " + testName + " (headless Rhino created no layout or detail)");
                return false;
            }

            // ZoomBoundingBox alone: it moves the camera and keeps the Top
            // direction. Setting the target first without moving the camera
            // tilts the view whenever the centre is not under it.
            detail.Viewport.ZoomBoundingBox(new BoundingBox(
                centre + new Vector3d(-20, -20, -1), centre + new Vector3d(20, 20, 1)));
            detail.CommitViewportChanges();
            return true;
        }

        private static void Cleanup(params Guid[] ids)
        {
            foreach (Guid id in ids)
                _doc.Objects.Delete(id, true);
        }

        private static Guid AddText(string text, Point3d origin)
        {
            var plane = new Plane(origin, Vector3d.ZAxis);
            TextEntity entity = TextEntity.Create(text, plane, _doc.DimStyles.Current, false, 0, 0);
            entity.TextHeight = 1.0;
            return _doc.Objects.AddText(entity);
        }

        private static IReadOnlyList<SpellIssue> Scan()
        {
            // Everything in the model and on every sheet - the widest net a
            // selection could cast.
            var settings = new ObjectEnumeratorSettings
            {
                ObjectTypeFilter = ObjectType.Annotation | ObjectType.TextDot,
                HiddenObjects = true,
                LockedObjects = true,
                IdefObjects = false
            };
            var records = DocumentScanner.Harvest(_doc, _doc.Objects.GetObjectList(settings));
            return DocumentScanner.FindIssues(records, _engine, TokenizerOptions.Default);
        }

        private static SpellSession NewSession(IEnumerable<SpellIssue> issues)
        {
            return new SpellSession(issues, _engine, new RhinoTextStore(_doc));
        }

        private static string[] WordsFor(IEnumerable<SpellIssue> issues, Guid id)
        {
            return issues.Where(i => i.ObjectId == id).Select(i => i.Word).ToArray();
        }

        private static string ReadAnnotation(Guid id)
        {
            RhinoObject obj = _doc.Objects.FindId(id);
            var annotation = obj == null ? null : obj.Geometry as AnnotationBase;
            return AnnotationText.Read(annotation);
        }

        private static string Trim(string value)
        {
            if (value == null)
                return "<null>";
            return value.Length <= 160 ? value : value.Substring(0, 160) + "...";
        }

        private static bool Check(string name, bool condition, string detail = null)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("  PASS  " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("  FAIL  " + name + (detail == null ? "" : "   [" + detail + "]"));
            }

            return condition;
        }

        /// <summary>Walks up from the harness output to the dictionaries in the source tree.</summary>
        private static string FindDictionaryDirectory()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "src", "RhinoSpellCheck", "Resources", "Dictionaries");
                if (Directory.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            // Fall back to the copy deployed next to the harness executable.
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionaries");
        }
    }
}
