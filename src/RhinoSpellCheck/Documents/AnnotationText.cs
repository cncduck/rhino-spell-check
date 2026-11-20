using System;
using Rhino.Geometry;
using RhinoSpellCheck.Core;

namespace RhinoSpellCheck.Documents
{
    /// <summary>
    /// Reads and writes the editable text of an annotation.
    ///
    /// Rhino stores it in two different places depending on the type. Text,
    /// leaders and text objects use <c>PlainTextWithFields</c> / <c>RichText</c>.
    /// Dimensions leave those empty and keep the user's override in
    /// <c>PlainUserText</c> / <c>TextFormula</c> instead, where a "&lt;&gt;" token
    /// stands in for the measured value. Writing to the wrong pair either does
    /// nothing or destroys the measurement, so every access goes through here.
    /// </summary>
    public static class AnnotationText
    {
        /// <summary>
        /// The annotation's text in the exact form that word offsets are measured
        /// against. Returns null when the object carries no editable text.
        /// </summary>
        public static string Read(AnnotationBase annotation)
        {
            if (annotation == null)
                return null;

            var dimension = annotation as Dimension;
            return dimension != null ? dimension.PlainUserText : annotation.PlainTextWithFields;
        }

        /// <summary>
        /// True when the annotation's layout cannot be derived from its text alone,
        /// so a per-word underline would be drawn in the wrong place.
        /// </summary>
        public static bool HasSubstitutedText(AnnotationBase annotation)
        {
            // A dimension's "<>" expands to the measured value at draw time, so the
            // rendered glyphs do not match the stored string.
            return annotation is Dimension;
        }

        /// <summary>
        /// Replaces a word on a duplicated annotation, preferring an RTF splice so
        /// formatting runs survive.
        /// </summary>
        /// <param name="flattened">
        /// Set when mixed formatting had to be discarded to apply the change.
        /// </param>
        public static bool TryWrite(
            AnnotationBase annotation,
            int start,
            int length,
            string expected,
            string replacement,
            out bool flattened)
        {
            flattened = false;

            if (annotation == null)
                return false;

            string plain = Read(annotation);
            if (!RangeStillMatches(plain, start, length, expected))
                return false;

            string updated = plain.Substring(0, start) + replacement + plain.Substring(start + length);

            var dimension = annotation as Dimension;
            if (dimension != null)
            {
                string formula = dimension.TextFormula;

                if (IsRtf(formula))
                {
                    string spliced;
                    if (TrySplice(formula, plain, start, length, expected, replacement, out spliced))
                    {
                        dimension.TextFormula = spliced;
                        return true;
                    }

                    flattened = true;
                }

                dimension.TextFormula = updated;
                return true;
            }

            if (annotation.TextHasRtfFormatting)
            {
                string spliced;
                if (TrySplice(annotation.RichText, plain, start, length, expected, replacement, out spliced))
                {
                    annotation.RichText = spliced;
                    return true;
                }

                flattened = true;
            }

            annotation.PlainText = updated;
            return true;
        }

        public static bool RangeStillMatches(string text, int start, int length, string expected)
        {
            if (text == null || start < 0 || length <= 0 || start + length > text.Length)
                return false;

            return string.Equals(text.Substring(start, length), expected, StringComparison.Ordinal);
        }

        private static bool IsRtf(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal);
        }

        private static bool TrySplice(
            string rtf,
            string referencePlain,
            int start,
            int length,
            string expected,
            string replacement,
            out string result)
        {
            result = null;

            if (string.IsNullOrEmpty(rtf))
                return false;

            // Our RTF reader and Rhino's need not agree on offsets, so re-locate
            // the word before touching anything.
            string ourPlain = RtfSplicer.GetPlainText(rtf);
            int aligned = RtfSplicer.AlignOffset(ourPlain, referencePlain, start, expected);

            return aligned >= 0 &&
                   RtfSplicer.TryReplaceRange(rtf, aligned, length, expected, replacement, out result);
        }
    }
}
