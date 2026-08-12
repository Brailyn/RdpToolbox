using System;
using System.IO;
using System.Linq;

namespace RdpToolbox.Services
{
    // Locates Microsoft's modern "Remote Desktop client for Windows" (msrdc.exe). Unlike the
    // built-in mstsc.exe it is per-monitor DPI aware, which fixes spanned multi-monitor
    // sessions on systems where monitors run at different scale percentages.
    internal static class RdpClientLocator
    {
        public static string FindMsrdc()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Apps", "Remote Desktop", "msrdc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Remote Desktop", "msrdc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Remote Desktop", "msrdc.exe")
            };

            return candidates.FirstOrDefault(File.Exists);
        }
    }
}
