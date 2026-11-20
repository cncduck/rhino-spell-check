using System;
using System.IO;
using System.Linq;
using RhinoSpellCheck.Core;
using Xunit;

namespace RhinoSpellCheck.Tests
{
    /// <summary>
    /// Exercises the real en_US dictionary that ships with the plug-in, so a
    /// broken or truncated dictionary file fails the build rather than Rhino.
    /// </summary>
    public class SpellEngineTests : IDisposable
    {
        private static readonly string DictionaryDirectory =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "RhinoSpellCheck", "Resources", "Dictionaries"));

        private readonly string _userDictionary =
            Path.Combine(Path.GetTempPath(), "rsc-tests-" + Guid.NewGuid().ToString("N") + ".dic");

        private SpellEngine Create()
        {
            return SpellEngine.Create(DictionaryDirectory, "en_US", _userDictionary);
        }

        public void Dispose()
        {
            if (File.Exists(_userDictionary))
                File.Delete(_userDictionary);
        }

        [Fact]
        public void Dictionary_files_are_present()
        {
            Assert.True(File.Exists(Path.Combine(DictionaryDirectory, "en_US.dic")),
                "en_US.dic missing from " + DictionaryDirectory);
            Assert.True(File.Exists(Path.Combine(DictionaryDirectory, "en_US.aff")),
                "en_US.aff missing from " + DictionaryDirectory);
        }

        [Fact]
        public void Discovers_bundled_languages()
        {
            Assert.Contains("en_US", SpellEngine.AvailableLanguages(DictionaryDirectory));
        }

        [Fact]
        public void Accepts_correctly_spelled_words()
        {
            var engine = Create();
            Assert.True(engine.IsCorrect("drawing"));
            Assert.True(engine.IsCorrect("elevation"));
            Assert.True(engine.IsCorrect("Drawing"));
        }

        [Fact]
        public void Rejects_misspelled_words()
        {
            var engine = Create();
            Assert.False(engine.IsCorrect("drawign"));
            Assert.False(engine.IsCorrect("elevaton"));
        }

        [Fact]
        public void Suggests_the_intended_word()
        {
            var engine = Create();
            Assert.Contains("drawing", engine.Suggest("drawign"));
            Assert.Contains("elevation", engine.Suggest("elevaton"));
        }

        [Fact]
        public void Caps_the_number_of_suggestions()
        {
            var engine = Create();
            Assert.True(engine.Suggest("aporximatley").Count <= SpellEngine.MaxSuggestions);
        }

        [Fact]
        public void Capitalises_suggestions_for_a_capitalised_input()
        {
            var engine = Create();
            var suggestions = engine.Suggest("Drawign");
            Assert.NotEmpty(suggestions);
            Assert.All(suggestions, s => Assert.True(char.IsUpper(s[0]), s + " should be capitalised"));
        }

        [Fact]
        public void Session_ignores_apply_until_cleared()
        {
            var engine = Create();
            Assert.False(engine.IsCorrect("zzqq"));

            engine.IgnoreForSession("zzqq");
            Assert.True(engine.IsCorrect("zzqq"));

            engine.ClearSessionIgnores();
            Assert.False(engine.IsCorrect("zzqq"));
        }

        [Fact]
        public void User_dictionary_words_persist_to_disk_and_reload()
        {
            var engine = Create();
            engine.AddToUserDictionary("Kerfing");
            Assert.True(engine.IsCorrect("Kerfing"));

            var reloaded = Create();
            Assert.True(reloaded.IsCorrect("Kerfing"));
            Assert.Contains("Kerfing", reloaded.UserWords);
        }

        [Fact]
        public void User_dictionary_matching_is_case_insensitive()
        {
            var engine = Create();
            engine.AddToUserDictionary("Kerfing");
            Assert.True(engine.IsCorrect("kerfing"));
            Assert.True(engine.IsCorrect("KERFING"));
        }

        [Fact]
        public void Curly_apostrophes_check_the_same_as_straight_ones()
        {
            var engine = Create();
            Assert.Equal(engine.IsCorrect("don't"), engine.IsCorrect("don’t"));
            Assert.True(engine.IsCorrect("don’t"));
        }

        [Fact]
        public void Missing_dictionary_reports_a_useful_error()
        {
            var ex = Assert.Throws<FileNotFoundException>(
                () => SpellEngine.Create(DictionaryDirectory, "zz_ZZ", _userDictionary));
            Assert.Contains("zz_ZZ", ex.Message);
        }

        [Fact]
        public void End_to_end_finds_the_misspelling_in_a_drawing_note()
        {
            var engine = Create();
            const string note = "GENERAL NOTES: All dimensons are in %<ModelUnits>% unless noted.";

            var bad = Tokenizer.Tokenize(note)
                .Where(t => !engine.IsCorrect(t.Text))
                .Select(t => t.Text)
                .ToArray();

            Assert.Equal(new[] { "dimensons" }, bad);
        }
    }
}
