using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WeCantSpell.Hunspell;

namespace RhinoSpellCheck.Core
{
    /// <summary>
    /// Wraps a Hunspell dictionary together with the user's personal word list
    /// and the words ignored for the current session.
    ///
    /// WeCantSpell.Hunspell is a managed port, so there are no native binaries to
    /// deploy and everything runs locally against the .aff/.dic files that ship
    /// next to the plug-in.
    /// </summary>
    public sealed class SpellEngine
    {
        public const int MaxSuggestions = 12;

        private readonly WordList _wordList;
        private readonly HashSet<string> _userWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ignoredWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _checkCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        private SpellEngine(WordList wordList, string language, string userDictionaryPath)
        {
            _wordList = wordList;
            Language = language;
            UserDictionaryPath = userDictionaryPath;
            LoadUserDictionary();
        }

        public string Language { get; private set; }

        public string UserDictionaryPath { get; private set; }

        /// <summary>Words the user permanently accepted, in insertion order-independent form.</summary>
        public IReadOnlyCollection<string> UserWords
        {
            get { lock (_gate) return _userWords.ToList(); }
        }

        public static SpellEngine Create(string dictionaryDirectory, string language, string userDictionaryPath)
        {
            if (string.IsNullOrEmpty(dictionaryDirectory))
                throw new ArgumentNullException("dictionaryDirectory");
            if (string.IsNullOrEmpty(language))
                language = "en_US";

            string dic = Path.Combine(dictionaryDirectory, language + ".dic");
            string aff = Path.Combine(dictionaryDirectory, language + ".aff");

            if (!File.Exists(dic) || !File.Exists(aff))
            {
                throw new FileNotFoundException(
                    "Could not find the '" + language + "' dictionary. Expected '" + dic + "' and '" + aff + "'.");
            }

            WordList list = WordList.CreateFromFiles(dic, aff);
            return new SpellEngine(list, language, userDictionaryPath);
        }

        /// <summary>Language codes with a complete .aff/.dic pair in the given folder.</summary>
        public static IReadOnlyList<string> AvailableLanguages(string dictionaryDirectory)
        {
            var languages = new List<string>();
            if (string.IsNullOrEmpty(dictionaryDirectory) || !Directory.Exists(dictionaryDirectory))
                return languages;

            foreach (string dic in Directory.GetFiles(dictionaryDirectory, "*.dic"))
            {
                string name = Path.GetFileNameWithoutExtension(dic);
                if (File.Exists(Path.Combine(dictionaryDirectory, name + ".aff")))
                    languages.Add(name);
            }

            languages.Sort(StringComparer.OrdinalIgnoreCase);
            return languages;
        }

        public bool IsCorrect(string word)
        {
            if (string.IsNullOrEmpty(word))
                return true;

            string normalized = Normalize(word);

            lock (_gate)
            {
                if (_ignoredWords.Contains(normalized) || _userWords.Contains(normalized))
                    return true;

                bool cached;
                if (_checkCache.TryGetValue(normalized, out cached))
                    return cached;
            }

            bool ok = _wordList.Check(normalized);

            // Hunspell is case sensitive for entries like "Rhino"; accept a word
            // whose lower-case form is known when it appears capitalised at the
            // start of a sentence.
            if (!ok && normalized.Length > 1 && char.IsUpper(normalized[0]))
                ok = _wordList.Check(normalized.ToLowerInvariant());

            lock (_gate)
            {
                _checkCache[normalized] = ok;
            }

            return ok;
        }

        public IReadOnlyList<string> Suggest(string word)
        {
            if (string.IsNullOrEmpty(word))
                return new string[0];

            string normalized = Normalize(word);

            IEnumerable<string> raw;
            try
            {
                raw = _wordList.Suggest(normalized);
            }
            catch (Exception)
            {
                return new string[0];
            }

            var suggestions = raw
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .Take(MaxSuggestions)
                .ToList();

            // Hunspell already matches the input's capitalisation in most cases,
            // but an all-lower suggestion for a capitalised word still happens.
            if (normalized.Length > 1 && char.IsUpper(normalized[0]))
            {
                for (int i = 0; i < suggestions.Count; i++)
                {
                    string s = suggestions[i];
                    if (s.Length > 0 && char.IsLower(s[0]))
                        suggestions[i] = char.ToUpperInvariant(s[0]) + s.Substring(1);
                }

                suggestions = suggestions.Distinct(StringComparer.Ordinal).ToList();
            }

            return suggestions;
        }

        /// <summary>Accepts a word for the rest of this Rhino session only.</summary>
        public void IgnoreForSession(string word)
        {
            if (string.IsNullOrEmpty(word))
                return;

            lock (_gate)
            {
                _ignoredWords.Add(Normalize(word));
            }
        }

        /// <summary>Accepts a word permanently and appends it to the user dictionary file.</summary>
        public void AddToUserDictionary(string word)
        {
            if (string.IsNullOrEmpty(word))
                return;

            string normalized = Normalize(word);

            lock (_gate)
            {
                if (!_userWords.Add(normalized))
                    return;
            }

            if (string.IsNullOrEmpty(UserDictionaryPath))
                return;

            try
            {
                string directory = Path.GetDirectoryName(UserDictionaryPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.AppendAllText(UserDictionaryPath, normalized + Environment.NewLine);
            }
            catch (Exception)
            {
                // A read-only profile should not take the whole scan down; the word
                // still counts as accepted for this session.
            }
        }

        public void ClearSessionIgnores()
        {
            lock (_gate)
            {
                _ignoredWords.Clear();
            }
        }

        private void LoadUserDictionary()
        {
            if (string.IsNullOrEmpty(UserDictionaryPath) || !File.Exists(UserDictionaryPath))
                return;

            try
            {
                foreach (string line in File.ReadAllLines(UserDictionaryPath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal))
                        _userWords.Add(Normalize(trimmed));
                }
            }
            catch (Exception)
            {
                // Corrupt or locked personal dictionary: fall back to an empty one.
            }
        }

        /// <summary>
        /// Folds typographic apostrophes onto the ASCII one, matching the ICONV
        /// rule in the en_US affix file.
        /// </summary>
        internal static string Normalize(string word)
        {
            if (string.IsNullOrEmpty(word))
                return word;

            if (word.IndexOf('’') < 0 && word.IndexOf('ʼ') < 0)
                return word;

            return word.Replace('’', '\'').Replace('ʼ', '\'');
        }
    }
}
