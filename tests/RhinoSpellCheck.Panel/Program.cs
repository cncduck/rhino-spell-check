using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RhinoSpellCheck.PanelTests
{
    /// <summary>
    /// Panel suite: drives the Eto Spell Check panel inside a hidden Rhino.
    ///
    /// Every bug reported by hand before this suite existed was panel
    /// behaviour that the other suites passed straight through, so the panel
    /// gets a suite of its own. See <see cref="PanelProbe"/>.
    ///
    /// Needs a licensed Rhino 8 with the plug-in installed - Rhino has to load
    /// it for <c>SpellCheckPlugin.Instance</c> to exist. Developer tool; not
    /// shipped.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePlugInAssembly;

            // Nothing above this line may touch a RhinoCommon type.
            try
            {
                RhinoInside.Resolver.Initialize();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not locate Rhino: " + ex.Message);
                return 2;
            }

            return RunPanelProbe();
        }

        /// <summary>
        /// Binds this harness's own build of the plug-in before Rhino starts.
        ///
        /// Rhino loads every installed plug-in during boot. If the user's
        /// installed Release .rhp gets there first it wins the `RhinoSpellCheck`
        /// assembly identity, and everything afterwards silently tests that
        /// binary rather than the one just compiled. Loading ours first means
        /// the identity is already taken.
        /// </summary>
        private static void PreloadOwnBuild()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RhinoSpellCheck.rhp");
                if (File.Exists(path))
                    Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("could not preload the harness's plug-in build: " + ex.Message);
            }
        }

        /// <summary>Says which plug-in binary is under test, loudly if it is not this build.</summary>
        private static void ReportPlugInBuild()
        {
            string loadedFrom = typeof(RhinoSpellCheck.SpellCheckPlugin).Assembly.Location;
            string expected = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RhinoSpellCheck.rhp");

            Console.WriteLine("plug-in assembly: " + loadedFrom);

            if (!string.Equals(Path.GetFullPath(loadedFrom), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("WARNING: that is NOT this harness's build (" + expected + ").");
                Console.WriteLine("         Rhino loaded the installed plug-in first, so results describe it.");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunPanelProbe()
        {
            Rhino.Runtime.InProcess.RhinoCore core;
            try
            {
                // Hidden, not NoWindow: NoWindow leaves RhinoDoc.ActiveDoc null
                // and does not initialise Eto.
                PreloadOwnBuild();
                core = new Rhino.Runtime.InProcess.RhinoCore(
                    new[] { "/NOSPLASH" },
                    Rhino.Runtime.InProcess.WindowStyle.Hidden);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Rhino.Inside startup failed: " + ex.GetType().Name + ": " + ex.Message);
                return 2;
            }

            ReportPlugInBuild();

            int code;
            try
            {
                code = PanelProbe.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine("panel probe threw: " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                code = 3;
            }

            // RhinoCore.Dispose() never returns in Hidden mode. Flush and take the
            // process down rather than hanging the run.
            Console.Out.Flush();
            GC.KeepAlive(core);
            Environment.Exit(code);
            return code;
        }

        // The plug-in ships as .rhp, which the default probing logic ignores.
        private static Assembly ResolvePlugInAssembly(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (!string.Equals(name, "RhinoSpellCheck", StringComparison.OrdinalIgnoreCase))
                return null;

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RhinoSpellCheck.rhp");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
    }
}
