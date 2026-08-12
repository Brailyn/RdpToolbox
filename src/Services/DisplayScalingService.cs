using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RdpToolbox.Services
{
    // Reads each monitor's effective DPI scale. mstsc has a known window-placement bug when
    // monitors run at different scale percentages: a spanned multi-monitor session can open
    // sized correctly but positioned on the wrong monitor. Detecting the mixed-scaling
    // condition lets the app route around it (msrdc) or warn before launching mstsc.
    internal static class DisplayScalingService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;

        public static List<int> GetScalePercents()
        {
            var scales = new List<int>();

            foreach (var screen in Screen.AllScreens)
            {
                try
                {
                    var center = new POINT
                    {
                        X = screen.Bounds.X + screen.Bounds.Width / 2,
                        Y = screen.Bounds.Y + screen.Bounds.Height / 2
                    };

                    var monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);

                    uint dpiX, dpiY;
                    if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0)
                        scales.Add((int)Math.Round(dpiX * 100.0 / 96.0));
                }
                catch
                {
                    // shcore unavailable (pre-8.1) or query failed - skip this monitor
                }
            }

            return scales;
        }

        public static bool HasMixedScaling()
        {
            var scales = GetScalePercents();
            return scales.Count > 1 && scales.Distinct().Count() > 1;
        }
    }
}
