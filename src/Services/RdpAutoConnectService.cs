using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

namespace RdpToolbox.Services
{
    internal static class RdpAutoConnectService
    {
        // mstsc's Connect button carries the standard Win32 dialog control id. Other clients
        // may not, so the button is also matched by name as a fallback.
        private const string ConnectButtonId = "1";

        private static readonly string[] ConnectButtonNames = { "Connect", "Yes", "OK" };

        public static Process Connect(
            string clientPath,
            string rdpPath,
            bool autoConnect,
            string[] checkboxNames,
            bool toggleAllCheckboxes)
        {
            var process = Process.Start(clientPath, "\"" + rdpPath + "\"");

            if (!autoConnect)
                return process;

            var dialog = WaitForConnectionPrompt(clientPath, 15000);
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

        // Finds the client's connection prompt. mstsc and msrdc show the same classic "#32770"
        // security warning and only differ in which process owns it, so any known client
        // process is accepted - a client may also hand the prompt to the other one. Windows
        // carrying a Connect button are accepted as a fallback, so a client that renders the
        // prompt with a different UI stack still works.
        private static AutomationElement WaitForConnectionPrompt(string clientPath, int timeoutMs)
        {
            var processNames = new System.Collections.Generic.List<string> { "mstsc", "msrdc" };
            try
            {
                var launched = Path.GetFileNameWithoutExtension(clientPath);
                if (!string.IsNullOrEmpty(launched) &&
                    !processNames.Any(n => n.Equals(launched, StringComparison.OrdinalIgnoreCase)))
                {
                    processNames.Add(launched);
                }
            }
            catch
            {
                // Unparseable path - fall back to the known client names
            }

            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                AutomationElement fallback = null;

                var windows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children,
                    Condition.TrueCondition);

                foreach (AutomationElement win in windows)
                {
                    try
                    {
                        var process = Process.GetProcessById(win.Current.ProcessId);
                        if (!processNames.Any(n => process.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        if (win.Current.ClassName == "#32770")
                        {
                            Thread.Sleep(50);
                            return win;
                        }

                        if (fallback == null && FindConnectButton(win) != null)
                            fallback = win;
                    }
                    catch
                    {
                        // Window may have closed while inspecting it
                    }
                }

                if (fallback != null)
                {
                    Thread.Sleep(50);
                    return fallback;
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

        private static AutomationElement FindConnectButton(AutomationElement dialog)
        {
            try
            {
                var byId = dialog.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, ConnectButtonId));
                if (byId != null && byId.Current.ControlType == ControlType.Button)
                    return byId;

                var buttons = dialog.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

                foreach (AutomationElement button in buttons)
                {
                    try
                    {
                        var name = (button.Current.Name ?? "").Trim();
                        if (ConnectButtonNames.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                            return button;
                    }
                    catch
                    {
                        // Button may have gone away - ignore
                    }
                }
            }
            catch
            {
                // Prompt closed or not inspectable - ignore
            }

            return null;
        }

        private static void ClickConnectButton(AutomationElement dialog)
        {
            try
            {
                var button = FindConnectButton(dialog);
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
