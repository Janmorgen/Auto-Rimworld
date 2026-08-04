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

        /// <summary>
        /// Colonists carrying something that wants tending and has not been tended.
        ///
        /// SummaryHealthPercent counts damage to body parts and ignores hediffs entirely, so
        /// infection, hypothermia, heatstroke and malnutrition all read health 1.00 right up to
        /// the death they cause. Every colony this project has lost to Infection (extreme) was
        /// reported in perfect health while it happened.
        ///
        /// This is the game's own HasHediffsNeedingTendByPlayer — the condition behind its
        /// "needs tending" alert — so it agrees with what a watching player would see, and it
        /// is specifically the harm a director *can* do something about by assigning a doctor.
        /// </summary>
        public int colonistsUntended;

        /// <summary>
        /// Colonists who are actually going hungry — <c>Need_Food.Starving</c>, the game's own
        /// definition, which is the point at which malnutrition starts accruing.
        ///
        /// Nothing measured this before, which is why the score could never see the way most of
        /// these colonies end. Run 56 finished with Food security 1.00, 8.8 days in the larder,
        /// and its last colonist on the floor at NeedFood 44.0; run 59 the same with 9.4 days.
        /// "Is there food" and "is anybody eating" only diverge when the colony is dying, and
        /// only the first was ever asked.
        /// </summary>
        public int colonistsStarving;

        /// <summary>
        /// The hungriest colonist's food need, 0 to 1. The average would hide exactly the person
        /// in trouble, for the same reason it does with mood — see <see cref="minMood"/>.
        /// </summary>
        public float minFood = 1f;

        /// <summary>
        /// Couples among the colonists, counted once per pair.
        ///
        /// Two people who share a bed and are not given one carry
        /// <c>WantToSleepWithSpouseOrLover</c> at −4 each, every night, for ever — seen on two
        /// colonists at once in run 57. It is one of the few mood costs the planner can remove
        /// outright rather than offset, and it costs no labour to remove: a double bed is 85
        /// stuff against 90 for the two singles it replaces.
        /// </summary>
        public int couples;

        /// <summary>
        /// Couples where both partners are colonists here, counted once per pair.
        ///
        /// Both halves have to be on this map and in this colony — a colonist whose spouse is a
        /// prisoner, a visitor or dead does not want a double bed, they want something the
        /// planner cannot build.
        /// </summary>
        static int CountCouples(Map map)
        {
            var paired = new HashSet<Pawn>();
            int couples = 0;
            try
            {
                foreach (var p in map.mapPawns.FreeColonists)
                {
                    if (p == null || p.relations == null || paired.Contains(p)) continue;

                    var partner = LovePartnerRelationUtility.ExistingLovePartner(p, false);
                    if (partner == null || partner.Dead || paired.Contains(partner)) continue;
                    if (partner.Faction != Faction.OfPlayer) continue;
                    if (partner.Map != map || partner.IsPrisoner) continue;

                    paired.Add(p);
                    paired.Add(partner);
                    couples++;
                }
            }
            catch (Exception) { return 0; }
            return couples;
        }

        // --- food and medicine ---
        /// <summary>Human-edible nutrition the colony can reach — stockpiled or lying about.</summary>
        public float foodNutrition;

        /// <summary>
        /// Of that, how much has been hauled into storage. Only interesting as a gap against
        /// <see cref="foodNutrition"/>, which is a hauling backlog rather than a shortage.
        /// </summary>
        public float foodStored;
        public float daysOfFood;
        /// <summary>Medicine the colony can reach, stockpiled or loose. What decides treatment.</summary>
        public int medicineCount;

        /// <summary>
        /// Medicine actually in storage. Only interesting against <see cref="medicineCount"/>:
        /// a gap between them is a hauling backlog, not a shortage.
        /// </summary>
        public int medicineStored;

        // --- raw materials ---
        public int wood;
        public int steel;
        public int components;
        public int textiles;
        public int silver;

        /// <summary>
        /// Everything the colony could actually put into a wall — wood, steel and cut stone,
        /// loose stacks included.
        ///
        /// Deliberately not read off <c>ResourceCounter</c> like the fields above, which see
        /// only what is in a stockpile. Whether the colony can afford another room is a question
        /// about material on the map, not material that has been tidied away yet.
        /// </summary>
        public int usableMaterial;

        // --- economy and infrastructure ---
        public float wealthTotal;
        public float wealthBuildings;
        public int colonistBeds;
        /// <summary>Turrets that exist, whether or not they can actually shoot.</summary>
        public int turrets;

        /// <summary>
        /// Turrets with power. The distinction matters: an unpowered turret is a wall
        /// decoration that the defence model was previously counting as a working gun, so a
        /// colony could look defended while owning nothing that fires.
        /// </summary>
        public int poweredTurrets;
        public int workTables;
        public int pendingBlueprints;
        public int pendingFrames;
        public int fires;

        // --- power ---
        // Split the same way turrets are, and for the same reason: a generator that was built
        // is not a generator that is producing. A solar panel under a roof, or a wood-fired
        // generator nobody has fuelled, is a building the colony paid for and gets nothing from.

        /// <summary>Generators that exist, whether or not they are producing anything.</summary>
        public int generators;

        /// <summary>Generators actually putting power onto a grid right now.</summary>
        public int workingGenerators;

        /// <summary>Total watts being generated.</summary>
        public float powerOutput;

        /// <summary>Coolers with power. A freezer whose cooler is dead is just a room.</summary>
        public int workingCoolers;

        /// <summary>Buildings that want power and are connected to no grid at all.</summary>
        public int unpoweredBuildings;

        /// <summary>
        /// Electrical buildings standing under open sky, conduits included.
        ///
        /// Rain shorts these out — `ShortCircuitUtility.TryShortCircuitInRain` — which starts a
        /// fire, and an explosion if the net has charged batteries. It matters because the
        /// director lays its own long conduit runs across open ground, so this is a hazard it
        /// creates rather than one it finds.
        ///
        /// This total drives the fire model, where a conduit shorting is exactly as dangerous as
        /// a generator shorting. The split below drives what to *do* about it, where they are
        /// nothing alike.
        /// </summary>
        public int unroofedPowered;

        /// <summary>
        /// Exposed conduit. Almost always the bulk of the count, because one run across open
        /// ground is a dozen of them — and not separately fixable: pulling one out breaks the
        /// grid, and open ground away from a wall cannot hold a roof. A routing problem.
        /// </summary>
        public int unroofedConduits;

        /// <summary>
        /// The exposed things worth acting on — generators, stoves, coolers — named rather than
        /// counted.
        ///
        /// A total said the colony had fourteen electrical buildings in the rain but not which,
        /// so no module could have moved one however much it wanted to. Anything meant to fix a
        /// building has to be handed the building.
        /// </summary>
        public readonly List<Thing> exposedPoweredDevices = new List<Thing>();

        /// <summary>
        /// Haulable items sitting under open sky. They deteriorate where they are, and in a
        /// dry climate they are also the easiest thing on the map to lose to a fire.
        /// </summary>
        public int itemsOutdoors;

        /// <summary>
        /// Cells the colony has under cultivation, and how many distinct crops are growing in
        /// them.
        ///
        /// A field is the only food supply that does not have to be fought for. Hunting answers
        /// hunger faster but spends the colonists themselves to do it, and most of the combat
        /// deaths in this project's test runs trace back to a colony reaching for meat because
        /// nothing was planted. The variety matters separately: blight takes a whole crop at
        /// once, so a colony living off a single field is one event from an empty larder.
        /// </summary>
        public int growingCells;
        public int distinctCrops;

        /// <summary>
        /// Map-wide conditions in force. Nothing in the director could see these at all, which
        /// meant toxic fallout — a condition whose whole nature is that being outdoors kills
        /// you — changed the colony's behaviour not one bit.
        /// </summary>
        public Conditions.ActiveConditions conditions;

        /// <summary>
        /// Outdoor temperature in Celsius, and how far it sits outside what an ordinary colonist
        /// can bear.
        ///
        /// Humans are comfortable between roughly 16 and 26 degrees, and ten degrees past either
        /// edge begins hypothermia or heatstroke, both of which are fatal at full severity. So
        /// this is not a comfort reading: it decides whether clothing is a nicety or the thing
        /// keeping people alive, and which direction the answer runs in.
        /// </summary>
        public float outdoorTemperature;

        /// <summary>
        /// The season, and whether anything will grow outdoors right now.
        ///
        /// The director knew the temperature this instant and the day number, and nothing about
        /// the year — so it sowed in late autumn, planned food as though the harvest were
        /// perpetual, and was surprised by every winter. A season is the difference between "it
        /// is cold today" and "it will be cold for the next fifteen days and nothing will grow
        /// in any of them".
        /// </summary>
        public Season season = Season.Undefined;

        /// <summary>Whether crops sown outdoors would actually grow at the moment.</summary>
        public bool growingSeasonNow;

        /// <summary>True in the half of the year that is heading into the cold.</summary>
        public bool winterComing;

        /// <summary>Degrees below the comfortable floor, 0 when it is not cold.</summary>
        public float coldShortfall;

        /// <summary>Degrees above the comfortable ceiling, 0 when it is not hot.</summary>
        public float heatExcess;

        /// <summary>
        /// Colonists whose clothing does not cover the weather they are actually in.
        ///
        /// Measured off each pawn's own comfortable range, which RimWorld already computes from
        /// the apparel they are wearing — so this asks the only question that matters, "is this
        /// person dressed for outside", rather than counting garments in a stockpile or checking
        /// whether a workbench exists.
        /// </summary>
        public int colonistsUnderdressed;

        /// <summary>Worst gap in degrees between what a colonist can bear and what it is outside.</summary>
        public float worstClothingGap;

        // --- research ---
        public int researchFinished;
        public bool hasResearchBench;

        /// <summary>
        /// Whether anybody in the colony is actually able to do research.
        ///
        /// A bench is not research. Intellectual work can be disabled outright by a backstory or
        /// a trait, and a colony where every colonist is incapable of it will build the bench,
        /// mark the goal satisfied, and never finish a project — which is the same shape as the
        /// unpowered turret and the kitchen with no stove, and this codebase has now been caught
        /// by it five times.
        /// </summary>
        public bool canResearch;

        // --- threat ---
        public StoryDanger danger = StoryDanger.None;
        public int hostilePawns;

        /// <summary>
        /// Raids the colony has seen. Used as the signal that prisoners are a live prospect: a
        /// prison has to be standing *before* anyone can be captured, so waiting until the colony
        /// holds prisoners to build one is a deadlock with no way in.
        /// </summary>
        public int raidsSurvived;

        /// <summary>
        /// Downed outsiders lying on the map right now, hostile or otherwise. A hostile one can
        /// only be captured, which needs a prison bed built in advance; anyone else can simply be
        /// rescued into an ordinary bed, which is cheaper and usually ends better.
        /// </summary>
        public int downedStrangers;

        /// <summary>
        /// Colony dead still lying about unburied, counting both loose corpses and any held in
        /// a container that is not a grave.
        ///
        /// An unburied colonist is the largest single mood penalty in the game, and it is the
        /// only reason to build a tomb — so a tomb waits on this rather than being planned on
        /// the chance of somebody dying.
        /// </summary>
        public int unburiedCorpses;

        /// <summary>
        /// Tamed animals belonging to the colony. Nothing here tames anything, so this counts
        /// the ones that arrived some other way — bought, bonded, or self-tamed — and is what a
        /// barn waits on.
        /// </summary>
        public int tamedAnimals;

        public bool Valid { get { return map != null && colonists > 0; } }

        // --- proximity, filled in by the director once the base location is known ---

        /// <summary>Fires close enough to the colony to matter.</summary>
        public int firesNearBase;

        /// <summary>Distance from the base to the closest fire, or -1 if none burning.</summary>
        public float nearestFireDistance = -1f;

        /// <summary>
        /// Empty colonist beds far enough from every fire to be worth carrying somebody to.
        ///
        /// A bed is the only way a downed colonist gets off the floor, and counting beds without
        /// asking where the fire is answers the wrong question: a colony can hold four beds, none
        /// occupied, and still have nowhere safe to put anybody. That reads as "no bed needed"
        /// to everything that looks at bed counts.
        /// </summary>
        public int freeBedsAwayFromFire;

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
        /// <summary>
        /// Counts the beds somebody could actually be carried to right now.
        ///
        /// Empty and out of the fire's reach, both of which have to hold: an occupied bed is not
        /// somewhere to put a second person, and a free one standing where the fire is going is
        /// not a rescue, it is a slower way to the same end.
        /// </summary>
        void CountBedsOutOfTheFire()
        {
            freeBedsAwayFromFire = 0;
            if (map.listerBuildings == null) return;

            var fireDef = AcDefs.Fire;
            var burning = fireDef != null && map.listerThings != null
                ? map.listerThings.ThingsOfDef(fireDef)
                : null;

            foreach (var bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == null || !bed.Spawned || !bed.ForColonists || bed.Medical) continue;
                if (Occupied(bed)) continue;
                if (NearAFire(burning, bed.Position)) continue;

                freeBedsAwayFromFire++;
            }
        }

        static bool Occupied(Building_Bed bed)
        {
            try
            {
                foreach (var sleeper in bed.CurOccupants)
                {
                    if (sleeper != null) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        static bool NearAFire(System.Collections.Generic.List<Thing> burning, IntVec3 cell)
        {
            if (burning == null) return false;

            for (int i = 0; i < burning.Count; i++)
            {
                var fire = burning[i];
                if (fire == null || !fire.Spawned) continue;

                float dist = AcMath.Sqrt((fire.Position - cell).LengthHorizontalSquared);
                if (FireFront.Threatens(dist)) return true;
            }
            return false;
        }

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
                    float dist = AcMath.Sqrt(distSq);
                    if (nearestFireDistance < 0f || dist < nearestFireDistance) nearestFireDistance = dist;

                    // Anything inside the home area counts however far out the area reaches.
                    bool inHome = home != null && home[fire.Position];
                    if (inHome || distSq <= radiusSq) firesNearBase++;
                }
            }

            CountBedsOutOfTheFire();

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || !p.HostileTo(Faction.OfPlayer)) continue;

                // Someone lying on the ground is not attacking anybody. Counting them kept the
                // colony permanently "under attack": it drafted against a downed raider hour
                // after hour, and because that reads as an emergency it also blocked the one
                // thing worth doing with them, which is picking them up.
                if (p.Downed) continue;

                if ((p.Position - origin).LengthHorizontalSquared <= radiusSq) hostilesNearBase++;
            }
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

                var stats = Find.StoryWatcher != null ? Find.StoryWatcher.statsRecord : null;
                if (stats != null) s.raidsSurvived = stats.numRaidsEnemy;
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

                // Somebody has to be able to use the bench once it exists.
                //
                // Asked of everyone rather than only the able, because this is a fact about who
                // the colony has rather than who is on their feet this minute — a researcher
                // asleep or in bed is still the reason to build a bench.
                if (!s.canResearch)
                {
                    try
                    {
                        if (!p.WorkTypeIsDisabled(WorkTypeDefOf.Research)) s.canResearch = true;
                    }
                    catch (Exception) { }
                }

                if (p.health != null)
                {
                    try
                    {
                        if (p.health.HasHediffsNeedingTendByPlayer(false)) s.colonistsUntended++;
                    }
                    catch (Exception) { }
                }

                if (p.needs != null && p.needs.food != null)
                {
                    if (p.needs.food.Starving) s.colonistsStarving++;
                    float level = p.needs.food.CurLevelPercentage;
                    if (level < s.minFood) s.minFood = level;
                }

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

            s.couples = CountCouples(map);
            s.avgMood = moodCount > 0f ? moodSum / moodCount : 0.5f;
            s.avgHealth = s.colonists > 0 ? healthSum / s.colonists : 1f;
            if (moodCount == 0f) s.minMood = 0.5f;
            if (s.colonists == 0) s.minFood = 1f;

            s.prisoners = map.mapPawns.PrisonersOfColonyCount;

            var hostiles = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < hostiles.Count; i++)
            {
                var p = hostiles[i];
                if (p == null || p.Dead) continue;
                // Downed hostiles are excluded here too: a raider on the ground is a decision
                // to be made about them, not a fight still in progress.
                if (p.HostileTo(Faction.OfPlayer) && !p.Downed) s.hostilePawns++;

                if (p.Downed && p.RaceProps.Humanlike && p.Faction != Faction.OfPlayer)
                    s.downedStrangers++;

                if (!p.RaceProps.Humanlike && p.Faction == Faction.OfPlayer) s.tamedAnimals++;
            }

            CountUnburiedDead(s, map);
        }

        /// <summary>
        /// Colony dead lying on the map with nowhere to be.
        ///
        /// A corpse inside a grave is buried and costs nothing; one inside anything else — a
        /// casket, a freezer shelf, the floor — still reads as unburied to the thought that
        /// charges for it. Only the colony's own dead count: a dead raider is not a funeral.
        /// </summary>
        static void CountUnburiedDead(ColonyState s, Map map)
        {
            var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            for (int i = 0; i < corpses.Count; i++)
            {
                var corpse = corpses[i] as Corpse;
                if (corpse == null || corpse.InnerPawn == null) continue;
                if (corpse.InnerPawn.Faction != Faction.OfPlayer) continue;
                if (!corpse.InnerPawn.RaceProps.Humanlike) continue;

                var grave = corpse.StoringThing() as Building_Grave;
                if (grave == null) s.unburiedCorpses++;
            }
        }

        static void CaptureResources(ColonyState s, Map map)
        {
            var rc = map.resourceCounter;
            if (rc == null) return;

            // Reachable food, not stockpiled food.
            //
            // ResourceCounter.TotalHumanEdibleNutrition only sees what has been hauled into
            // storage, and a colony's starting supplies lie on the ground until somebody moves
            // them. So every run opened on "food 0.0d" for its first half-day — run 82 for
            // seven readings, run 84 for four — and then jumped straight to 0.9 and 1.9 days.
            // A colony does not acquire two days of food from nowhere on the morning of day
            // zero. That was hauling catching up, and the number had been wrong until it did.
            //
            // It is wrong in the direction that does the most damage. daysOfFood drives the
            // Immediate "Feed the colony" goal, which halts discretionary building, and the
            // starvation work weights, which throw everybody at hunting and cooking. Every
            // colony therefore began its most formative hours in a food emergency it was not
            // in, with the food lying in front of it.
            //
            // Colonists eat any reachable unforbidden food, stockpiled or not, so reachable is
            // the honest measure of what the colony has to eat.
            s.foodNutrition = ReachableHumanEdibleNutrition(map);
            s.foodStored = rc.TotalHumanEdibleNutrition;
            s.daysOfFood = s.colonists > 0
                ? s.foodNutrition / (s.colonists * NutritionPerColonistDay)
                : s.foodNutrition;

            // Map-wide, like the rest. These decide whether to go and get more — ResourceModule
            // stops chopping at "wood >= target" and starts mining at "steel < target" — and a
            // colony that has felled a forest but not tidied it away reads zero and fells
            // another one. Every count here is spent in the currency these colonies die short
            // of, which is hands, and a builder fetches material from wherever it is lying.
            s.wood = CountOnMap(map, ThingDefOf.WoodLog);
            s.steel = CountOnMap(map, ThingDefOf.Steel);
            s.components = CountOnMap(map, ThingDefOf.ComponentIndustrial);
            // Everything a coat can be made of, not just cloth.
            //
            // A parka's stuffCategories are Fabric and Leathery. This counted Cloth alone, so a
            // colony that hunts — which is every colony here — read zero textiles while sitting
            // on the hides of everything it had killed, and the clothing goal reported "0 cloth
            // to sew with" as two colonists froze to death at -31C.
            //
            // Asked of the stuff categories rather than a list of items, so leathers, wools and
            // anything a mod adds all count without being named.
            s.textiles = ClothingStuffOnMap(map);
            s.silver = rc.Silver;

            // Map-wide, not stockpile-only.
            //
            // ResourceCounter sees what has been tidied away, and a colony's starting medicine
            // sits in a drop pod or on the ground until somebody hauls it. Counted off the
            // counter, a fully-supplied colony reads "med 0" — which is what run 84 reported on
            // day 0 with medicine plainly on the map. A doctor will fetch medicine from
            // anywhere reachable, so the stockpile is irrelevant to whether a wound gets
            // treated, and it is the treatable question this number is for.
            //
            // The same trap usableMaterial was written to avoid, in a different costume.
            s.medicineStored = Count(rc, ThingDefOf.MedicineHerbal)
                             + Count(rc, ThingDefOf.MedicineIndustrial)
                             + Count(rc, ThingDefOf.MedicineUltratech);

            s.medicineCount = CountOnMap(map, ThingDefOf.MedicineHerbal)
                            + CountOnMap(map, ThingDefOf.MedicineIndustrial)
                            + CountOnMap(map, ThingDefOf.MedicineUltratech);

            s.usableMaterial = PlacementUtil.AvailableCount(map, ThingDefOf.WoodLog)
                             + PlacementUtil.AvailableCount(map, ThingDefOf.Steel);
            for (int i = 0; i < AcDefs.StoneBlockStuff.Length; i++)
                s.usableMaterial += PlacementUtil.AvailableCount(map, AcDefs.Thing(AcDefs.StoneBlockStuff[i]));
        }

        static int Count(ResourceCounter rc, ThingDef def)
        {
            return def == null ? 0 : rc.GetCount(def);
        }

        /// <summary>
        /// Everything of this def the colony could actually reach — stockpiled and loose alike,
        /// forbidden items excluded. Deliberately not <c>ResourceCounter</c>, which only sees
        /// what has been hauled into storage.
        /// </summary>
        /// <summary>
        /// Human-edible nutrition lying anywhere the colony can get at it. Mirrors what
        /// ResourceCounter reports, minus its restriction to storage.
        /// </summary>
        static float ReachableHumanEdibleNutrition(Map map)
        {
            if (map == null || map.listerThings == null) return 0f;

            float total = 0f;
            var things = map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
            if (things == null) return 0f;

            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing == null || !thing.Spawned) continue;
                if (thing.IsForbidden(Faction.OfPlayer)) continue;

                // Not corpses, and not things still walking around.
                //
                // FoodSourceNotPlantOrTree includes both, and a corpse carries an enormous
                // amount of nutrition — a thrumbo's more than a colony eats in a month. Run 102
                // killed a thrumbo, read "food 50.7d", and starved two colonists to death while
                // the game's own alert said "Low food" on the same screen. A corpse is not food
                // until somebody butchers it, and butchering is a separate job the director
                // already orders; the meat that comes out is counted then, once.
                //
                // This is a fault I introduced tonight. Counting map-wide instead of
                // stockpile-only was right — a colony eats food it has not tidied away — but
                // the group I walked to do it contains things that are food only in the sense
                // that something could eventually eat them.
                if (thing is Corpse || thing is Pawn) continue;

                var def = thing.def;
                if (def == null || def.ingestible == null) continue;
                if (!def.IsNutritionGivingIngestible || !def.ingestible.HumanEdible) continue;

                total += def.ingestible.CachedNutrition * thing.stackCount;
            }
            return total;
        }

        static List<ThingDef> clothingStuffCache;

        /// <summary>
        /// Everything on the map that a coat could be made from — fabric, leather, wool.
        ///
        /// The categories come from the stuff system rather than a list of defNames, so this
        /// holds for modded materials, and it is the same question the tailor bench asks when
        /// it decides whether a bill can run.
        /// </summary>
        static int ClothingStuffOnMap(Map map)
        {
            if (clothingStuffCache == null)
            {
                clothingStuffCache = new List<ThingDef>();
                var all = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    var def = all[i];
                    if (!def.IsStuff || def.stuffProps == null || def.stuffProps.categories == null) continue;

                    var categories = def.stuffProps.categories;
                    for (int c = 0; c < categories.Count; c++)
                    {
                        // Fabric covers cloth and the wools; Leathery covers every hide. Those
                        // are exactly the two a parka accepts.
                        if (categories[c] == StuffCategoryDefOf.Fabric ||
                            categories[c] == StuffCategoryDefOf.Leathery)
                        { clothingStuffCache.Add(def); break; }
                    }
                }
            }

            int total = 0;
            for (int i = 0; i < clothingStuffCache.Count; i++)
                total += CountOnMap(map, clothingStuffCache[i]);
            return total;
        }

        static int CountOnMap(Map map, ThingDef def)
        {
            if (map == null || def == null) return 0;

            int total = 0;
            var things = map.listerThings != null ? map.listerThings.ThingsOfDef(def) : null;
            if (things == null) return 0;

            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing == null || !thing.Spawned) continue;
                if (thing.IsForbidden(Faction.OfPlayer)) continue;
                total += thing.stackCount;
            }
            return total;
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
                    // Sleeping slots, not beds. A double bed is one building and sleeps two, so
                    // counting buildings would have the colony believe it is a bed short for
                    // every couple it houses — and go on building beds nobody needs.
                    if (bed != null && bed.ForColonists && !bed.Medical)
                        s.colonistBeds += bed.TotalSleepingSlots;
                }
                foreach (var t in lister.AllBuildingsColonistOfClass<Building_Turret>())
                {
                    if (t == null) continue;
                    s.turrets++;

                    // No power component at all means it needs none (a trap or mortar).
                    var power = t.TryGetComp<CompPowerTrader>();
                    if (power == null || power.PowerOn) s.poweredTurrets++;
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

                CapturePower(s, lister);
            }

            CaptureFields(s, map);
            CaptureConditions(s, map);
            CaptureTemperature(s, map);

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

        /// <summary>
        /// Splits the colony's electrical buildings into what generates, what is producing, and
        /// what wants power but is on no grid.
        ///
        /// The distinction between the first two is the whole point. <c>PowerOn</c> alone is not
        /// enough for a generator: a roofed solar panel reports on and produces nothing, which is
        /// exactly the failure this was written to make visible.
        /// </summary>
        /// <summary>
        /// What the colony has planted, counted in cells rather than zones — one large field and
        /// three small ones feed the same number of people, and the goal layer cares about the
        /// area under cultivation.
        /// </summary>
        static void CaptureFields(ColonyState s, Map map)
        {
            var zones = map.zoneManager != null ? map.zoneManager.AllZones : null;
            if (zones == null) return;

            var crops = new HashSet<string>();
            for (int i = 0; i < zones.Count; i++)
            {
                var grow = zones[i] as Zone_Growing;
                if (grow == null) continue;

                // Food zones only.
                //
                // These two numbers answer "can the colony feed itself" and "is it one blight
                // from an empty larder", and both counted every growing zone on the map. That
                // was harmless while the only zones were food and one healroot plot. The moment
                // cotton, haygrass and hops got plots of their own, a colony with a single food
                // crop read "6 of 2 crops" and the variety rule stood down satisfied — insurance
                // against blight, provided by three fields nobody can eat.
                //
                // Which is the psychoid mistake again, one level up: the count was of growing
                // zones and the question was about dinner.
                var growing = grow.GetPlantDefToGrow();
                if (growing == null) continue;
                if (Plants.PlantTaxonomy.RoleOf(growing) != Plants.PlantRole.Food) continue;

                // Only the cells that can actually grow something.
                //
                // A roofed cell with no sun lamp over it grows nothing however long it is
                // tended, and counting it told the food goals the fields were bigger than they
                // were — "169 of 180 growing cells" reads as solved whether or not half of it
                // is in the dark under a room somebody built on top of it later.
                foreach (var cell in grow.Cells)
                {
                    if (!cell.InBounds(map)) continue;
                    if (map.roofGrid != null && map.roofGrid.Roofed(cell) &&
                        (map.glowGrid == null || map.glowGrid.GroundGlowAt(cell) < 0.51f)) continue;
                    s.growingCells++;
                }

                crops.Add(growing.defName);
            }
            s.distinctCrops = crops.Count;
        }

        /// <summary>What an ordinary colonist can bear, in Celsius, before they start taking harm.</summary>
        public const float ComfortableMin = 16f;
        public const float ComfortableMax = 26f;

        static void CaptureTemperature(ColonyState s, Map map)
        {
            try
            {
                s.outdoorTemperature = map.mapTemperature.OutdoorTemp;

                // Where the year is, not just where today is.
                s.season = GenLocalDate.Season(map);
                // Asked of the outdoor temperature, not of a cell.
                //
                // The first version asked PlantUtility.GrowthSeasonNow about map.Center, which
                // is a cell like any other — under a mountain roof on this map — and it
                // answered for that cell rather than for the fields. The report said "Spring,
                // nothing grows outdoors, 17C", which is how the mistake showed up an hour
                // after being written.
                //
                // Plants grow between freezing and 58C and stop outside it, so the outdoor
                // temperature is the whole answer for an outdoor field and does not depend on
                // which cell happens to be the middle of the map.
                s.growingSeasonNow = s.outdoorTemperature > 0f && s.outdoorTemperature < 58f;
                s.winterComing = s.season == Season.Fall || s.season == Season.Winter
                                 || s.season == Season.PermanentWinter;

                s.coldShortfall = ComfortableMin - s.outdoorTemperature;
                if (s.coldShortfall < 0f) s.coldShortfall = 0f;
                s.heatExcess = s.outdoorTemperature - ComfortableMax;
                if (s.heatExcess < 0f) s.heatExcess = 0f;

                // Against each colonist's own tolerance, which already includes what they are
                // wearing. A colony in parkas is not underdressed at -20; a colony in shirts is.
                for (int i = 0; i < s.allColonists.Count; i++)
                {
                    var pawn = s.allColonists[i];
                    if (pawn == null) continue;

                    float bearableMin = pawn.GetStatValue(StatDefOf.ComfyTemperatureMin);
                    float bearableMax = pawn.GetStatValue(StatDefOf.ComfyTemperatureMax);

                    float gap = 0f;
                    if (s.outdoorTemperature < bearableMin) gap = bearableMin - s.outdoorTemperature;
                    else if (s.outdoorTemperature > bearableMax) gap = s.outdoorTemperature - bearableMax;

                    if (gap <= 0f) continue;
                    s.colonistsUnderdressed++;
                    if (gap > s.worstClothingGap) s.worstClothingGap = gap;
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Reads the map-wide conditions in force. Looked up by defName rather than through
        /// <c>GameConditionDefOf</c> so a missing or renamed condition degrades to "not active"
        /// instead of throwing on a version the mod was not built against.
        /// </summary>
        static void CaptureConditions(ColonyState s, Map map)
        {
            var manager = map.gameConditionManager;
            if (manager == null) return;

            s.conditions.toxicFallout = IsActive(manager, map, "ToxicFallout");
            s.conditions.solarFlare = IsActive(manager, map, "SolarFlare");
            s.conditions.eclipse = IsActive(manager, map, "Eclipse");
            s.conditions.coldSnap = IsActive(manager, map, "ColdSnap");
            s.conditions.heatWave = IsActive(manager, map, "HeatWave");
            s.conditions.volcanicWinter = IsActive(manager, map, "VolcanicWinter");
            s.conditions.flashstorm = IsActive(manager, map, "Flashstorm");
        }

        static bool IsActive(GameConditionManager manager, Map map, string defName)
        {
            try
            {
                var def = DefDatabase<GameConditionDef>.GetNamedSilentFail(defName);
                return def != null && manager.ConditionIsActive(def);
            }
            catch (Exception) { return false; }
        }

        static void CapturePower(ColonyState s, ListerBuildings lister)
        {
            var coolerDef = AcDefs.Cooler;
            var buildings = lister.allBuildingsColonist;

            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                if (building == null || !building.Spawned) continue;

                // Anything electrical, conduits included — CompPowerTrader and the conduits'
                // transmitter both derive from CompPower.
                if (building.TryGetComp<CompPower>() != null)
                {
                    var roofs = building.Map != null ? building.Map.roofGrid : null;
                    if (roofs != null && !roofs.Roofed(building.Position))
                    {
                        s.unroofedPowered++;

                        // Conduit is a routing problem; everything else is a thing that can be
                        // roofed or moved, so it gets named rather than counted.
                        bool conduit = building.def != null && building.def.building != null &&
                                       building.def.building.isPowerConduit;
                        if (conduit) s.unroofedConduits++;
                        else s.exposedPoweredDevices.Add(building);
                    }
                }

                var trader = building.TryGetComp<CompPowerTrader>();
                if (trader == null) continue;

                // Negative base consumption is how the game marks something as a source.
                bool generates = trader.Props != null && trader.Props.PowerConsumption < 0f;
                if (generates)
                {
                    s.generators++;
                    if (trader.PowerOn && trader.PowerOutput > 0f)
                    {
                        s.workingGenerators++;
                        s.powerOutput += trader.PowerOutput;
                    }
                    continue;
                }

                if (trader.PowerNet == null) s.unpoweredBuildings++;
                if (coolerDef != null && building.def == coolerDef && trader.PowerOn) s.workingCoolers++;
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
            m.minMood = minMood;
            m.minFood = minFood;
            m.colonistsStarving = colonistsStarving;
            m.colonistsUntended = colonistsUntended;
            m.medicineCount = medicineCount;
            m.medicineStored = medicineStored;
            m.usableMaterial = usableMaterial;
            m.avgHealth = avgHealth;
            m.daysOfFood = daysOfFood;
            m.outdoorTemperature = outdoorTemperature;
            m.wealthTotal = wealthTotal;
            m.colonistBeds = colonistBeds;
            m.poweredTurrets = poweredTurrets;
            m.fires = fires;
            m.firesNearBase = firesNearBase;
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
