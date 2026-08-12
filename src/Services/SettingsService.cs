using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace RdpToolbox.Services
{
    internal class AppSettings
    {
        public string Monitors = "0";
        public string AutoConnect = "1";
        public string AutoClickAll = "0";
        public string AutoClickWebAuthn = "0";
        public string AutoClickDrives = "0";
        public string AutoClickClipboard = "1";
        public string AutoClickPrinters = "0";
        // "auto" | "mstsc" | "msrdc"
        public string Client = "auto";
        // Console/admin session. Off by default: it is rarely needed, and admin sessions can be
        // denied the advanced graphics pipeline, forcing slow legacy bitmap encoding.
        public string AdminSession = "0";
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
                    case "AutoConnect": settings.AutoConnect = value; break;
                    case "AutoClickAll": settings.AutoClickAll = value; break;
                    case "AutoClickWebAuthn": settings.AutoClickWebAuthn = value; break;
                    case "AutoClickDrives": settings.AutoClickDrives = value; break;
                    case "AutoClickClipboard": settings.AutoClickClipboard = value; break;
                    case "AutoClickPrinters": settings.AutoClickPrinters = value; break;
                    case "Client": settings.Client = value; break;
                    case "AdminSession": settings.AdminSession = value; break;
                }
            }

            return settings;
        }

        public static void Save(
            string settingsFile,
            string monitors,
            bool autoConnect,
            bool autoClickAll,
            bool autoClickWebAuthn,
            bool autoClickDrives,
            bool autoClickClipboard,
            bool autoClickPrinters,
            string client,
            bool adminSession)
        {
            var lines = new List<string>
            {
                "Monitors=" + monitors,
                "AutoConnect=" + (autoConnect ? "1" : "0"),
                "AutoClickAll=" + (autoClickAll ? "1" : "0"),
                "AutoClickWebAuthn=" + (autoClickWebAuthn ? "1" : "0"),
                "AutoClickDrives=" + (autoClickDrives ? "1" : "0"),
                "AutoClickClipboard=" + (autoClickClipboard ? "1" : "0"),
                "AutoClickPrinters=" + (autoClickPrinters ? "1" : "0"),
                "Client=" + client,
                "AdminSession=" + (adminSession ? "1" : "0")
            };

            File.WriteAllLines(settingsFile, lines);
        }
    }
}
