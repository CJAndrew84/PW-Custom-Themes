using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PWDarkMode
{
    /// <summary>
    /// Every P/Invoke used by the add-in, in one file.
    /// Grouped: DWM, uxtheme (documented), uxtheme (undocumented ordinals),
    /// comctl32 subclassing, user32 windowing/hooks, gdi32 brushes.
    /// </summary>
    internal static class NativeMethods
    {
        // ------------------------------------------------------------------
        // DWM — dark titlebar / frame colours
        // ------------------------------------------------------------------

        // 20 on 1903+, 19 on 1809. Caption/border/text colour attrs are Win11 (build 22000+) only.
        internal const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        internal const int DWMWA_BORDER_COLOR = 34;
        internal const int DWMWA_CAPTION_COLOR = 35;
        internal const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ------------------------------------------------------------------
        // uxtheme — documented
        // ------------------------------------------------------------------

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        internal static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        // ------------------------------------------------------------------
        // uxtheme — undocumented dark-mode ordinals (stable since 1809,
        // used by Notepad++, WinMerge, Windows Terminal, etc.)
        //
        //   #104 RefreshImmersiveColorPolicyState()
        //   #133 AllowDarkModeForWindow(HWND, BOOL)
        //   #135 SetPreferredAppMode(int)  (build 18362+; on 17763 this
        //        ordinal is AllowDarkModeForApp(BOOL) — passing 1 still works)
        //   #136 FlushMenuThemes()
        //
        // Resolved manually via GetProcAddress + MAKEINTRESOURCE because
        // DllImport can't bind ordinals with EntryPoint="#n" reliably on
        // all runtimes. See UxThemeDark below.
        // ------------------------------------------------------------------

        internal enum PreferredAppMode
        {
            Default = 0,
            AllowDark = 1,
            ForceDark = 2,
            ForceLight = 3,
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern IntPtr LoadLibrary(string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate int SetPreferredAppModeDelegate(int mode);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate bool AllowDarkModeForWindowDelegate(IntPtr hwnd, bool allow);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void ParameterlessDelegate();

        /// <summary>Lazy resolver for the undocumented uxtheme ordinals. All members
        /// may be null on OS builds where the ordinal doesn't exist — callers must null-check.</summary>
        internal static class UxThemeDark
        {
            internal static readonly SetPreferredAppModeDelegate SetPreferredAppMode;
            internal static readonly AllowDarkModeForWindowDelegate AllowDarkModeForWindow;
            internal static readonly ParameterlessDelegate FlushMenuThemes;
            internal static readonly ParameterlessDelegate RefreshImmersiveColorPolicyState;

            static UxThemeDark()
            {
                IntPtr ux = GetModuleHandle("uxtheme.dll");
                if (ux == IntPtr.Zero) ux = LoadLibrary("uxtheme.dll");
                if (ux == IntPtr.Zero) return;

                SetPreferredAppMode = Resolve<SetPreferredAppModeDelegate>(ux, 135);
                AllowDarkModeForWindow = Resolve<AllowDarkModeForWindowDelegate>(ux, 133);
                FlushMenuThemes = Resolve<ParameterlessDelegate>(ux, 136);
                RefreshImmersiveColorPolicyState = Resolve<ParameterlessDelegate>(ux, 104);
            }

            private static T Resolve<T>(IntPtr module, int ordinal) where T : class
            {
                IntPtr p = GetProcAddress(module, new IntPtr(ordinal));
                return p == IntPtr.Zero ? null : (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
            }
        }

        // ------------------------------------------------------------------
        // comctl32 — window subclassing
        // ------------------------------------------------------------------

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
                                              IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        internal static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
                                                      IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        internal static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
                                                         IntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        internal static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        // ------------------------------------------------------------------
        // user32 — enumeration, hooks, class names, redraw
        // ------------------------------------------------------------------

        internal const int WH_CBT = 5;
        internal const int HCBT_ACTIVATE = 5;
        internal const int HCBT_CREATEWND = 3;

        internal const uint WM_CTLCOLOREDIT = 0x0133;
        internal const uint WM_CTLCOLORLISTBOX = 0x0134;
        internal const uint WM_CTLCOLORBTN = 0x0135;
        internal const uint WM_CTLCOLORDLG = 0x0136;
        internal const uint WM_CTLCOLORSTATIC = 0x0138;
        internal const uint WM_ERASEBKGND = 0x0014;
        internal const uint WM_THEMECHANGED = 0x031A;
        internal const uint WM_DESTROY = 0x0002;

        internal const uint RDW_INVALIDATE = 0x0001;
        internal const uint RDW_ERASE = 0x0004;
        internal const uint RDW_ALLCHILDREN = 0x0080;
        internal const uint RDW_FRAME = 0x0400;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        internal static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpfn, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags); // 2 = GA_ROOT

        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr hWnd);

        // ------------------------------------------------------------------
        // gdi32 — brushes and DC colours for WM_CTLCOLOR*
        // ------------------------------------------------------------------

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateSolidBrush(uint colorref);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        internal static extern uint SetTextColor(IntPtr hdc, uint colorref);

        [DllImport("gdi32.dll")]
        internal static extern uint SetBkColor(IntPtr hdc, uint colorref);

        [DllImport("gdi32.dll")]
        internal static extern int SetBkMode(IntPtr hdc, int mode); // 1 = TRANSPARENT, 2 = OPAQUE

        internal static string ClassNameOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
