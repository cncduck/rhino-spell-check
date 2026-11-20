using System;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoSpellCheck.Core;

namespace RhinoSpellCheck.Documents
{
    /// <summary>
    /// Takes the user to the current misspelling: selects the object holding it
    /// and zooms a view onto it. With no underlines drawn, the selection
    /// highlight and the zoom are how the user sees which word the panel means.
    ///
    /// Two rules shape where it goes:
    ///
    /// - **A detail is never zoomed.** Zooming a detail changes its scale, which
    ///   edits the layout rather than moving the camera. Every zoom on a sheet
    ///   moves the sheet's own view instead.
    /// - **Stay on the sheet.** Model-space text seen through a detail - the
    ///   dimensions and callouts on a drawing - is framed where it appears on
    ///   the page, by mapping it through the detail. Sending the user to a
    ///   modelling view for each of those would throw them off the sheet they
    ///   are reviewing on every other word.
    /// </summary>
    public sealed class IssueNavigator
    {
        private Guid _lastModelViewId;

        /// <summary>Extra room around the object when zooming, as a multiple of its diagonal.</summary>
        public double ZoomPaddingFactor { get; set; }

        public IssueNavigator()
        {
            ZoomPaddingFactor = 1.0;
        }

        /// <summary>Selects the object for <paramref name="issue"/> and zooms to it.</summary>
        /// <returns>True if the view actually moved.</returns>
        public bool Follow(RhinoDoc doc, SpellIssue issue)
        {
            if (doc == null || issue == null)
                return false;

            try
            {
                RememberActiveModelView(doc);

                doc.Objects.UnselectAll();

                RhinoObject obj = doc.Objects.FindId(issue.ObjectId);
                if (obj == null || obj.Geometry == null)
                    return false;

                obj.Select(true);

                BoundingBox frame;
                RhinoViewport viewport = ActivateViewportFor(doc, issue, out frame);
                if (viewport == null || !frame.IsValid)
                    return false;

                double diagonal = frame.Diagonal.Length;
                frame.Inflate(diagonal > RhinoMath.ZeroTolerance ? diagonal * ZoomPaddingFactor : 1.0);

                return viewport.ZoomBoundingBox(frame);
            }
            catch (Exception ex)
            {
                // Navigation is how the user finds the word, so stay loud about
                // why it failed rather than looking like the button does nothing.
                RhinoApp.WriteLine("Spell Check: could not navigate to the word: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Brings the view that should show this issue to the front, and returns
        /// the viewport to zoom plus the box to frame, in that viewport's
        /// coordinates. Never returns a detail viewport.
        /// </summary>
        public RhinoViewport ActivateViewportFor(RhinoDoc doc, SpellIssue issue, out BoundingBox frame)
        {
            frame = BoundingBox.Unset;

            if (doc == null || issue == null)
                return null;

            RhinoObject obj = doc.Objects.FindId(issue.ObjectId);
            if (obj == null || obj.Geometry == null)
                return null;

            BoundingBox box = obj.Geometry.GetBoundingBox(true);
            if (!box.IsValid)
                return null;

            // Page-space text lives on its own sheet, in that sheet's coordinates.
            if (issue.IsPageSpace)
            {
                RhinoPageView own = FindPage(doc, issue.LayoutId);
                if (own == null)
                    return null;

                frame = box;
                return ActivateSheet(doc, own);
            }

            // Model-space text: frame it on the sheet being reviewed if there is
            // one - the sheet the walk found it on, otherwise the sheet the user
            // is already looking at.
            RhinoPageView sheet = issue.SheetId != Guid.Empty
                ? FindPage(doc, issue.SheetId)
                : doc.Views.ActiveView as RhinoPageView;

            BoundingBox onPage;
            if (sheet != null && TryFrameOnSheet(sheet, box, out onPage))
            {
                frame = onPage;
                return ActivateSheet(doc, sheet);
            }

            frame = box;
            return ActivateModelView(doc);
        }

        /// <summary>
        /// Maps a model-space box onto the sheet, through the first detail that
        /// shows it. The same detail test the sheet harvest uses, so a word the
        /// walk attributes to a sheet can always be framed on it.
        /// </summary>
        public static bool TryFrameOnSheet(RhinoPageView sheet, BoundingBox worldBox, out BoundingBox onPage)
        {
            onPage = BoundingBox.Unset;

            if (sheet == null || !worldBox.IsValid)
                return false;

            DetailViewObject detail = DocumentScanner.FindDetailShowing(sheet.GetDetailViews(), worldBox);
            if (detail == null)
                return false;

            BoundingBox mapped = worldBox;
            if (!mapped.Transform(detail.WorldToPageTransform) || !mapped.IsValid)
                return false;

            // Text is flat on the page; give it no depth so the zoom is decided
            // by its width and height alone.
            mapped = new BoundingBox(
                new Point3d(mapped.Min.X, mapped.Min.Y, 0),
                new Point3d(mapped.Max.X, mapped.Max.Y, 0));

            onPage = mapped;
            return true;
        }

        private void RememberActiveModelView(RhinoDoc doc)
        {
            RhinoView active = doc.Views.ActiveView;
            if (active != null && !(active is RhinoPageView))
                _lastModelViewId = active.ActiveViewportID;
        }

        private static RhinoPageView FindPage(RhinoDoc doc, Guid mainViewportId)
        {
            RhinoPageView[] pages = doc.Views.GetPageViews();
            if (pages == null)
                return null;

            foreach (RhinoPageView page in pages)
            {
                if (page == null || page.MainViewport == null)
                    continue;

                if (mainViewportId == Guid.Empty || page.MainViewport.Id == mainViewportId)
                    return page;
            }

            return null;
        }

        private static RhinoViewport ActivateSheet(RhinoDoc doc, RhinoPageView page)
        {
            if (!ReferenceEquals(doc.Views.ActiveView, page))
                doc.Views.ActiveView = page;

            // Step out of any active detail, so the zoom moves the sheet view
            // instead of rescaling a detail.
            page.SetPageAsActive();

            return page.MainViewport;
        }

        private RhinoViewport ActivateModelView(RhinoDoc doc)
        {
            RhinoView active = doc.Views.ActiveView;
            if (active != null && !(active is RhinoPageView))
                return active.ActiveViewport;

            RhinoView[] standard = doc.Views.GetStandardRhinoViews();
            if (standard == null || standard.Length == 0)
                return null;

            // Prefer whichever modelling view the user was last in, so repeated
            // Next presses stay in one place.
            foreach (RhinoView view in standard)
            {
                if (view != null && view.ActiveViewportID == _lastModelViewId)
                {
                    doc.Views.ActiveView = view;
                    return view.ActiveViewport;
                }
            }

            doc.Views.ActiveView = standard[0];
            return standard[0].ActiveViewport;
        }
    }
}
