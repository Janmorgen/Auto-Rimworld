using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoColony.Modules
{
    /// <summary>
    /// Handles threats: builds static defenses in peacetime and takes manual control of
    /// colonists when a raid arrives.
    ///
    /// Two evolvable trade-offs live here. How much wealth to sink into turrets is a real
    /// cost — over-building starves the economy, under-building loses colonists — and how
    /// eagerly to draft matters because a drafted colonist does no work, so a jumpy strategy
    /// quietly bleeds productivity all year to survive a handful of raids.
    /// </summary>
    public class DefenseModule : DirectorModule
    {
        public override string Name { get { return "Defense"; } }

        // Checked often: a raid landing needs a response in game-minutes, not game-hours.
        public override int IntervalTicks { get { return 600; } }

        /// <summary>True once a response has begun, until the threat is genuinely over.</summary>
        bool engaged;

        /// <summary>
        /// Module passes since anything hostile was last within reach. At a 600-tick interval
        /// six passes is roughly an in-game hour and a half of quiet before standing down.
        /// </summary>
        const int HoldPassesAfterContact = 6;
        int passesSinceContact;

        readonly List<Pawn> drafted = new List<Pawn>();

        /// <summary>
        /// Fortification scans hundreds of cells for a legal turret spot, which is far too
        /// expensive to repeat at the module's combat-response cadence. Only every Nth quiet
        /// pass looks for somewhere to build.
        /// </summary>
        const int FortifyEveryNPasses = 16;
        int quietPasses;

        protected override void Act(DirectorContext ctx)
        {
            // Fire first, and before anything else. It spreads far faster than the work
            // scheduler reconsiders priorities, and an unattended fire will take a base apart
            // while the director is still deciding who should be hauling.
            if (ctx.state.firesNearBase > 0) HandleFires(ctx);
            else if (ctx.state.fires > 0) NoteDistantFire(ctx);

            if (ThreatActive(ctx))
            {
                HandleThreat(ctx);
                return;
            }

            StandDown(ctx);
            if (ctx.state.firesNearBase == 0 && firefightingUnderway)
            {
                firefightingUnderway = false;
                distantFireNoted = false;
                Chronicle.Record(ChronicleCategory.Fire, "fires near the colony are out");
            }

            if (++quietPasses >= FortifyEveryNPasses)
            {
                quietPasses = 0;
                MaintainDefenses(ctx);
            }
        }

        /// <summary>
        /// Whether to take manual control against hostiles on the map.
        ///
        /// A single raider registers as <see cref="StoryDanger.Low"/>, and this used to ignore
        /// Low outright at the default setting. That is how a lone raider walked into a colony,
        /// set it on fire and killed most of it while the director stood by: low danger to the
        /// storyteller is not low danger to a wooden base. Any hostile now draws a response
        /// unless the strategy has been tuned never to draft.
        /// </summary>
        bool ThreatActive(DirectorContext ctx)
        {
            if (ctx.state.hostilePawns <= 0) { engaged = false; return false; }

            float willingness = ctx.Gene(Genes.DefenseDraftDanger);
            if (willingness < 0.5f) { engaged = false; return false; }   // never takes control
            if (willingness >= 1.5f) { engaged = true; return true; }    // answers anything

            // Committing is stickier than starting. Proximity moves as the colonists themselves
            // move, so a response measured on it alone flipped on and off every hour: withdraw,
            // stand down, wander back out, withdraw again. Once engaged, hold until nothing
            // hostile is left anywhere near — a wider circle than the one that started it.
            //
            // The wider circle was not enough on its own. Raiders milling about its edge cross
            // it in both directions, and a colony facing fourteen of them drafted and stood down
            // seven times in seven hours — every stand-down sending colonists back out towards
            // the thing they had just withdrawn from. So the hold is also in time: contact has
            // to have been lost for a while, not merely at this instant.
            if (engaged)
            {
                if (HostilesWithin(ctx, 60, 45))
                {
                    passesSinceContact = 0;
                    return true;
                }
                if (++passesSinceContact < HoldPassesAfterContact) return true;
                engaged = false;
                return false;
            }

            passesSinceContact = 0;

            if (ctx.state.danger == StoryDanger.High || HostilesWithin(ctx, 45, 30))
            {
                engaged = true;
                return true;
            }
            return false;
        }

        /// <summary>Combined strength of everything hostile currently on the map.</summary>
        static float HostileStrength(DirectorContext ctx)
        {
            float total = 0f;
            var pawns = ctx.map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || p.Downed || !p.HostileTo(Faction.OfPlayer)) continue;
                total += CombatAssessment.ThreatValue(p);
            }
            return total;
        }

        /// <summary>
        /// Whether anything hostile is close enough to what the colony is protecting.
        ///
        /// The base is not the only thing worth protecting — the colonists are, and they do not
        /// stay in it. Measuring only from the base origin meant a hunter who met a raider out
        /// in the field was on her own: the director saw nothing near the base, drafted nobody,
        /// and the two colonists still at home carried on sowing while she was shot down. That
        /// is how a colony loses people one at a time to a threat it could have met together.
        /// </summary>
        static bool HostilesWithin(DirectorContext ctx, int ofBase, int ofColonist)
        {
            var origin = ctx.Origin;
            int baseSq = ofBase * ofBase;
            int colonistSq = ofColonist * ofColonist;

            var pawns = ctx.map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || p.Downed || !p.HostileTo(Faction.OfPlayer)) continue;
                if ((p.Position - origin).LengthHorizontalSquared <= baseSq) return true;

                var colonists = ctx.state.allColonists;
                for (int c = 0; c < colonists.Count; c++)
                {
                    var colonist = colonists[c];
                    if (colonist == null || !colonist.Spawned) continue;
                    if ((p.Position - colonist.Position).LengthHorizontalSquared <= colonistSq)
                        return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------ fire

        bool firefightingUnderway;

        /// <summary>
        /// Gets colonists onto a fire.
        ///
        /// Two things stop them by default. Colonists only fight fires inside the home area,
        /// so a fire started at the edge of a base — or by a raider outside it — burns
        /// unopposed; the home area is extended to cover it. And work priorities are only
        /// reconsidered every few in-game hours, which is far too slow, so the work module is
        /// pushed to re-run immediately rather than waiting for its turn.
        /// </summary>
        void HandleFires(DirectorContext ctx)
        {
            var map = ctx.map;
            var fireDef = AcDefs.Fire;
            if (fireDef == null) return;

            var home = map.areaManager.Home;
            var fires = map.listerThings.ThingsOfDef(fireDef);
            var origin = ctx.Origin;
            float radius = ctx.Gene(Genes.FireResponseRadius);
            float radiusSq = radius * radius;
            int claimed = 0;

            for (int i = 0; i < fires.Count && claimed < 200; i++)
            {
                var fire = fires[i];
                if (fire == null || !fire.Spawned) continue;

                // Only the fires that could actually reach the colony. Claiming a distant
                // wildfire sends colonists across the map to fight something that was never
                // coming, while whatever is burning at home goes unattended.
                bool inHome = home != null && home[fire.Position];
                if (!inHome && (fire.Position - origin).LengthHorizontalSquared > radiusSq) continue;

                // Claim the fire and a ring around it, so the whole burning front is inside
                // the area colonists are willing to work in.
                foreach (var cell in GenRadial.RadialCellsAround(fire.Position, 4f, true))
                {
                    if (!cell.InBounds(map)) continue;
                    if (home != null && !home[cell]) { home[cell] = true; claimed++; }
                }
            }

            if (!firefightingUnderway)
            {
                firefightingUnderway = true;
                ctx.director.ForceModuleDue("Work priorities");
                Note("fire detected — claimed " + claimed + " cells and re-prioritised work");
                Chronicle.Record(ChronicleCategory.Fire, string.Format(
                    "{0} fires burning; claimed {1} cells into the home area and forced a work re-prioritisation",
                    ctx.state.fires, claimed));
            }
        }

        bool distantFireNoted;

        /// <summary>Records a fire being deliberately left alone, so the log explains inaction.</summary>
        void NoteDistantFire(DirectorContext ctx)
        {
            if (distantFireNoted) return;
            distantFireNoted = true;
            Chronicle.Record(ChronicleCategory.Fire, string.Format(
                "{0} fires burning but none within {1:0} of the colony (nearest {2:0}) — leaving them",
                ctx.state.fires, ctx.Gene(Genes.FireResponseRadius), ctx.state.nearestFireDistance));
        }

        // ------------------------------------------------------------ combat

        void HandleThreat(DirectorContext ctx)
        {
            float strength = CombatAssessment.ColonyStrength(ctx.state);
            float threat = HostileStrength(ctx);

            // Whether this fight is worth having, rather than merely whether it is happening.
            //
            // The assessment used to be computed here purely to print it, and the colony charged
            // regardless. It said "strength 2 vs threat 50" in its own log three times running
            // while sending two broken colonists out to be downed, and they starved where they
            // fell because nobody was left standing to carry food to them. Answering a raid is
            // not optional; meeting it in the open is.
            // Best fighters first. A raid is not elective — it is happening regardless of
            // whether anyone suitable exists — so the question is who goes, not whether.
            var fighters = CombatAssessment.RankFighters(ctx.state.ableColonists);

            // What losing this fight would cost, alongside how likely losing it is. With most of
            // the colony already on the floor, the few still upright are the only thing standing
            // between it and nobody left to tend or feed anyone, so they hold cover on odds they
            // would have met in the open at full strength.
            float caution = CasualtyPolicy.EngagementCaution(fighters.Count, ctx.state.colonistsDowned);
            float required = ctx.Gene(Genes.DefenseEngageRatio) * caution;

            bool winnable = threat <= 0f || strength / threat >= required;

            var rally = winnable ? RallyPoint(ctx) : Refuge(ctx);
            float retreatAt = ctx.Gene(Genes.DefenseRetreatHealth);
            int mobilised = 0;

            // Somebody has to still be standing afterwards to tend whoever is not.
            var medic = ChooseReservedMedic(ctx, fighters);

            for (int i = 0; i < fighters.Count; i++)
            {
                var pawn = fighters[i];
                if (pawn.drafter == null) continue;

                if (pawn == medic)
                {
                    // Released rather than merely not drafted: they may have been in the line
                    // when the casualty happened, and work priorities already put Doctor at the
                    // top the moment anyone went down, so letting go of them is the whole order.
                    if (pawn.drafter.Drafted) pawn.drafter.Drafted = false;
                    drafted.Remove(pawn);
                    continue;
                }

                float health = pawn.health != null && pawn.health.summaryHealth != null
                    ? pawn.health.summaryHealth.SummaryHealthPercent
                    : 1f;

                // Too hurt to fight: release them so they seek treatment instead.
                if (health < retreatAt)
                {
                    if (pawn.drafter.Drafted) pawn.drafter.Drafted = false;
                    drafted.Remove(pawn);
                    continue;
                }

                if (!pawn.drafter.Drafted)
                {
                    pawn.drafter.Drafted = true;
                    if (!drafted.Contains(pawn)) drafted.Add(pawn);
                    mobilised++;
                }

                SendToRally(pawn, rally, ctx.map);

                // Drafting is not fighting. A drafted colonist with no orders stands where it
                // was put and shoots only what happens to walk into its line of sight — which
                // is why two were mobilised against one raider and only one ever engaged.
                if (winnable) Engage(ctx, pawn);
            }

            if (mobilised > 0)
            {
                Note((winnable ? "drafted " : "withdrew ") + mobilised + " colonists");

                // The numbers the decision was actually taken on. This used to print the
                // desperation-scaled ratio, which is a different rule from the one applied and
                // read as an explanation of a choice it had not made.
                Chronicle.Record(ChronicleCategory.Threat, string.Format(
                    "{0} hostiles (danger {1}); {2} {3} to {4} — strength {5:0} vs threat {6:0} " +
                    "({7:0.00}x), needed {8:0.00}x{9}{10}",
                    ctx.state.hostilePawns, ctx.state.danger,
                    winnable ? "engaging with" : "WITHDRAWING",
                    mobilised, rally, strength, threat,
                    threat > 0f ? strength / threat : 999f, required,
                    caution > 1f
                        ? " (" + ctx.state.colonistsDowned + " already down, so the bar is " +
                          caution.ToString("0.0") + "x higher)"
                        : "",
                    winnable ? "" : " — not worth meeting in the open, holding the base instead"));
            }
        }

        /// <summary>Who was last held out of the fighting, so it is only reported when it changes.</summary>
        Pawn reservedMedic;

        /// <summary>
        /// Picks the colonist to keep out of the fighting while somebody is down.
        ///
        /// The best medic, not the worst fighter: whoever is held back is going to be doing
        /// medicine, and a colonist with no skill at it will lose the patient anyway. Combat
        /// value breaks ties, so of two equally capable doctors the one the line can better
        /// spare stays behind.
        /// </summary>
        Pawn ChooseReservedMedic(DirectorContext ctx, List<Pawn> fighters)
        {
            if (!CasualtyPolicy.ShouldReserveMedic(fighters.Count, ctx.state.colonistsDowned))
            {
                if (reservedMedic != null)
                {
                    Chronicle.Record(ChronicleCategory.Health,
                        "nobody down any more; " + reservedMedic.LabelShortCap + " rejoins the line");
                    reservedMedic = null;
                }
                return null;
            }

            Pawn best = null;
            int bestSkill = -1;
            float bestValue = float.MaxValue;

            for (int i = 0; i < fighters.Count; i++)
            {
                var pawn = fighters[i];
                if (pawn == null || pawn.drafter == null) continue;
                if (pawn.WorkTagIsDisabled(WorkTags.Caring)) continue;

                int skill = CombatAssessment.SkillLevel(pawn, SkillDefOf.Medicine);
                float value = CombatAssessment.ColonistValue(pawn);

                if (skill > bestSkill || (skill == bestSkill && value < bestValue))
                {
                    best = pawn;
                    bestSkill = skill;
                    bestValue = value;
                }
            }

            if (best != null && best != reservedMedic)
            {
                reservedMedic = best;
                Chronicle.Record(ChronicleCategory.Health, string.Format(
                    "{0} colonists down — holding {1} back from the fight to tend them " +
                    "(medicine {2}, leaving {3} in the line)",
                    ctx.state.colonistsDowned, best.LabelShortCap, bestSkill, fighters.Count - 1));
            }

            return best;
        }

        /// <summary>
        /// Points a colonist at something and tells them to shoot it.
        ///
        /// Melee chases; ranged fires from where it stands, so anyone out of range is walked
        /// closer first rather than left holding a rally point they cannot shoot from.
        /// </summary>
        static void Engage(DirectorContext ctx, Pawn pawn)
        {
            var target = NearestHostile(ctx, pawn);
            if (target == null) return;

            // Leave them alone if they are already on this target; re-issuing every pass would
            // restart the attack and they would never actually loose a shot.
            if (pawn.CurJob != null && pawn.CurJob.targetA.Thing == target &&
                (pawn.CurJobDef == JobDefOf.AttackMelee || pawn.CurJobDef == JobDefOf.AttackStatic))
                return;

            var verb = pawn.CurrentEffectiveVerb;
            bool melee = verb == null || verb.verbProps == null || verb.verbProps.IsMeleeAttack;

            Job job;
            if (melee)
            {
                job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            }
            else if (verb.CanHitTarget(target))
            {
                job = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            }
            else
            {
                // Out of range: close the distance instead of standing there aiming at nothing.
                var approach = CellFinder.RandomClosewalkCellNear(target.Position, ctx.map, 6);
                if (!approach.IsValid) return;
                job = JobMaker.MakeJob(JobDefOf.Goto, approach);
            }

            job.playerForced = true;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        static Pawn NearestHostile(DirectorContext ctx, Pawn to)
        {
            Pawn best = null;
            float bestDist = float.MaxValue;

            var pawns = ctx.map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || p.Downed) continue;
                if (!p.HostileTo(Faction.OfPlayer)) continue;

                float dist = (p.Position - to.Position).LengthHorizontalSquared;
                if (dist < bestDist) { bestDist = dist; best = p; }
            }
            return best;
        }

        /// <summary>
        /// Somewhere to hold when the fight cannot be won in the open: the enclosed room
        /// furthest from whatever is coming.
        ///
        /// Withdrawing is not standing down. Everyone stays drafted, so nobody wanders back out
        /// to haul something, and raiders have to come through a doorway to reach them — which
        /// is a far better fight than the one outside.
        /// </summary>
        static IntVec3 Refuge(DirectorContext ctx)
        {
            if (ctx.layout == null || ctx.layout.rooms.Count == 0) return RallyPoint(ctx);

            var threatAt = NearestHostileCell(ctx);
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < ctx.layout.rooms.Count; i++)
            {
                var room = ctx.layout.rooms[i];
                var centre = room.Center;
                if (!centre.InBounds(ctx.map)) continue;
                if (ctx.map.roofGrid == null || !ctx.map.roofGrid.Roofed(centre)) continue;

                float score = threatAt.IsValid
                    ? (centre - threatAt).LengthHorizontalSquared
                    : 0f;
                if (score > bestScore) { bestScore = score; best = centre; }
            }

            return best.IsValid ? best : RallyPoint(ctx);
        }

        static IntVec3 NearestHostileCell(DirectorContext ctx)
        {
            var origin = ctx.Origin;

            IntVec3 nearest = IntVec3.Invalid;
            float bestDist = float.MaxValue;

            var pawns = ctx.map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || p.Downed || !p.HostileTo(Faction.OfPlayer)) continue;

                float dist = (p.Position - origin).LengthHorizontalSquared;
                if (dist < bestDist) { bestDist = dist; nearest = p.Position; }
            }
            return nearest;
        }

        static void SendToRally(Pawn pawn, IntVec3 rally, Map map)
        {
            if (!rally.IsValid) return;
            if (pawn.CurJobDef == JobDefOf.Goto) return;
            // Already close enough to be useful; let the pawn pick its own targets.
            if ((pawn.Position - rally).LengthHorizontalSquared <= 36) return;

            var cell = CellFinder.RandomClosewalkCellNear(rally, map, 3);
            if (!cell.IsValid || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return;

            var job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        void StandDown(DirectorContext ctx)
        {
            reservedMedic = null;
            if (drafted.Count == 0) return;

            for (int i = 0; i < drafted.Count; i++)
            {
                var pawn = drafted[i];
                if (pawn == null || pawn.Dead || pawn.drafter == null) continue;
                if (pawn.drafter.Drafted) pawn.drafter.Drafted = false;
            }

            Note("stood down " + drafted.Count + " colonists");
            Chronicle.Record(ChronicleCategory.Threat, "threat over; stood down " + drafted.Count + " colonists");
            drafted.Clear();
        }

        /// <summary>Colonists fall back to the base entrance rather than meeting raiders in the open.</summary>
        static IntVec3 RallyPoint(DirectorContext ctx)
        {
            if (ctx.layout != null && ctx.layout.established) return ctx.layout.origin;
            return ctx.map.Center;
        }

        // ------------------------------------------------------------ fortification

        void MaintainDefenses(DirectorContext ctx)
        {
            var turretDef = AcDefs.TurretMini;
            if (turretDef == null) return;

            // Threat scales with wealth, so the defense budget does too.
            float budgetFraction = ctx.Gene(Genes.DefenseWealthRatio);
            int wanted = ctx.GeneInt(Genes.DefenseTurretCount);
            int affordable = (int)(ctx.state.wealthTotal * budgetFraction / 300f);
            if (wanted > affordable) wanted = affordable;
            if (wanted <= ctx.state.poweredTurrets) return;

            if (!CanAfford(ctx, turretDef)) return;

            var origin = ctx.Origin;
            var stuff = PlacementUtil.ChooseStuff(ctx.map, turretDef,
                FireRisk.StonePreference(ctx, FireRisk.Assess(ctx.map, ctx.state)));

            foreach (var cell in GenRadial.RadialCellsAround(origin, 14, true))
            {
                if ((cell - origin).LengthHorizontalSquared < 25) continue;   // keep a standoff distance
                if (!cell.InBounds(ctx.map)) continue;

                if (PlacementUtil.TryPlace(ctx.map, turretDef, cell, Rot4.North, stuff))
                {
                    PlacementUtil.MarkHome(ctx.map, cell);
                    Note("queued a turret at " + cell);
                    return;
                }
            }
        }

        static bool CanAfford(DirectorContext ctx, ThingDef def)
        {
            var costs = def.CostList;
            if (costs == null) return true;

            for (int i = 0; i < costs.Count; i++)
            {
                var need = costs[i];
                if (need.thingDef == null) continue;
                if (ctx.map.resourceCounter.GetCount(need.thingDef) < need.count * 2) return false;
            }
            return true;
        }
    }
}
