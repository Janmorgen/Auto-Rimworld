using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// An immutable read of everything the director needs to know about a colony.
    ///
    /// Captured on a cadence rather than per tick: every module reads from one shared
    /// snapshot so a decision pass is internally consistent and the expensive lookups
    /// (wealth, resource counts, pawn iteration) happen once instead of once per module.
    /// </summary>
    public class ColonyState
    {
        /// <summary>Nutrition a colonist consumes per day at the standard hunger rate.</summary>
        public const float NutritionPerColonistDay = 1.6f;

        public Map map;
        public int tick;
        public int day;

        // --- population ---
        public int colonists;
        public int colonistsDowned;
        public int colonistsInMentalState;
        public int prisoners;
        public float avgMood = 0.5f;
        public float avgHealth = 1f;
        public float minMood = 1f;

        // --- food and medicine ---
        public float foodNutrition;
        public float daysOfFood;
        public int medicineCount;

        // --- raw materials ---
        public int wood;
        public int steel;
        public int components;
        public int textiles;
        public int silver;

        // --- economy and infrastructure ---
        public float wealthTotal;
        public float wealthBuildings;
        public int colonistBeds;
        public int turrets;
        public int workTables;
        public int pendingBlueprints;
        public int pendingFrames;
        public int fires;

        /// <summary>
        /// Haulable items sitting under open sky. They deteriorate where they are, and in a
        /// dry climate they are also the easiest thing on the map to lose to a fire.
        /// </summary>
        public int itemsOutdoors;

        // --- research ---
        public int researchFinished;
        public bool hasResearchBench;

        // --- threat ---
        public StoryDanger danger = StoryDanger.None;
        public int hostilePawns;

        public bool Valid { get { return map != null && colonists > 0; } }

        // --- proximity, filled in by the director once the base location is known ---

        /// <summary>Fires close enough to the colony to matter.</summary>
        public int firesNearBase;

        /// <summary>Distance from the base to the closest fire, or -1 if none burning.</summary>
        public float nearestFireDistance = -1f;

        /// <summary>Hostiles close enough to the colony to matter.</summary>
        public int hostilesNearBase;

        /// <summary>
        /// True when something is happening at the colony itself that colonists should be
        /// dealing with rather than walking away from.
        /// </summary>
        public bool EmergencyAtHome { get { return firesNearBase > 0 || hostilesNearBase > 0; } }

        /// <summary>
        /// Works out what is close enough to matter.
        ///
        /// Distance is what separates a threat from a curiosity. A fire on the far side of the
        /// map will never reach the colony and is not worth a single work-hour; the same fire
        /// against a wall is an emergency. Nothing else in the snapshot can tell them apart,
        /// because only the director knows where the base is.
        /// </summary>
        public void AnnotateProximity(IntVec3 origin, float radius)
        {
            if (map == null) return;
            float radiusSq = radius * radius;

            var fireDef = AcDefs.Fire;
            if (fireDef != null && map.listerThings != null)
            {
                var burning = map.listerThings.ThingsOfDef(fireDef);
                var home = map.areaManager != null ? map.areaManager.Home : null;

                for (int i = 0; i < burning.Count; i++)
                {
                    var fire = burning[i];
                    if (fire == null || !fire.Spawned) continue;

                    float distSq = (fire.Position - origin).LengthHorizontalSquared;
                    float dist = Mathf_Sqrt(distSq);
                    if (nearestFireDistance < 0f || dist < nearestFireDistance) nearestFireDistance = dist;

                    // Anything inside the home area counts however far out the area reaches.
                    bool inHome = home != null && home[fire.Position];
                    if (inHome || distSq <= radiusSq) firesNearBase++;
                }
            }

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || !p.HostileTo(Faction.OfPlayer)) continue;
                if ((p.Position - origin).LengthHorizontalSquared <= radiusSq) hostilesNearBase++;
            }
        }

        static float Mathf_Sqrt(float v)
        {
            return (float)System.Math.Sqrt(v);
        }

        /// <summary>Colonists able to take orders: alive, not downed, not in a mental break.</summary>
        public List<Pawn> ableColonists = new List<Pawn>();
        /// <summary>Every free colonist on the map, including downed ones.</summary>
        public List<Pawn> allColonists = new List<Pawn>();

        public static ColonyState Capture(Map map)
        {
            var s = new ColonyState();
            s.map = map;
            if (map == null) return s;

            try
            {
                s.tick = Find.TickManager.TicksGame;
                s.day = s.tick / GenDate.TicksPerDay;

                CapturePawns(s, map);
                CaptureResources(s, map);
                CaptureBuildings(s, map);
                CaptureResearch(s);

                if (map.dangerWatcher != null) s.danger = map.dangerWatcher.DangerRating;
            }
            catch (Exception e)
            {
                AcLog.WarningOnce("captureFail", "Colony state capture failed: " + e);
            }
            return s;
        }

        static void CapturePawns(ColonyState s, Map map)
        {
            var pawns = map.mapPawns.FreeColonists;
            float moodSum = 0f, moodCount = 0f, healthSum = 0f;

            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead) continue;

                s.allColonists.Add(p);
                s.colonists++;

                if (p.Downed) s.colonistsDowned++;

                bool broken = p.mindState != null && p.mindState.mentalStateHandler != null &&
                              p.mindState.mentalStateHandler.InMentalState;
                if (broken) s.colonistsInMentalState++;

                if (!p.Downed && !broken && p.Spawned) s.ableColonists.Add(p);

                if (p.needs != null && p.needs.mood != null)
                {
                    float mood = p.needs.mood.CurLevel;
                    moodSum += mood;
                    moodCount++;
                    if (mood < s.minMood) s.minMood = mood;
                }

                if (p.health != null && p.health.summaryHealth != null)
                    healthSum += p.health.summaryHealth.SummaryHealthPercent;
                else
                    healthSum += 1f;
            }

            s.avgMood = moodCount > 0f ? moodSum / moodCount : 0.5f;
            s.avgHealth = s.colonists > 0 ? healthSum / s.colonists : 1f;
            if (moodCount == 0f) s.minMood = 0.5f;

            s.prisoners = map.mapPawns.PrisonersOfColonyCount;

            var hostiles = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < hostiles.Count; i++)
            {
                var p = hostiles[i];
                if (p != null && !p.Dead && p.HostileTo(Faction.OfPlayer)) s.hostilePawns++;
            }
        }

        static void CaptureResources(ColonyState s, Map map)
        {
            var rc = map.resourceCounter;
            if (rc == null) return;

            s.foodNutrition = rc.TotalHumanEdibleNutrition;
            s.daysOfFood = s.colonists > 0
                ? s.foodNutrition / (s.colonists * NutritionPerColonistDay)
                : s.foodNutrition;

            s.wood = Count(rc, ThingDefOf.WoodLog);
            s.steel = Count(rc, ThingDefOf.Steel);
            s.components = Count(rc, ThingDefOf.ComponentIndustrial);
            s.textiles = Count(rc, AcDefs.Cloth);
            s.silver = rc.Silver;

            s.medicineCount = Count(rc, ThingDefOf.MedicineHerbal)
                            + Count(rc, ThingDefOf.MedicineIndustrial)
                            + Count(rc, ThingDefOf.MedicineUltratech);
        }

        static int Count(ResourceCounter rc, ThingDef def)
        {
            return def == null ? 0 : rc.GetCount(def);
        }

        static void CaptureBuildings(ColonyState s, Map map)
        {
            if (map.wealthWatcher != null)
            {
                s.wealthTotal = map.wealthWatcher.WealthTotal;
                s.wealthBuildings = map.wealthWatcher.WealthBuildings;
            }

            var lister = map.listerBuildings;
            if (lister != null)
            {
                foreach (var bed in lister.AllBuildingsColonistOfClass<Building_Bed>())
                {
                    if (bed != null && bed.ForColonists && !bed.Medical) s.colonistBeds++;
                }
                foreach (var t in lister.AllBuildingsColonistOfClass<Building_Turret>())
                {
                    if (t != null) s.turrets++;
                }
                foreach (var wt in lister.AllBuildingsColonistOfClass<Building_WorkTable>())
                {
                    if (wt != null) s.workTables++;
                }
                s.hasResearchBench = false;
                foreach (var rb in lister.AllBuildingsColonistOfClass<Building_ResearchBench>())
                {
                    if (rb != null) { s.hasResearchBench = true; break; }
                }
            }

            var things = map.listerThings;
            if (things != null)
            {
                s.pendingBlueprints = things.ThingsInGroup(ThingRequestGroup.Blueprint).Count;
                s.pendingFrames = things.ThingsInGroup(ThingRequestGroup.BuildingFrame).Count;
                var fireDef = AcDefs.Fire;
                if (fireDef != null) s.fires = things.ThingsOfDef(fireDef).Count;

                var haulable = things.ThingsInGroup(ThingRequestGroup.HaulableEver);
                var roofs = map.roofGrid;
                for (int i = 0; i < haulable.Count; i++)
                {
                    var thing = haulable[i];
                    if (thing == null || !thing.Spawned) continue;
                    if (thing.def.category != ThingCategory.Item) continue;
                    if (roofs != null && roofs.Roofed(thing.Position)) continue;
                    s.itemsOutdoors++;
                }
            }
        }

        static void CaptureResearch(ColonyState s)
        {
            var all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
                if (all[i].IsFinished) s.researchFinished++;
        }

        /// <summary>
        /// Projects this snapshot down to the plain numbers the scoring layer works on.
        /// The global counters come from the game's own stats record, which already tracks
        /// deaths and raids for the whole run.
        /// </summary>
        public ColonyMetrics ToMetrics()
        {
            var m = new ColonyMetrics();
            m.day = day;
            m.colonists = colonists;
            m.colonistsDowned = colonistsDowned;
            m.colonistsInMentalState = colonistsInMentalState;
            m.avgMood = avgMood;
            m.avgHealth = avgHealth;
            m.daysOfFood = daysOfFood;
            m.wealthTotal = wealthTotal;
            m.colonistBeds = colonistBeds;
            m.turrets = turrets;
            m.fires = fires;
            m.researchFinished = researchFinished;

            var stats = Find.StoryWatcher != null ? Find.StoryWatcher.statsRecord : null;
            if (stats != null)
            {
                m.cumulativeDeaths = stats.colonistsKilled;
                m.cumulativeRaids = stats.numRaidsEnemy;
            }
            return m;
        }

        /// <summary>Fraction of a stock target currently held, clamped to [0,2].</summary>
        public float StockRatio(int have, float target)
        {
            if (target <= 0f) return 1f;
            float r = have / target;
            return r > 2f ? 2f : r;
        }
    }
}
