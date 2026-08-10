using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

namespace BomgarMultiScreenRDP.Services
{
    internal static class RdpAutoConnectService
    {
        private const string ConnectButtonId = "1";

        public static void Connect(string rdpPath, bool autoConnect, string[] checkboxNames)
        {
            Process.Start("mstsc.exe", "\"" + rdpPath + "\"");

            if (!autoConnect)
                return;

            var dialog = WaitForMstscDialog(15000);
            if (dialog == null)
            {
                // No security prompt appeared (e.g. already-trusted host) - nothing to click
                return;
            }

            foreach (var name in checkboxNames)
            {
                ToggleCheckboxByName(dialog, name);
            }

            ClickConnectButton(dialog);
        }

        private static AutomationElement WaitForMstscDialog(int timeoutMs)
        {
            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                var windows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children,
                    Condition.TrueCondition);

                foreach (AutomationElement win in windows)
                {
                    try
                    {
                        if (win.Current.ClassName != "#32770")
                            continue;

                        var process = Process.GetProcessById(win.Current.ProcessId);
                        if (!process.ProcessName.Equals("mstsc", StringComparison.OrdinalIgnoreCase))
                            continue;

                        Thread.Sleep(50);
                        return win;
                    }
                    catch
                    {
                        // Window may have closed while inspecting it
                    }
                }

                Thread.Sleep(50);
                elapsed += 50;
            }

            return null;
        }

        private static void ToggleCheckboxByName(AutomationElement dialog, string name)
        {
            try
            {
                var cond = new PropertyCondition(AutomationElement.NameProperty, name);
                var checkbox = dialog.FindFirst(TreeScope.Descendants, cond);
                if (checkbox == null || checkbox.Current.ControlType != ControlType.CheckBox)
                    return;

                var pattern = (TogglePattern)checkbox.GetCurrentPattern(TogglePattern.Pattern);
                if (pattern.Current.ToggleState != ToggleState.On)
                    pattern.Toggle();
            }
            catch
            {
                // Checkbox not present on this prompt variant - ignore
            }
        }

        private static void ClickConnectButton(AutomationElement dialog)
        {
            try
            {
                var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, ConnectButtonId);
                var button = dialog.FindFirst(TreeScope.Descendants, cond);
                if (button == null)
                    return;

                var pattern = (InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern);
                pattern.Invoke();
            }
            catch
            {
                // Connect button not found/clickable - leave the prompt for the user
            }
        }
    }
}
