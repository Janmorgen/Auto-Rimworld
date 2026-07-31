using AutoColony.Learning;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoColony
{
    public enum ChronicleCategory
    {
        System,
        Vitals,
        Threat,
        Fire,
        Death,
        Health,
        Economy,
        Build,
        Research,
        Hunt,
        Incident,
        Learning
    }

    public struct ChronicleEntry
    {
        public int tick;
        public int day;
        public int hour;
        public ChronicleCategory category;
        public string message;

        public string Stamp
        {
            get { return "day " + day + " " + hour.ToString("00") + "h"; }
        }

        public override string ToString()
        {
            return Stamp.PadRight(12) + category.ToString().ToUpperInvariant().PadRight(9) + message;
        }
    }

    /// <summary>
    /// A running record of what the director saw and what it did about it.
    ///
    /// Colony failures are almost never one thing. A raider arrives, nobody is drafted, a fire
    /// starts, the fire is not fought, the survivors are short of food, and the response to
    /// being short of food kills the rest. Reading only the end state invites picking whichever
    /// cause is most visible — a frozen corpse in the snow says "cold" and says nothing about
    /// the raider an hour earlier.
    ///
    /// So this records events as they happen, in order, with the colony's vitals interleaved,
    /// and keeps it in a file that outlives the session. Reading backwards from a death gives
    /// the chain rather than the last link.
    /// </summary>
    public static class Chronicle
    {
        public const string FileName = "chronicle.log";

        /// <summary>Entries kept in memory for the status window.</summary>
        const int MaxInMemory = 400;

        /// <summary>Entries buffered before touching the disk.</summary>
        const int FlushThreshold = 8;

        /// <summary>
        /// Longest a buffered entry may wait before being written, in real seconds.
        ///
        /// Buffering alone lost the tail of every session: with events arriving a few per
        /// in-game hour the threshold was rarely reached, so quitting discarded exactly the
        /// recent history a post-mortem needs. Time-based flushing coalesces bursts without
        /// ever holding the last few lines hostage.
        /// </summary>
        const float MaxBufferSeconds = 2f;

        static float lastFlushTime;

        /// <summary>Rotate the file past this size so it cannot grow without bound.</summary>
        const long MaxFileBytes = 4L * 1024 * 1024;

        static readonly List<ChronicleEntry> recent = new List<ChronicleEntry>();
        static readonly List<string> pending = new List<string>();
        static bool headerWritten;

        public static IReadOnlyList<ChronicleEntry> Recent { get { return recent; } }

        public static string FilePath
        {
            get
            {
                try
                {
                    var dir = Path.Combine(GenFilePaths.SaveDataFolderPath, StrategyArchive.FolderName);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    return Path.Combine(dir, FileName);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static void Record(ChronicleCategory category, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (AutoColonyMod.Settings != null && !AutoColonyMod.Settings.keepChronicle) return;

            var entry = new ChronicleEntry();
            entry.category = category;
            entry.message = message;

            try
            {
                entry.tick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            }
            catch (Exception) { entry.tick = 0; }

            entry.day = entry.tick / GenDate.TicksPerDay;
            entry.hour = (entry.tick % GenDate.TicksPerDay) / GenDate.TicksPerHour;

            recent.Add(entry);
            if (recent.Count > MaxInMemory) recent.RemoveAt(0);

            pending.Add(entry.ToString());

            // Anything that can end a colony is written through immediately; losing the last
            // few lines of the record to a crash would defeat the point of keeping one.
            bool urgent = category == ChronicleCategory.Death
                       || category == ChronicleCategory.Threat
                       || category == ChronicleCategory.Fire;

            float now = Time.realtimeSinceStartup;
            if (urgent || pending.Count >= FlushThreshold || now - lastFlushTime >= MaxBufferSeconds)
                Flush();
        }

        /// <summary>
        /// Periodic snapshot of the colony's condition, so a reader can see a decline building
        /// rather than only the moment it became fatal.
        /// </summary>
        public static void RecordVitals(ColonyMetrics m)
        {
            Record(ChronicleCategory.Vitals, string.Format(
                CultureInfo.InvariantCulture,
                "colonists {0} (down {1}, breaking {2})  mood {3:0.00}  health {4:0.00}  " +
                "food {5:0.0}d  wealth {6:N0}  beds {7}  fires {8}",
                m.colonists, m.colonistsDowned, m.colonistsInMentalState, m.avgMood, m.avgHealth,
                m.daysOfFood, m.wealthTotal, m.colonistBeds, m.fires));
        }

        public static void Flush()
        {
            if (pending.Count == 0) return;

            try
            {
                var path = FilePath;
                if (path == null) { pending.Clear(); return; }

                RotateIfLarge(path);

                var sb = new StringBuilder();
                if (!headerWritten)
                {
                    headerWritten = true;
                    sb.AppendLine();
                    sb.AppendLine("=== session start ===");
                }
                for (int i = 0; i < pending.Count; i++) sb.AppendLine(pending[i]);

                File.AppendAllText(path, sb.ToString());
            }
            catch (Exception e)
            {
                AcLog.WarningOnce("chronicleWrite", "Could not write the chronicle: " + e.Message);
            }
            finally
            {
                pending.Clear();
                lastFlushTime = Time.realtimeSinceStartup;
            }
        }

        static void RotateIfLarge(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var info = new FileInfo(path);
                if (info.Length < MaxFileBytes) return;

                var old = path + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(path, old);
            }
            catch (Exception) { }
        }

        /// <summary>Called when a game is loaded or started, to mark a new session boundary.</summary>
        public static void BeginSession(string colonyName)
        {
            headerWritten = false;
            recent.Clear();
            Record(ChronicleCategory.System, "session begins for colony '" + (colonyName ?? "unknown") + "'");
        }

        public static string RenderRecent(int count)
        {
            var sb = new StringBuilder();
            int start = Math.Max(0, recent.Count - count);
            for (int i = start; i < recent.Count; i++) sb.AppendLine(recent[i].ToString());
            return sb.ToString();
        }
    }
}
