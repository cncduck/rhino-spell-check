using System;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoSpellCheck.Core;

namespace RhinoSpellCheck.Documents
{
    /// <summary>
    /// Applies corrections to real Rhino objects.
    ///
    /// Annotations store their text as RTF. Assigning <c>PlainText</c> would be far
    /// simpler but throws away every formatting run in the annotation, so the
    /// preferred path splices the replacement into the RTF and only falls back to a
    /// flattening write when that is not possible.
    /// </summary>
    public sealed class RhinoTextStore : ITextStore
    {
        private readonly RhinoDoc _doc;

        public RhinoTextStore(RhinoDoc doc)
        {
            if (doc == null)
                throw new ArgumentNullException("doc");

            _doc = doc;
        }

        /// <summary>
        /// Raised when a correction had to be written as plain text, discarding
        /// mixed formatting inside that annotation.
        /// </summary>
        public event EventHandler<Guid> FormattingFlattened;

        public string ReadText(Guid objectId, TextSlot slot)
        {
            var obj = _doc.Objects.FindId(objectId);
            return obj == null ? null : ReadText(obj.Geometry, slot);
        }

        internal static string ReadText(GeometryBase geometry, TextSlot slot)
        {
            if (slot == TextSlot.Annotation)
                return AnnotationText.Read(geometry as AnnotationBase);

            var dot = geometry as TextDot;
            if (dot == null)
                return null;

            return slot == TextSlot.TextDotPrimary ? dot.Text : dot.SecondaryText;
        }

        public bool ReplaceRange(Guid objectId, TextSlot slot, int start, int length, string expected, string replacement)
        {
            if (string.IsNullOrEmpty(replacement) || length <= 0 || start < 0)
                return false;

            var obj = _doc.Objects.FindId(objectId);
            if (obj == null)
                return false;

            try
            {
                return slot == TextSlot.Annotation
                    ? ReplaceAnnotation(objectId, obj, start, length, expected, replacement)
                    : ReplaceTextDot(objectId, obj, slot, start, length, expected, replacement);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Spell Check: could not update object {0}: {1}", objectId, ex.Message);
                return false;
            }
        }

        private bool ReplaceAnnotation(Guid objectId, RhinoObject obj, int start, int length, string expected, string replacement)
        {
            var annotation = obj.Geometry as AnnotationBase;
            if (annotation == null)
                return false;

            var duplicate = annotation.Duplicate() as AnnotationBase;
            if (duplicate == null)
                return false;

            bool flattened;
            if (!AnnotationText.TryWrite(duplicate, start, length, expected, replacement, out flattened))
                return false;

            if (flattened)
                OnFormattingFlattened(objectId);

            return _doc.Objects.Replace(objectId, duplicate, false);
        }

        private bool ReplaceTextDot(Guid objectId, RhinoObject obj, TextSlot slot, int start, int length, string expected, string replacement)
        {
            var dot = obj.Geometry as TextDot;
            if (dot == null)
                return false;

            var duplicate = dot.Duplicate() as TextDot;
            if (duplicate == null)
                return false;

            bool primary = slot == TextSlot.TextDotPrimary;
            string text = primary ? duplicate.Text : duplicate.SecondaryText;

            if (!AnnotationText.RangeStillMatches(text, start, length, expected))
                return false;

            string updated = SplicePlain(text, start, length, replacement);
            if (primary)
                duplicate.Text = updated;
            else
                duplicate.SecondaryText = updated;

            return _doc.Objects.Replace(objectId, duplicate);
        }

        private static string SplicePlain(string text, int start, int length, string replacement)
        {
            return text.Substring(0, start) + replacement + text.Substring(start + length);
        }

        private void OnFormattingFlattened(Guid objectId)
        {
            var handler = FormattingFlattened;
            if (handler != null)
                handler(this, objectId);
        }
    }
}
