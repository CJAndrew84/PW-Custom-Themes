using System;
using System.Runtime.Serialization;

namespace PWDarkMode
{
    /// <summary>
    /// A theme definition — JSON-serializable (DataContractJsonSerializer, no
    /// NuGet deps on net481). Colours are "#RRGGBB" strings in the JSON;
    /// converted to COLORREF (0x00BBGGRR) once at load.
    ///
    /// IsDark is not declared — it's *computed* from background luminance,
    /// because it controls which uxtheme parts we request: the OS dark-mode
    /// control parts ("DarkMode_Explorer" scrollbars, menus, etc.) only make
    /// sense when the theme background is actually dark. A light corporate
    /// theme keeps the standard light parts and gets its branding from the
    /// DWM caption colours + WM_CTLCOLOR brushes + Telerik theme instead.
    /// </summary>
    [DataContract]
    public sealed class Theme
    {
        [DataMember(Name = "name")] public string Name { get; set; }

        // Core surfaces
        [DataMember(Name = "background")] public string BackgroundHex { get; set; } = "#212223";
        [DataMember(Name = "surface")] public string SurfaceHex { get; set; } = "#2A2B2C";
        [DataMember(Name = "surfaceAlt")] public string SurfaceAltHex { get; set; } = "#303234";
        [DataMember(Name = "text")] public string TextHex { get; set; } = "#E8E6E4";
        [DataMember(Name = "textMuted")] public string TextMutedHex { get; set; } = "#A0A4A8";
        [DataMember(Name = "accent")] public string AccentHex { get; set; } = "#3F32F1";
        [DataMember(Name = "border")] public string BorderHex { get; set; } = "#414345";

        // Window chrome (Win11 DWM attrs; titlebar falls back to plain
        // dark/light immersive mode on Win10)
        [DataMember(Name = "titleBar")] public string TitleBarHex { get; set; } = "#000000";
        [DataMember(Name = "titleText")] public string TitleTextHex { get; set; } = "#FFFFFF";

        // Telerik: EITHER a Visual Style Builder package to load (full custom
        // branding — the proper route for corporate themes) OR the name of a
        // theme already available in-process. Package wins if both set.
        [DataMember(Name = "telerikPackageFile")] public string TelerikPackageFile { get; set; }
        [DataMember(Name = "telerikThemeName")] public string TelerikThemeName { get; set; }

        // ---- computed, not serialized ----

        public uint Background => ColorRef(BackgroundHex);
        public uint Surface => ColorRef(SurfaceHex);
        public uint SurfaceAlt => ColorRef(SurfaceAltHex);
        public uint Text => ColorRef(TextHex);
        public uint TextMuted => ColorRef(TextMutedHex);
        public uint Accent => ColorRef(AccentHex);
        public uint Border => ColorRef(BorderHex);
        public uint TitleBar => ColorRef(TitleBarHex);
        public uint TitleText => ColorRef(TitleTextHex);

        /// <summary>Relative luminance of the background decides whether we ask
        /// the OS for dark control parts. Threshold 0.4 rather than 0.5 —
        /// mid-tones look better with dark scrollbars.</summary>
        public bool IsDark
        {
            get
            {
                ParseHex(BackgroundHex, out int r, out int g, out int b);
                double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                return lum < 0.4;
            }
        }

        /// <summary>"#RRGGBB" → COLORREF 0x00BBGGRR.</summary>
        public static uint ColorRef(string hex)
        {
            ParseHex(hex, out int r, out int g, out int b);
            return (uint)((b << 16) | (g << 8) | r);
        }

        public static System.Drawing.Color ToColor(string hex)
        {
            ParseHex(hex, out int r, out int g, out int b);
            return System.Drawing.Color.FromArgb(r, g, b);
        }

        private static void ParseHex(string hex, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex)) return;
            string h = hex.TrimStart('#');
            if (h.Length != 6) return;
            r = Convert.ToInt32(h.Substring(0, 2), 16);
            g = Convert.ToInt32(h.Substring(2, 2), 16);
            b = Convert.ToInt32(h.Substring(4, 2), 16);
        }
    }
}
