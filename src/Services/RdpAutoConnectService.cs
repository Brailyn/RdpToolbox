using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

namespace RdpToolbox.Services
{
    internal static class RdpAutoConnectService
    {
        private const string ConnectButtonId = "1";

        public static Process Connect(string rdpPath, bool autoConnect, string[] checkboxNames, bool toggleAllCheckboxes)
        {
            var process = Process.Start("mstsc.exe", "\"" + rdpPath + "\"");

            if (!autoConnect)
                return process;

            var dialog = WaitForMstscDialog(15000);
            if (dialog == null)
            {
                // No connection prompt appeared (e.g. already-trusted host) - nothing to click
                return process;
            }

            if (toggleAllCheckboxes)
                ToggleAllCheckboxes(dialog);
            else
                foreach (var name in checkboxNames)
                    ToggleCheckboxByName(dialog, name);

            ClickConnectButton(dialog);

            return process;
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
                        if (!process.ProcessName.Equals("mstsc", System.StringComparison.OrdinalIgnoreCase))
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

        private static void ToggleAllCheckboxes(AutomationElement dialog)
        {
            try
            {
                var checkboxes = dialog.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));

                foreach (AutomationElement checkbox in checkboxes)
                {
                    try
                    {
                        var pattern = (TogglePattern)checkbox.GetCurrentPattern(TogglePattern.Pattern);
                        if (pattern.Current.ToggleState != ToggleState.On)
                            pattern.Toggle();
                    }
                    catch
                    {
                        // Not every checkbox exposes TogglePattern - ignore
                    }
                }
            }
            catch
            {
                // No checkboxes present on this prompt variant - ignore
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
