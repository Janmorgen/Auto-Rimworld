using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Verse;

namespace AutoColony.Learning
{
    /// <summary>What a goal is waiting on, because the kinds do not take the same time.</summary>
    public enum BlockerKind
    {
        /// <summary>A research project somebody has to sit at a bench for.</summary>
        Research,

        /// <summary>A room, or anything else with walls to put up.</summary>
        Construction,

        /// <summary>Nothing the colony can point at. The honest answer is that it does not know.</summary>
        Unknown
    }

    /// <summary>
    /// How long each kind of wait really takes, against how long the arithmetic said.
    ///
    /// Patience is estimated from a measured rate — points remaining over points per tick —
    /// and that estimate is systematically optimistic, because it assumes the rate holds. It
    /// does not. Researchers get pulled onto hauling when the larder empties; builders get
    /// drafted the moment something walks onto the map; a mental break takes a third of a
    /// three-person workforce off the job for a day. None of that is in the arithmetic and all
    /// of it is in the outcome.
    ///
    /// So the colony keeps the ratio. If research consistently takes 1.4 times as long as the
    /// bank rate predicts, it learns 1.4 and stops standing its research goals down a day
    /// early. That is a thing only experience can supply, and it is the same argument
    /// ThreatMemory makes about how much force a wolf actually wants.
    ///
    /// Falls back to a gene until a kind has been met, so a fresh colony starts from an evolved
    /// prior rather than a guess, and the two disagree only where experience has earned it.
    /// </summary>
    public static class PatienceMemory
    {
        public const string FileName = "patience_memory.xml";

        /// <summary>
        /// What one kind of wait has cost, and the multiplier that follows from it.
        ///
        /// <c>ratio</c> is the only thing the planner reads. The rest is the evidence behind
        /// it, kept so a reader can see why the number is where it is.
        /// </summary>
        public class Record
        {
            public BlockerKind kind;
            public int spells;             // times a goal waited on this kind and finished waiting
            public long estimatedTicks;    // what the arithmetic said, summed
            public long actualTicks;       // what it took, summed
            public float ratio = 1f;       // learned multiplier, the thing that is used

            public XElement ToXml()
            {
                return new XElement("blocker",
                    new XAttribute("kind", kind.ToString()),
                    new XAttribute("spells", spells),
                    new XAttribute("estimated", estimatedTicks),
                    new XAttribute("actual", actualTicks),
                    new XAttribute("ratio", ratio.ToString("0.###")));
            }

            public static Record FromXml(XElement el)
            {
                var r = new Record();
                try
                {
                    r.kind = (BlockerKind)Enum.Parse(typeof(BlockerKind), Attr(el, "kind", "Unknown"));
                    r.spells = int.Parse(Attr(el, "spells", "0"));
                    r.estimatedTicks = long.Parse(Attr(el, "estimated", "0"));
                    r.actualTicks = long.Parse(Attr(el, "actual", "0"));
                    r.ratio = float.Parse(Attr(el, "ratio", "1"));
                }
                catch (Exception) { }
                return r;
            }

            static string Attr(XElement el, string name, string fallback)
            {
                var a = el.Attribute(name);
                return a != null ? a.Value : fallback;
            }
        }

        static readonly Dictionary<BlockerKind, Record> records = new Dictionary<BlockerKind, Record>();

        /// <summary>
        /// Bounds on the learned ratio.
        ///
        /// The floor is not zero: a colony that once finished a project early should not
        /// conclude that research is instant. The ceiling stops a run of interrupted spells
        /// convincing it that nothing ever finishes, which is its own way to stall — a goal
        /// held for twenty days is a colony doing one thing while everything else rots.
        /// </summary>
        public const float MinRatio = 0.5f;
        public const float MaxRatio = 4.0f;

        /// <summary>How fast one spell moves the number. Slow enough that a fluke does not.</summary>
        const float LearningRate = 0.25f;

        static bool loaded;

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            Load();
        }

        public static Record For(BlockerKind kind)
        {
            EnsureLoaded();
            Record r;
            if (!records.TryGetValue(kind, out r))
            {
                r = new Record { kind = kind };
                records[kind] = r;
            }
            return r;
        }

        /// <summary>
        /// The multiplier to put on an estimate for this kind of wait.
        ///
        /// Falls back to the genome until this colony family has actually finished a wait of
        /// this kind, the same way ThreatMemory falls back for a threat it has never met.
        /// </summary>
        public static float RatioFor(BlockerKind kind, float geneDefault)
        {
            var r = For(kind);
            return r.spells <= 0 ? geneDefault : r.ratio;
        }

        /// <summary>
        /// What the wait actually cost, folded into what to expect next time.
        ///
        /// Only called when a spell ends in the goal's own terms — the urgency finally moved,
        /// or the blocker cleared. A spell that ended because something more urgent happened
        /// teaches nothing about how long the work takes and must not be recorded.
        /// </summary>
        public static void RecordOutcome(BlockerKind kind, int estimatedTicks, int actualTicks)
        {
            if (estimatedTicks <= 0 || actualTicks <= 0) return;

            var r = For(kind);
            r.spells++;
            r.estimatedTicks += estimatedTicks;
            r.actualTicks += actualTicks;

            float observed = actualTicks / (float)estimatedTicks;
            r.ratio = Clamp(r.ratio + (observed - r.ratio) * LearningRate, MinRatio, MaxRatio);
        }

        static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        /// <summary>What has been learned, in one line, for the record.</summary>
        public static string Explain(BlockerKind kind)
        {
            var r = For(kind);
            if (r.spells <= 0) return kind + ": never waited on one";

            return string.Format(
                "{0}: {1} waits, {2:0.00}x the arithmetic",
                kind, r.spells, r.ratio);
        }

        // ------------------------------------------------------------------ persistence

        static string Path
        {
            get
            {
                try
                {
                    var dir = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath,
                                                     StrategyArchive.FolderName);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    return System.IO.Path.Combine(dir, FileName);
                }
                catch (Exception) { return null; }
            }
        }

        public static void Save()
        {
            try
            {
                var path = Path;
                if (path == null) return;

                var root = new XElement("AutoColonyPatienceMemory", new XAttribute("version", "1"));
                foreach (var r in records.Values) root.Add(r.ToXml());

                var tmp = path + ".tmp";
                new XDocument(root).Save(tmp);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { AcLog.Warning("patience memory save failed: " + e.Message); }
        }

        public static void Load()
        {
            try
            {
                loaded = true;
                records.Clear();
                var path = Path;
                if (path == null || !File.Exists(path)) return;

                var doc = XDocument.Load(path);
                foreach (var el in doc.Root.Elements("blocker"))
                {
                    var r = Record.FromXml(el);
                    records[r.kind] = r;
                }
            }
            catch (Exception e) { AcLog.Warning("patience memory load failed: " + e.Message); }
        }

        /// <summary>For the self-test and for starting a run clean.</summary>
        public static void Clear() { records.Clear(); loaded = true; }
    }
}
