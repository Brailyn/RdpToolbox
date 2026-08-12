using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace RdpToolbox.Services
{
    // Writes a diagnostics log describing the machine, its displays, which Remote Desktop
    // clients are present and where they came from, and what was actually launched. Collected
    // automatically after each launch so a session that misbehaves has a record of the state
    // that produced it.
    internal static class DiagnosticsService
    {
        private const int MaxLogsToKeep = 10;

        public static string Collect(string dataDir, string rdpFile, string settingsFile, string launchedClient, string launchReason)
        {
            var sb = new StringBuilder();

            AppendHeader(sb, launchedClient, launchReason);
            AppendSystem(sb);
            AppendGraphics(sb);
            AppendDisplays(sb);
            AppendClients(sb, launchedClient);
            AppendFile(sb, "GENERATED .RDP", rdpFile);
            AppendFile(sb, "SETTINGS", settingsFile);
            AppendClientTraceLocations(sb);
            AppendEventLog(sb);

            var path = Path.Combine(
                dataDir,
                "diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");

            // Write with a BOM: version strings carry characters like (R), and without one
            // readers fall back to the ANSI codepage and mangle them.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            PruneOldLogs(dataDir);
            return path;
        }

        private static void Section(StringBuilder sb, string title)
        {
            sb.AppendLine();
            sb.AppendLine(new string('=', 78));
            sb.AppendLine("  " + title);
            sb.AppendLine(new string('=', 78));
        }

        private static void AppendHeader(StringBuilder sb, string launchedClient, string launchReason)
        {
            sb.AppendLine("RDP Toolbox diagnostics");
            sb.AppendLine("Collected : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Version   : " + Assembly.GetExecutingAssembly().GetName().Version);
            sb.AppendLine("Computer  : " + Environment.MachineName);
            sb.AppendLine("User      : " + Environment.UserName);
            sb.AppendLine("Launched  : " + (launchedClient ?? "(nothing yet)"));
            sb.AppendLine("Reason    : " + (launchReason ?? "-"));
        }

        private static void AppendSystem(StringBuilder sb)
        {
            Section(sb, "SYSTEM");
            Try(sb, () =>
            {
                sb.AppendLine("  OS            : " + Environment.OSVersion.VersionString);
                sb.AppendLine("  64-bit OS     : " + Environment.Is64BitOperatingSystem);
                sb.AppendLine("  64-bit process: " + Environment.Is64BitProcess);
                sb.AppendLine("  CLR           : " + Environment.Version);
                sb.AppendLine("  Terminal sess : " + SystemInformation.TerminalServerSession +
                              "   (true means this machine is itself inside an RDP session)");
            });
        }

        private static void AppendGraphics(StringBuilder sb)
        {
            Section(sb, "GRAPHICS ADAPTERS  (decode capability affects repaint speed)");
            Try(sb, () =>
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        sb.AppendLine("  Adapter      : " + mo["Name"]);
                        sb.AppendLine("    Driver ver : " + mo["DriverVersion"]);
                        sb.AppendLine("    Driver date: " + FormatWmiDate(mo["DriverDate"]));
                        sb.AppendLine("    Video proc : " + mo["VideoProcessor"]);
                        sb.AppendLine("    Mode       : " + mo["VideoModeDescription"]);
                        sb.AppendLine("    Status     : " + mo["Status"]);
                        sb.AppendLine("    PNP ID     : " + mo["PNPDeviceID"]);
                        sb.AppendLine();
                    }
                }
            });
        }

        private static string FormatWmiDate(object value)
        {
            var raw = value as string;
            if (string.IsNullOrEmpty(raw) || raw.Length < 8)
                return "(unknown)";

            return raw.Substring(0, 4) + "-" + raw.Substring(4, 2) + "-" + raw.Substring(6, 2);
        }

        private static void AppendDisplays(StringBuilder sb)
        {
            Section(sb, "DISPLAYS AND SCALING");
            Try(sb, () =>
            {
                var screens = Screen.AllScreens;
                var scales = DisplayScalingService.GetScalePercents();

                for (int i = 0; i < screens.Length; i++)
                {
                    var s = screens[i];
                    sb.AppendLine("  [" + i + "] " + s.DeviceName);
                    sb.AppendLine("      Bounds : " + s.Bounds.Width + "x" + s.Bounds.Height +
                                  " at (" + s.Bounds.X + "," + s.Bounds.Y + ")");
                    sb.AppendLine("      Primary: " + s.Primary);
                    sb.AppendLine("      Scaling: " + (i < scales.Count ? scales[i] + "%" : "unknown"));
                    sb.AppendLine("      Depth  : " + s.BitsPerPixel + "-bit");
                }

                sb.AppendLine();
                sb.AppendLine("  Mixed scaling: " + DisplayScalingService.HasMixedScaling() +
                              "   (true routes spanned sessions to msrdc)");
            });
        }

        private static void AppendClients(StringBuilder sb, string launchedClient)
        {
            Section(sb, "REMOTE DESKTOP CLIENTS - WHERE THEY CAME FROM");
            Try(sb, () =>
            {
                sb.AppendLine("  Chosen this launch: " + (launchedClient ?? "(none)"));
                sb.AppendLine();

                var mstsc = Path.Combine(Environment.SystemDirectory, "mstsc.exe");
                if (File.Exists(mstsc))
                    sb.AppendLine("  Built-in : " + mstsc + "  (v" + FileVersion(mstsc) + ")");

                sb.AppendLine();
                sb.AppendLine("  msrdc copies found:");

                foreach (var path in CandidateClientPaths().Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    sb.AppendLine("    " + path);
                    sb.AppendLine("        Version : " + info.FileVersion);
                    sb.AppendLine("        Product : " + info.ProductName);
                    sb.AppendLine("        Company : " + info.CompanyName);
                    sb.AppendLine("        Modified: " + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                    sb.AppendLine("        Origin  : " + DescribeOrigin(path));
                    sb.AppendLine("        Usable  : " + (IsLaunchable(path) ? "yes" : "NO - inside a Store package, cannot be started by path"));
                }
            });
        }

        // Deliberately a fixed list rather than a disk scan: these are the locations clients
        // actually ship into, and a recursive search costs minutes for nothing extra.
        private static IEnumerable<string> CandidateClientPaths()
        {
            var appDir = AppDirectory();
            if (appDir.Length > 0)
            {
                yield return Path.Combine(appDir, "msrdc", "msrdc.exe");
                yield return Path.Combine(appDir, "msrdc.exe");
            }

            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Apps", "Remote Desktop", "msrdc.exe");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Remote Desktop", "msrdc.exe");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Remote Desktop", "msrdc.exe");

            // WSL bundles its own copy for WSLg, a common reason a machine "already has" one
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WSL", "msrdc.exe");

            foreach (var p in WindowsAppPackageClients())
                yield return p;

            foreach (var p in ExtractedClients())
                yield return p;
        }

        private static IEnumerable<string> WindowsAppPackageClients()
        {
            var results = new List<string>();
            try
            {
                using (var packages = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                {
                    if (packages == null)
                        return results;

                    foreach (var name in packages.GetSubKeyNames()
                        .Where(n => n.IndexOf("Windows365", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("RemoteDesktop", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        using (var package = packages.OpenSubKey(name))
                        {
                            var root = package == null ? null : package.GetValue("PackageRootFolder") as string;
                            if (!string.IsNullOrEmpty(root))
                                results.Add(Path.Combine(root, "msrdc", "msrdc.exe"));
                        }
                    }
                }
            }
            catch
            {
                // Registry unavailable - skip
            }

            return results;
        }

        private static IEnumerable<string> ExtractedClients()
        {
            var results = new List<string>();
            try
            {
                var cache = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RdpToolbox");

                if (Directory.Exists(cache))
                {
                    foreach (var dir in Directory.GetDirectories(cache, "client-*"))
                        results.Add(Path.Combine(dir, "msrdc.exe"));
                }
            }
            catch
            {
                // Cache unreadable - skip
            }

            return results;
        }

        private static string DescribeOrigin(string path)
        {
            if (path.IndexOf(@"\WSL\", StringComparison.OrdinalIgnoreCase) >= 0)
                return "bundled with WSL (WSLg uses msrdc for GUI apps)";
            if (path.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0)
                return "inside a Store app package (Windows App)";
            if (path.IndexOf(@"\RdpToolbox\client-", StringComparison.OrdinalIgnoreCase) >= 0)
                return "extracted by the self-contained RDP Toolbox build";
            if (path.IndexOf(@"\Apps\Remote Desktop\", StringComparison.OrdinalIgnoreCase) >= 0)
                return "standalone MSI, per-user install (often absent from the system Programs list)";
            if (path.IndexOf(@"\Program Files\Remote Desktop\", StringComparison.OrdinalIgnoreCase) >= 0)
                return "standalone MSI, per-machine install";
            if (path.StartsWith(AppDirectory(), StringComparison.OrdinalIgnoreCase))
                return "deployed next to RdpToolbox.exe";

            return "unknown";
        }

        private static bool IsLaunchable(string path)
        {
            return path.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static void AppendClientTraceLocations(StringBuilder sb)
        {
            Section(sb, "MSRDC TRACE LOGS  (written by the client after a connection)");
            Try(sb, () =>
            {
                var roots = new[]
                {
                    Path.Combine(Path.GetTempPath(), "DiagOutputDir", "RdClientAutoTrace"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rdclientwpf")
                };

                bool any = false;
                foreach (var root in roots.Where(Directory.Exists))
                {
                    any = true;
                    sb.AppendLine("  " + root);
                    var files = new DirectoryInfo(root).GetFiles("*", SearchOption.AllDirectories)
                        .OrderByDescending(f => f.LastWriteTime)
                        .Take(10);

                    foreach (var f in files)
                        sb.AppendLine("      " + f.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                                      "  " + (f.Length / 1024) + " KB  " + f.Name);
                    sb.AppendLine();
                }

                if (!any)
                    sb.AppendLine("  None found. These appear only after connecting with msrdc.");
            });
        }

        private static void AppendEventLog(StringBuilder sb)
        {
            Section(sb, "RDP CLIENT EVENT LOG (most recent 30)");
            Try(sb, () =>
            {
                var query = new EventLogQuery(
                    "Microsoft-Windows-TerminalServices-RDPClient/Operational",
                    PathType.LogName)
                {
                    ReverseDirection = true
                };

                using (var reader = new EventLogReader(query))
                {
                    for (int i = 0; i < 30; i++)
                    {
                        EventRecord record = reader.ReadEvent();
                        if (record == null)
                            break;

                        using (record)
                        {
                            string message;
                            try { message = record.FormatDescription(); }
                            catch { message = "(description unavailable)"; }

                            if (message != null)
                                message = message.Replace("\r\n", " ").Replace("\n", " ").Trim();

                            sb.AppendLine("  [" + (record.TimeCreated.HasValue
                                    ? record.TimeCreated.Value.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                                    : "??") +
                                "] id " + record.Id + "  " + message);
                        }
                    }
                }
            });
        }

        private static void AppendFile(StringBuilder sb, string title, string path)
        {
            Section(sb, title);
            Try(sb, () =>
            {
                sb.AppendLine("  " + path);
                sb.AppendLine();
                if (!File.Exists(path))
                {
                    sb.AppendLine("  (not found)");
                    return;
                }

                foreach (var line in File.ReadAllLines(path))
                    sb.AppendLine("    " + line);
            });
        }

        private static void PruneOldLogs(string dataDir)
        {
            try
            {
                var logs = new DirectoryInfo(dataDir).GetFiles("diagnostics-*.log")
                    .OrderByDescending(f => f.LastWriteTime)
                    .Skip(MaxLogsToKeep);

                foreach (var log in logs)
                {
                    try { log.Delete(); }
                    catch { /* in use - leave it */ }
                }
            }
            catch
            {
                // Folder unreadable - nothing to prune
            }
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

        private static string FileVersion(string path)
        {
            try { return FileVersionInfo.GetVersionInfo(path).FileVersion; }
            catch { return "?"; }
        }

        private static void Try(StringBuilder sb, Action action)
        {
            try { action(); }
            catch (Exception ex) { sb.AppendLine("  [collection failed: " + ex.Message + "]"); }
        }
    }
}
