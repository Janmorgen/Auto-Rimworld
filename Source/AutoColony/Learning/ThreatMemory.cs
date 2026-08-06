using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace AutoColony.Learning
{
    /// <summary>What kind of thing is attacking, because they do not cost the same.</summary>
    public enum ThreatKind
    {
        /// <summary>A faction raid that walks in and fights.</summary>
        Raid,

        /// <summary>A siege: sits at range, builds mortars, and is resupplied.</summary>
        Siege,

        /// <summary>A predator hunting a colonist. One animal, and it does not lose interest.</summary>
        Predator,

        /// <summary>A manhunter pack. Many animals, all of them coming.</summary>
        Manhunter,

        /// <summary>Insects from a hive.</summary>
        Infestation,

        Other
    }

    /// <summary>
    /// What each kind of fight has actually cost this colony, and how much force it wants.
    ///
    /// The engagement rule used to be one flat ratio for everything: strength over threat against
    /// a single gene, adjusted for refuge and casualties. That number could only ever be wrong
    /// for most of the things it was applied to. A lone tribal raider and an arctic wolf and a
    /// manhunter pack of twelve are not the same problem, and a colony that has just watched two
    /// of its three people bleed out has learned something a constant cannot hold.
    ///
    /// So force is per kind and it moves. After every engagement the colony reads what the fight
    /// did to it — damage taken across the colonists who fought, against how many it sent — and
    /// adjusts the force it will bring to that kind next time. Hurt badly, bring more. Walked
    /// away clean, it can afford to send fewer and leave hands on the harvest.
    ///
    /// Sending more is not free, which is why this is learned rather than set high. Every
    /// colonist drafted is a colonist not hauling, cooking or building, and in a colony of three
    /// that is most of the workforce. The right amount is the amount that ends the fight without
    /// a casualty, and only the fight can say what that is.
    /// </summary>
    public static class ThreatMemory
    {
        public const string FileName = "threat_memory.xml";

        /// <summary>
        /// What one encounter taught, and what has been learned from all of them.
        ///
        /// <c>force</c> is the multiplier on the strength advantage this kind is engaged at, and
        /// it is the only thing the director reads. Everything else is the evidence behind it,
        /// kept so a reader can see why the number is where it is.
        /// </summary>
        public class Record
        {
            public ThreatKind kind;
            public int encounters;
            public int committed;          // colonists sent, summed
            public float damageTaken;      // health lost across those colonists, summed
            public int casualties;         // times somebody went down or died
            public float force = 1.5f;     // learned multiplier, the thing that is used

            public float DamagePerColonist
            {
                get { return committed > 0 ? damageTaken / committed : 0f; }
            }

            public XElement ToXml()
            {
                return new XElement("threat",
                    new XAttribute("kind", kind.ToString()),
                    new XAttribute("encounters", encounters),
                    new XAttribute("committed", committed),
                    new XAttribute("damage", damageTaken.ToString("0.###")),
                    new XAttribute("casualties", casualties),
                    new XAttribute("force", force.ToString("0.###")));
            }

            public static Record FromXml(XElement el)
            {
                var r = new Record();
                try
                {
                    r.kind = (ThreatKind)Enum.Parse(typeof(ThreatKind), Attr(el, "kind", "Other"));
                    r.encounters = int.Parse(Attr(el, "encounters", "0"));
                    r.committed = int.Parse(Attr(el, "committed", "0"));
                    r.damageTaken = float.Parse(Attr(el, "damage", "0"));
                    r.casualties = int.Parse(Attr(el, "casualties", "0"));
                    r.force = float.Parse(Attr(el, "force", "1.5"));
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

        static readonly Dictionary<ThreatKind, Record> records = new Dictionary<ThreatKind, Record>();

        /// <summary>
        /// Bounds on the learned force.
        ///
        /// The floor is not 0: a colony that has been lucky twice should not conclude that a
        /// manhunter pack can be met by one person. The ceiling stops a run of bad fights
        /// convincing it that nothing is ever worth engaging, which is its own way to die —
        /// raiders left alone burn the base down.
        /// </summary>
        public const float MinForce = 0.8f;
        public const float MaxForce = 4.0f;

        /// <summary>How fast one fight moves the number. Slow enough that a fluke does not.</summary>
        const float LearningRate = 0.25f;

        /// <summary>
        /// Damage per committed colonist above which the colony decides it brought too few.
        ///
        /// Expressed as a fraction of a colonist's health, so it does not care how many people
        /// were sent — the question is what the fight did to each of them.
        /// </summary>
        const float HurtBadly = 0.25f;

        /// <summary>Below this the fight was cheap and the colony can try sending fewer.</summary>
        const float WalkedAway = 0.05f;

        static bool loaded;

        /// <summary>
        /// Loaded on first use, the way the strategy archive is, so nothing has to remember to
        /// call it. What a colony learned about wolves should outlive the colony.
        /// </summary>
        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            Load();
        }

        public static Record For(ThreatKind kind)
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
        /// The strength advantage to bring to this kind of fight.
        ///
        /// Falls back to the genome until this colony has met the kind at least once, so a fresh
        /// colony still starts from an evolved prior rather than a guess, and the two disagree
        /// only where experience has earned it.
        /// </summary>
        public static float ForceFor(ThreatKind kind, float geneDefault)
        {
            var r = For(kind);
            return r.encounters <= 0 ? geneDefault : r.force;
        }

        /// <summary>
        /// What the fight cost, folded into what to bring next time.
        ///
        /// A casualty counts for more than its health value: somebody on the floor is not just
        /// hurt, they are out of the workforce, need carrying, need tending, and may die of it.
        /// The adjustment is proportional to how far the outcome sat from what was wanted, so a
        /// mauling moves the number further than a scrape.
        /// </summary>
        public static void RecordOutcome(ThreatKind kind, int committed, float damageTaken,
                                         int casualties)
        {
            RecordOutcome(kind, committed, damageTaken, casualties, 0);
        }

        /// <summary>
        /// The same, told how many came out of it still bleeding.
        ///
        /// Run 168 day 23 is why. A mad cougar was met at 1.55x against a required 1.50x, the
        /// colony won it without anybody going down, and it cost 0.52 health across three sent
        /// and left two colonists bleeding out. That averages to 0.17 each, which falls in the
        /// gap between WalkedAway and HurtBadly — so the fight taught this memory **nothing at
        /// all**. Health went 1.00 to 0.83 and the next fight starts from there.
        ///
        /// Two separate blindnesses, and the second is the one that matters:
        ///
        /// The mean hides the distribution. Two colonists badly hurt and one untouched reads the
        /// same as three lightly grazed, and it is the first that loses colonies. This project
        /// already learned that lesson for mood and fixed it there — worst mood is tracked
        /// beside the average precisely because an average colony can be fine while one person
        /// in it breaks. It was never carried across to fights.
        ///
        /// And "casualty" here means downed. Somebody on a blood-loss clock who is still on
        /// their feet is neither a casualty nor a scrape, and was visible to this as neither.
        ///
        /// So bleeding disqualifies "cheap". That is not a new threshold — it makes an existing
        /// category honest, because a colonist bleeding out has not walked away from anything.
        /// </summary>
        public static void RecordOutcome(ThreatKind kind, int committed, float damageTaken,
                                         int casualties, int leftBleeding)
        {
            RecordOutcome(kind, committed, damageTaken, casualties, leftBleeding,
                          DefaultBleedingAsCasualty);
        }

        /// <summary>How much of a casualty a still-bleeding colonist counts as, absent a genome.</summary>
        public const float DefaultBleedingAsCasualty = 0.5f;

        /// <summary>
        /// The same, with the genome's view of what a bleeding colonist is worth.
        ///
        /// The weight has to come from somewhere and it is not something this file can know, so
        /// it is the colony's to argue with. What is *not* a matter of opinion is the direction:
        /// somebody still losing blood is partway to being on the floor, and treating them as
        /// unhurt is how run 168's cougar taught nothing.
        /// </summary>
        public static void RecordOutcome(ThreatKind kind, int committed, float damageTaken,
                                         int casualties, int leftBleeding,
                                         float bleedingAsCasualty)
        {
            if (committed <= 0) return;

            var r = For(kind);
            r.encounters++;
            r.committed += committed;
            r.damageTaken += damageTaken;
            r.casualties += casualties;

            float perColonist = damageTaken / committed;
            float target = r.force;

            // A colonist still bleeding when the fight ends has not finished paying for it, and
            // the health delta measured at that moment understates what the fight cost by
            // however much they lose before somebody reaches them. Counted as a fraction of a
            // casualty rather than as a separate signal, because that is what it is.
            if (bleedingAsCasualty < 0f) bleedingAsCasualty = 0f;
            float effectiveCasualties = casualties + leftBleeding * bleedingAsCasualty;

            if (effectiveCasualties > 0f)
            {
                // Somebody went down, or is going that way. Whatever was brought, it was not
                // enough.
                target = r.force * (1f + 0.5f * effectiveCasualties);
            }
            else if (perColonist > HurtBadly)
            {
                target = r.force * (1f + (perColonist - HurtBadly));
            }
            else if (perColonist < WalkedAway && leftBleeding <= 0)
            {
                // Cheap, and everybody actually walked away. Try holding a pair of hands back.
                target = r.force * 0.9f;
            }

            r.force = Clamp(r.force + (target - r.force) * LearningRate, MinForce, MaxForce);
        }

        static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        /// <summary>What has been learned, in one line, for the record.</summary>
        public static string Explain(ThreatKind kind)
        {
            var r = For(kind);
            if (r.encounters <= 0) return kind + ": never met one";

            return string.Format(
                "{0}: {1} met, force {2:0.00}x, {3:0.00} health lost per colonist sent, {4} casualties",
                kind, r.encounters, r.force, r.DamagePerColonist, r.casualties);
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

                var root = new XElement("AutoColonyThreatMemory", new XAttribute("version", "1"));
                foreach (var r in records.Values) root.Add(r.ToXml());

                var tmp = path + ".tmp";
                new XDocument(root).Save(tmp);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { AcLog.Warning("threat memory save failed: " + e.Message); }
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
                foreach (var el in doc.Root.Elements("threat"))
                {
                    var r = Record.FromXml(el);
                    records[r.kind] = r;
                }
            }
            catch (Exception e) { AcLog.Warning("threat memory load failed: " + e.Message); }
        }

        /// <summary>For the self-test and for starting a run clean.</summary>
        public static void Clear() { records.Clear(); loaded = true; }
    }
}
