using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Verse;

namespace AutoColony.Learning
{
    /// <summary>One learned strategy for a particular kind of situation.</summary>
    public class ArchiveEntry
    {
        public string contextKey = "global";
        public StrategyGenome genome = StrategyGenome.Default();
        public float score;
        public int contributions;
        public int totalEpochs;
        public string sourceColony = "";

        public XElement ToXml()
        {
            var el = new XElement("entry",
                new XAttribute("context", contextKey),
                new XAttribute("score", score.ToString("R", CultureInfo.InvariantCulture)),
                new XAttribute("contributions", contributions),
                new XAttribute("epochs", totalEpochs),
                new XAttribute("source", sourceColony ?? ""));
            el.Add(genome.ToXml("genome"));
            return el;
        }

        public static ArchiveEntry FromXml(XElement el)
        {
            var e = new ArchiveEntry();
            e.contextKey = Attr(el, "context", "global");
            e.score = ParseFloat(Attr(el, "score", "0"), 0f);
            e.contributions = ParseInt(Attr(el, "contributions", "0"), 0);
            e.totalEpochs = ParseInt(Attr(el, "epochs", "0"), 0);
            e.sourceColony = Attr(el, "source", "");
            e.genome = StrategyGenome.FromXml(el.Element("genome"));
            return e;
        }

        static string Attr(XElement el, string name, string fallback)
        {
            var a = el.Attribute(name);
            return a != null ? a.Value : fallback;
        }

        static float ParseFloat(string s, float fallback)
        {
            float f;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : fallback;
        }

        static int ParseInt(string s, int fallback)
        {
            int i;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : fallback;
        }
    }

    /// <summary>
    /// Long-term memory that outlives any single save file.
    ///
    /// Within one colony the <see cref="EvolutionEngine"/> hill-climbs; across colonies this
    /// archive is what makes the mod actually improve "over time". Every colony writes back
    /// the best strategy it found, keyed by its situation (biome + difficulty), and every new
    /// colony starts from the best strategy previously learned for a comparable situation
    /// instead of from scratch.
    ///
    /// Stored as plain XML under the RimWorld save-data folder. All IO is defensive: a missing,
    /// unreadable, or corrupt archive degrades to "no prior knowledge" and never breaks a game.
    /// </summary>
    public static class StrategyArchive
    {
        public const string FolderName = "AutoColony";
        public const string FileName = "strategy_archive.xml";
        public const string GlobalKey = "global";

        static Dictionary<string, ArchiveEntry> entries;
        static Bandit researchBandit;
        static Bandit buildBandit;
        static bool loaded;

        public static string ArchivePath
        {
            get
            {
                try
                {
                    var dir = Path.Combine(GenFilePaths.SaveDataFolderPath, FolderName);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    return Path.Combine(dir, FileName);
                }
                catch (Exception e)
                {
                    AcLog.WarningOnce("archivePath", "Could not resolve archive folder: " + e.Message);
                    return null;
                }
            }
        }

        public static IEnumerable<ArchiveEntry> Entries
        {
            get
            {
                EnsureLoaded();
                return entries.Values;
            }
        }

        public static Bandit ResearchPrior
        {
            get { EnsureLoaded(); return researchBandit; }
        }

        public static Bandit BuildPrior
        {
            get { EnsureLoaded(); return buildBandit; }
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            entries = new Dictionary<string, ArchiveEntry>();
            researchBandit = new Bandit();
            buildBandit = new Bandit();

            try
            {
                var path = ArchivePath;
                if (path == null || !File.Exists(path)) return;

                var doc = XDocument.Load(path);
                var root = doc.Root;
                if (root == null) return;

                foreach (var el in root.Elements("entry"))
                {
                    var entry = ArchiveEntry.FromXml(el);
                    if (!string.IsNullOrEmpty(entry.contextKey))
                        entries[entry.contextKey] = entry;
                }

                researchBandit = Bandit.FromXml(root.Element("researchBandit"));
                buildBandit = Bandit.FromXml(root.Element("buildBandit"));

                AcLog.Message("Loaded strategy archive: " + entries.Count + " context entries from " + path);
            }
            catch (Exception e)
            {
                AcLog.Warning("Could not read strategy archive, starting fresh: " + e.Message);
                entries = new Dictionary<string, ArchiveEntry>();
                researchBandit = new Bandit();
                buildBandit = new Bandit();
            }
        }

