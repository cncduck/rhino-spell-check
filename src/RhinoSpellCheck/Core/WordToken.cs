namespace RhinoSpellCheck.Core
{
    /// <summary>
    /// A candidate word located inside a block of plain text, together with the
    /// offset it was found at. Offsets are indices into the exact string that was
    /// handed to <see cref="Tokenizer"/>, which for annotations is always
    /// <c>PlainTextWithFields</c> so that they stay valid when splicing RTF.
    /// </summary>
    public readonly struct WordToken
    {
        public WordToken(string text, int start)
        {
            Text = text;
            Start = start;
        }

        public string Text { get; }

        public int Start { get; }

        public int Length { get { return Text == null ? 0 : Text.Length; } }

        public int End { get { return Start + Length; } }

        public override string ToString()
        {
            return "'" + Text + "'@" + Start;
        }
    }
}
