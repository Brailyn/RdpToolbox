using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RdpToolbox.Services
{
    // Support for FreeRDP's Windows client (wfreerdp.exe).
    //
    // It exists here because the Microsoft clients each fail a case this tool needs: mstsc
    // misplaces a spanned session across monitors of differing scale, and msrdc collapses to
    // legacy encoding through a loopback tunnel. FreeRDP speaks plain TCP with no cloud
    // transport stack, spans same-resolution monitors directly, and offers dynamic scaling.
    //
    // It takes command-line arguments rather than a .rdp file, so it does not share the launch
    // path used for the Microsoft clients.
    internal static class FreeRdpService
    {
        public static string Find()
        {
            var candidates = new List<string>();

            var appDir = AppDirectory();
            if (appDir.Length > 0)
            {
                candidates.Add(Path.Combine(appDir, "freerdp", "wfreerdp.exe"));
                candidates.Add(Path.Combine(appDir, "wfreerdp.exe"));
            }

            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FreeRDP", "wfreerdp.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FreeRDP", "wfreerdp.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FreeRDP", "wfreerdp.exe"));

            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            candidates.Add(Path.Combine(programData, "chocolatey", "bin", "wfreerdp.exe"));

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(profile, "scoop", "shims", "wfreerdp.exe"));

            var found = candidates.FirstOrDefault(SafeExists);
            return found ?? FindOnPath("wfreerdp.exe");
        }

        // FreeRDP 3 renamed several options: certificate handling moved from "/cert-ignore" to
        // "/cert:ignore". Pick by the executable's major version so either generation works.
        public static int MajorVersion(string path)
        {
            try
            {
                var raw = FileVersionInfo.GetVersionInfo(path).FileVersion;
                if (string.IsNullOrEmpty(raw))
                    return 3;

                var firstPart = raw.Split('.', ' ')[0];
                int major;
                return int.TryParse(firstPart, out major) && major > 0 ? major : 3;
            }
            catch
            {
                return 3;
            }
        }

        public class LaunchOptions
        {
            public string Server;
            public string Username;
            public string Password;
            // FreeRDP monitor ids for the session. Empty means let it choose.
            public List<string> MonitorIds = new List<string>();
            public bool SpanMonitors;
            public Size? CustomResolution;
            public bool RedirectClipboard;
            public bool RedirectDrives;
            public bool RedirectPrinters;
            public bool DynamicResolution;
        }

        public static string BuildArguments(string clientPath, LaunchOptions options)
        {
            var args = new List<string>();
            int major = MajorVersion(clientPath);

            args.Add("/v:" + options.Server);

            if (!string.IsNullOrWhiteSpace(options.Username))
                args.Add("/u:" + Quote(options.Username));

            if (!string.IsNullOrEmpty(options.Password))
                args.Add("/p:" + Quote(options.Password));

            // The certificate presented through a jump host or gateway belongs to that hop, not
            // the target, so it never validates. Without this the client stops on a prompt.
            args.Add(major >= 3 ? "/cert:ignore" : "/cert-ignore");

            if (options.CustomResolution.HasValue)
            {
                // A fixed desktop size in a resizable window, rescaled as the window changes -
                // the behaviour asked of a single-monitor session.
                args.Add("/w:" + options.CustomResolution.Value.Width);
                args.Add("/h:" + options.CustomResolution.Value.Height);
                args.Add("/smart-sizing");
            }
            else if (options.MonitorIds.Count > 1)
            {
                // "/multimon" and "/span" are alternative mechanisms, not complementary: the
                // first runs a true multi-monitor session across the listed monitors, the second
                // stretches one desktop over them. Send only one.
                args.Add("/multimon");
                args.Add("/monitors:" + string.Join(",", options.MonitorIds));
                args.Add("/f");
            }
            else
            {
                if (options.MonitorIds.Count == 1)
                    args.Add("/monitors:" + options.MonitorIds[0]);
                args.Add("/f");
            }

            if (options.DynamicResolution && !options.CustomResolution.HasValue)
                args.Add("/dynamic-resolution");

            args.Add(options.RedirectClipboard ? "+clipboard" : "-clipboard");

            if (options.RedirectDrives)
                args.Add("/drives");

            if (options.RedirectPrinters)
                args.Add("/printer");

            // Let it adapt rather than pinning a profile - the link behind a tunnel is unknown.
            args.Add("/network:auto");

            return string.Join(" ", args);
        }

        // Arguments containing the password are kept out of anything that gets logged.
        public static string Redact(string arguments)
        {
            if (string.IsNullOrEmpty(arguments))
                return arguments;

            var rebuilt = new StringBuilder();
            foreach (var part in arguments.Split(' '))
            {
                if (part.StartsWith("/p:", StringComparison.OrdinalIgnoreCase))
                    rebuilt.Append("/p:*** ");
                else
                    rebuilt.Append(part).Append(' ');
            }

            return rebuilt.ToString().TrimEnd();
        }

        // FreeRDP numbers monitors by its own enumeration, which need not match the order used
        // elsewhere in this tool. Asking the client itself avoids guessing at the mapping.
        public static string ListMonitors(string clientPath)
        {
            try
            {
                var psi = new ProcessStartInfo(clientPath, "/monitor-list")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit(8000);

                    var combined = (output + Environment.NewLine + error).Trim();
                    return combined.Length > 0 ? combined : "(no output)";
                }
            }
            catch (Exception ex)
            {
                return "(failed: " + ex.Message + ")";
            }
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return value.IndexOf(' ') >= 0 ? "\"" + value + "\"" : value;
        }

        private static string FindOnPath(string fileName)
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in path.Split(';').Where(d => !string.IsNullOrWhiteSpace(d)))
                {
                    var candidate = Path.Combine(dir.Trim(), fileName);
                    if (SafeExists(candidate))
                        return candidate;
                }
            }
            catch
            {
                // Malformed PATH entry - ignore
            }

            return null;
        }

        private static string AppDirectory()
        {
            try
            {
                return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool SafeExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }
    }
}
