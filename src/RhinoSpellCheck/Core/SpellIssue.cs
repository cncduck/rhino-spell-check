using System;
using System.Collections.Generic;

namespace RhinoSpellCheck.Core
{
    /// <summary>Which piece of text on an object an issue belongs to.</summary>
    public enum TextSlot
    {
        /// <summary>Text, leader, dimension or any other annotation body.</summary>
        Annotation = 0,

        /// <summary>The main label of a text dot.</summary>
        TextDotPrimary = 1,

        /// <summary>The secondary label of a text dot.</summary>
        TextDotSecondary = 2
    }

    public enum IssueState
    {
        Pending = 0,
        Corrected = 1,
        Ignored = 2,

        /// <summary>The object changed underneath us, so the offsets no longer apply.</summary>
        Stale = 3
    }

    /// <summary>One misspelled word at a known location in the document.</summary>
    public sealed class SpellIssue
    {
        private IReadOnlyList<string> _suggestions;

        public SpellIssue(Guid objectId, TextSlot slot, string word, int start)
        {
            ObjectId = objectId;
            Slot = slot;
            Word = word;
            Start = start;
            Length = word == null ? 0 : word.Length;
            State = IssueState.Pending;
        }

        public Guid ObjectId { get; private set; }

        public TextSlot Slot { get; private set; }

        public string Word { get; private set; }

        /// <summary>Offset into the slot's plain text. Shifts as earlier fixes are applied.</summary>
        public int Start { get; internal set; }

        public int Length { get; private set; }

        public int End { get { return Start + Length; } }

        public IssueState State { get; internal set; }

        /// <summary>Human-readable object type, e.g. "Text" or "Dimension".</summary>
        public string ObjectKind { get; set; }

        public string LayerName { get; set; }

        /// <summary>True when the annotation lives on a layout sheet rather than in the model.</summary>
        public bool IsPageSpace { get; set; }

        /// <summary>For page-space annotations, the layout they belong to.</summary>
        public Guid LayoutId { get; set; }

        /// <summary>
        /// The layout sheet this issue was found on, when it came from a sheet
        /// walk (<c>CheckSpellLayout</c>); <see cref="Guid.Empty"/> otherwise.
        ///
        /// Not the same as <see cref="LayoutId"/>. That says where a page-space
        /// object *lives*; this says which sheet the user is reviewing it on,
        /// which for model-space text seen through a detail is the only link
        /// between the word and a sheet at all.
        /// </summary>
        public Guid SheetId { get; set; }

        /// <summary>The name of <see cref="SheetId"/>, for the panel.</summary>
        public string SheetName { get; set; }

        /// <summary>A short slice of surrounding text for display in the panel.</summary>
        public string Context { get; set; }

        /// <summary>Offset of <see cref="Word"/> inside <see cref="Context"/>, or -1.</summary>
        public int ContextOffset { get; set; }

        public bool HasSuggestions { get { return _suggestions != null; } }

        /// <summary>
        /// Suggestions are filled in on demand: Hunspell's suggest pass is far more
        /// expensive than its check pass, and a large drawing can produce hundreds
        /// of issues the user never navigates to.
        /// </summary>
        public IReadOnlyList<string> GetSuggestions(SpellEngine engine)
        {
            if (_suggestions == null)
                _suggestions = engine == null ? new string[0] : engine.Suggest(Word);

            return _suggestions;
        }

        public override string ToString()
        {
            return Word + " (" + ObjectKind + " " + ObjectId.ToString("D").Substring(0, 8) + " @" + Start + ")";
        }
    }
}
