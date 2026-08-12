using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace RdpToolbox.Services
{
    // Locates msrdc.exe, Microsoft's per-monitor-DPI-aware Remote Desktop client. Unlike the
    // built-in mstsc.exe it places spanned multi-monitor sessions correctly when monitors run
    // at different scale percentages.
    //
    // Only a standalone copy is usable. The Windows App (msrdc's Store-delivered replacement)
    // does ship a copy of msrdc.exe inside its package, but binaries under
    // "C:\Program Files\WindowsApps" cannot be started by path - Windows denies both
    // ShellExecute and CreateProcess, permitting only package activation - so it is no help
    // here. The Windows App itself registers no .rdp file association or command line, so it
    // cannot be driven with a generated .rdp file either.
    internal static class RdpClientLocator
    {
        // Optional. When a copy of the client is compressed into src\msrdc.zip at build time it
        // is embedded under this name, making the build self-contained; when it is absent the
        // build simply has no embedded client and relies on the paths below.
        private const string EmbeddedClientResource = "RdpToolbox.msrdc.zip";

        public static string FindMsrdc()
        {
            var candidates = new[]
            {
                // A copy deployed next to RdpToolbox.exe wins, so a known-good client can be
                // pinned. Nothing is redistributed with RDP Toolbox itself - supplying that
                // copy is up to whoever deploys it.
                Path.Combine(AppDirectory, "msrdc", "msrdc.exe"),
                Path.Combine(AppDirectory, "msrdc.exe"),

                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Apps", "Remote Desktop", "msrdc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Remote Desktop", "msrdc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Remote Desktop", "msrdc.exe")
            };

            var found = candidates.FirstOrDefault(SafeFileExists);
            if (found != null)
                return found;

            // Nothing installed or deployed - fall back to the embedded copy, if this build has
            // one. Extraction happens once and is reused on later runs.
            return ExtractEmbeddedClient();
        }

        public static bool HasEmbeddedClient()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedClientResource))
                    return stream != null;
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractEmbeddedClient()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();

                using (var stream = assembly.GetManifestResourceStream(EmbeddedClientResource))
                {
                    if (stream == null)
                        return null;

                    // Key the extraction folder on the resource size so a build carrying a
                    // different client extracts alongside rather than reusing a stale copy.
                    var target = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RdpToolbox",
                        "client-" + stream.Length);

                    var exe = Path.Combine(target, "msrdc.exe");
                    if (SafeFileExists(exe))
                        return exe;

                    // Extract to a staging folder first, so an interrupted extraction is never
                    // mistaken for a usable client.
                    var staging = target + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    Directory.CreateDirectory(staging);

                    try
                    {
                        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                            archive.ExtractToDirectory(staging);

                        if (Directory.Exists(target))
                            Directory.Delete(target, true);

                        Directory.Move(staging, target);
                    }
                    catch
                    {
                        TryDeleteDirectory(staging);
                        throw;
                    }

                    return SafeFileExists(exe) ? exe : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        private static string AppDirectory
        {
            get
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
        }

        private static bool SafeFileExists(string path)
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
