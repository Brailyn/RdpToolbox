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
        // FreeRDP 3 replaced the Windows GDI client (wfreerdp) with an SDL-based one, and current
        // Windows builds ship only sdl-freerdp.exe. Prefer that, and still accept wfreerdp so an
        // older build keeps working. Both take the same command line.
        private static readonly string[] ExecutableNames = { "sdl-freerdp.exe", "wfreerdp.exe" };

        public static string Find()
        {
            var roots = new List<string>();

            var appDir = AppDirectory();
            if (appDir.Length > 0)
            {
                roots.Add(Path.Combine(appDir, "freerdp"));
                roots.Add(appDir);
            }

            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FreeRDP"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FreeRDP"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FreeRDP"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims"));

            foreach (var root in roots)
            {
                foreach (var name in ExecutableNames)
                {
                    var candidate = Path.Combine(root, name);
                    if (SafeExists(candidate))
                        return candidate;
                }
            }

            foreach (var name in ExecutableNames)
            {
                var onPath = FindOnPath(name);
                if (onPath != null)
                    return onPath;
            }

            return null;
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

        public class FreeRdpMonitor
        {
            public int Id;
            public int X, Y, Width, Height;
        }

        // Parses the monitor list the client prints, so its own ids can be used rather than the
        // ids this tool assigns. The two enumerations need not agree, and sending the wrong ones
        // puts the session on the wrong monitors.
        public static List<FreeRdpMonitor> GetMonitors(string clientPath)
        {
            var monitors = new List<FreeRdpMonitor>();

            try
            {
                // Lines look like "[0] 1920x1080 +0+0", possibly behind a log prefix and with
                // negative offsets for monitors left of or above the primary.
                var pattern = new System.Text.RegularExpressions.Regex(
                    @"\[(?<id>\d+)\]\s+(?<w>\d+)\s*x\s*(?<h>\d+)\s+(?<x>[+-]\d+)(?<y>[+-]\d+)");

                foreach (var line in ListMonitors(clientPath)
                             .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var match = pattern.Match(line);
                    if (!match.Success)
                        continue;

                    monitors.Add(new FreeRdpMonitor
                    {
                        Id = int.Parse(match.Groups["id"].Value),
                        Width = int.Parse(match.Groups["w"].Value),
                        Height = int.Parse(match.Groups["h"].Value),
                        X = int.Parse(match.Groups["x"].Value),
                        Y = int.Parse(match.Groups["y"].Value)
                    });
                }
            }
            catch
            {
                // Unparseable output - caller falls back to its own ids
            }

            return monitors;
        }

        // Translates monitors described by their screen position into the client's own ids.
        // Returns null when any monitor cannot be matched, so the caller can fall back rather
        // than send a partly-wrong list.
        public static List<string> MapToClientIds(
            string clientPath,
            IEnumerable<Rectangle> selectedBounds)
        {
            var available = GetMonitors(clientPath);
            if (available.Count == 0)
                return null;

            var ids = new List<string>();
            foreach (var bounds in selectedBounds)
            {
                // Position identifies a monitor unambiguously; allow a little slack in case the
                // client reports rounded coordinates.
                var match = available.FirstOrDefault(m =>
                    Math.Abs(m.X - bounds.X) <= 2 &&
                    Math.Abs(m.Y - bounds.Y) <= 2 &&
                    Math.Abs(m.Width - bounds.Width) <= 2 &&
                    Math.Abs(m.Height - bounds.Height) <= 2);

                if (match == null)
                    return null;

                ids.Add(match.Id.ToString());
            }

            return ids;
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
