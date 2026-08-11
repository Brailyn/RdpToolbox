using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RdpToolbox.Services
{
    internal static class ServerHistoryService
    {
        private const int MaxEntries = 25;
        private const string IgnoredServer = "127.0.0.1";

        public static List<string> Load(string historyFile)
        {
            if (!File.Exists(historyFile))
                return new List<string>();

            return File.ReadAllLines(historyFile)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
        }

        public static void Add(string historyFile, string server)
        {
            server = (server ?? "").Trim();

            if (server.Length == 0 || server.Equals(IgnoredServer, StringComparison.OrdinalIgnoreCase))
                return;

            var entries = Load(historyFile);
            entries.RemoveAll(e => string.Equals(e, server, StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, server);

            if (entries.Count > MaxEntries)
                entries = entries.Take(MaxEntries).ToList();

            File.WriteAllLines(historyFile, entries);
        }

        public static void Remove(string historyFile, string server)
        {
            var entries = Load(historyFile);
            entries.RemoveAll(e => string.Equals(e, server, StringComparison.OrdinalIgnoreCase));
            File.WriteAllLines(historyFile, entries);
        }
    }
}
