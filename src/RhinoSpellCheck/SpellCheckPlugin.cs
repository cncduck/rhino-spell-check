using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using Rhino;
using Rhino.PlugIns;
using Rhino.UI;
using RhinoSpellCheck.Core;
using RhinoSpellCheck.UI;

namespace RhinoSpellCheck
{
    /// <summary>
    /// Entry point. Registers the Spell Check panel and owns the shared state that
    /// the commands and the panel both read: the loaded dictionary, the tokenizer
    /// options and the current check.
    /// </summary>
    public sealed class SpellCheckPlugin : PlugIn
    {
        private const string DefaultLanguage = "en_US";

        private readonly object _engineGate = new object();
        private SpellEngine _engine;
        private string _engineError;
        private string _loadedLanguage;

        public SpellCheckPlugin()
        {
            Instance = this;
            TokenizerOptions = TokenizerOptions.Default;
        }

        public static SpellCheckPlugin Instance { get; private set; }

        public TokenizerOptions TokenizerOptions { get; private set; }

        public string Language { get; set; }

        /// <summary>The check the panel is reviewing, or null.</summary>
        public ScanOutcome CurrentScan { get; private set; }

        /// <summary>The document <see cref="CurrentScan"/> was taken from.</summary>
        public RhinoDoc ScanDocument { get; private set; }

        /// <summary>
        /// Raised when a new check is published, so an open panel can pick it up.
        /// </summary>
        public event EventHandler ScanChanged;

        /// <summary>
        /// Publishes a check as the current one.
        ///
        /// The commands scan and the panel reviews; this is the only place the
        /// two meet. The panel both listens for this event and pulls
        /// <see cref="CurrentScan"/> when it is shown, because Rhino does not
        /// create the panel until it is first opened - a command that published
        /// and then opened the panel would otherwise announce to nobody.
        /// </summary>
        public void PublishScan(RhinoDoc doc, ScanOutcome outcome)
        {
            CurrentScan = outcome;
            ScanDocument = outcome == null ? null : doc;

            EventHandler handler = ScanChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        /// <summary>Folder holding the bundled .aff/.dic pairs, next to the .rhp.</summary>
        public string DictionaryDirectory
        {
            get
            {
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                string folder = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
                return Path.Combine(folder, "Dictionaries");
            }
        }

        /// <summary>Personal word list, kept outside the install folder so it survives updates.</summary>
        public string UserDictionaryPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "RhinoSpellCheck", "user.dic");
            }
        }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            LoadSettings();

            try
            {
                Panels.RegisterPanel(this, typeof(SpellCheckPanel), "Spell Check", LoadPanelIcon());
            }
            catch (Exception ex)
            {
                errorMessage = "Could not register the Spell Check panel: " + ex.Message;
                return LoadReturnCode.ErrorShowDialog;
            }

            RhinoDoc.CloseDocument += OnCloseDocument;

            return LoadReturnCode.Success;
        }

        /// <summary>
        /// Drops the check when its document closes. Otherwise opening another
        /// file would leave the panel offering words from objects that are gone.
        /// </summary>
        private void OnCloseDocument(object sender, DocumentEventArgs e)
        {
            if (CurrentScan != null && e != null && ReferenceEquals(e.Document, ScanDocument))
                PublishScan(null, null);
        }

        /// <summary>
        /// The panel tab icon, or null to let Rhino draw the default.
        ///
        /// <c>LoadIconWithScaleDown</c> is Rhino's own loader: it picks the entry
        /// matching the current DPI and scales down from a larger one rather than
        /// up from a smaller one, which is what keeps the tab crisp at 150%.
        /// A missing or unreadable icon is not worth failing the load over, so
        /// every failure path returns null and the panel registers without one.
        /// </summary>
        private static Icon LoadPanelIcon()
        {
            const string resource = "RhinoSpellCheck.Resources.Icons.SpellCheck.ico";
            Assembly assembly = Assembly.GetExecutingAssembly();

            try
            {
                Icon icon = DrawingUtilities.LoadIconWithScaleDown(resource, 32, assembly);
                if (icon != null)
                    return icon;
            }
            catch (Exception)
            {
                // Fall through to the plain loader below.
            }

            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(resource))
                    return stream == null ? null : new Icon(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }

        protected override void OnShutdown()
        {
            SavePreferences();
            base.OnShutdown();
        }

        /// <summary>
        /// Returns the loaded dictionary, loading it on first use. Parsing the
        /// en_US word list takes a noticeable moment the first time.
        /// </summary>
        public SpellEngine GetEngine(out string error)
        {
            lock (_engineGate)
            {
                string wanted = string.IsNullOrEmpty(Language) ? DefaultLanguage : Language;

                if (_engine != null && string.Equals(_loadedLanguage, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    error = null;
                    return _engine;
                }

                try
                {
                    _engine = SpellEngine.Create(DictionaryDirectory, wanted, UserDictionaryPath);
                    _loadedLanguage = wanted;
                    _engineError = null;
                }
                catch (Exception ex)
                {
                    _engine = null;
                    _engineError = ex.Message;
                }

                error = _engineError;
                return _engine;
            }
        }

        /// <summary>True once the dictionary has been parsed, so a command can warn before the first slow load.</summary>
        public bool IsEngineLoaded
        {
            get
            {
                lock (_engineGate)
                    return _engine != null;
            }
        }

        private void LoadSettings()
        {
            try
            {
                Language = Settings.GetString("Language", DefaultLanguage);
                TokenizerOptions.MinimumWordLength = Settings.GetInteger("MinimumWordLength", 3);
                TokenizerOptions.IgnoreAllCaps = Settings.GetBool("IgnoreAllCaps", true);
                TokenizerOptions.IgnoreMixedCase = Settings.GetBool("IgnoreMixedCase", true);
                TokenizerOptions.IgnoreWordsAdjacentToDigits = Settings.GetBool("IgnoreWordsAdjacentToDigits", true);
            }
            catch (Exception)
            {
                // First run, or a settings file from an older build.
                Language = DefaultLanguage;
            }
        }

        /// <summary>Writes the panel's options into the plug-in's persistent settings.</summary>
        public void SavePreferences()
        {
            try
            {
                Settings.SetString("Language", string.IsNullOrEmpty(Language) ? DefaultLanguage : Language);
                Settings.SetInteger("MinimumWordLength", TokenizerOptions.MinimumWordLength);
                Settings.SetBool("IgnoreAllCaps", TokenizerOptions.IgnoreAllCaps);
                Settings.SetBool("IgnoreMixedCase", TokenizerOptions.IgnoreMixedCase);
                Settings.SetBool("IgnoreWordsAdjacentToDigits", TokenizerOptions.IgnoreWordsAdjacentToDigits);
            }
            catch (Exception)
            {
                // Non-fatal: the user just loses their preferences.
            }
        }
    }
}
