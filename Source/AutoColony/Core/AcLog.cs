using System.Collections.Generic;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Prefixed logging with deduplication. An autonomous director runs unattended for
    /// hours, so an unthrottled warning inside a per-tick module would flood the log and
    /// tank the frame rate; <see cref="WarningOnce"/> keeps repeat offenders to one line.
    /// </summary>
    public static class AcLog
    {
        const string Prefix = "[AutoColony] ";
        static readonly HashSet<string> seenOnce = new HashSet<string>();

        public static bool VerboseEnabled;

        public static void Message(string msg)
        {
            Log.Message(Prefix + msg);
        }

        public static void Verbose(string msg)
        {
            if (VerboseEnabled) Log.Message(Prefix + msg);
        }

        public static void Warning(string msg)
        {
            Log.Warning(Prefix + msg);
        }

        public static void Error(string msg)
        {
            Log.Error(Prefix + msg);
        }

        public static void WarningOnce(string key, string msg)
        {
            if (!seenOnce.Add(key)) return;
            Log.Warning(Prefix + msg);
        }

        public static void ErrorOnce(string key, string msg)
        {
            if (!seenOnce.Add(key)) return;
            Log.Error(Prefix + msg);
        }
    }
}
