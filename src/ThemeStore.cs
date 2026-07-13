using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PWDarkMode
{
    /// <summary>
    /// Loads theme definitions from JSON and keeps track of the active one.
    ///
    /// Probe order (first match per theme name wins, so admins can override
    /// the built-ins without touching the deployment):
    ///   1. %ProgramData%\PWThemes\*.json      — enterprise push (Intune/SCCM)
    ///   2. &lt;add-in dir&gt;\themes\*.json    — deployed alongside the .mrr
    ///   3. Built-in defaults                    — compiled fallbacks below
    ///
    /// The last-used theme name persists to %ProgramData%\PWThemes\active.txt
    /// so the choice survives PW restarts per-machine. Swap this for a
    /// per-user location (%APPDATA%) if machines are shared.
    /// </summary>
    internal sealed class ThemeStore
    {
        internal static readonly string ProgramDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PWThemes");

        private readonly List<Theme> _themes = new List<Theme>();
        internal IReadOnlyList<Theme> Themes => _themes;

        internal void Load()
        {
            _themes.Clear();

            string addinThemesDir = Path.Combine(
                Path.GetDirectoryName(typeof(ThemeStore).Assembly.Location) ?? ".", "themes");

            foreach (string dir in new[] { ProgramDataDir, addinThemesDir })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.json"))
                {
                    Theme t = TryRead(file);
                    if (t != null && !string.IsNullOrEmpty(t.Name)
                        && !_themes.Any(x => x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Resolve a relative telerikPackageFile against the JSON's folder.
                        if (!string.IsNullOrEmpty(t.TelerikPackageFile) && !Path.IsPathRooted(t.TelerikPackageFile))
                            t.TelerikPackageFile = Path.Combine(dir, t.TelerikPackageFile);
                        _themes.Add(t);
                    }
                }
            }

            foreach (Theme t in BuiltIns())
                if (!_themes.Any(x => x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase)))
                    _themes.Add(t);
        }

        internal Theme Find(string name) =>
            _themes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        internal Theme Next(Theme current)
        {
            if (_themes.Count == 0) return null;
            int i = current == null ? -1 : _themes.IndexOf(current);
            return _themes[(i + 1) % _themes.Count];
        }

        // ---- persistence of the active choice ----

        private static string ActiveFile => Path.Combine(ProgramDataDir, "active.txt");

        internal void SaveActive(string name)
        {
            try
            {
                Directory.CreateDirectory(ProgramDataDir);
                File.WriteAllText(ActiveFile, name ?? "");
            }
            catch { /* non-fatal */ }
        }

        internal string LoadActive()
        {
            try { return File.Exists(ActiveFile) ? File.ReadAllText(ActiveFile).Trim() : null; }
            catch { return null; }
        }

        // ---- helpers ----

        private static Theme TryRead(string path)
        {
            try
            {
                var ser = new DataContractJsonSerializer(typeof(Theme));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path))))
                    return (Theme)ser.ReadObject(ms);
            }
            catch { return null; }
        }

        private static IEnumerable<Theme> BuiltIns()
        {
            // Neutral dark — the original behaviour.
            yield return new Theme
            {
                Name = "Dark",
                BackgroundHex = "#212223", SurfaceHex = "#2A2B2C", SurfaceAltHex = "#303234",
                TextHex = "#E8E6E4", TextMutedHex = "#A0A4A8",
                AccentHex = "#3F32F1", BorderHex = "#414345",
                TitleBarHex = "#000000", TitleTextHex = "#FFFFFF",
            };

            // Corporate dark: deep petrol surfaces, sky text accents, ultramarine accent.
            yield return new Theme
            {
                Name = "AtkinsRealis Dark",
                BackgroundHex = "#182D38",   // brand deep petrol as the canvas
                SurfaceHex = "#1F3844", SurfaceAltHex = "#27424F",
                TextHex = "#FFFFFF", TextMutedHex = "#BEDAE5",  // brand sky for secondary text
                AccentHex = "#3F32F1",       // brand ultramarine
                BorderHex = "#2E4A57",
                TitleBarHex = "#182D38", TitleTextHex = "#BEDAE5",
            };

            // Corporate light: standard light control parts, branded chrome +
            // sky-tinted surfaces. Titlebar carries the brand.
            yield return new Theme
            {
                Name = "AtkinsRealis Light",
                BackgroundHex = "#FFFFFF",
                SurfaceHex = "#F2F7FA", SurfaceAltHex = "#BEDAE5",
                TextHex = "#182D38", TextMutedHex = "#4A6472",
                AccentHex = "#3F32F1",
                BorderHex = "#BEDAE5",
                TitleBarHex = "#182D38", TitleTextHex = "#FFFFFF",
            };
        }
    }
}
