using System.Diagnostics;

namespace RdpToolbox.Services
{
    // Stages a credential in Windows Credential Manager (TERMSRV/<server>) so mstsc can
    // sign in without a typed password, then removes it once the session ends.
    internal static class CredentialStagingService
    {
        public static bool Stage(string server, string username, string password)
        {
            return RunCmdKey("/generic:TERMSRV/" + server + " /user:" + Quote(username) + " /pass:" + Quote(password));
        }

        public static void Remove(string server)
        {
            RunCmdKey("/delete:TERMSRV/" + server);
        }

        private static bool RunCmdKey(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("cmdkey.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = Process.Start(psi))
                {
                    process.WaitForExit(5000);
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }
    }
}
