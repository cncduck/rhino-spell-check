using System;

namespace RhinoSpellCheck.Core
{
    /// <summary>
    /// The document operations the session needs, kept behind an interface so the
    /// navigation and offset bookkeeping can be tested without a running Rhino.
    /// </summary>
    public interface ITextStore
    {
        /// <summary>
        /// Current plain text of a slot, in the same form the offsets were built
        /// against. Returns null if the object no longer exists.
        /// </summary>
        string ReadText(Guid objectId, TextSlot slot);

        /// <summary>
        /// Replaces <paramref name="length"/> characters at <paramref name="start"/>,
        /// but only if the text there still equals <paramref name="expected"/>.
        /// </summary>
        /// <returns>True if the document was modified.</returns>
        bool ReplaceRange(Guid objectId, TextSlot slot, int start, int length, string expected, string replacement);
    }
}