        /// <summary>
        /// Best known strategy for this situation: an exact context match if one exists,
        /// otherwise the global best, otherwise null (meaning "use defaults").
        /// </summary>
        public static ArchiveEntry GetSeed(string contextKey)
        {
            EnsureLoaded();
            ArchiveEntry e;
            if (contextKey != null && entries.TryGetValue(contextKey, out e)) return e;
            if (entries.TryGetValue(GlobalKey, out e)) return e;

            // No global entry yet: fall back to the highest-scoring context we have.
            ArchiveEntry best = null;
            foreach (var candidate in entries.Values)
                if (best == null || candidate.score > best.score) best = candidate;
            return best;
        }

        /// <summary>
        /// Writes back what this colony learned. An entry is replaced only when the new
        /// strategy scored better, so the archive is a monotone record of the best found.
        /// </summary>
        public static void Contribute(string contextKey, StrategyGenome genome, float score,
                                      int epochs, string colonyName)
        {
            if (genome == null || float.IsNaN(score)) return;
            EnsureLoaded();

            UpsertBest(contextKey, genome, score, epochs, colonyName);
            UpsertBest(GlobalKey, genome, score, epochs, colonyName);
            Save();
        }

        static void UpsertBest(string key, StrategyGenome genome, float score, int epochs, string colonyName)
        {
            if (string.IsNullOrEmpty(key)) return;
            ArchiveEntry entry;
            if (!entries.TryGetValue(key, out entry))
            {
                entry = new ArchiveEntry();
                entry.contextKey = key;
                entry.score = float.NegativeInfinity;
                entries[key] = entry;
            }

            entry.contributions++;
            entry.totalEpochs += epochs;

            if (score > entry.score)
            {
                entry.score = score;
                entry.genome = genome.Clone();
                entry.sourceColony = colonyName ?? "";
            }
        }

        /// <summary>Merges a finished colony's bandit statistics into the cross-save priors.</summary>
        public static void ContributeBandits(Bandit research, Bandit build)
        {
            EnsureLoaded();
            // Half weight: another colony's experience is informative but not authoritative.
            if (research != null) researchBandit.MergeFrom(research, 0.5f);
            if (build != null) buildBandit.MergeFrom(build, 0.5f);
            Save();
        }

        public static void Save()
        {
            EnsureLoaded();
            try
            {
                var path = ArchivePath;
                if (path == null) return;

                var root = new XElement("AutoColonyArchive", new XAttribute("version", "1"));
                foreach (var e in entries.Values) root.Add(e.ToXml());
                root.Add(researchBandit.ToXml("researchBandit"));
                root.Add(buildBandit.ToXml("buildBandit"));

                // Write to a temp file then swap, so an interrupted write cannot corrupt the archive.
                var tmp = path + ".tmp";
                new XDocument(root).Save(tmp);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                AcLog.Warning("Could not write strategy archive: " + e.Message);
            }
        }

        /// <summary>Wipes all accumulated cross-save learning. Exposed through mod settings.</summary>
        public static void ResetAll()
        {
            EnsureLoaded();
            entries.Clear();
            researchBandit = new Bandit();
            buildBandit = new Bandit();
            Save();
            AcLog.Message("Strategy archive reset.");
        }

        /// <summary>
        /// Identifies "the kind of situation this colony is in", so strategies learned in a
        /// boreal forest on Rough are not blindly applied to an extreme desert on Losing Is Fun.
        /// </summary>
        public static string BuildContextKey(string biomeDefName, string difficultyDefName)
        {
            var biome = string.IsNullOrEmpty(biomeDefName) ? "unknown" : biomeDefName;
            var diff = string.IsNullOrEmpty(difficultyDefName) ? "unknown" : difficultyDefName;
            return biome + "|" + diff;
        }
    }
}
