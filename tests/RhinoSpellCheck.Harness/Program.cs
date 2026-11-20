using System;
using System.Runtime.CompilerServices;

namespace RhinoSpellCheck.Harness
{
    /// <summary>
    /// Integration harness. Boots Rhino in-process so the document-facing half of
    /// the plug-in (scanning, RTF-preserving edits, underline placement) can be
    /// exercised against real Rhino geometry rather than only compiled.
    ///
    /// Needs a licensed Rhino 8 on the machine. It is a developer tool, not part
    /// of the shipped plug-in.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            // The plug-in ships as RhinoSpellCheck.rhp, so the default probing
            // logic (which only looks for .dll/.exe) cannot find it.
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePlugInAssembly;

            try
            {
                RhinoInside.Resolver.Initialize();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not locate Rhino: " + ex.Message);
                return 2;
            }

            return RunInsideRhino();
        }

        private static System.Reflection.Assembly ResolvePlugInAssembly(object sender, ResolveEventArgs args)
        {
            string simpleName = new System.Reflection.AssemblyName(args.Name).Name;
            if (!string.Equals(simpleName, "RhinoSpellCheck", StringComparison.OrdinalIgnoreCase))
                return null;

            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RhinoSpellCheck.rhp");
            return System.IO.File.Exists(path) ? System.Reflection.Assembly.LoadFrom(path) : null;
        }

        // RhinoCommon types must not be JIT-resolved before the resolver is set up.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunInsideRhino()
        {
            Rhino.Runtime.InProcess.RhinoCore core = null;
            try
            {
                core = new Rhino.Runtime.InProcess.RhinoCore(
                    new[] { "/NOSPLASH" },
                    Rhino.Runtime.InProcess.WindowStyle.NoWindow);

                return IntegrationTests.RunAll();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Rhino.Inside startup failed: " + ex.GetType().Name + ": " + ex.Message);
                return 2;
            }
            finally
            {
                if (core != null)
                {
                    try { core.Dispose(); } catch (Exception) { }
                }
            }
        }
    }
}
