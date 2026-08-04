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

        /// <summary>
        /// What this line belongs to, when it is not the colony being played — currently
        /// "trial 2/4". Empty for ordinary live play.
        /// </summary>
        public string tag;

        public string Stamp
        {
            get { return "day " + day + " " + hour.ToString("00") + "h"; }
        }

        public override string ToString()
        {
            return Stamp.PadRight(12)
                 + (string.IsNullOrEmpty(tag) ? "" : "[" + tag + "] ")
                 + category.ToString().ToUpperInvariant().PadRight(9) + message;
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

        /// <summary>
        /// Marks every line written while something other than the live colony is being played.
        ///
        /// A training trial is indistinguishable from the real colony in the record, and
        /// "COLONY LOST" appeared sixteen times in one overnight run — every one of which had
        /// to be checked by hand against whether a reload followed, to tell a deliberate
        /// experiment from a disaster worth intervening in. That check very nearly produced a
        /// wrong intervention, so the distinction belongs on the line itself.
        /// </summary>
        public static string Tag = "";

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
            entry.tag = Tag;

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
                "colonists {0} (down {1}, breaking {2})  mood {3:0.00} (worst {10:0.00})  health {4:0.00}{12}  " +
                "food {5:0.0}d{11}{13}{15}  med {14}  wealth {6:N0}  beds {7}  fires {8}  {9:0}C",
                m.colonists, m.colonistsDowned, m.colonistsInMentalState, m.avgMood, m.avgHealth,
                m.daysOfFood, m.wealthTotal, m.colonistBeds, m.fires, m.outdoorTemperature,
                m.minMood,
                m.colonistsStarving > 0
                    ? string.Format(CultureInfo.InvariantCulture, " ({0} STARVING, hungriest {1:0.00})",
                        m.colonistsStarving, m.minFood)
                    : "",
                m.colonistsUntended > 0
                    ? string.Format(CultureInfo.InvariantCulture, " ({0} UNTENDED)", m.colonistsUntended)
                    : "",
                m.daysOfFoodUnbutchered >= 0.1f
                    ? string.Format(CultureInfo.InvariantCulture, " (+{0:0.0}d unbutchered)", m.daysOfFoodUnbutchered)
                    : "",
                m.medicineCount > m.medicineStored
                    ? string.Format(CultureInfo.InvariantCulture, "{0} ({1} stored)",
                        m.medicineCount, m.medicineStored)
                    : m.medicineCount.ToString(),
                m.daysOfFoodSpoiling >= 0.5f
                    ? string.Format(CultureInfo.InvariantCulture, " ({0:0.0}d SPOILING)", m.daysOfFoodSpoiling)
                    : ""));
        }

        /// <summary>Real seconds between heartbeats.</summary>
        const float HeartbeatSeconds = 120f;

        static float lastHeartbeatTime = float.NegativeInfinity;
        static int lastHeartbeatTick = -1;

        /// <summary>
        /// Says the run is still alive, on a wall-clock schedule, whatever else is happening.
        ///
        /// Nothing in the record distinguished "quiet" from "stopped": entries carry in-game
        /// stamps only, so a stalled process and a healthy uneventful colony look identical from
        /// outside, and a monitor matching on process name alone reported a dead game as healthy
        /// for half an hour. Carrying the wall clock and the tick together answers both halves —
        /// a stale timestamp means the process is gone, a fresh one with an unchanged tick means
        /// it is up but not simulating.
        ///
        /// The cadence is unconditional on purpose. A heartbeat that only fired when the log was
        /// otherwise quiet would make its own absence ambiguous again.
        /// </summary>
        public static void Heartbeat(int tick, string status)
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastHeartbeatTime < HeartbeatSeconds) return;

            // "+0 since last" is the signature of a stalled run, so the first beat after a
            // reload must not print it — a trial rollback winds the tick backwards and would
            // otherwise announce itself in exactly the words that mean "this game is not
            // simulating". Two very different things had one appearance, in the one line whose
            // whole job is telling them apart.
            bool firstThisSession = lastHeartbeatTick < 0;
            string progress = firstThisSession
                ? "first beat this session"
                : "+" + (tick - lastHeartbeatTick) + " since last";

            lastHeartbeatTime = now;
            lastHeartbeatTick = tick;

            Record(ChronicleCategory.System, string.Format(
                CultureInfo.InvariantCulture,
                "heartbeat {0} — tick {1} ({2}){3}",
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                tick, progress,
                string.IsNullOrEmpty(status) ? "" : ", " + status));
            Flush();
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
                    // The savedata folder is what tells two RimWorld processes on one machine
                    // apart; the command line is otherwise the only way, and a watcher that
                    // matches on the process name alone will happily report someone else's game
                    // as yours. The wall clock anchors the in-game stamps on every line below.
                    sb.AppendLine("=== session start " +
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                        " savedata " + SaveDataFolder() + " ===");
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

        static string SaveDataFolder()
        {
            try { return GenFilePaths.SaveDataFolderPath; }
            catch (Exception) { return "unknown"; }
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
            // A trial reload winds the clock back, so the next heartbeat's tick delta would be
            // negative and read as a fault rather than as the rollback it is.
            lastHeartbeatTick = -1;
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
