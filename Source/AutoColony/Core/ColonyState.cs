using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

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
        /// Colonists carrying an untended condition that can actually kill them.
        ///
        /// "Needs tending" covers a grazed knuckle and a plague alike, and treating those the
        /// same cost Lubov. They walked around with an untended infection for days — the
        /// vitals said "1 UNTENDED" the whole time and the colony held twenty medicine — while
        /// Doctor sat at the untended tier of 3.0 and Tailoring sat at 3.2, because it was 39C
        /// and nobody was dressed for it. Sewing outranked treating an infection, and the
        /// infection won.
        ///
        /// A hediff with lethalSeverity above zero is the game stating that this one ends in a
        /// death if it is left alone. That is a different urgency from a scratch, and it is the
        /// distinction the priority ladder was missing.
        /// </summary>
        public int colonistsUntendedLethal;

        /// <summary>
        /// Colonists whose disease is ahead of their immunity — losing the race, whether or not
        /// anybody has tended them.
        ///
        /// This is the number that says a death is coming rather than that somebody is ill.
        /// Tending helps the immunity side and does not guarantee it; an infection that is
        /// winning is answered by removing the part it is in, which is a decision the director
        /// cannot currently take at all.
        /// </summary>
        public int colonistsLosingToDisease;

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
        /// Colonists who cannot reach any food on this map, asked of the game's own pathfinder.
        ///
        /// The director has had a flag called <c>notReachingThem</c> since run 56 and it never
        /// asked this. It inferred being cut off from "somebody is starving while the larder is
        /// full", which is the symptom — true of a walled-in colonist and true of half a dozen
        /// other things, and silent until somebody is already starving.
        ///
        /// It cost a colony on the first biome of the first matrix. The planner sited a Kitchen
        /// whose west wall ran along a rock face, built it, and sealed Solomon into the one-cell
        /// gap between the two. He starved at food 0.00 for a day beside four days of cooked
        /// meals, broke, set fires, and died of heatstroke in them. Doctor sat at 5.0 the whole
        /// time — the proxy fired correctly and named nothing.
        ///
        /// A director that can wall its own colonists in needs to be able to ask whether it has.
        /// </summary>
        public int colonistsCutOff;

        /// <summary>
        /// Colonists with nothing to do, read as the game's own <c>MindState.IsIdle</c> — the
        /// property behind its "N colonists idle" alert.
        ///
        /// Nothing in the director has ever measured this, and it is the most direct statement
        /// the game makes about the work table being wrong. Run 115 stood at three of three idle
        /// with zero days of food and a Low food alert up: the priorities were set, they were
        /// simply set over work that had no jobs in it.
        ///
        /// Idle is not laziness, it is an assignment that has run out of things to cover. A
        /// colonist is given as few as four of nineteen work types, and if all four happen to be
        /// empty — nothing to cook because there is no food, nothing to haul because there is
        /// nothing loose — they stand still while the colony starves.
        /// </summary>
        public int colonistsIdle;

        /// <summary>
        /// Colonists on their feet, sane, and not drafted — the hands that can take a work order.
        ///
        /// Distinct from <see cref="ableColonists"/>, which is who can act at all and therefore
        /// includes the drafted. Any question of the form "have we got the people to do this
        /// work" wants this one.
        /// </summary>
        public int colonistsFreeForWork;

        /// <summary>
        /// Ticks until the first colonist dies of blood loss, or -1 when nobody is on that clock.
        ///
        /// The deadline itself rather than a count of people who have one. Run 162 lost Pansy
        /// with a Medicine 7 doctor reserved to tend them and one leg between the doctor and the
        /// patient; nothing anywhere could compare how long the walk took against how long the
        /// patient had, because this number was computed and discarded.
        /// </summary>
        public int ticksToFirstBloodLoss = -1;

        /// <summary>The colonist that deadline belongs to, so help can be sent to the right one.</summary>
        public Pawn soonestBleedingOut;

        /// <summary>
        /// Colonists the game says will die of blood loss within a day if nobody tends them.
        ///
        /// Read as <c>HealthUtility.TicksUntilDeathDueToBloodLoss</c>, which is the number
        /// RimWorld puts on the health tab — not "is downed" and not "is untended", both of
        /// which are true of people who will be fine.
        ///
        /// The distinction is a deadline. Run 116 lost Chen and Jane to blood loss with medicine
        /// in the cupboard and Doctor correctly raised to 4.0, because a food crisis had Cooking
        /// at 5.9 and Hunting at 5.0 and both outrank it. Jane carried Chen to a bed and then
        /// went to cook. Chen bled out six hours later.
        ///
        /// Both emergencies were real. They are not the same clock: a colony at 0.4 days of food
        /// has a day to find a meal, and a colonist bleeding out has hours, and no amount of
        /// cooking answers the second.
        /// </summary>
        public int colonistsBleedingOut;

        /// <summary>
        /// Colonists losing a disease race whose room is too dirty to operate in.
        ///
        /// Sharper than "somebody is losing", and it is the number that should move the mop.
        /// Leslie died with the surgery held a full day waiting for a clean room that never
        /// came: Cleaning rises to 3.0 while anyone is losing, and 3.0 loses to Cooking at 4.0
        /// and Construction at 3.7, so nobody ever cleaned it.
        ///
        /// A held surgery is a different statement from a sick colonist. It says the colony has
        /// decided what to do, knows how to do it, and is refusing — for a reason it could fix
        /// in ten minutes with a broom.
        /// </summary>
        public int colonistsLosingInADirtyRoom;

        /// <summary>Which ones, so something can go and let them out.</summary>
        public List<Pawn> cutOff;

        /// <summary>Who could not reach food on the previous capture, so transients are filtered.</summary>
        static readonly HashSet<int> unreachableLastPass = new HashSet<int>();
        static readonly HashSet<int> unreachableNow = new HashSet<int>();

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
        /// <summary>
        /// Whether any tendable condition on this colonist is one the game says can kill.
        ///
        /// lethalSeverity is the marker: infections, plague and malaria carry it, a bruise does
        /// not. Asked of the def rather than by name, so a modded disease counts without anyone
        /// listing it.
        /// </summary>
        static bool HasLethalUntended(Pawn pawn)
        {
            try
            {
                var hediffs = pawn.health.hediffSet.hediffs;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    var hediff = hediffs[i];
                    if (hediff == null || hediff.def == null) continue;
                    if (!hediff.def.tendable) continue;
                    if (hediff.def.lethalSeverity <= 0f) continue;
                    if (hediff.TendableNow(false)) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>
        /// Whether a colonist is losing the race against something that kills.
        ///
        /// An infection is not one condition but two outcomes. The disease climbs towards
        /// lethalSeverity while the body builds immunity towards 1, and whichever arrives first
        /// decides. Tending speeds the immunity side; it does not guarantee it, which is why
        /// some infections end in an amputation instead — removing the part removes the disease,
        /// and no amount of medicine substitutes for that once the race is lost.
        ///
        /// So "has an infection" and "is dying of one" are different questions, and only the
        /// second is an emergency. Compared as fractions of their own finish lines, which is
        /// what the game's own health tab shows the player.
        /// </summary>
        static bool LosingToDisease(Pawn pawn)
        {
            try
            {
                var hediffs = pawn.health.hediffSet.hediffs;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    var hediff = hediffs[i];
                    if (hediff == null || hediff.def == null) continue;
                    if (hediff.def.lethalSeverity <= 0f) continue;

                    var immunizable = hediff.TryGetComp<HediffComp_Immunizable>();
                    if (immunizable == null) continue;
                    if (immunizable.FullyImmune) continue;

                    float towardsDeath = hediff.Severity / hediff.def.lethalSeverity;
                    if (towardsDeath > immunizable.Immunity) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

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

        /// <summary>
        /// Nutrition standing in fresh animal corpses nobody has butchered yet, and the days of
        /// food that would become if they did.
        ///
        /// A corpse is not food — colonists will not eat one, and counting it as food is what
        /// let a colony read fifty days while starving beside a dead thrumbo. But it is not
        /// nothing either: it is food two jobs away, hauling and butchering, and the right
        /// answer to an empty larder with a full field is to butcher rather than to hunt again.
        /// A colony here once hunted thirteen gazelles and starved at 0.0 days with the meat
        /// lying where it fell.
        ///
        /// ResourceModule has computed this for its own hunting decision all along. Kept here
        /// so the work priorities and the chronicle can see the same number instead of each
        /// deciding for themselves what a corpse is worth.
        /// </summary>
        public float unbutcheredNutrition;

        /// <summary>Days of food locked in corpses, on the same scale as <see cref="daysOfFood"/>.</summary>
        public float daysOfFoodUnbutchered;

        /// <summary>
        /// Days of food that will rot before it can be eaten.
        ///
        /// A larder is a promise with a clock on it. Food spoils, so an excess the colony cannot
        /// eat in time is not security — it is work already done and about to be thrown away,
        /// and the same is true of every perishable in the chain: a hunted animal that is never
        /// hauled, meat that is never cooked, meals stacked beyond what gets eaten.
        ///
        /// Asked as TicksUntilRotAtCurrentTemp, which is the game's own answer and already
        /// accounts for temperature — so a freezer shows up here as food that is not spoiling,
        /// without this code needing to know what a cooler is.
        /// </summary>
        public float daysOfFoodSpoiling;

        /// <summary>
        /// Days of food that is actually a cooked meal.
        ///
        /// The last step of the food chain, and the one nothing could see. A colonist with no
        /// meal to hand eats raw and takes AteRawFood at -7 — run 107 carried that thought on
        /// all four colonists at once while holding five days of food and running a working
        /// kitchen. Nutrition was never the problem; none of it had been through a stove.
        ///
        /// Asked as IngestibleProperties.IsMeal, which is the game's own distinction between
        /// dinner and an ingredient.
        /// </summary>
        public float daysOfMeals;
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

        /// <summary>
        /// Sleeping slots that are actually inside an enclosed room.
        ///
        /// <see cref="colonistBeds"/> counts beds, which is the right question for a rescue —
        /// a bed under open sky still beats the floor to carry a casualty to. It is the wrong
        /// question for whether anybody is housed, and the two were the same number.
        ///
        /// Run 140, day 7: four beds, 1447 material, means 1.00, no enclosed room anywhere, and
        /// its two survivors carrying SleptOutside and SleptOnGround at -4 apiece into a mood of
        /// 0.02 and an extreme break. The colony believed it had housed everyone and the game
        /// was scoring them as sleeping in a field, because a bed in three walls and a gap is
        /// furniture in the open.
        ///
        /// Third place the same distinction has bitten: Refuge() called a roofed cell cover,
        /// RoomCensus has always asked correctly, and this counted furniture as shelter.
        /// </summary>
        public int shelteredBeds;
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
        /// Buildings whose fuel hopper the game itself says wants filling right now.
        ///
        /// Read as <c>CompRefuelable.ShouldAutoRefuelNow</c> — the same test WorkGiver_Refuel
        /// applies before queueing the job — so this is never a guess about how empty is empty.
        /// A stove, a generator, a campfire and a smithy are all the same fact: the colony owns
        /// a thing that cannot run until somebody carries wood to it, and carrying is Hauling.
        /// </summary>
        public int buildingsWantingFuel;

        /// <summary>Which ones, so the chronicle can name the thing rather than count it.</summary>
        public List<Building> fuelStarved;

        /// <summary>
        /// Units of fuel the dry buildings could actually be filled with, map-wide and
        /// unforbidden. Zero with hoppers standing dry is a supply failure, not a labour one —
        /// and they want opposite responses, so the director has to be able to tell them apart.
        /// </summary>
        public int fuelOnHand;

        /// <summary>
        /// Buildings that burn something. Nothing to burn is only a problem if something burns.
        /// </summary>
        public int burners;

        /// <summary>
        /// Fuel still standing as a plant. Not fuel yet — it has to be cut, and then the logs
        /// have to be hauled. Kept apart from <see cref="fuelOnHand"/> so the director can tell
        /// "nobody is chopping" from "nobody is hauling" from "there is none".
        /// </summary>
        public int fuelStanding;

        /// <summary>
        /// Fuel standing as a plant *outside* the circle the gatherer works in, and how far the
        /// nearest of it is.
        ///
        /// The gather radius was made one number so that "standing fuel" and "what the gatherer
        /// marks" could not disagree — which was right, and left a second way to be wrong. A
        /// colony cuts its circle bare and then reads zero standing fuel, which is true of the
        /// circle and false of the world. Run 137 marked 17 trees inside 55 cells on day 6, had
        /// felled all of them by day 10, and committed its long-term goal to 1000 research
        /// points of tree sowing — "the only wood that grows back" — while a forest stood just
        /// beyond the radius.
        ///
        /// So the colony measures the world as well as its reach, and can tell "there is none"
        /// from "there is none *here*". Those want opposite answers: one is research, the other
        /// is a longer walk.
        /// </summary>
        public int fuelBeyondReach;

        /// <summary>Cells to the nearest standing fuel outside the gather radius, 0 if none.</summary>
        public int nearestFuelDistance;

        /// <summary>Wood it would take to fill every hopper the colony owns.</summary>
        public int fuelWanted;

        /// <summary>Dry hoppers awaiting a single fuel reading, cleared once taken.</summary>
        List<CompRefuelable> fuelKinds;

        /// <summary>Whether a growing zone is already raising the fuel back.</summary>
        public bool growingWood;

        /// <summary>Cells below which a tree plot is a gesture rather than a wood supply.</summary>
        public const int MeaningfulWoodPlot = 10;

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
        /// What those items are worth. A count treats a rifle and a slag chunk alike; this is
        /// what the colony is actually losing to the weather, and what decides whether hauling
        /// them in is worth an afternoon.
        /// </summary>
        public float valueOutdoors;

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

        /// <summary>
        /// Days until the fields stop, and how long they stay stopped.
        ///
        /// growingSeasonNow is a thermometer: it answers "is anything growing today" and
        /// nothing else. A colony that reads only that farms through summer with a comfortable
        /// larder and starves in fall, because the moment it can see the problem is the moment
        /// it is already too late to sow. Run 159 bought four days of food four days before
        /// winter and was starving again on day 25 with six finished rooms and Starvation on
        /// screen.
        ///
        /// RimWorld can answer this properly — the growing season is a per-tile fact it
        /// computes from twelfth-by-twelfth average temperatures, which is where the world
        /// map's own growing-period readout comes from. So the colony asks rather than guesses,
        /// and gets a number it can plan against instead of a boolean it can only react to.
        /// </summary>
        public int growingDaysLeft;

        /// <summary>
        /// Days of non-growing season that follow, which is the gap food has to cross.
        ///
        /// Zero on a map with no winter, which is a real answer and not a missing one — a
        /// permanent-summer colony genuinely never has to stockpile against a season.
        /// </summary>
        public int barrenDaysAhead;

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

        /// <summary>
        /// Research points banked across every project, finished or part-done.
        ///
        /// The score used to count *finished projects*, which in 91 of 93 measured epochs was
        /// zero. A colony ninety-five percent through Pemmican scored exactly the same as one
        /// that never built a bench, so six percent of the score weight sat permanently at zero
        /// and the optimiser had no way to learn its way towards research at all.
        ///
        /// Points are continuous and they move every hour somebody sits at a bench, which is
        /// what a gradient needs. Finishing still matters — a finished project unlocks things —
        /// but it is the milestone, not the measurement.
        /// </summary>
        public float researchPoints;
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
        /// Wild animals currently hunting a colonist, asked as the game's own
        /// <c>JobDefOf.PredatorHunt</c> with a colonist as prey.
        ///
        /// A hunting predator is not hostile to the player faction — it is a neutral wild animal
        /// running a job — so <c>HostileTo</c> is false and hostilePawns stays at zero. The
        /// defense module therefore never engaged, never drafted, and never noticed: two colonies
        /// tonight lost a colonist to a lynx with no THREAT line in the record for days either
        /// side of the death.
        ///
        /// The colonist is not merely attacked. Predators stalk, down, and then eat, which is why
        /// these show up as "Bite (lynx teeth)" on someone who was alone and unarmed.
        /// </summary>
        public int predatorsHunting;

        /// <summary>
        /// Fighting strength of everyone able to take the field, from weapons, armour and skill.
        /// </summary>
        public float colonyStrength;

        /// <summary>
        /// What the game expects to send at this colony — StorytellerUtility's own points figure,
        /// which scales with wealth and population and is the number raids are generated from.
        ///
        /// Measured so the colony can ask whether it is armed for what is coming rather than only
        /// for what has arrived. A colony whose wealth has outrun its weapons is in danger it has
        /// no way to see, because nothing hostile is on the map yet.
        /// </summary>
        public float expectedThreat;

        /// <summary>
        /// Strength over expected threat. Below 1 the colony is under-armed for its own wealth.
        /// </summary>
        public float readiness = 1f;

        /// <summary>Which colonists are being hunted, so the response can go to them.</summary>
        public List<Pawn> huntedColonists;

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
                CaptureReadiness(s, map);

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

                bool able = !p.Downed && !broken && p.Spawned;
                if (able) s.ableColonists.Add(p);

                // Able-bodied and free to take a work order are different questions, and this
                // codebase answered both with ableColonists until the connection map was asked
                // which modules write world.labourAvailable and found DefenseModule and
                // WorkPriorityModule both did.
                //
                // A drafted colonist is able — CombatAssessment.RankFighters wants exactly
                // those, they are the ones holding the line — and is not available, because the
                // draft has already claimed them. DefenseModule was reading the first where it
                // meant the second: it drafts against a raid, then asks FireFront.Fightable
                // whether the colony's people can still put a fire out, counting the people it
                // has just committed to a firing line. A raid with incendiaries produces both
                // conditions at once, which is not a coincidence — it is the same event.
                //
                // The distinction was already drawn three lines below for colonistsIdle, which
                // has excluded the drafted since it was written. One tally had the sense and
                // the other did not.
                bool drafted = p.drafter != null && p.drafter.Drafted;
                if (able && !drafted) s.colonistsFreeForWork++;

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
                        if (p.health.HasHediffsNeedingTendByPlayer(false))
                        {
                            s.colonistsUntended++;
                            if (HasLethalUntended(p)) s.colonistsUntendedLethal++;
                        }

                        // Asked of everyone, tended or not. A tended infection can still be
                        // losing, and that is exactly the case where tending is not the answer.
                        if (LosingToDisease(p))
                    {
                        s.colonistsLosingToDisease++;

                        try
                        {
                            var room = p.GetRoom();
                            if (room != null && !room.PsychologicallyOutdoors &&
                                room.GetStat(RoomStatDefOf.Cleanliness) < 0f)
                                s.colonistsLosingInADirtyRoom++;
                        }
                        catch (Exception) { }
                    }
                    }
                    catch (Exception) { }
                }

                if (p.needs != null && p.needs.food != null)
                {
                    if (p.needs.food.Starving) s.colonistsStarving++;
                    float level = p.needs.food.CurLevelPercentage;
                    if (level < s.minFood) s.minFood = level;
                }

                // Asked of everyone every pass, not only of the hungry. A colonist sealed in at
                // full belly is a death in two days; waiting for the hunger to show wastes the
                // day in which the wall could still be taken down cheaply.
                // Idle is only meaningful for someone able to work: a colonist in bed, drafted,
                // or having a breakdown is not idle, they are busy being unavailable.
                if (p.Spawned && !p.Downed && !p.InBed() && p.mindState != null &&
                    p.mindState.IsIdle && (p.drafter == null || !p.drafter.Drafted))
                    s.colonistsIdle++;

                // A day, because that is the horizon everything else here is scored against —
                // days of food, days of medicine. Anything sooner than that is not "unwell", it
                // is a death with a time on it.
                try
                {
                    if (p.health != null && p.health.hediffSet != null &&
                        p.health.hediffSet.BleedRateTotal > 0.001f)
                    {
                        int ticks = HealthUtility.TicksUntilDeathDueToBloodLoss(p);
                        if (ticks < 60000)
                        {
                            s.colonistsBleedingOut++;

                            // Keep the number, not only the fact. This line used to read the
                            // deadline and throw it away, which left every downstream decision
                            // answering "is somebody bleeding" when the question was "how long
                            // have we got" — see MedicChoice for what that cost in run 162.
                            if (s.ticksToFirstBloodLoss < 0 || ticks < s.ticksToFirstBloodLoss)
                            {
                                s.ticksToFirstBloodLoss = ticks;
                                s.soonestBleedingOut = p;
                            }
                        }
                    }
                }
                catch (Exception) { }

                // Held across passes, because a wall is permanent and a blocked path is not.
                //
                // CanReach uses the pawn's own traverse parms, so anything they will not walk
                // through reads as unreachable — and a colony with 202 fires burning reads as
                // every colonist walled in. Run 123 reported "2 WALLED IN" in the middle of a
                // firestorm, which would have sent the repair to deconstruct walls with the map
                // alight and the one thing that mattered was water.
                //
                // Two consecutive captures. A fire moves; a wall does not.
                if (p.Spawned && !CanReachFood(p))
                {
                    if (unreachableLastPass.Contains(p.thingIDNumber))
                    {
                        s.colonistsCutOff++;
                        if (s.cutOff == null) s.cutOff = new List<Pawn>();
                        s.cutOff.Add(p);
                    }
                    unreachableNow.Add(p.thingIDNumber);
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

                // And anything stalking one of ours, which HostileTo does not cover.
                try
                {
                    if (!p.Downed && p.RaceProps != null && p.RaceProps.Animal &&
                        p.CurJob != null && p.CurJob.def == JobDefOf.PredatorHunt)
                    {
                        var prey = p.CurJob.targetA.Thing as Pawn;
                        if (prey != null && prey.Faction == Faction.OfPlayer && prey.RaceProps != null &&
                            prey.RaceProps.Humanlike)
                        {
                            s.predatorsHunting++;
                            if (s.huntedColonists == null) s.huntedColonists = new List<Pawn>();
                            if (!s.huntedColonists.Contains(prey)) s.huntedColonists.Add(prey);
                        }
                    }
                }
                catch (Exception) { }

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
            float spoilingNutrition, mealNutrition;
            s.foodNutrition = ReachableHumanEdibleNutrition(map, out spoilingNutrition, out mealNutrition);
            s.foodStored = rc.TotalHumanEdibleNutrition;
            s.daysOfFood = s.colonists > 0
                ? s.foodNutrition / (s.colonists * NutritionPerColonistDay)
                : s.foodNutrition;

            s.daysOfFoodSpoiling = s.colonists > 0
                ? spoilingNutrition / (s.colonists * NutritionPerColonistDay)
                : spoilingNutrition;

            s.daysOfMeals = s.colonists > 0
                ? mealNutrition / (s.colonists * NutritionPerColonistDay)
                : mealNutrition;

            // One reading per distinct fuel, now that every dry hopper has been seen.
            if (s.fuelKinds != null && s.fuelKinds.Count > 0)
            {
                var counted = new HashSet<ushort>();
                for (int i = 0; i < s.fuelKinds.Count; i++)
                {
                    var comp = s.fuelKinds[i];
                    if (comp == null || comp.Props == null || comp.Props.fuelFilter == null) continue;

                    var any = comp.Props.fuelFilter.AnyAllowedDef;
                    if (any == null || !counted.Add(any.shortHash)) continue;

                    s.fuelOnHand += FuelReachableFor(map, comp);
                    s.fuelStanding += StandingFuelFor(map, comp);
                    BeyondReachFuelFor(map, comp, s);
                }
                s.fuelKinds = null;
            }


            unreachableLastPass.Clear();
            foreach (var id in unreachableNow) unreachableLastPass.Add(id);
            unreachableNow.Clear();

            // Cut off is a statement about this colonist against the rest of the colony, so it
            // means nothing when nobody can reach food.
            //
            // At tick zero the colony's supplies are still inside drop pods and are not spawned
            // things, so every colonist reads as unable to reach food and the vitals opened with
            // "3 WALLED IN" before a single wall existed. Left alone that would put Construction
            // and Mining at 5.0 on the first pass of every game.
            //
            // A colony where genuinely nobody can reach any food has a food problem, and the
            // repair below could not help anyway: it frees people by finding somebody on the
            // outside who can already reach the far side of the wall, and here there is no
            // outside.
            if (s.cutOff != null && s.colonists > 0 && s.colonistsCutOff >= s.colonists)
            {
                s.colonistsCutOff = 0;
                s.cutOff = null;
            }

            s.unbutcheredNutrition = UnbutcheredNutrition(map);
            s.daysOfFoodUnbutchered = s.colonists > 0
                ? s.unbutcheredNutrition / (s.colonists * NutritionPerColonistDay)
                : s.unbutcheredNutrition;

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
        /// <summary>Shelf life below which food counts as about to be lost.</summary>
        /// <summary>
        /// How soon "spoiling" means. Public because the honest food-security figure has to
        /// subtract only what cannot be eaten inside this window — see
        /// DirectorContext.DaysOfFoodKeeping — and two places disagreeing about the horizon
        /// would be the duplicated-quantity fault again.
        /// </summary>
        public const float SpoilingSoonDays = 3f;

        static float ReachableHumanEdibleNutrition(Map map, out float spoiling, out float meals)
        {
            spoiling = 0f;
            meals = 0f;
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

                float nutrition = def.ingestible.CachedNutrition * thing.stackCount;
                total += nutrition;
                if (def.ingestible.IsMeal) meals += nutrition;

                // And how much of it will not last. A stack with days left in it is fine
                // wherever it is; one about to turn is work the colony is about to lose.
                var rot = thing.TryGetComp<CompRottable>();
                if (rot != null && rot.Active)
                {
                    float daysLeft = rot.TicksUntilRotAtCurrentTemp / 60000f;
                    if (daysLeft < SpoilingSoonDays) spoiling += nutrition;
                }
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

        /// <summary>
        /// Meat waiting inside fresh animal corpses. Fresh only — a rotted corpse yields
        /// nothing worth hauling, and counting it would promise food that is not coming.
        /// </summary>
        static float UnbutcheredNutrition(Map map)
        {
            if (map == null || map.listerThings == null) return 0f;

            float total = 0f;
            try
            {
                var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
                for (int i = 0; i < corpses.Count; i++)
                {
                    var corpse = corpses[i] as Corpse;
                    if (corpse == null || !corpse.Spawned) continue;
                    if (corpse.IsForbidden(Faction.OfPlayer)) continue;

                    var pawn = corpse.InnerPawn;
                    if (pawn == null || pawn.RaceProps == null) continue;
                    if (!pawn.RaceProps.Animal) continue;
                    if (corpse.GetRotStage() != RotStage.Fresh) continue;

                    total += pawn.RaceProps.baseBodySize * 3.5f;   // roughly the meat it yields
                }
            }
            catch (Exception) { }
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
                    {
                        s.colonistBeds += bed.TotalSleepingSlots;

                        // The same enclosure test RoomCensus uses. A roof is not a room.
                        var room = bed.Position.GetRoom(map);
                        if (room != null && !room.TouchesMapEdge && !room.PsychologicallyOutdoors)
                            s.shelteredBeds += bed.TotalSleepingSlots;
                    }
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

                    // Only things the weather is actually taking.
                    //
                    // This counted every haulable item under open sky, so a colony with seven
                    // hundred granite chunks in a field read as a storage emergency while one
                    // with five components and a rifle in the rain read as almost fine. Chunks
                    // do not deteriorate; components deteriorate at 2.0 a day, and weapons,
                    // apparel and medicine all rot away outdoors — which is most of what a raid
                    // leaves lying on the ground.
                    //
                    // DeteriorationRate is the game's own answer to "is the sky costing me
                    // this", so it holds for modded items without a list.
                    float rate;
                    try { rate = thing.GetStatValue(StatDefOf.DeteriorationRate); }
                    catch (Exception) { rate = 0f; }
                    if (rate <= 0f) continue;

                    s.itemsOutdoors++;

                    // And what it is worth, because one rifle is not one steel slag chunk. This
                    // is the number that says whether hauling is worth a colonist's afternoon.
                    try { s.valueOutdoors += thing.MarketValue * thing.stackCount; }
                    catch (Exception) { }
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

                // Trees before the food filter, because a woodlot is not food and the filter
                // below skips everything that is not.
                //
                // This detection sat *under* that continue. A saguaro is PlantRole.Wood, so the
                // loop skipped it, growingWood could never become true, and EnsureWoodPlot sowed
                // a fresh forty-cell plot every single pass — one every six in-game hours for
                // days. Both rules were right on their own: the food filter has its own note
                // above about why it exists, and the tree check is correct. Only the order was
                // wrong, and the order is what made one silently switch off the other.
                //
                // The tell was in the chronicle from the first day and I read past it: the same
                // line, over and over, which is the exact signature this project has a note
                // about.
                if (growing.plant != null && growing.plant.harvestedThingDef != null &&
                    growing.plant.harvestedThingDef.IsStuff &&
                    growing.plant.IsTree &&
                    grow.CellCount >= MeaningfulWoodPlot)
                    s.growingWood = true;

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

                // A tree plot is the answer to a wood-poor map, and WoodSupplyGoal stands down
                // once one exists — asked as "does its harvest yield what the fires burn"
                // rather than by naming a tree.
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
                CaptureSeasonAhead(s, map);
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

                // Anything that burns something, asked with the game's own question.
                //
                // ShouldAutoRefuelNow is the condition WorkGiver_Refuel itself tests before it
                // will queue a job, so a true here means the game wants a colonist to carry fuel
                // and is waiting for one to be free. It covers the stove, the generator, a
                // campfire and a torch alike, which is the point: the previous reading was
                // "generators built minus generators running", and a generator is only the
                // instance of this that happened to be noticed first.
                //
                // Run 108 lost a colony to the version of this that was not being asked. The
                // fuelled stove ran dry on day 3's wood and sat at 0.85 of a 50-unit hopper for
                // eighteen days. Nothing cooked; colonists ate raw and carried AteRawFood at -7
                // apiece; mood fell to 0.15; Aisu broke and slaughtered the livestock the pen and
                // the fodder plot had been built to hold. Every one of those is a downstream
                // symptom of a hopper nobody filled, and the director's answer to "nothing is
                // cooked" was to raise *Cooking* — which puts a cook in front of a stove they
                // cannot light.
                var refuelable = building.TryGetComp<CompRefuelable>();
                if (refuelable != null)
                {
                    s.burners++;

                    // What it would take to fill every hopper the colony owns. The wood target
                    // was a gene plus whatever the plan wanted to *build* with; the fires the
                    // colony had already lit were in no target at all, so a colony could sit at
                    // "wood target met" with eight of them empty.
                    if (refuelable.Props != null)
                        s.fuelWanted += (int)(refuelable.Props.fuelCapacity - refuelable.Fuel);
                }
                if (refuelable != null && refuelable.ShouldAutoRefuelNow)
                {
                    s.buildingsWantingFuel++;
                    if (s.fuelStarved == null) s.fuelStarved = new List<Building>();
                    s.fuelStarved.Add(building);

                    // And whether there is anything to carry.
                    //
                    // "Behind on refuelling" was read as a labour shortage — more dry hoppers
                    // than colonists to fill them — which quietly assumes the fuel exists and
                    // hands are what is short. On run 110's map there was no wood at all: not a
                    // low stock, none, and no tree that yields any. The colonists were not busy.
                    // There was nothing to carry, and a colony can be idle and dry at once.
                    //
                    // Asked through the hopper's own fuelFilter rather than by naming wood, so a
                    // chemfuel generator or a modded burner answers for itself.
                    // Measured once per kind of fuel, after this loop. Summing per hopper
                    // counted the same woodpile once for every dry building — ten dry hoppers
                    // reported 84,690 units of standing timber inside a 55-cell circle, which
                    // is roughly ten times the truth and wrong in the direction of "there is
                    // plenty", against a WoodSupplyGoal that stands down at 400.
                    if (s.fuelKinds == null) s.fuelKinds = new List<CompRefuelable>();
                    s.fuelKinds.Add(refuelable);
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

        /// <summary>
        /// How much of what this thing burns is lying about the map, unforbidden.
        ///
        /// Map-wide rather than stockpiled, for the reason every other count here is: a colony
        /// burns wood it has not tidied away. Zero here does not mean "running low" — it means
        /// the job the game is waiting for cannot be taken by anybody, and no amount of Hauling
        /// priority will change that.
        /// </summary>
        static int FuelReachableFor(Map map, CompRefuelable refuelable)
        {
            if (map == null || refuelable == null || refuelable.Props == null) return 0;

            var filter = refuelable.Props.fuelFilter;
            if (filter == null) return 0;

            int total = 0;
            try
            {
                foreach (var def in filter.AllowedThingDefs)
                {
                    if (def == null) continue;

                    var things = map.listerThings.ThingsOfDef(def);
                    if (things != null)
                        for (int i = 0; i < things.Count; i++)
                        {
                            var thing = things[i];
                            if (thing == null || !thing.Spawned) continue;
                            if (thing.IsForbidden(Faction.OfPlayer)) continue;
                            total += thing.stackCount;
                        }

                    // And what is still standing.
                    //
                    // Counting only the cut logs was a straightforward regression: every colony
                    // begins with a forest and no woodpile, so a wood-fired map read as NO FUEL
                    // on day one and the rule would have refused the colony its first stove. A
                    // tree is fuel that has not been chopped yet, which is a labour question
                    // again — the thing this measurement exists to separate out.
                    //
                    // Asked as plant.harvestedThingDef, so it holds for whatever a mod makes
                    // burnable and for the wood-bearing trees of any biome.
                    // Counted apart, never added in. A standing tree is not fuel; it is two
                    // jobs away from fuel — somebody has to cut it, and somebody has to carry
                    // the logs to the hopper. Folding it into the same number said run 110 had
                    // three thousand units of fuel while eight of its fires were out.
                    //
                    // The distinction is the whole point of the measurement, because the three
                    // states want three different levers: logs on the ground is a Hauling
                    // problem, trees standing with no logs is a chopping problem, and neither is
                    // a supply problem no work priority can answer.
                }
            }
            catch (Exception) { }
            return total;
        }

        /// <summary>
        /// Fuel still standing as a plant — harvestable, not yet harvested.
        ///
        /// Yield scaled by growth, because a sapling is not a log. This answers "could the
        /// colony get more if it went and cut some", which is a different question from "is
        /// there any to carry right now".
        /// </summary>
        static int StandingFuelFor(Map map, CompRefuelable refuelable)
        {
            if (map == null || refuelable == null || refuelable.Props == null) return 0;
            var filter = refuelable.Props.fuelFilter;
            if (filter == null) return 0;

            // Only what the colony would actually go and cut.
            //
            // This counted every tree on the map while ResourceModule marks trees within 55
            // cells of the base. Run 122 sat on sand with no tree in reach and read "1990
            // standing, so chopping is the lever" for eighteen days — six hoppers dry the whole
            // time, the generator never lit. The number was true about the map and false about
            // the colony, and the two scopes have to be one scope or the report is a lie in the
            // direction of doing nothing.
            var origin = ColonyOrigin(map);
            int radiusSq = Modules.ResourceModule.GatherRadius * Modules.ResourceModule.GatherRadius;

            int total = 0;
            try
            {
                foreach (var def in filter.AllowedThingDefs)
                {
                    if (def == null) continue;
                    var sources = HarvestSourcesOf(def);
                    for (int i = 0; i < sources.Count; i++)
                    {
                        var standing = map.listerThings.ThingsOfDef(sources[i]);
                        if (standing == null) continue;
                        for (int j = 0; j < standing.Count; j++)
                        {
                            var plant = standing[j] as Plant;
                            if (plant == null || !plant.Spawned) continue;
                            if (plant.IsForbidden(Faction.OfPlayer)) continue;
                            if ((plant.Position - origin).LengthHorizontalSquared > radiusSq) continue;
                            total += (int)(plant.def.plant.harvestYield * plant.Growth);
                        }
                    }
                }
            }
            catch (Exception) { }
            return total;
        }

        /// <summary>
        /// The same count, for everything outside the gather radius, plus how far the nearest is.
        ///
        /// Deliberately a separate pass rather than a widened one: the in-reach figure decides
        /// whether anyone is sent to chop, and it must keep meaning exactly what the gatherer
        /// will act on. This one only answers whether the world still has wood in it.
        /// </summary>
        static void BeyondReachFuelFor(Map map, CompRefuelable refuelable, ColonyState s)
        {
            if (map == null || refuelable == null || refuelable.Props == null) return;
            var filter = refuelable.Props.fuelFilter;
            if (filter == null) return;

            var origin = ColonyOrigin(map);
            int radiusSq = Modules.ResourceModule.GatherRadius * Modules.ResourceModule.GatherRadius;
            int nearestSq = int.MaxValue;

            try
            {
                foreach (var def in filter.AllowedThingDefs)
                {
                    if (def == null) continue;
                    var sources = HarvestSourcesOf(def);
                    for (int i = 0; i < sources.Count; i++)
                    {
                        var standing = map.listerThings.ThingsOfDef(sources[i]);
                        if (standing == null) continue;
                        for (int j = 0; j < standing.Count; j++)
                        {
                            var plant = standing[j] as Plant;
                            if (plant == null || !plant.Spawned) continue;
                            if (plant.IsForbidden(Faction.OfPlayer)) continue;

                            int distSq = (plant.Position - origin).LengthHorizontalSquared;
                            if (distSq <= radiusSq) continue;

                            s.fuelBeyondReach += (int)(plant.def.plant.harvestYield * plant.Growth);
                            if (distSq < nearestSq) nearestSq = distSq;
                        }
                    }
                }
            }
            catch (Exception) { }

            if (nearestSq == int.MaxValue) return;
            int nearest = (int)Math.Sqrt(nearestSq);
            if (s.nearestFuelDistance == 0 || nearest < s.nearestFuelDistance)
                s.nearestFuelDistance = nearest;
        }

        /// <summary>
        /// Walk the year ahead and find where the growing stops and starts again.
        ///
        /// Bounds are the game's own DefaultMinGrowthTemperature and DefaultMaxGrowthTemperature
        /// — 0 and 58 — the same pair growingSeasonNow already tests today's temperature
        /// against, so the forecast and the thermometer cannot disagree about what "growing"
        /// means. A twelfth is five days.
        /// </summary>
        static void CaptureSeasonAhead(ColonyState s, Map map)
        {
            try
            {
                var growable = GenTemperature.TwelfthsInAverageTemperatureRange(
                    map.Tile, Plant.DefaultMinGrowthTemperature, Plant.DefaultMaxGrowthTemperature);

                if (growable == null || growable.Count == 0)
                {
                    // Nothing grows here at any time of year — an ice sheet. The whole year is
                    // the gap, and saying "zero days left" would read as "it just ended".
                    s.growingDaysLeft = 0;
                    s.barrenDaysAhead = GenDate.DaysPerTwelfth * GenDate.TwelfthsPerYear;
                    return;
                }
                if (growable.Count >= GenDate.TwelfthsPerYear)
                {
                    s.growingDaysLeft = GenDate.DaysPerTwelfth * GenDate.TwelfthsPerYear;
                    s.barrenDaysAhead = 0;   // permanent summer: a real answer, not a missing one
                    return;
                }

                var now = GenLocalDate.Twelfth(map);
                int intoTwelfth = GenLocalDate.DayOfTwelfth(map);

                // Days of growing still ahead, counting the rest of this twelfth if it grows.
                int left = 0;
                var t = now;
                if (growable.Contains(t))
                {
                    left += GenDate.DaysPerTwelfth - intoTwelfth;
                    for (int i = 1; i < GenDate.TwelfthsPerYear; i++)
                    {
                        t = TwelfthUtility.NextTwelfth(t);
                        if (!growable.Contains(t)) break;
                        left += GenDate.DaysPerTwelfth;
                    }
                }

                // And the barren stretch that follows it.
                int barren = 0;
                for (int i = 0; i < GenDate.TwelfthsPerYear; i++)
                {
                    t = TwelfthUtility.NextTwelfth(t);
                    if (growable.Contains(t)) break;
                    barren += GenDate.DaysPerTwelfth;
                }
                if (!growable.Contains(now)) barren += GenDate.DaysPerTwelfth - intoTwelfth;

                s.growingDaysLeft = left;
                s.barrenDaysAhead = barren;
            }
            catch (Exception) { s.growingDaysLeft = 0; s.barrenDaysAhead = 0; }
        }

        /// <summary>
        /// Roughly where the colony is — the centre of what it has built, or the map centre
        /// before it has built anything. The same circle the gatherer works in.
        /// </summary>
        static IntVec3 ColonyOrigin(Map map)
        {
            try
            {
                var built = map.listerBuildings.allBuildingsColonist;
                if (built == null || built.Count == 0) return map.Center;

                int x = 0, z = 0, n = 0;
                for (int i = 0; i < built.Count; i++)
                {
                    if (built[i] == null || !built[i].Spawned) continue;
                    x += built[i].Position.x; z += built[i].Position.z; n++;
                }
                return n == 0 ? map.Center : new IntVec3(x / n, 0, z / n);
            }
            catch (Exception) { return map.Center; }
        }

        static readonly Dictionary<ushort, List<ThingDef>> harvestSourceCache =
            new Dictionary<ushort, List<ThingDef>>();

        /// <summary>
        /// Every plant def whose harvest yields this thing — the trees, for wood.
        ///
        /// Built once per fuel def and kept, because it is a scan of the whole def database and
        /// the answer cannot change during a game.
        /// </summary>
        static List<ThingDef> HarvestSourcesOf(ThingDef yield)
        {
            List<ThingDef> known;
            if (harvestSourceCache.TryGetValue(yield.shortHash, out known)) return known;

            known = new List<ThingDef>();
            var all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def == null || def.plant == null) continue;
                if (def.plant.harvestedThingDef != yield) continue;
                if (def.plant.harvestYield <= 0f) continue;
                known.Add(def);
            }

            harvestSourceCache[yield.shortHash] = known;
            return known;
        }

        /// <summary>
        /// Whether this colonist can get to anything they could eat.
        ///
        /// <c>Reachability.CanReach</c> is the game's own pathfinder answering the question a
        /// player answers by looking — and it is region-based, so it is cheap and it early-outs
        /// on the first reachable stack, which for a colonist standing in the base is the first
        /// thing tested.
        ///
        /// Food rather than "the base" on purpose. A colonist sealed in with a stockpile is not
        /// in trouble; one sealed out of every larder is, whatever else they can walk to.
        /// </summary>
        static bool CanReachFood(Pawn pawn)
        {
            var map = pawn.Map;
            if (map == null || map.listerThings == null) return true;

            try
            {
                var things = map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
                if (things == null) return true;

                var parms = TraverseParms.For(pawn);
                for (int i = 0; i < things.Count; i++)
                {
                    var thing = things[i];
                    if (thing == null || !thing.Spawned) continue;
                    if (thing is Corpse || thing is Pawn) continue;

                    var def = thing.def;
                    if (def == null || def.ingestible == null) continue;
                    if (!def.IsNutritionGivingIngestible || !def.ingestible.HumanEdible) continue;
                    if (thing.IsForbidden(Faction.OfPlayer)) continue;

                    if (map.reachability.CanReach(pawn.Position, thing,
                                                  PathEndMode.ClosestTouch, parms))
                        return true;
                }
            }
            catch (Exception) { return true; }   // never report a trap on a thrown exception

            return false;
        }

        /// <summary>
        /// How the colony would fare against what it is inviting.
        ///
        /// ThreatForecast already owns this arithmetic — it reconstructs RimWorld's raid-points
        /// curve from the published anchors and FortifyGoal has been reading it all along. I
        /// nearly added a second readiness here computed a different way, which is the fault this
        /// codebase has hit three times tonight: two places holding the same quantity and
        /// disagreeing. It reads the existing one.
        ///
        /// Measured every pass so the number is available to the score and the vitals, not only
        /// to the goal that happened to want it. A colony whose wealth has outrun its weapons is
        /// in danger it cannot see, because nothing hostile is on the map yet.
        /// </summary>
        static void CaptureReadiness(ColonyState s, Map map)
        {
            try
            {
                s.colonyStrength = CombatAssessment.ColonyStrength(s);
                s.expectedThreat = ThreatForecast.ExpectedRaidPoints(s.wealthTotal, s.colonists);
                s.readiness = ThreatForecast.Readiness(s.colonyStrength, s.expectedThreat);
            }
            catch (Exception) { s.readiness = 1f; }
        }

        static void CaptureResearch(ColonyState s)
        {
            var all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            var manager = Find.ResearchManager;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].IsFinished) s.researchFinished++;

                try
                {
                    if (manager != null) s.researchPoints += manager.GetProgress(all[i]);
                }
                catch (Exception) { }
            }
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
            m.colonistsCutOff = colonistsCutOff;
            m.colonistsIdle = colonistsIdle;
            m.readiness = readiness;
            m.colonistsBleedingOut = colonistsBleedingOut;
            m.colonistsLosingInADirtyRoom = colonistsLosingInADirtyRoom;
            m.colonistsUntended = colonistsUntended;
            m.colonistsUntendedLethal = colonistsUntendedLethal;
            m.colonistsLosingToDisease = colonistsLosingToDisease;
            m.daysOfFoodUnbutchered = daysOfFoodUnbutchered;
            m.daysOfFoodSpoiling = daysOfFoodSpoiling;
            m.daysOfMeals = daysOfMeals;
            m.buildingsWantingFuel = buildingsWantingFuel;
            m.fuelOnHand = fuelOnHand;
            m.fuelStanding = fuelStanding;
            m.medicineCount = medicineCount;
            m.medicineStored = medicineStored;
            m.usableMaterial = usableMaterial;
            m.avgHealth = avgHealth;
            m.daysOfFood = daysOfFood;
            m.outdoorTemperature = outdoorTemperature;
            m.wealthTotal = wealthTotal;
            m.colonistBeds = colonistBeds;
            m.shelteredBeds = shelteredBeds;
            m.poweredTurrets = poweredTurrets;
            m.fires = fires;
            m.firesNearBase = firesNearBase;
            m.researchFinished = researchFinished;
            m.researchPoints = researchPoints;

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
