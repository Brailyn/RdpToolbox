using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BomgarMultiScreenRDP.Services
{
    internal class AppSettings
    {
        public string Monitors = "0";
        public string Clipboard = "1";
        public string AutoConnect = "1";
    }

    internal static class SettingsService
    {
        public static AppSettings Load(string settingsFile)
        {
            var settings = new AppSettings();

            if (!File.Exists(settingsFile))
                return settings;

            foreach (var line in File.ReadAllLines(settingsFile))
            {
                var match = Regex.Match(line, @"^\s*([^=]+)=(.*)$");
                if (!match.Success)
                    continue;

                var key = match.Groups[1].Value.Trim();
                var value = match.Groups[2].Value.Trim();

                switch (key)
                {
                    case "Monitors": settings.Monitors = value; break;
                    case "Clipboard": settings.Clipboard = value; break;
                    case "AutoConnect": settings.AutoConnect = value; break;
                }
            }

            return settings;
        }

        public static void Save(string settingsFile, string monitors, bool clipboard, bool autoConnect)
        {
            var lines = new List<string>
            {
                "Monitors=" + monitors,
                "Clipboard=" + (clipboard ? "1" : "0"),
                "AutoConnect=" + (autoConnect ? "1" : "0")
            };

            File.WriteAllLines(settingsFile, lines);
        }
    }
}
