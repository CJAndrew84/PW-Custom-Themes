using System;
using static PWDarkMode.NativeMethods;

namespace PWDarkMode
{
    /// <summary>
    /// Thread-local WH_CBT hook on PW Explorer's UI thread. PW spawns dialogs
    /// constantly (login, wizards, properties, check-in…) and each one would
    /// otherwise appear light. We theme on HCBT_ACTIVATE rather than
    /// HCBT_CREATEWND because at activate time the window and its children
    /// fully exist — at create time child controls haven't been made yet.
    ///
    /// NOTE: this hooks only the thread it is installed from. If PW creates
    /// UI on secondary threads (it does for some progress dialogs), install
    /// one instance per UI thread or fall back to the periodic sweep in
    /// DarkModeManager, which exists exactly to mop those up.
    /// </summary>
    internal sealed class CbtHook : IDisposable
    {
        private readonly HookProc _proc;          // rooted — GC must not collect while hooked
        private readonly Action<IntPtr> _onWindowActivated;
        private IntPtr _hook;

        internal CbtHook(Action<IntPtr> onWindowActivated)
        {
            _onWindowActivated = onWindowActivated ?? throw new ArgumentNullException(nameof(onWindowActivated));
            _proc = HookCallback;
        }

        /// <summary>Install on the *current* thread. Call from PW's UI thread.</summary>
        internal void Install()
        {
            if (_hook != IntPtr.Zero) return;
            _hook = SetWindowsHookEx(WH_CBT, _proc, IntPtr.Zero, GetCurrentThreadId());
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == HCBT_ACTIVATE)
            {
                try
                {
                    // wParam is the HWND being activated. Theme its whole tree.
                    _onWindowActivated(wParam);
                }
                catch
                {
                    // Never let an exception escape a hook proc inside pwc.exe.
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
