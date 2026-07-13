using System;
using System.Runtime.InteropServices;

namespace PWDarkMode
{
    /// <summary>
    /// Surface the native .mrr shim calls into. Primitive signatures only;
    /// nothing throws across the native boundary.
    ///
    ///   Initialize      → module load, on the UI thread
    ///   CycleTheme      → single menu command: step through themes
    ///   ApplyThemeUtf16 → apply a named theme (for a themes submenu — the
    ///                     shim builds one item per name from GetThemeList)
    ///   Shutdown        → module unload
    /// </summary>
    public static class ModuleEntry
    {
        /// <returns>0 on success, non-zero on failure (native convention).</returns>
        public static int Initialize()
        {
            try
            {
                ThemeManager.Instance.Initialize(startupDelayMs: 4000);
                return 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[PWThemes] Initialize failed: " + ex);
                return 1;
            }
        }

        public static int CycleTheme()
        {
            try { ThemeManager.Instance.CycleTheme(); return 0; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[PWThemes] CycleTheme failed: " + ex);
                return 1;
            }
        }

        /// <summary>Apply a named theme. The shim passes a UTF-16 (LPCWSTR)
        /// pointer; marshalled here so the shim side stays a plain C call.</summary>
        public static int ApplyThemeUtf16(IntPtr nameUtf16)
        {
            try
            {
                string name = Marshal.PtrToStringUni(nameUtf16);
                return ThemeManager.Instance.ApplyTheme(name) ? 0 : 1;
            }
            catch { return 1; }
        }

        /// <summary>Newline-separated theme names, for the shim to build a submenu.</summary>
        public static string GetThemeList()
        {
            try { return string.Join("\n", ThemeManager.Instance.ThemeNames); }
            catch { return ""; }
        }

        public static int Shutdown()
        {
            try { ThemeManager.Instance.Dispose(); return 0; }
            catch { return 1; }
        }
    }
}
