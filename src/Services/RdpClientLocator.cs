using System;
using System.IO;
using System.Linq;

namespace RdpToolbox.Services
{
    // Locates msrdc.exe, Microsoft's per-monitor-DPI-aware Remote Desktop client. Unlike the
    // built-in mstsc.exe it places spanned multi-monitor sessions correctly when monitors run
    // at different scale percentages.
    //
    // Only the standalone install is usable. The Windows App (msrdc's Store-delivered
    // replacement) does ship a copy of msrdc.exe inside its package, but binaries under
    // "C:\Program Files\WindowsApps" cannot be started by path - Windows denies both
    // ShellExecute and CreateProcess, permitting only package activation - so it is no help
    // here. The Windows App itself registers no .rdp file association or command line, so it
    // cannot be driven with a generated .rdp file either.
    internal static class RdpClientLocator
    {
        public static string FindMsrdc()
        {
            var candidates = new[]
            {
                // Checked first so a copy placed next to RdpToolbox.exe wins, which keeps the
                // tool portable on machines without msrdc installed. Nothing is redistributed
                // with RDP Toolbox - supplying that copy is up to whoever deploys it.
                Path.Combine(AppDirectory, "msrdc", "msrdc.exe"),
                Path.Combine(AppDirectory, "msrdc.exe"),

                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Apps", "Remote Desktop", "msrdc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Remote Desktop", "msrdc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Remote Desktop", "msrdc.exe")
            };

            return candidates.FirstOrDefault(SafeFileExists);
        }

        private static string AppDirectory
        {
            get
            {
                try
                {
                    return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
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
