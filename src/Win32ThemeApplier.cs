using System;
using System.Collections.Generic;
using System.Windows.Forms;
using static PWDarkMode.NativeMethods;

namespace PWDarkMode
{
    /// <summary>
    /// The Win32 layer, generalised from "dark" to "any theme":
    ///
    ///   - DWM caption/border/text colours come from the theme (Win11 gives
    ///     genuinely branded titlebars; Win10 falls back to dark/light
    ///     immersive mode chosen by Theme.IsDark).
    ///   - The OS dark control parts ("DarkMode_Explorer" etc.) are requested
    ///     ONLY for dark themes — they're binary, not colourable. On light
    ///     branded themes the standard parts stay and branding comes from the
    ///     WM_CTLCOLOR brushes below plus the Telerik layer.
    ///   - WM_CTLCOLOR* brushes are built from the theme and rebuilt on
    ///     theme change, then everything is invalidated.
    /// </summary>
    internal sealed class Win32ThemeApplier : IDisposable
    {
        private readonly SubclassProc _subclassProc;                 // rooted — GC must not collect
        private readonly HashSet<IntPtr> _subclassed = new HashSet<IntPtr>();
        private Theme _theme;
        private IntPtr _bgBrush, _surfaceBrush;
        private bool _disposed;

        internal Win32ThemeApplier(Theme initial)
        {
            _subclassProc = CtlColorSubclassProc;
            SetTheme(initial);
        }

        internal Theme CurrentTheme => _theme;

        /// <summary>Swap the active theme: rebuild brushes and flip the
        /// process-wide app mode to match light/dark.</summary>
        internal void SetTheme(Theme theme)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));

            IntPtr oldBg = _bgBrush, oldSurface = _surfaceBrush;
            _bgBrush = CreateSolidBrush(_theme.Background);
            _surfaceBrush = CreateSolidBrush(_theme.Surface);
            if (oldBg != IntPtr.Zero) DeleteObject(oldBg);
            if (oldSurface != IntPtr.Zero) DeleteObject(oldSurface);

            UxThemeDark.SetPreferredAppMode?.Invoke(
                (int)(_theme.IsDark ? PreferredAppMode.ForceDark : PreferredAppMode.ForceLight));
            UxThemeDark.RefreshImmersiveColorPolicyState?.Invoke();
            UxThemeDark.FlushMenuThemes?.Invoke();
        }

        /// <summary>Branded titlebar/border on a top-level window.</summary>
        internal void ApplyFrame(IntPtr topLevel)
        {
            int darkFlag = _theme.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(topLevel, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int)) != 0)
                DwmSetWindowAttribute(topLevel, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref darkFlag, sizeof(int));

            // Win11-only: real brand colours in the chrome. No-ops on Win10.
            int cap = unchecked((int)_theme.TitleBar);
            int txt = unchecked((int)_theme.TitleText);
            int brd = unchecked((int)_theme.Border);
            DwmSetWindowAttribute(topLevel, DWMWA_CAPTION_COLOR, ref cap, sizeof(int));
            DwmSetWindowAttribute(topLevel, DWMWA_TEXT_COLOR, ref txt, sizeof(int));
            DwmSetWindowAttribute(topLevel, DWMWA_BORDER_COLOR, ref brd, sizeof(int));
        }

        /// <summary>Walk a top-level window and theme every native descendant.</summary>
        internal int ApplyToTree(IntPtr topLevel)
        {
            if (!IsWindow(topLevel)) return 0;
            ApplyFrame(topLevel);
            int count = 1;

            ApplyToSingle(topLevel);
            EnumChildWindows(topLevel, (child, _) =>
            {
                ApplyToSingle(child);
                count++;
                return true;
            }, IntPtr.Zero);

            RedrawWindow(topLevel, IntPtr.Zero, IntPtr.Zero,
                RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_FRAME);
            return count;
        }

        internal void ApplyToSingle(IntPtr hwnd)
        {
            // Managed control? The Telerik/WinForms layer owns it.
            if (Control.FromHandle(hwnd) != null) return;

            bool dark = _theme.IsDark;
            UxThemeDark.AllowDarkModeForWindow?.Invoke(hwnd, dark);

            string cls = ClassNameOf(hwnd);
            switch (cls)
            {
                case "SysTreeView32":
                case "SysListView32":
                case "SysHeader32":
                case "ScrollBar":
                case "Button":
                case "SysTabControl32":
                case "ToolbarWindow32":
                case "ReBarWindow32":
                case "msctls_statusbar32":
                    // Dark parts for dark themes; null resets to standard parts
                    // for light themes (matters when switching dark -> light live).
                    SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : null, null);
                    break;

                case "ComboBox":
                case "ComboBoxEx32":
                    SetWindowTheme(hwnd, dark ? "DarkMode_CFD" : null, null);
                    break;

                case "Edit":
                case "RichEdit20W":
                case "RICHEDIT50W":
                    SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : null, null);
                    Subclass(hwnd); // brushes recolour the client area for any theme
                    break;

                case "#32770":
                case "Static":
                case "MDIClient":
                    Subclass(hwnd);
                    break;

                default:
                    if (cls.StartsWith("Afx", StringComparison.OrdinalIgnoreCase))
                        Subclass(hwnd);
                    break;
            }
        }

        private void Subclass(IntPtr hwnd)
        {
            if (_disposed || _subclassed.Contains(hwnd)) return;
            if (SetWindowSubclass(hwnd, _subclassProc, (IntPtr)0xDA12, IntPtr.Zero))
                _subclassed.Add(hwnd);
        }

        private IntPtr CtlColorSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
                                            IntPtr uIdSubclass, IntPtr dwRefData)
        {
            switch (uMsg)
            {
                case WM_CTLCOLORDLG:
                case WM_ERASEBKGND when ClassNameOf(hWnd) == "#32770":
                    return _surfaceBrush;

                case WM_CTLCOLORSTATIC:
                case WM_CTLCOLORBTN:
                    SetTextColor(wParam, _theme.Text);
                    SetBkColor(wParam, _theme.Surface);
                    return _surfaceBrush;

                case WM_CTLCOLOREDIT:
                case WM_CTLCOLORLISTBOX:
                    SetTextColor(wParam, _theme.Text);
                    SetBkColor(wParam, _theme.Background);
                    return _bgBrush;

                case WM_DESTROY:
                    RemoveWindowSubclass(hWnd, _subclassProc, uIdSubclass);
                    _subclassed.Remove(hWnd);
                    break;
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (IntPtr hwnd in _subclassed)
                if (IsWindow(hwnd))
                    RemoveWindowSubclass(hwnd, _subclassProc, (IntPtr)0xDA12);
            _subclassed.Clear();
            if (_bgBrush != IntPtr.Zero) DeleteObject(_bgBrush);
            if (_surfaceBrush != IntPtr.Zero) DeleteObject(_surfaceBrush);
        }
    }
}
