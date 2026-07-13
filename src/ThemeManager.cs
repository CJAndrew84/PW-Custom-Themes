using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using static PWDarkMode.NativeMethods;

namespace PWDarkMode
{
    /// <summary>
    /// Orchestrator, generalised from dark-mode toggle to named themes.
    ///
    ///   Initialize()      — shim calls this at module load on PW's UI thread.
    ///                       Loads theme JSONs, restores the last-used theme,
    ///                       defers the heavy pass until PW settles.
    ///   ApplyTheme(name)  — switch live. Menu can enumerate ThemeNames to
    ///                       build a submenu (one item per theme).
    ///   CycleTheme()      — single menu-command fallback: steps through
    ///                       Dark → AtkinsRealis Dark → AtkinsRealis Light → …
    ///
    /// Staging flags retained so layers can still be rolled out incrementally.
    /// </summary>
    public sealed class ThemeManager : IDisposable
    {
        [Flags]
        public enum Stage
        {
            None = 0,
            ProcessMode = 1,   // uxtheme app mode: menus, scrollbars
            FramesAndControls = 2,   // DWM chrome + control parts + WM_CTLCOLOR
            CbtHook = 4,   // theme new dialogs as they appear
            Telerik = 8,   // tssp package / named Rad theme
            All = ProcessMode | FramesAndControls | CbtHook | Telerik,
        }

        private static ThemeManager _instance;
        public static ThemeManager Instance => _instance ?? (_instance = new ThemeManager());

        public Stage EnabledStages { get; set; } = Stage.All;
        public bool IsActive { get; private set; }
        public string ActiveThemeName => _win32?.CurrentTheme?.Name;
        public string[] ThemeNames => _store.Themes.Select(t => t.Name).ToArray();

        private readonly ThemeStore _store = new ThemeStore();
        private Win32ThemeApplier _win32;
        private TelerikThemeApplier _telerik;
        private CbtHook _cbt;
        private Timer _startupDelay;
        private Timer _sweep;
        private bool _fullPassDone;

        private ThemeManager() { }

        /// <summary>Must run on PW Explorer's UI thread.</summary>
        public void Initialize(int startupDelayMs = 4000)
        {
            if (IsActive) return;

            _store.Load();
            Theme initial = _store.Find(_store.LoadActive()) ?? _store.Themes.FirstOrDefault();
            if (initial == null) { Log("No themes available; aborting."); return; }

            _win32 = new Win32ThemeApplier(initial);   // sets process app mode (cheap, safe)
            _telerik = new TelerikThemeApplier();
            Log($"Initialized with theme '{initial.Name}' ({_store.Themes.Count} themes loaded).");

            _startupDelay = new Timer { Interval = Math.Max(500, startupDelayMs) };
            _startupDelay.Tick += (s, e) =>
            {
                _startupDelay.Stop();
                ApplyFullPass();
            };
            _startupDelay.Start();
            IsActive = true;
        }

        /// <summary>Switch to a named theme live. Safe to call from the menu handler.</summary>
        public bool ApplyTheme(string name)
        {
            Theme theme = _store.Find(name);
            if (theme == null || !IsActive) return false;

            _win32.SetTheme(theme);
            _store.SaveActive(theme.Name);
            Log($"Theme -> {theme.Name}");

            if (_fullPassDone)
            {
                // Live switch: re-run the invasive bits against the new palette.
                if (EnabledStages.HasFlag(Stage.Telerik))
                {
                    bool ok = _telerik.Apply(theme);
                    Log(ok ? $"Telerik: {_telerik.AppliedThemeName}" : "Telerik: nothing applied.");
                }
                if (EnabledStages.HasFlag(Stage.FramesAndControls))
                    ThemeAllThreadWindows();
                if (EnabledStages.HasFlag(Stage.Telerik))
                    _telerik.RefreshOpenForms();
            }
            return true;
        }

        /// <summary>Menu-command fallback when a submenu isn't wired: step to the next theme.</summary>
        public void CycleTheme()
        {
            if (!IsActive) { Initialize(500); return; }
            Theme next = _store.Next(_win32.CurrentTheme);
            if (next != null) ApplyTheme(next.Name);
        }

        private void ApplyFullPass()
        {
            try
            {
                Theme theme = _win32.CurrentTheme;

                if (EnabledStages.HasFlag(Stage.Telerik))
                {
                    bool ok = _telerik.Apply(theme);
                    Log(ok ? $"Telerik: {_telerik.AppliedThemeName}" : "Telerik: nothing applied.");
                }

                if (EnabledStages.HasFlag(Stage.FramesAndControls))
                    ThemeAllThreadWindows();

                if (EnabledStages.HasFlag(Stage.CbtHook) && _cbt == null)
                {
                    _cbt = new CbtHook(hwnd => _win32.ApplyToTree(GetAncestor(hwnd, 2 /*GA_ROOT*/)));
                    _cbt.Install();
                }

                if (EnabledStages.HasFlag(Stage.Telerik))
                    _telerik.RefreshOpenForms();

                if (_sweep == null)
                {
                    _sweep = new Timer { Interval = 3000 };
                    _sweep.Tick += (s, e) =>
                    {
                        if (EnabledStages.HasFlag(Stage.FramesAndControls))
                            ThemeAllThreadWindows();
                    };
                    _sweep.Start();
                }
                _fullPassDone = true;
            }
            catch (Exception ex)
            {
                Log("ApplyFullPass failed: " + ex);
            }
        }

        private void ThemeAllThreadWindows()
        {
            foreach (ProcessThread t in Process.GetCurrentProcess().Threads)
            {
                EnumThreadWindows((uint)t.Id, (hwnd, _) =>
                {
                    _win32.ApplyToTree(hwnd);
                    return true;
                }, IntPtr.Zero);
            }
        }

        public void Dispose()
        {
            _startupDelay?.Dispose();
            _sweep?.Dispose();
            _cbt?.Dispose();
            _win32?.Dispose();
            IsActive = false;
            _instance = null;
        }

        private static void Log(string msg)
        {
            Debug.WriteLine("[PWThemes] " + msg);
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PWThemes.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n");
            }
            catch { /* never throw from logging inside pwc.exe */ }
        }
    }
}
