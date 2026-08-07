using System;
using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Issues the standing orders that feed the colony's material supply: chopping wood,
    /// mining ore, and hunting.
    ///
    /// Each activity is gated on a stock target from the genome and scaled by its own
    /// aggression gene, so the optimiser can learn a materially different economy — a
    /// wood-poor tundra colony that mines hard, or a forest colony that barely mines at all.
    /// </summary>
    public class ResourceModule : DirectorModule
    {
        public override string Name { get { return "Resource gathering"; } }
        /// <summary>
        /// Holding off gathering is time-critical in a way that designating it is not: a hunt
        /// leads the only able colonist across the map for hours while somebody bleeds out at
        /// home. This is the four in-game hours measured between a colonist going down at 14h
        /// and gathering finally being held off at 18h.
        /// </summary>
        public override bool Urgent(DirectorContext ctx)
        {
            return ctx.state.colonistsDowned > 0 || ctx.state.EmergencyAtHome;
        }

        public override int IntervalTicks { get { return 12500; } }

        bool yieldedToEmergency;

        /// <summary>Set while gathering is suspended for a map-wide condition, so it is said once.</summary>
        bool yieldedToCondition;

        /// <summary>Designations added per pass, per activity.</summary>
        const int MaxPerPass = 25;

        /// <summary>How far from the base colonists are sent to gather.</summary>
        /// <summary>
        /// How far from the colony the gatherer will mark anything. Public because
        /// ColonyState measures standing fuel against the same circle — two scopes for the
        /// same question is what let run 122 report "1990 standing, chopping is the lever"
        /// for eighteen days while the base sat on sand with no tree within reach.
        /// </summary>
        public const int GatherRadius = 55;

        /// <summary>
        /// How far each gatherer actually reached last pass. Kept so the record names the radius
        /// that was searched rather than the constant it starts from — a message that says "55"
        /// while the code searched 115 is the same jointly-misleading shape this session has now
        /// corrected nine times.
        /// </summary>
        int lastChopRadius = GatherRadius, lastMineRadius = GatherRadius, lastHuntRadius = GatherRadius;

        /// <summary>Whether the last pass marked anything, so transitions can be spoken.</summary>
        bool wasGathering;

        protected override void Act(DirectorContext ctx)
        {
            var origin = ctx.Origin;

            // Something is burning or shooting at the colony. Sending people out to fell trees
            // or chase animals now spends the exact labour needed at home, and walks it out of
            // range of the emergency. Gathering waits.
            // A colonist on the floor is an emergency at home too.
            //
            // `EmergencyAtHome` counts fires and hostiles, and casualties outlive both: the
            // doctor hold-back that keeps someone tending is scoped to an active threat, so the
            // moment a raid ends the last able colonist is free to walk off after an animal. It
            // is not a priority problem — the work module already puts Doctor at 4x — it is that
            // a hunt takes hours and leads them across the map.
            //
            // Watched live: raid over at 14h, two down at 16h with three days of food, the one
            // colonist still standing sent hunting at 20h, both casualties dead by 22h.
            //
            // Relieved below a day of food, on the same reasoning that stops the plan barring a
            // hungry colony from its own kitchen: tending costs minutes and starvation is then
            // the nearer of the two ways to die.
            bool casualtiesAtHome = ctx.state.colonistsDowned > 0 && ctx.state.daysOfFood >= 1f;

            if (ctx.state.EmergencyAtHome || casualtiesAtHome
                || (ctx.plan != null && ctx.plan.EmergencyActive
                    && ctx.state.daysOfFood >= 2f))
            {
                if (!yieldedToEmergency)
                {
                    yieldedToEmergency = true;
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "holding off gathering: {0} fires, {1} hostiles and {2} down at the colony",
                        ctx.state.firesNearBase, ctx.state.hostilesNearBase,
                        ctx.state.colonistsDowned));
                }
                return;
            }
            yieldedToEmergency = false;

            // The sky itself is the hazard.
            //
            // Toxic fallout poisons anything under open sky, and the director's standing answer
            // to an empty larder is to send somebody out after an animal — which during fallout
            // poisons the hunter and the meat together, and never once looks like combat in the
            // record. Nothing here could see a game condition at all before this, so a colony
            // carried on chopping, mining and hunting straight through it.
            //
            // Only the elective errands stop. Fires and raids are still answered outdoors,
            // because those are not optional and refusing them costs more than the fallout.
            if (Conditions.ConditionResponse.SuspendElectiveOutdoorWork(ctx.state.conditions))
            {
                if (!yieldedToCondition)
                {
                    yieldedToCondition = true;
                    Chronicle.Record(ChronicleCategory.Economy,
                        "holding off gathering: " +
                        Conditions.ConditionResponse.Describe(ctx.state.conditions) +
                        " — outside is the hazard, so chopping, mining and hunting wait");
                }
                return;
            }
            if (yieldedToCondition)
            {
                yieldedToCondition = false;
                Chronicle.Record(ChronicleCategory.Economy,
                    "conditions have passed; gathering resumes");
            }

            wanting.Clear();
            int chopped = MaybeChopWood(ctx, origin);
            int mined = MaybeMine(ctx, origin);
            int hunted = MaybeHunt(ctx, origin);

            if (chopped + mined + hunted > 0)
                Note("designated " + chopped + " trees, " + mined + " rock, " + hunted + " animals");

            // Said in the record, not only in a verbose log nobody turns on.
            //
            // Designating is the whole job of this module and none of it reached the chronicle:
            // Note() writes to AcLog.Verbose, which is off. So "6 DRY, UNCUT with PlantCutting
            // at 5.0 and a colonist idle" was unanswerable from the record — there was no way to
            // tell whether the trees were marked and unreachable, or never marked at all.
            //
            // Spoken on the edges rather than every pass: gathering starting after a lull, and
            // gathering stopping. A line per pass would bury the log; a line per transition is
            // the shape of the question anyone actually asks of it.
            bool gathering = chopped + mined + hunted > 0;
            if (gathering != wasGathering)
            {
                wasGathering = gathering;
                if (gathering)
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "gathering: marked {0} trees, {1} rock, {2} animals within {3}/{4}/{5} " +
                        "cells of the base (chop/mine/hunt — each reaches as far as being short " +
                        "of that one justifies)",
                        chopped, mined, hunted,
                        lastChopRadius, lastMineRadius, lastHuntRadius));
                else if (wanting.Count == 0)
                    Chronicle.Record(ChronicleCategory.Economy,
                        "gathering: nothing left to mark — every target is at its stock level");
                else
                    // The other half of what used to be one sentence joined by "or".
                    //
                    // "every target is at its stock level, or there is nothing in range to mark"
                    // covered a colony that needs nothing and a colony whose reachable map is
                    // stripped bare, and those want opposite work: the first is fine, the second
                    // is a radius that is too small or a map that is finished. The gatherers knew
                    // which it was — each returns 0 for both — and the caller could not see it.
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "gathering: still wanting {0} and nothing within {1} cells to mark — " +
                        "this is not a shortage of hands and no work priority answers it",
                        string.Join(", ", wanting.ToArray()),
                        AcMath.Max(lastChopRadius, AcMath.Max(lastMineRadius, lastHuntRadius))));
            }

            // Onto the roadmap, now that a run has shown it persists.
            //
            // This was left off deliberately when the message was split, because "nothing in
            // range" might have been a radius that widens under pressure rather than a real
            // absence. Run 184 answered within a day: still wanting wood at day 1 12h, day 1
            // 22h and day 2 18h, on a map whose trees near the base are gone. That is not
            // transient, and it is the same statement FuelUpkeep makes about a map with nothing
            // that burns — a want no amount of labour reaches.
            //
            // Reported every pass and idempotent, so the clock runs; the chronicle line above
            // still speaks only on the transition, which is why it is not said four times.
            if (wanting.Count > 0 && chopped + mined + hunted == 0)
                CapabilityGaps.Report(string.Join(", ", wanting.ToArray()) + " within reach",
                                      "anything to mark within " +
                                      AcMath.Max(lastChopRadius,
                                          AcMath.Max(lastMineRadius, lastHuntRadius)) + " cells",
                                      1f, 0f, ctx.state.tick);
            else
                CapabilityGaps.Close(lastReachGap);

            lastReachGap = wanting.Count > 0 && chopped + mined + hunted == 0
                ? string.Join(", ", wanting.ToArray()) + " within reach"
                : null;
        }

        /// <summary>The reach gap reported last pass, so a changed want closes the old entry.</summary>
        string lastReachGap;

        int MaybeChopWood(DirectorContext ctx, IntVec3 origin)
        {
            // As with mining: what the plan needs raises the standing target. A wood-fired
            // generator burns its fuel continuously, so wanting power is also wanting wood.
            float target = ctx.Gene(Genes.WoodTarget);
            if (ctx.plan != null) target = AcMath.Max(target,
                AcMath.Max(ctx.plan.Needs.For("WoodLog"), ctx.plan.QuantityWanted("WoodLog")));

            // And what the colony's own fires want.
            //
            // The target covered the gene and whatever the plan meant to *build* with, and had
            // nothing to say about the stove, the campfire and the passive coolers already
            // standing — so a colony could read "wood target met" with eight hoppers empty and
            // stop chopping. Run 110 did exactly that: PlantCutting sat at its ceiling, the
            // trees stayed up, and the fires went out.
            //
            // fuelWanted is the sum of what every hopper has room for, so this is the colony's
            // actual demand rather than a number anybody picked.
            target = AcMath.Max(target, ctx.state.fuelWanted);
            if (ctx.state.wood >= target) return 0;
            wanting.Add("wood");

            float aggression = ctx.Gene(Genes.ChopAggression);
            int budget = (int)(MaxPerPass * (0.2f + aggression));
            if (budget <= 0) return 0;

            var map = ctx.map;
            var des = DesignationDefOf.HarvestPlant;
            int done = 0;

            // As far as being short of wood justifies walking. Hunting has always done this and
            // these two never did, which is how run 196 marked an animal and no trees in the
            // same pass from the same cell.
            lastChopRadius = GatherReach.Radius(GatherRadius,
                GatherReach.Shortfall(ctx.state.wood, target), ctx.Gene(Genes.GatherReachStretch));

            foreach (var cell in GenRadial.RadialCellsAround(origin, lastChopRadius, true))
            {
                if (done >= budget) break;
                if (!cell.InBounds(map)) continue;

                var plant = cell.GetPlant(map);
                if (plant == null || !plant.def.plant.IsTree) continue;
                if (plant.def.plant.harvestedThingDef != ThingDefOf.WoodLog) continue;
                if (map.designationManager.DesignationOn(plant, des) != null) continue;
                if (plant.Growth < 0.4f) continue;

                map.designationManager.AddDesignation(new Designation(plant, des));
                done++;
            }

            return done;
        }

        int MaybeMine(DirectorContext ctx, IntVec3 origin)
        {
            // The plan's requirements raise the bar: wanting power means wanting the steel and
            // components power is made of, which is what turns a long-term goal into digging.
            float target = ctx.Gene(Genes.SteelTarget);
            float componentTarget = ctx.Gene(Genes.ComponentsTarget);
            if (ctx.plan != null)
            {
                target = AcMath.Max(target,
                    AcMath.Max(ctx.plan.Needs.For("Steel"), ctx.plan.QuantityWanted("Steel")));
                componentTarget = AcMath.Max(componentTarget,
                    AcMath.Max(ctx.plan.Needs.For("ComponentIndustrial"),
                               ctx.plan.QuantityWanted("ComponentIndustrial")));
            }

            bool needSteel = ctx.state.steel < target;
            bool needComponents = ctx.state.components < componentTarget;
            if (!needSteel && !needComponents) return 0;
            wanting.Add(needSteel && needComponents ? "steel and components"
                      : needSteel ? "steel" : "components");

            float aggression = ctx.Gene(Genes.MiningAggression);
            int budget = (int)(MaxPerPass * (0.2f + aggression));
            if (budget <= 0) return 0;

            var map = ctx.map;
            var des = DesignationDefOf.Mine;
            int done = 0;

            lastMineRadius = GatherReach.Radius(GatherRadius,
                GatherReach.Shortfall(ctx.state.steel, target), ctx.Gene(Genes.GatherReachStretch));

            foreach (var cell in GenRadial.RadialCellsAround(origin, lastMineRadius, true))
            {
                if (done >= budget) break;
                if (!cell.InBounds(map)) continue;
                if (map.designationManager.DesignationAt(cell, des) != null) continue;

                var edifice = cell.GetEdifice(map);
                if (edifice == null || !edifice.def.mineable) continue;

                var yield = edifice.def.building != null ? edifice.def.building.mineableThing : null;
                if (yield == null) continue;

                bool wanted = (needSteel && yield == ThingDefOf.Steel)
                           || (needComponents && yield == ThingDefOf.ComponentIndustrial);
                if (!wanted) continue;

                // Only mine what colonists can actually reach without tunnelling blind.
                if (!HasOpenNeighbour(map, cell)) continue;

                map.designationManager.AddDesignation(new Designation(cell, des));
                done++;
            }

            return done;
        }

        static bool HasOpenNeighbour(Map map, IntVec3 cell)
        {
            for (int i = 0; i < 4; i++)
            {
                var n = cell + GenAdj.CardinalDirections[i];
                if (n.InBounds(map) && n.Walkable(map)) return true;
            }
            return false;
        }

        /// <summary>
        /// Hunts when food is short, choosing targets the colony can actually beat.
        ///
        /// Targets are considered safest-first, so easy game is taken while it lasts and
        /// dangerous game is only reached for once nothing else is left. Whether a given animal
        /// is worth fighting is a judgement about these colonists — their skills, health and
        /// weapons — against that animal, eased by how close the colony is to starving. A
        /// well-armed group will take a thrumbo; a comfortable one will not bother; a starving
        /// one with nothing else on the map will try regardless, because starving quietly is
        /// not the safer choice.
        /// </summary>
        int MaybeHunt(DirectorContext ctx, IntVec3 origin)
        {
            float foodTarget = ctx.FoodDaysWanted;
            float daysOfFood = ctx.state.daysOfFood;
            if (daysOfFood >= foodTarget) return 0;
            wanting.Add("food");

            // 0 when comfortably stocked, 1 when the larder will be empty before anything
            // decided now can reach it. Measured against the food left after the hunt-haul-
            // butcher-cook lead rather than the food in the store, so escalation happens while
            // there is still margin for the hunt to fail.
            float urgency = FoodTiming.Urgency(daysOfFood, foodTarget);

            float aggression = ctx.Gene(Genes.HuntAggression);
            float effective = AcMath.Clamp01(aggression + urgency * 0.5f);
            if (effective < 0.15f) return 0;

            // Meat already killed and not yet dealt with counts as food on its way.
            //
            // `daysOfFood` sees stockpiled goods, so a field of fresh corpses reads as an empty
            // larder — and the colony answers an empty larder by hunting, which produces another
            // corpse it has not processed either. That is the loop: colonists hunting endlessly
            // with nothing to show for it, each kill making the next one look more necessary.
            // The shared measurement, not a private recount — the same number the work
            // priorities and the vitals line read, so the three can never disagree about how
            // much meat is lying in the field.
            float waiting = ctx.state.unbutcheredNutrition;
            float needed = ctx.state.colonists * ColonyState.NutritionPerColonistDay;
            if (needed > 0f && waiting >= needed * BacklogDays)
            {
                if (!notedBacklog)
                {
                    notedBacklog = true;
                    Chronicle.Record(ChronicleCategory.Hunt, string.Format(
                        "not hunting: {0:0.0} days of meat already killed and waiting to be " +
                        "butchered — the larder reads empty because it is lying in the field",
                        waiting / needed));
                }
                return 0;
            }
            notedBacklog = false;

            // Desperation is mostly about how empty the larder is, nudged by how bold the
            // strategy is in general.
            float desperation = AcMath.Clamp01(urgency * 0.8f + aggression * 0.2f);
            blastWeight = ctx.Gene(Genes.HuntBlastWeight);
            float strength = CombatAssessment.ColonyStrength(ctx.state);

            int radius = GatherReach.Radius(GatherRadius, urgency,
                                            ctx.Gene(Genes.GatherReachStretch));
            lastHuntRadius = radius;
            int radiusSq = radius * radius;

            var map = ctx.map;
            var des = DesignationDefOf.Hunt;
            int budget = (int)(8 * effective) + 1;
            float woundsPerHealth = ctx.Gene(Genes.HuntWoundsPerHealth);

            // What this colony has learned a manhunter fight costs it. Run 161 met three and
            // went 1.50x, 1.69x, 1.90x — a lesson correctly drawn, recorded, and until now
            // heard only by the module that fights the revenge and never by the one that buys
            // it. DangerousPreyFloor stays as the prior for a colony that has met none.
            float revengeFloor = Learning.ThreatMemory.ForceFor(
                Learning.ThreatKind.Manhunter, HuntPolicy.DangerousPreyFloor);

            // Gather candidates first so they can be taken in order of danger rather than in
            // whatever order the map happens to list them.
            candidates.Clear();
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var animal = pawns[i];
                if (animal == null || animal.Dead || !animal.RaceProps.Animal) continue;
                if (animal.Faction != null) continue;
                if (animal.RaceProps.foodType == FoodTypeFlags.None) continue;
                if (map.designationManager.DesignationOn(animal, des) != null) continue;
                if ((animal.Position - origin).LengthHorizontalSquared > radiusSq) continue;
                candidates.Add(animal);
            }

            candidates.Sort((a, b) => ThreatOf(a).CompareTo(ThreatOf(b)));

            // Withdraw hunts the colony no longer wants.
            //
            // A designation outlives the reasoning that produced it. Colonists do not hunt the
            // animal the director chose — they hunt the nearest *designated* one, so a Megasloth
            // marked an hour ago while desperate keeps pulling hunters onto it long after the
            // larder filled and the same animal is being explicitly passed over in the log every
            // sweep. The director decides again every pass; the standing orders have to be
            // decided again with it.
            // What survives that cull is every hunt the colony still endorses at this strength
            // and this desperation — which is the only honest measure of how much food is
            // already on its way.
            int alreadyHunting = ReleaseUnwantedHunts(ctx, strength, desperation, radiusSq,
                                                     origin, woundsPerHealth, revengeFloor);

            taken.Clear();
            declined.Clear();
            largestDeclined = 0f;
            int done = 0;

            for (int i = 0; i < candidates.Count && done < budget; i++)
            {
                var animal = candidates[i];

                // Prey that fights back is held to a floor hunger cannot talk it out of, and
                // the fight it is held against is the one the whole session is buying — see
                // SessionCanAfford. Harmless prey adds no risk and so is never refused here,
                // which is the old ShouldEngage branch arriving at the same answer without a
                // branch to pick it.
                if (!SessionCanAfford(animal, strength, desperation, woundsPerHealth,
                                                    revengeFloor))
                {
                    Tally(declined, animal);
                    continue;
                }

                map.designationManager.AddDesignation(new Designation(animal, des));
                Tally(taken, animal);
                done++;
            }

            // Last resort. If the colony is out of food and every animal on the map is one it
            // would rather not fight, it fights anyway: refusing is not survival, it is just a
            // slower way to lose. The least dangerous target is taken, since that is the fight
            // most likely to be survived.
            //
            // But `done == 0` does not mean there is nothing safe to hunt. Candidates exclude
            // anything already designated, so once the safe prey is all marked — which is what
            // a working hunt module does within a couple of passes — the only animals left to
            // consider are the ones it deliberately refused, and nothing new can be taken no
            // matter how much food is on its way. The escalation then fires on the least
            // dangerous *undesignated* animal, which is precisely the most dangerous animal on
            // the map, for the reason that every safer one is already spoken for.
            //
            // That killed a colony inside six hours. Three passes in the same in-game hour: the
            // first marked a Red fox, the second marked a Rat, the third found nothing new to
            // mark and sent everyone after a Warg at 0.61x. An hour earlier the same reasoning
            // had bought a Megasloth at 0.49x. Neither died; both went manhunter and came home.
            // Three colonists became one, and a colony that had 11.6 days of meat lying in its
            // own fields by that afternoon never got to eat any of it.
            //
            // So the premise is checked rather than assumed. The conditions live in
            // <see cref="HuntPolicy"/> where they can be argued with in a test.
            if (HuntPolicy.LastResortWarranted(done, alreadyHunting, ctx.state.colonistsDowned,
                                               desperation, candidates.Count))
            {
                var lastResort = candidates[0];
                map.designationManager.AddDesignation(new Designation(lastResort, des));
                done++;
                Chronicle.Record(ChronicleCategory.Hunt, string.Format(
                    "LAST RESORT: nothing safe to hunt and {0:0.0} days of food left ({1:0.0} once " +
                    "the {2:0.0}-day butcher-and-cook lead is allowed for), so taking on {3} " +
                    "(threat {4:0}) with strength {5:0}",
                    daysOfFood, FoodTiming.EffectiveDays(daysOfFood), FoodTiming.SupplyLeadDays,
                    lastResort.LabelShortCap, ThreatOf(lastResort), strength));
                return done;
            }

            // One line per pass rather than one per animal. The same elephant gets refused on
            // every sweep, and a log that repeats itself is a log nobody can read.
            if (taken.Count > 0 || declined.Count > 0)
            {
                // When nothing new was taken, say what is already out. Otherwise a pass that
                // marked nothing reads identically whether the colony has eight hunts running
                // or none at all — and that is the distinction the last resort turns on.
                string inFlight = taken.Count == 0 && alreadyHunting > 0
                    ? "; " + alreadyHunting + " hunts already out"
                    : "";

                Chronicle.Record(ChronicleCategory.Hunt, string.Format(
                    "{0:0.0} days of food — hunting {1}{2}{3} [{4}]",
                    daysOfFood,
                    taken.Count > 0 ? Describe(taken) : "nothing",
                    inFlight,
                    declined.Count > 0 ? "; passed over " + Describe(declined) : "",
                    CombatAssessment.Explain(strength, largestDeclined, desperation)));
            }

            return done;
        }

        /// <summary>
        /// Cancels hunt designations the colony would not choose again now.
        ///
        /// Three reasons to withdraw one: the animal is no longer worth fighting at the current
        /// desperation, it has wandered outside the radius the colony is willing to walk, or it
        /// is dead and the designation is moot. Anything still worth hunting is left alone —
        /// re-issuing a designation restarts the job and the hunter never fires.
        ///
        /// Returns how many hunts are still standing afterwards, which is the colony's real
        /// answer to "is any food already coming".
        /// </summary>
        int ReleaseUnwantedHunts(DirectorContext ctx, float strength, float desperation,
                                 int radiusSq, IntVec3 origin, float woundsPerHealth,
                                 float revengeFloor)
        {
            var map = ctx.map;
            var des = DesignationDefOf.Hunt;

            // The session starts here, not in the take loop: hunts already standing are risk
            // the colony is already carrying, and anything taken this pass is added on top.
            sessionChances.Clear();
            sessionThreats.Clear();

            released.Clear();

            // Copied before iterating: removing a designation mutates the manager's own list.
            standing.Clear();
            foreach (var d in map.designationManager.SpawnedDesignationsOfDef(des)) standing.Add(d);

            int kept = 0;

            for (int i = standing.Count - 1; i >= 0; i--)
            {
                var designation = standing[i];
                var animal = designation.target.Thing as Pawn;
                if (animal == null) continue;

                bool gone = animal.Dead || !animal.Spawned;
                bool tooFar = !gone &&
                              (animal.Position - origin).LengthHorizontalSquared > radiusSq;
                // Judged cumulatively, exactly as the take loop judges: a standing order the
                // colony would not issue again *given the others it is already holding* is one
                // it should withdraw. Keeping this per-animal while the take loop counted the
                // set would have let five muffalo stand for ever, each individually defensible,
                // because nothing would ever be the one that broke the bar.
                bool notWorthIt = !gone && !tooFar &&
                                  !SessionCanAfford(animal, strength, desperation, woundsPerHealth,
                                                    revengeFloor);

                if (!gone && !tooFar && !notWorthIt) { kept++; continue; }

                map.designationManager.RemoveDesignation(designation);
                if (!gone) Tally(released, animal);
            }

            if (released.Count > 0)
                Chronicle.Record(ChronicleCategory.Hunt,
                    "called off the hunt on " + Describe(released) +
                    " — no longer worth taking, and a standing designation pulls hunters onto " +
                    "whichever marked animal is nearest rather than the one now chosen");

            return kept;
        }

        /// <summary>Days of unprocessed meat that count as enough already in hand.</summary>
        const float BacklogDays = 1.5f;

        bool notedBacklog;

        /// <summary>
        /// Nutrition lying on the map as fresh animal corpses nobody has butchered yet.
        ///
        /// Only fresh ones: a rotted corpse is not food and counting it would stop the colony
        /// hunting when it genuinely needs to. Only wild animals too — a dead colonist is not
        /// dinner, and the burial layer has its own opinion about those.
        /// </summary>

        readonly Dictionary<string, int> released = new Dictionary<string, int>();
        readonly List<Designation> standing = new List<Designation>();

        /// <summary>What this pass wanted, so "found none" reads differently from "needed none".</summary>
        readonly List<string> wanting = new List<string>();

        readonly List<Pawn> candidates = new List<Pawn>();
        readonly Dictionary<string, int> taken = new Dictionary<string, int>();
        readonly Dictionary<string, int> declined = new Dictionary<string, int>();
        float largestDeclined;

        static bool FightsBack(Pawn animal)
        {
            var race = animal.RaceProps;
            return race.predator || race.manhunterOnDamageChance > 0.05f;
        }

        /// <summary>
        /// The chance this hunt ends with the animal coming for the hunter.
        ///
        /// manhunterOnDamageChance is a chance per wound, not per hunt — the field says so and
        /// docs/rimworld/animals.md agrees, "the odds a wounded animal turns". Reading it as a
        /// per-hunt figure understates every large animal by however many shots it takes to put
        /// down, which for a muffalo at healthScale 1.75 against a rat's 0.29 is most of them.
        ///
        /// A predator is a certainty rather than a chance: it does not flee a hunter, and the
        /// existing FightsBack has always treated it as unconditional. Giving it one keeps every
        /// predator hunt judged exactly as it is today, so this change moves the herbivores it
        /// was wrong about and leaves the ones it was right about alone.
        /// </summary>
        static float RevengeChanceOf(Pawn animal, float woundsPerHealth)
        {
            var race = animal.RaceProps;
            if (race == null) return 0f;
            if (race.predator) return 1f;

            float perWound = race.manhunterOnDamageChance;
            if (perWound <= 0f) return 0f;

            return HuntRisk.RevengeChance(
                perWound, HuntRisk.WoundsToFell(race.baseHealthScale, woundsPerHealth));
        }

        /// <summary>The risk of every hunt this colony currently endorses, rebuilt each pass.</summary>
        readonly List<float> sessionChances = new List<float>();
        readonly List<float> sessionThreats = new List<float>();

        /// <summary>
        /// Whether the colony can still afford this hunt, given the ones it has already taken.
        ///
        /// The single change this whole class exists for. The bar is the same bar — colony
        /// strength against a fight, at the current hunger — but the fight it judges is the one
        /// the whole session is buying rather than the one animal under consideration. Five
        /// hunts that each turn half the time is not five separate coin flips the colony gets to
        /// win individually.
        ///
        /// Harmless prey adds nothing and so can never be refused by this, which is how deer and
        /// rats stay free to hunt in any number without a clause exempting them.
        /// </summary>
        bool SessionCanAfford(Pawn animal, float strength, float desperation, float woundsPerHealth,
                              float revengeFloor)
        {
            float chance = RevengeChanceOf(animal, woundsPerHealth);
            float threat = chance > 0f ? CombatAssessment.ThreatValue(animal) : 0f;

            // What killing it does to the map, which is certain rather than a roll.
            //
            // A boomalope's revenge chance is what this used to weigh, and its revenge is not
            // what kills anybody — the explosion on death is, and hunting it means causing that.
            // So the hazard enters at certainty, and an animal that carries one can never be
            // free however harmless its bite. Harmless prey has no such comp and is untouched.
            float blast = BlastHazardOf(animal, blastWeight);
            if (blast > 0f)
            {
                chance = 1f;
                threat += blast;
            }

            sessionChances.Add(chance);
            sessionThreats.Add(threat);

            float retaliation = HuntRisk.ExpectedRetaliation(sessionChances, sessionThreats);
            if (CombatAssessment.ShouldHuntDangerous(strength, retaliation, desperation,
                                                     revengeFloor)) return true;

            sessionChances.RemoveAt(sessionChances.Count - 1);
            sessionThreats.RemoveAt(sessionThreats.Count - 1);
            return false;
        }

        /// <summary>
        /// Read off the def rather than off a name. Anything the game gives an explosive comp is
        /// treated the same way, so a mod's exploding beast is priced without this code having
        /// heard of it — and a boomalope is not special-cased anywhere.
        /// </summary>
        static float BlastHazardOf(Pawn animal, float incendiaryWeight)
        {
            if (animal == null || animal.def == null) return 0f;

            var comp = animal.def.GetCompProperties<CompProperties_Explosive>();
            if (comp == null) return 0f;

            bool incendiary = comp.explosiveDamageType != null &&
                              comp.explosiveDamageType.defName.IndexOf(
                                  "Flame", System.StringComparison.OrdinalIgnoreCase) >= 0;

            return HuntRisk.BlastHazard(comp.explosiveRadius, incendiary, incendiaryWeight);
        }

        /// <summary>How much worse an incendiary blast is than a plain one. Set once a pass.</summary>
        float blastWeight = 1f;

        static float ThreatOf(Pawn animal)
        {
            return FightsBack(animal) ? CombatAssessment.ThreatValue(animal) : 0f;
        }


        void Tally(Dictionary<string, int> into, Pawn animal)
        {
            float power = animal.kindDef != null ? animal.kindDef.combatPower : 0f;
            if (into == declined && power > largestDeclined) largestDeclined = power;

            // combatPower is what decides; the measured value is what is under suspicion of
            // being the honest number. Printed together, for anything that could fight back, so
            // a run says which of the two matches what these fights actually cost. See
            // CombatAssessment.MeasuredAnimalValue.
            string key = animal.LabelShortCap + " (" + power.ToString("0");
            if (FightsBack(animal))
            {
                float measured = CombatAssessment.MeasuredAnimalValue(animal);
                key += ", measured " + measured.ToString("0");
            }
            key += ")";
            int n;
            into.TryGetValue(key, out n);
            into[key] = n + 1;
        }

        static string Describe(Dictionary<string, int> tally)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in tally)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Key);
                if (kv.Value > 1) sb.Append(" x").Append(kv.Value);
            }
            return sb.ToString();
        }


    }
}
