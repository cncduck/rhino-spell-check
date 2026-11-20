using System;
using System.Collections.Generic;
using RhinoSpellCheck.Core;

namespace RhinoSpellCheck.Tests
{
    /// <summary>In-memory stand-in for the Rhino document.</summary>
    internal sealed class FakeTextStore : ITextStore
    {
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);

        public int WriteCount { get; private set; }

        private static string Key(Guid id, TextSlot slot)
        {
            return id.ToString("N") + "/" + (int)slot;
        }

        public void Set(Guid id, string text, TextSlot slot = TextSlot.Annotation)
        {
            _texts[Key(id, slot)] = text;
        }

        public string Get(Guid id, TextSlot slot = TextSlot.Annotation)
        {
            string value;
            return _texts.TryGetValue(Key(id, slot), out value) ? value : null;
        }

        public void Delete(Guid id, TextSlot slot = TextSlot.Annotation)
        {
            _texts.Remove(Key(id, slot));
        }

        public string ReadText(Guid objectId, TextSlot slot)
        {
            return Get(objectId, slot);
        }

        public bool ReplaceRange(Guid objectId, TextSlot slot, int start, int length, string expected, string replacement)
        {
            string text = Get(objectId, slot);
            if (text == null)
                return false;

            if (start < 0 || length <= 0 || start + length > text.Length)
                return false;

            if (!string.Equals(text.Substring(start, length), expected, StringComparison.Ordinal))
                return false;

            _texts[Key(objectId, slot)] = text.Substring(0, start) + replacement + text.Substring(start + length);
            WriteCount++;
            return true;
        }
    }
}
