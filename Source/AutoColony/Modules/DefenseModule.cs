using System;
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

        /// <summary>Said once per hunt rather than every pass.</summary>
        bool predatorNoted;

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
            //
            // A fire is met where it is rather than where the colony is: by the time a front
            // crosses the response radius it is no longer the fire that could have been put out.
            bool closing = TrackFireFront(ctx);
            if (ctx.state.firesNearBase > 0 || closing) HandleFires(ctx, closing);
            else if (ctx.state.fires > 0) NoteDistantFire(ctx);

            // People before property, and before the fire arrives rather than after.
            if (ctx.state.fires > 0 && ctx.state.colonistsDowned > 0) EvacuateCasualties(ctx);

            // And whether or not anything is burning.
            if (ctx.state.colonistsDowned > 0) CarryTheFallenToBed(ctx);
            else ForgetTheFallen();

            if (ThreatActive(ctx))
            {
                HandleThreat(ctx);
                return;
            }

            StandDown(ctx);

            // Fighting a front that has not arrived yet is still firefighting. Standing down on
            // `firesNearBase` alone would call it off on the very pass it was ordered, since a
            // front the colony went out to meet is by definition not near the base.
            if (ctx.state.firesNearBase == 0 && !closing && firefightingUnderway)
            {
                firefightingUnderway = false;
                distantFireNoted = false;
                hotRoomNoted = false;
                beyondReachNoted = false;
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
            // A predator stalking a colonist is a threat with a name on it.
            //
            // It is not hostile to the player faction, so it never reached hostilePawns and this
            // method returned false while a lynx ran a colonist down. Two colonies lost somebody
            // that way in one night, both with no THREAT line in the record for days around the
            // death — the director was not deciding badly, it could not see the animal.
            //
            // Answered before the strength arithmetic below, because that compares faction raid
            // points and a lone animal scores near zero on it — which would read as "not worth
            // meeting" for the exact case where meeting it is the whole job.
            if (ctx.state.predatorsHunting > 0)
            {
                if (!predatorNoted)
                {
                    predatorNoted = true;
                    var prey = ctx.state.huntedColonists;
                    Chronicle.Record(ChronicleCategory.Threat, string.Format(
                        "{0} predator(s) hunting {1} — drafting, because a stalked colonist alone " +
                        "is how this colony has lost people without a single threat line in the log",
                        ctx.state.predatorsHunting,
                        prey != null && prey.Count > 0 ? prey[0].LabelShortCap : "somebody"));
                }
                engaged = true;
                return true;
            }
            predatorNoted = false;

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

        /// <summary>
        /// Whether the hostiles on the map are besieging rather than assaulting.
        ///
        /// Read off the lord that is actually directing them rather than guessed from what they
        /// are carrying, because a siege is defined by its intent: they will sit at range, build
        /// mortars, and be resupplied for days. Every other raid archetype eventually walks into
        /// the base, which is what makes a doorway worth holding against them and not against
        /// this one.
        /// </summary>
        /// <summary>
        /// What kind of fight this is, because they do not cost the same and the colony learns
        /// them separately.
        ///
        /// Read off what the attackers are and what they are doing rather than off a points
        /// number: a siege is defined by its lord job, a predator by the job it is running, a
        /// manhunter pack by being animals that are hostile. The points say how big; this says
        /// what, and the two are different questions.
        /// </summary>
        static Learning.ThreatKind KindOfThreat(DirectorContext ctx)
        {
            if (ctx.state.predatorsHunting > 0) return Learning.ThreatKind.Predator;
            if (Besieged(ctx)) return Learning.ThreatKind.Siege;

            try
            {
                bool anyAnimal = false, anyHumanlike = false, anyInsect = false;
                var pawns = ctx.map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (p == null || p.Downed || !p.HostileTo(Faction.OfPlayer)) continue;
                    if (p.RaceProps == null) continue;

                    if (p.RaceProps.Insect) anyInsect = true;
                    else if (p.RaceProps.Humanlike) anyHumanlike = true;
                    else if (p.RaceProps.Animal) anyAnimal = true;
                }

                if (anyInsect) return Learning.ThreatKind.Infestation;
                if (anyHumanlike) return Learning.ThreatKind.Raid;
                if (anyAnimal) return Learning.ThreatKind.Manhunter;
            }
            catch (Exception) { }

            return Learning.ThreatKind.Other;
        }

        static bool Besieged(DirectorContext ctx)
        {
            try
            {
                var lords = ctx.map.lordManager != null ? ctx.map.lordManager.lords : null;
                if (lords == null) return false;

                for (int i = 0; i < lords.Count; i++)
                {
                    var lord = lords[i];
                    if (lord == null || lord.faction == null) continue;
                    if (!lord.faction.HostileTo(Faction.OfPlayer)) continue;
                    if (lord.LordJob is LordJob_Siege) return true;
                }
            }
            catch (Exception) { }
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
        int lastFireCount = -1;
        float lastNearestFire = -1f;

        /// <summary>
        /// Samples the fire front and says whether to go out and meet it.
        ///
        /// Two samples are what separates a front that is coming from one that never was; the
        /// judgement itself is <see cref="FireFront"/>, this only remembers the previous one.
        /// </summary>
        bool TrackFireFront(DirectorContext ctx)
        {
            int count = ctx.state.fires;
            float nearest = ctx.state.nearestFireDistance;

            int previousCount = lastFireCount;
            float previousNearest = lastNearestFire;

            lastFireCount = count;
            lastNearestFire = nearest;

            if (count == 0)
            {
                frontNoted = false;
                beyondReachNoted = false;
                committedToFront = false;
                return false;
            }

            // Hands free, not hands able. Drafting is this module's own doing, and reading the
            // able count here counted the people it had just sent to the line as available to
            // fight a fire — see ColonyState.colonistsFreeForWork for the whole account.
            int hands = ctx.state.colonistsFreeForWork;
            int perColonist = ctx.GeneInt(Genes.DefenseFiresPerColonist);

            // Having gone out to a fire, stay at it until it is out.
            //
            // Growth is what identifies a front worth meeting, and growth is bursty — a front
            // goes 0 to 2, then 2 to 2, then 2 to 3. Deciding afresh every pass therefore
            // disengages on the flat samples, and the first version of this did exactly that:
            // three contradictory lines in one in-game hour, "going out to meet it", "claimed 60
            // cells", then "leaving them" and "fires near the colony are out" while three cells
            // were still burning. Work priorities went back to normal in the middle of the fire.
            //
            // Growth answers whether to start. It is the wrong question for whether to stop, and
            // the answers to stopping are that the fire is out or that it has outgrown the people
            // fighting it.
            if (committedToFront)
            {
                if (FireFront.Fightable(count, hands, perColonist)) return true;

                committedToFront = false;
                NoteFrontBeyondReach(ctx, count, hands);
                return false;
            }

            bool closing = FireFront.IsClosing(count, previousCount, nearest,
                                              previousNearest, hands, perColonist);

            if (closing)
            {
                committedToFront = true;
                NoteClosingFront(ctx, count, previousCount, nearest);
            }
            else if (previousCount >= 0 && count > previousCount &&
                     !FireFront.Fightable(count, hands, perColonist))
                NoteFrontBeyondReach(ctx, count, hands);

            return closing;
        }

        /// <summary>True while the colony is working a fire it went out to meet.</summary>
        bool committedToFront;

        bool frontNoted;

        void NoteClosingFront(DirectorContext ctx, int count, int previous, float nearest)
        {
            if (frontNoted) return;
            frontNoted = true;
            Chronicle.Record(ChronicleCategory.Fire, string.Format(
                "fire front is spreading ({0} up from {1}, nearest {2:0}) — going out to meet it " +
                "now rather than waiting for it to reach the response radius",
                count, previous, nearest));
        }

        bool beyondReachNoted;

        void NoteFrontBeyondReach(DirectorContext ctx, int count, int able)
        {
            if (beyondReachNoted) return;
            beyondReachNoted = true;
            Chronicle.Record(ChronicleCategory.Fire, string.Format(
                "{0} fires burning and {1} able colonists — past what they could beat out, so " +
                "nobody is sent into it",
                count, able));
        }

        readonly HashSet<int> evacuating = new HashSet<int>();

        /// <summary>When each colonist was first seen on the floor, by thing ID.</summary>
        readonly Dictionary<int, int> downSince = new Dictionary<int, int>();

        /// <summary>
        /// How long somebody may lie there before the colony is told to go and get them.
        ///
        /// Deliberately not immediate. Rescuing is part of the Doctor work type and colonists do
        /// it unprompted, so forcing a job the moment anyone falls would fight the game's own
        /// scheduler for no gain and take a doctor off a patient to fetch another. An hour is
        /// long enough that if nobody has come, nobody is coming.
        /// </summary>
        const int RescueGraceTicks = 2500;

        /// <summary>
        /// Whether this casualty is losing blood, which is the difference between a colonist
        /// who can wait for the colony to notice and one who cannot.
        /// </summary>
        static bool Bleeding(Pawn pawn)
        {
            try
            {
                return pawn.health != null && pawn.health.hediffSet != null &&
                       pawn.health.hediffSet.BleedRateTotal > 0.01f;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Nobody is down; drop everything remembered about who was.</summary>
        void ForgetTheFallen()
        {
            if (downSince.Count > 0) downSince.Clear();
            if (evacuating.Count > 0) evacuating.Clear();
        }

        /// <summary>
        /// Carries a colonist who has been lying on the floor to a bed, fire or no fire.
        ///
        /// Evacuation only ever ran when something was burning — the call site required it and
        /// the method required it again — so a colonist downed by a raid, an animal or a fall
        /// was never carried anywhere by the director.
        ///
        /// It is a genuine backstop and nothing more, which is worth stating because the first
        /// version of this comment claimed otherwise. Run 38 lost four colonists who were downed
        /// and then died rather than killed outright, with five days of food they could not walk
        /// to, and the missing evacuation looked like the cause. It was not. The `casualty`
        /// scenario put a colonist on the floor with nothing burning and the colony handled it
        /// unprompted and quickly: one colonist tended the casualty where they lay within
        /// minutes, another carried them to a bed, and this code did not fire once because it
        /// was never needed. Rescuing and tending are Doctor work and the game schedules both.
        ///
        /// What killed run 38 was the cascade rather than the rescue: three colonists, a raid,
        /// people going down faster than they could be recovered, ending at one alive and that
        /// one down. Once the last able colonist falls there is nobody to tend anybody, and no
        /// amount of rescue logic reaches that state.
        ///
        /// So this earns its place only where the game's own scheduler does not act — everybody
        /// drafted through a long fight, or a doctor who cannot path — and it waits an hour
        /// before deciding that has happened. Acting sooner would take a doctor off a patient to
        /// fetch another and fight the scheduler for no gain.
        /// </summary>
        void CarryTheFallenToBed(DirectorContext ctx)
        {
            int now = Find.TickManager.TicksGame;
            var colonists = ctx.map.mapPawns.FreeColonistsSpawned;

            // Anyone back on their feet or already in a bed is no longer a casualty, and has to
            // be forgotten or they can never be rescued a second time. `evacuating` was only
            // ever added to, so one rescue per colonist per colony was the standing limit.
            for (int i = 0; i < colonists.Count; i++)
            {
                var pawn = colonists[i];
                if (pawn == null) continue;
                if (pawn.Downed && !pawn.InBed()) continue;

                downSince.Remove(pawn.thingIDNumber);
                evacuating.Remove(pawn.thingIDNumber);
            }

            for (int i = 0; i < colonists.Count; i++)
            {
                var victim = colonists[i];
                if (victim == null || victim.Dead || !victim.Downed) continue;
                if (victim.InBed()) continue;
                if (evacuating.Contains(victim.thingIDNumber)) continue;

                int since;
                if (!downSince.TryGetValue(victim.thingIDNumber, out since))
                {
                    downSince[victim.thingIDNumber] = now;
                    if (!Bleeding(victim)) continue;      // bleeding cannot afford even one pass
                }

                // An hour is the wrong wait for somebody bleeding out.
                //
                // The grace exists so this does not fight the game's own scheduler in the
                // ordinary case, and an hour is a fair reading of "if nobody has come, nobody is
                // coming". It is the wrong reading when the casualty has a clock: run 72 lost
                // Aly to "Blood loss (extreme)" on day one, downed and dead inside the hour this
                // was still waiting out, with the backstop never firing once.
                //
                // So bleeding skips the wait entirely. Everything else keeps it.
                if (!Bleeding(victim) && now - since < RescueGraceTicks) continue;

                var carrier = NearestCarrier(ctx, victim);
                if (carrier == null) continue;

                Building_Bed bed = null;
                try { bed = RestUtility.FindBedFor(victim, carrier, false, false, null); }
                catch (Exception) { }
                if (bed == null) { NoteNowhereSafe(ctx, victim, NoFires); continue; }

                var job = JobMaker.MakeJob(JobDefOf.Rescue, victim, bed);
                job.count = 1;
                if (!carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc)) continue;

                evacuating.Add(victim.thingIDNumber);
                Chronicle.Record(ChronicleCategory.Health, string.Format(
                    "{0} is {1} and nobody has come — {2} is carrying them to a bed. Lying there " +
                    "they cannot eat, and being tended is what a bed is for",
                    victim.LabelShortCap,
                    Bleeding(victim) ? "on the floor and bleeding" : "has been on the floor for an hour",
                    carrier.LabelShortCap));
                Note("carried " + victim.LabelShortCap + " to a bed");
                return;
            }
        }

        /// <summary>
        /// Carries a colonist who cannot walk out of a fire's way before it reaches them.
        ///
        /// Three colonies in this session burned around their own casualties, and the question
        /// that blocked a fix for all of them was whether a rescue would path a carrier through
        /// flame. Moving people *before* the fire arrives never asks it: at twelve cells there is
        /// no fire between anybody and anybody, and the whole problem was waiting until there was.
        ///
        /// The colony's own doctors would do this unprompted, given a free bed and the Doctor
        /// priority the work module already raises. What they will not do is choose *which* bed —
        /// the game's own rescue takes the nearest, and the nearest bed to a colonist lying in a
        /// burning room is generally in the burning room. So the bed is chosen here and the job
        /// is ordered, which is the one thing that makes this worth overriding work priorities
        /// for: an ordered job that saves someone beats a chosen job that reaches them later.
        ///
        /// Only for people actually in danger, and only once each. Re-issuing an ordered job
        /// every pass would restart the carry and the carrier would never arrive — the same
        /// mistake that made hunters chase a new animal every sweep.
        /// </summary>
        void EvacuateCasualties(DirectorContext ctx)
        {
            var map = ctx.map;
            var fireDef = AcDefs.Fire;
            if (fireDef == null) return;

            var fires = map.listerThings.ThingsOfDef(fireDef);
            if (fires.Count == 0) return;

            var colonists = map.mapPawns.FreeColonistsSpawned;

            for (int i = 0; i < colonists.Count; i++)
            {
                var victim = colonists[i];
                if (victim == null || victim.Dead || !victim.Downed) continue;
                if (victim.InBed()) continue;                       // already off the floor
                if (evacuating.Contains(victim.thingIDNumber)) continue;
                if (!FireIsComingFor(fires, victim.Position)) continue;

                var carrier = NearestCarrier(ctx, victim);
                if (carrier == null) continue;

                var bed = SafestBedFor(ctx, victim, carrier, fires);
                if (bed == null)
                {
                    NoteNowhereSafe(ctx, victim, fires);
                    continue;
                }

                var job = JobMaker.MakeJob(JobDefOf.Rescue, victim, bed);
                job.count = 1;
                if (!carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc)) continue;

                evacuating.Add(victim.thingIDNumber);
                Chronicle.Record(ChronicleCategory.Fire, string.Format(
                    "{0} is down with fire {1:0} cells away — {2} is carrying them to a bed clear " +
                    "of it now, rather than waiting to see whether it comes this way",
                    victim.LabelShortCap, NearestFireDistance(fires, victim.Position),
                    carrier.LabelShortCap));
                Note("evacuated " + victim.LabelShortCap + " ahead of the fire");
                return;
            }
        }

        /// <summary>
        /// The closest colonist who could carry them, ignoring how busy they are.
        ///
        /// Deliberately not filtered on Caring the way a capture is: hauling somebody out of a
        /// fire is not medicine, and a colonist who cannot treat a wound can still pick a person
        /// up. Drafted colonists are left alone — they are in a fight, and taking them out of it
        /// to fetch someone tends to produce a second casualty.
        /// </summary>
        static Pawn NearestCarrier(DirectorContext ctx, Pawn victim)
        {
            return NearestCarrier(ctx, victim, false);
        }

        /// <summary>
        /// Somebody to carry the casualty, optionally including the drafted.
        ///
        /// The drafted are excluded by default and rightly so: a work job handed to a drafted
        /// pawn breaks the draft, and a draft broken mid-fight is how a line collapses. But a
        /// withdrawal is not a fight — see RetreatCargo — and during one every colonist is
        /// drafted, so the default makes rescue impossible at precisely the moment three
        /// colonists have now been lost to its absence.
        ///
        /// When the drafted are allowed, the choice is scored rather than taken by distance
        /// alone, because how long the walk takes is what the casualty is actually waiting on.
        /// </summary>
        static Pawn NearestCarrier(DirectorContext ctx, Pawn victim, bool allowDrafted)
        {
            Pawn best = null;
            float bestFitness = 0f;
            float bestDist = float.MaxValue;

            var able = ctx.state.ableColonists;
            for (int i = 0; i < able.Count; i++)
            {
                var pawn = able[i];
                if (pawn == null || pawn == victim) continue;
                if (pawn.Drafted && !allowDrafted) continue;
                if (!pawn.CanReach(victim, PathEndMode.OnCell, Danger.Deadly)) continue;

                if (!allowDrafted)
                {
                    float d = (pawn.Position - victim.Position).LengthHorizontalSquared;
                    if (d < bestDist) { bestDist = d; best = pawn; }
                    continue;
                }

                float distance = (pawn.Position - victim.Position).LengthHorizontal;
                float speed = CombatAssessment.SafeStat(pawn, StatDefOf.MoveSpeed, 4.6f);
                float fitness = RetreatCargo.CarrierFitness(
                    distance, CombatAssessment.ColonistValue(pawn), speed);

                if (fitness > bestFitness) { bestFitness = fitness; best = pawn; }
            }
            return best;
        }

        /// <summary>
        /// Send a retreating colonist to carry a casualty out, rather than past.
        ///
        /// Only while withdrawing. RetreatCargo.WorthCarrying says why that case is easy: the
        /// line is already being given up, so the fighter spent on the carry gives up nothing
        /// that was in use. Undrafting is the whole order — a drafted pawn will not take the
        /// rescue job, which is the reason none of the three lost colonists was ever collected.
        /// </summary>
        void CarryTheFallen(DirectorContext ctx)
        {
            var colonists = ctx.state.allColonists;
            if (colonists == null) return;

            for (int i = 0; i < colonists.Count; i++)
            {
                var victim = colonists[i];
                if (victim == null || victim.Dead || !victim.Downed) continue;
                if (victim.InBed()) continue;
                if (evacuating.Contains(victim.thingIDNumber)) continue;

                var carrier = NearestCarrier(ctx, victim, true);
                if (carrier == null) continue;

                if (!RetreatCargo.WorthCarrying(true, CombatAssessment.ColonistValue(carrier), 0f))
                    continue;

                Building_Bed bed = null;
                try { bed = RestUtility.FindBedFor(victim, carrier, false, false, null); }
                catch (Exception) { }
                if (bed == null) { NoteNowhereSafe(ctx, victim, NoFires); continue; }

                // The draft has to be released or the job is refused, and releasing it is the
                // point: this colonist's fight is over and their job now is the carry.
                if (carrier.drafter != null && carrier.drafter.Drafted)
                    carrier.drafter.Drafted = false;
                drafted.Remove(carrier);

                var job = JobMaker.MakeJob(JobDefOf.Rescue, victim, bed);
                job.count = 1;
                if (!carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc)) continue;

                evacuating.Add(victim.thingIDNumber);
                Chronicle.Record(ChronicleCategory.Health, string.Format(
                    "withdrawing, and {0} is down where they fell — {1} is carrying them out " +
                    "rather than past. The line is already being given up, so the fighter spent " +
                    "on this gives up nothing; three colonists have been taken or lost lying "  +
                    "where a retreat walked by them",
                    victim.LabelShortCap, carrier.LabelShortCap));
                Note("carried " + victim.LabelShortCap + " out of a withdrawal");
                return;
            }
        }

        static bool FireIsComingFor(List<Thing> fires, IntVec3 cell)
        {
            return FireFront.Threatens(NearestFireDistance(fires, cell));
        }

        static float NearestFireDistance(List<Thing> fires, IntVec3 cell)
        {
            float nearest = -1f;
            for (int i = 0; i < fires.Count; i++)
            {
                var fire = fires[i];
                if (fire == null || !fire.Spawned) continue;

                float dist = AcMath.Sqrt((fire.Position - cell).LengthHorizontalSquared);
                if (nearest < 0f || dist < nearest) nearest = dist;
            }
            return nearest;
        }

        /// <summary>
        /// A bed the fire is not going to reach, asked for on the carrier's behalf.
        ///
        /// The game's own answer is tried first, because it knows about ownership, reservations
        /// and reachability and this does not. It is only overruled when what it returns is
        /// standing in the fire.
        /// </summary>
        static Building_Bed SafestBedFor(DirectorContext ctx, Pawn victim, Pawn carrier,
                                         List<Thing> fires)
        {
            Building_Bed chosen = null;
            try { chosen = RestUtility.FindBedFor(victim, carrier, false, false, null); }
            catch (Exception) { }

            if (chosen != null && !FireIsComingFor(fires, chosen.Position)) return chosen;

            foreach (var bed in ctx.map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == null || !bed.Spawned || !bed.ForColonists) continue;
                if (FireIsComingFor(fires, bed.Position)) continue;
                if (!carrier.CanReach(bed, PathEndMode.OnCell, Danger.Deadly)) continue;

                bool taken = false;
                try
                {
                    foreach (var sleeper in bed.CurOccupants)
                    {
                        if (sleeper != null && sleeper != victim) { taken = true; break; }
                    }
                }
                catch (Exception) { }
                if (taken) continue;

                return bed;
            }
            return chosen != null && !FireIsComingFor(fires, chosen.Position) ? chosen : null;
        }

        readonly HashSet<int> nowhereSafeNoted = new HashSet<int>();

        /// <summary>Nothing burning — the ordinary casualty case, where any clear cell will do.</summary>
        static readonly List<Thing> NoFires = new List<Thing>();

        void NoteNowhereSafe(DirectorContext ctx, Pawn victim, List<Thing> fires)
        {
            // A sleeping spot is a bed as far as rescue is concerned, and it costs nothing.
            //
            // This used to log the problem and force the base planner due — which asks for a
            // *proper* bed to be sited, walled, materialled and built, several minutes of work,
            // while the colonist lies where the fire is going. Happy died of heatstroke in run
            // 114 nine in-game hours after this line was written about him.
            //
            // SleepingSpot has no cost list, no stuff cost and no work to build; RestUtility
            // treats it as a bed, so a rescue can target it the instant it exists. The planner
            // is still woken, because a spot on bare ground is a stopgap and the colony still
            // wants a real bed — but the stopgap is what decides whether there is anybody left
            // to put in the real one.
            if (PlaceRescueSpot(ctx, victim, fires))
            {
                if (nowhereSafeNoted.Add(victim.thingIDNumber))
                    Chronicle.Record(ChronicleCategory.Fire, string.Format(
                        "{0} is down with fire closing and every bed is taken or in its path — " +
                        "put a sleeping spot down clear of the fire so they can be moved now " +
                        "rather than when a bed is finished",
                        victim.LabelShortCap));
                if (ctx.director != null) ctx.director.ForceModuleDue("Base planner");
                return;
            }

            if (!nowhereSafeNoted.Add(victim.thingIDNumber)) return;
            Chronicle.Record(ChronicleCategory.Fire, string.Format(
                "{0} is down with fire closing, every bed is taken or in its path, and there is " +
                "nowhere clear to put even a sleeping spot",
                victim.LabelShortCap));

            if (ctx.director != null) ctx.director.ForceModuleDue("Base planner");
        }

        /// <summary>
        /// Somewhere clear to lay them down, right now.
        ///
        /// Searched outward from the casualty so the carry is short, and every candidate has to
        /// be somewhere the fire is not heading — a spot placed in the fire's path is worse than
        /// none, because the rescue will run towards it.
        /// </summary>
        static bool PlaceRescueSpot(DirectorContext ctx, Pawn victim, List<Thing> fires)
        {
            var spot = AcDefs.SleepingSpot;
            if (spot == null || ctx.map == null) return false;

            var map = ctx.map;
            foreach (var cell in GenRadial.RadialCellsAround(victim.Position, 24f, true))
            {
                if (!cell.InBounds(map)) continue;
                if (FireIsComingFor(fires, cell)) continue;
                if (PlacementUtil.HasAnyConstructionAt(map, cell)) continue;
                if (cell.GetEdifice(map) != null) continue;
                if (!cell.Standable(map)) continue;

                // No use putting it where the carrier cannot walk to.
                if (!map.reachability.CanReach(victim.Position, cell, PathEndMode.OnCell,
                                               TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly)))
                    continue;

                if (PlacementUtil.TryPlace(map, spot, cell, Rot4.North, null)) return true;
            }
            return false;
        }

        void HandleFires(DirectorContext ctx, bool meetTheFront)
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
                //
                // Unless the front is on its way, in which case every cell of it is a fire that
                // could reach the colony and the radius is measuring the wrong thing.
                bool inHome = home != null && home[fire.Position];
                if (!meetTheFront && !inHome &&
                    (fire.Position - origin).LengthHorizontalSquared > radiusSq) continue;

                // A room the fire has already won is not a room to send anyone into.
                //
                // Fire heats the air, and an enclosed space holds that heat: past about sixty
                // degrees a colonist walking in takes heatstroke and burns on top of whatever
                // the fire itself does, and they are slower to leave than to enter. The same
                // fire fought from the doorway costs nothing but time. So the interior is left
                // unclaimed once it is dangerously hot, and the ring around the room is claimed
                // instead — the front still gets fought, from the side of the wall where people
                // survive doing it.
                if (RoomIsAnOven(map, fire.Position))
                {
                    NoteRoomTooHot(map, fire.Position);
                    continue;
                }

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

        /// <summary>
        /// Room temperature past which going in to fight the fire costs more than the fire does.
        ///
        /// Colonists take heatstroke roughly ten degrees past what they can bear, and an
        /// enclosed burning room runs far hotter than that — the injuries come from the air as
        /// much as the flames, and a pawn who walks in is slower getting out than getting in.
        /// </summary>
        const float RoomTooHotToEnter = 60f;

        /// <summary>
        /// Whether the fire is inside a room that has become an oven.
        ///
        /// An outdoor fire, or one in a room still open to the sky, is never this — it is the
        /// walls that trap the heat, which is exactly why the same fire is safe to fight in the
        /// open and not safe to fight indoors.
        /// </summary>
        static bool RoomIsAnOven(Map map, IntVec3 at)
        {
            try
            {
                var room = at.GetRoom(map);
                if (room == null || room.UsesOutdoorTemperature) return false;
                return room.Temperature >= RoomTooHotToEnter;
            }
            catch (Exception) { return false; }
        }

        bool hotRoomNoted;

        void NoteRoomTooHot(Map map, IntVec3 at)
        {
            if (hotRoomNoted) return;
            hotRoomNoted = true;

            float temperature = 0f;
            try
            {
                var room = at.GetRoom(map);
                if (room != null) temperature = room.Temperature;
            }
            catch (Exception) { }

            Chronicle.Record(ChronicleCategory.Fire, string.Format(
                "the burning room is at {0:0}C — not claiming its interior, so the fire is " +
                "fought from outside rather than by sending colonists into an oven",
                temperature));
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
            float roster = CombatAssessment.ColonyStrength(ctx.state);
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

            // The force that can take the field, as distinct from the roster it comes from.
            //
            // These were the same number and they are not. The winnability test read the
            // strength of everyone able, and only afterwards did the loop release the reserved
            // medic and everyone too hurt to stand — so the colony settled the question on a
            // body it then declined to send. Run 132, day 3:
            //
            //   07h  WITHDRAWING 2  — strength 118 vs threat 165 (0.71x), needed 1.25x
            //   09h  engaging with 1 — strength 388 vs threat 158 (2.46x), needed 1.40x
            //
            // Three of the four were already on the floor at 09h. 388 was the roster; what
            // walked out was one person worth a fraction of it, at a true ratio nearer 0.4x
            // than the 2.46x printed beside it. All four bled to death within ten hours.
            //
            // No decision rule changes here. The colony is told how many hands it actually has,
            // and the rule it already had reaches the right answer on its own.
            var medic = ChooseReservedMedic(ctx, fighters);
            float retreatAt = ctx.Gene(Genes.DefenseRetreatHealth);
            var fieldable = Fieldable(fighters, medic, retreatAt);
            float strength = CombatAssessment.StrengthOf(fieldable);

            // What losing this fight would cost, alongside how likely losing it is. With most of
            // the colony already on the floor, the few still upright are the only thing standing
            // between it and nobody left to tend or feed anyone, so they hold cover on odds they
            // would have met in the open at full strength.
            float caution = CasualtyPolicy.EngagementCaution(fieldable.Count, ctx.state.colonistsDowned);
            var refuge = Refuge(ctx);
            bool hasRefuge = refuge.IsValid && refuge != RallyPoint(ctx);

            // A siege makes holding a room the wrong answer, which is the one case where the
            // rule above inverts.
            //
            // Besiegers do not come to the door. They build mortars at range and shell the base,
            // resupplied indefinitely, and only assault once their mortars are gone or they have
            // taken losses. Waiting behind a wall for that is waiting to be shelled, so a room
            // is not cover here and must not raise the bar for going out to meet them.
            bool besieged = Besieged(ctx);
            if (besieged) hasRefuge = false;

            // A bed is what a rescue carries someone to; with none, a colonist who goes down
            // stays down, so the fight has to be worth more before it is taken.
            bool canRecover = ctx.state.colonistBeds > 0;

            // How much advantage this *kind* of fight has been shown to want.
            //
            // The gene is the prior; the memory is what this colony has actually paid. A flat
            // ratio could only ever be right for one of the things it was applied to — a lone
            // tribal, an arctic wolf and a manhunter pack of twelve are not one problem, and the
            // colony that has just lost two of three people to one of them has learned something
            // a constant cannot hold.
            var kind = KindOfThreat(ctx);
            float learned = Learning.ThreatMemory.ForceFor(kind, ctx.Gene(Genes.DefenseEngageRatio));
            OpenEncounter(ctx, kind, fieldable);

            float required = CasualtyPolicy.RequiredAdvantage(
                learned, fieldable.Count,
                ctx.state.colonistsDowned, hasRefuge, canRecover);

            bool winnable = threat <= 0f || strength / threat >= required;

            // A predator eating a colonist is not a fight you get to decline.
            //
            // ThreatActive already answers yes for a hunt, ahead of this arithmetic and for this
            // exact reason — and then this ran its own comparison and withdrew anyway. Run 129:
            //
            //   day 47 14h  1 predator(s) hunting Trofim — drafting
            //   day 47 14h  WITHDRAWING 1 — strength 95 vs threat 75 (1.26x), needed 3.00x
            //   day 47 14h  died of Bite (arctic wolf teeth)
            //
            // Detected, drafted, and then walked away from in the same hour. The bar was 3.00x
            // because somebody was already down, which is the raid rule working correctly and
            // being exactly wrong here: with a raid, a colonist on the floor is a reason to hold
            // the base; with a predator, the colonist on the floor *is* what the animal is eating.
            //
            // Withdrawing does not save them either. The wolf does not lose interest.
            //
            // But only where there is nowhere to put them. A door is a better answer than a
            // fight, and the colony can now tell a real room from a patch of roof.
            //
            // Run 145, day 9, with a finished Kitchen standing since day 5:
            //
            //   05h  1 predator(s) hunting Fox — drafting
            //   05h  engaging with 2 — strength 154 vs threat 160 (0.96x), needed 1.50x
            //        (a room to hold, so the open is elective)
            //   06h  died of Bite (warg razorfangs) — Pablo
            //   14h  COLONY LOST
            //
            // The override read "a predator is hunting" and forced the fight at 0.96x against a
            // warg — the animal the reference puts at a hundred percent revenge — while an
            // enclosed room stood a short walk away. It turned "we cannot win this" into
            // "engage" and the margin never entered the decision.
            //
            // Animals cannot open doors, so with a genuine refuge the stalked colonist is saved
            // by walking inside, which is what withdrawing now means since the enclosure test
            // went in. Without one, withdrawing is still just standing somewhere else and the
            // original reasoning holds exactly as written.
            if (ctx.state.predatorsHunting > 0 && !hasRefuge) winnable = true;

            var rally = winnable ? RallyPoint(ctx) : refuge;
            int mobilised = 0;
            float committedStrength = 0f;

            // Nobody is left where they fell.
            //
            // Run 164 lost Simon eleven minutes after he went down: the colony judged the fight
            // lost, withdrew two able colonists past him to the refuge, and a raider carried him
            // off. The rescue that would have taken him could not run, because rescuing is work
            // and every colonist was drafted. Two colonies before that lost one each the same
            // way. See RetreatCargo.
            //
            // Done before the withdrawal orders rather than after, so the carrier is chosen out
            // of the people who are about to walk away instead of being missed by a loop that has
            // already sent everyone somewhere else.
            if (!winnable) CarryTheFallen(ctx);

            for (int i = 0; i < fighters.Count; i++)
            {
                var pawn = fighters[i];
                if (pawn.drafter == null) continue;

                // Not fieldable — the reserved medic, or too hurt to stand.
                //
                // Released rather than merely left undrafted: they may have been in the line
                // when the casualty happened, and work priorities already put Doctor at the top
                // the moment anyone went down, so letting go of them is the whole order. The
                // hurt are released for the same reason, to go and seek treatment.
                //
                // One definition, consulted here and used in the decision above, so the force
                // the fight was accepted on and the force that walks out cannot drift apart.
                if (!fieldable.Contains(pawn))
                {
                    if (pawn.drafter.Drafted) pawn.drafter.Drafted = false;
                    drafted.Remove(pawn);
                    continue;
                }

                // Enough, rather than everyone.
                //
                // This drafted every able colonist for a lone tribal and for a manhunter pack
                // alike. Sending one person means that one takes all of the damage; sending
                // three spreads it and ends the fight sooner, and both of those are why more is
                // usually better. But every colonist drafted is one not hauling, cooking or
                // building, and in a colony of three that is the entire workforce standing in a
                // field — so more is not free, and "all of them" is only right when the fight
                // actually needs all of them.
                //
                // Committed down the ranked list until the force the memory asks for is met.
                // The ranking is by fitness, so the people best able to survive it go first.
                // Anyone past that point stays on the work that keeps the colony fed.
                if (winnable && committedStrength >= threat * required && mobilised > 0)
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
                committedStrength += CombatAssessment.ColonistValue(pawn);

                SendToPosition(ctx, pawn, rally, NearestHostileCell(ctx));

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
                    "({7:0.00}x), needed {8:0.00}x{9}{10}{11}",
                    ctx.state.hostilePawns, ctx.state.danger,
                    winnable ? "engaging with" : "WITHDRAWING",
                    mobilised, rally, strength, threat,
                    threat > 0f ? strength / threat : 999f, required,
                    // Say when the roster is bigger than what can be sent, so the gap between
                    // "how strong is this colony" and "who can walk out" stays readable rather
                    // than having to be inferred from a body count two hours later.
                    roster > strength * 1.05f
                        ? string.Format(" ({0:0} of {1:0} fieldable — {2} held back)",
                                        strength, roster, fighters.Count - fieldable.Count)
                        : "",
                    besieged
                        ? " (a siege — they shell from range and will not come to the door, so " +
                          "there is no cover to hold)"
                        : caution > 1f
                            ? " (" + ctx.state.colonistsDowned + " already down, so the bar is " +
                              caution.ToString("0.0") + "x higher)"
                            : hasRefuge
                            ? " (a room to hold, so the open is elective)"
                            : (canRecover ? "" : " (no bed to carry a casualty to, so an even " +
                                                 "fight is one the colony cannot afford)"),
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
        /// <summary>
        /// Who out of the ranked fighters can actually be sent.
        ///
        /// The single definition of "fieldable". It is asked once before the fight is accepted
        /// and consulted again while drafting, which is the point — when the same filter lived
        /// only inside the drafting loop, the decision above it was taken on a roster that
        /// included people the loop was about to release.
        /// </summary>
        static List<Pawn> Fieldable(List<Pawn> fighters, Pawn medic, float retreatAt)
        {
            var able = new List<Pawn>();
            if (fighters == null) return able;

            for (int i = 0; i < fighters.Count; i++)
            {
                var pawn = fighters[i];
                if (pawn == null || pawn == medic) continue;

                float health = pawn.health != null && pawn.health.summaryHealth != null
                    ? pawn.health.summaryHealth.SummaryHealthPercent
                    : 1f;
                if (health < retreatAt) continue;

                able.Add(pawn);
            }
            return able;
        }

        /// <summary>Set once per spell so the too-late line is stated rather than repeated.</summary>
        bool tooLateNoted;

        /// <summary>
        /// How long this colonist needs to reach that one, in ticks.
        ///
        /// Straight-line distance at their own move speed, which is what makes a lost leg
        /// visible: RimWorld charges a missing part to MoveSpeed, so a one-legged doctor reads
        /// as the slow walker they are without this having to know what a leg is.
        /// </summary>
        static int TicksToReach(Pawn pawn, Pawn patient)
        {
            if (pawn == null || patient == null) return 0;
            if (!patient.Spawned || !pawn.Spawned) return MedicChoice.Unreachable;

            try
            {
                if (!pawn.CanReach(patient, PathEndMode.Touch, Danger.Deadly))
                    return MedicChoice.Unreachable;

                float speed = pawn.GetStatValue(StatDefOf.MoveSpeed);
                float distance = (pawn.Position - patient.Position).LengthHorizontal;
                return MedicChoice.TicksToCross(distance, speed);
            }
            catch (Exception) { return MedicChoice.Unreachable; }
        }

        /// <summary>The shortest walk anyone has to the patient, in hours, for the chronicle.</summary>
        static float NearestWalkHours(List<Pawn> fighters, Pawn patient)
        {
            int best = int.MaxValue;
            for (int i = 0; i < fighters.Count; i++)
            {
                int t = TicksToReach(fighters[i], patient);
                if (t >= 0 && t < best) best = t;
            }
            return best == int.MaxValue ? -1f : best / 2500f;
        }

        Pawn ChooseReservedMedic(DirectorContext ctx, List<Pawn> fighters)
        {
            if (!CasualtyPolicy.ShouldReserveMedic(fighters.Count, ctx.state.colonistsDowned,
                                                   ctx.state.colonistsBleedingOut))
            {
                if (reservedMedic != null)
                {
                    Chronicle.Record(ChronicleCategory.Health,
                        "nobody down any more; " + reservedMedic.LabelShortCap + " rejoins the line");
                    reservedMedic = null;
                }
                return null;
            }

            // Who is bleeding, and how long they have. Both are read rather than assumed: the
            // deadline decides whether a walk is worth starting, and the patient decides how far
            // the walk is.
            var patient = ctx.state.soonestBleedingOut;
            int deadline = ctx.state.ticksToFirstBloodLoss;

            int bestSkill = -1;
            Pawn best = null;
            float bestUse = 0f;
            float bestValue = float.MaxValue;

            for (int i = 0; i < fighters.Count; i++)
            {
                var pawn = fighters[i];
                if (pawn == null || pawn.drafter == null) continue;
                if (pawn.WorkTagIsDisabled(WorkTags.Caring)) continue;

                int skill = CombatAssessment.SkillLevel(pawn, SkillDefOf.Medicine);
                int toReach = TicksToReach(pawn, patient);
                float use = MedicChoice.Usefulness(skill, toReach, deadline);
                float value = CombatAssessment.ColonistValue(pawn);

                if (use > bestUse || (use == bestUse && best != null && value < bestValue))
                {
                    best = pawn;
                    bestUse = use;
                    bestSkill = skill;
                    bestValue = value;
                }
            }

            // Everybody arrives too late. Holding one back saves nobody and costs the line a
            // fighter, so the honest answer is to say so rather than reserve a medic for a
            // deadline that has already passed.
            if (best == null && patient != null)
            {
                if (!tooLateNoted)
                {
                    tooLateNoted = true;
                    Chronicle.Record(ChronicleCategory.Health, string.Format(
                        "nobody can reach {0} before they bleed out ({1:0.0} hours of walking " +
                        "against {2:0.0} left) — keeping everyone in the line, because a doctor " +
                        "who arrives to a corpse has cost the fight a fighter and saved nothing",
                        patient.LabelShortCap, NearestWalkHours(fighters, patient),
                        deadline / 2500f));
                }
                return null;
            }
            tooLateNoted = false;

            if (best != null && best != reservedMedic)
            {
                reservedMedic = best;
                Chronicle.Record(ChronicleCategory.Health, string.Format(
                    // "medicine 0" here meant a skill level and was read as a supply count by the
                    // one person who has ever had to interpret this line, next to a vitals line
                    // saying med 30 in the same hour. Said in full.
                    "{0} colonists down — holding {1} back from the fight to tend them " +
                    "(Medicine skill {2}, leaving {3} in the line)",
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

                // Roofed is not enclosed, and only enclosed keeps anything out.
                //
                // A roofed cell inside three walls and a gap passes the test above, and run 138
                // withdrew two colonists into one. The manhunter walked in after them:
                //
                //   day 8 12h  WITHDRAWING 2 — strength 69 vs threat 57 (1.21x), needed 1.50x
                //              (a room to hold, so the open is elective)
                //   day 8 13h  holding Grove back from the fight to tend them (leaving 0 in the line)
                //   day 8 19h  died of Blood loss — Stevie
                //   day 8 19h  died of Blood loss — Grove
                //
                // The medic reservation worked exactly as designed and the medic was killed
                // where they stood, because "a room to hold" was a claim nothing had checked.
                // Animals cannot open doors, so a genuinely enclosed room is the one answer a
                // pre-industrial colony has to a manhunter pack — and it was being confused
                // with a patch of shade.
                //
                // RoomCensus already asks this question correctly and has since it was written.
                // The same test, from the same knowledge, arriving late in the other place that
                // needed it.
                var actual = centre.GetRoom(ctx.map);
                if (actual == null || actual.TouchesMapEdge || actual.PsychologicallyOutdoors)
                    continue;

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

        /// <summary>Cells already given to somebody this pass, so nobody is sent to stand on an ally.</summary>
        readonly List<IntVec3> taken = new List<IntVec3>();

        /// <summary>How far around the rally point to look for somewhere worth standing.</summary>
        const int PositionSearchRadius = 12;

        /// <summary>
        /// Sends a colonist to the best place to fight from, rather than to a coordinate.
        ///
        /// Every drafted colonist used to be sent to the same cell — the base origin — and told
        /// to shoot whatever was nearest. That is not a position: no cover, no spacing, no use
        /// of a doorway, and one grenade catching all of them. The cells around the rally point
        /// are now scored on cover, range, spacing and chokepoints, every weight of it a gene,
        /// and each colonist takes the best one still free.
        ///
        /// A colonist already somewhere good is left alone. Re-issuing an order restarts the job
        /// and they never loose a shot — the same trap the engage logic was already written
        /// around — so moving has to be worth clearly more than standing still.
        /// </summary>
        void SendToPosition(DirectorContext ctx, Pawn pawn, IntVec3 rally, IntVec3 threatAt)
        {
            if (!rally.IsValid) return;
            if (pawn.CurJobDef == JobDefOf.Goto) return;

            var map = ctx.map;
            var weights = PositionWeightsFrom(ctx);

            float bestScore = float.NegativeInfinity;
            var best = IntVec3.Invalid;

            foreach (var cell in GenRadial.RadialCellsAround(rally, PositionSearchRadius, true))
            {
                if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                if (taken.Contains(cell)) continue;
                if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;

                float score = Combat.FiringPosition.Score(FeaturesOf(map, cell, threatAt), weights);
                if (score > bestScore) { bestScore = score; best = cell; }
            }

            if (!best.IsValid) return;

            // Worth the walk? Compare against where they already are.
            float current = Combat.FiringPosition.Score(
                FeaturesOf(map, pawn.Position, threatAt), weights);
            if (!Combat.FiringPosition.WorthMoving(current, bestScore))
            {
                taken.Add(pawn.Position);
                return;
            }

            taken.Add(best);
            var job = JobMaker.MakeJob(JobDefOf.Goto, best);
            job.playerForced = true;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        Combat.PositionWeights PositionWeightsFrom(DirectorContext ctx)
        {
            var w = new Combat.PositionWeights();
            w.cover = ctx.Gene(Genes.CombatCoverWeight);
            w.standoff = ctx.Gene(Genes.CombatStandoffWeight);
            w.preferredRange = ctx.Gene(Genes.CombatPreferredRange);
            w.spread = ctx.Gene(Genes.CombatSpreadWeight);
            w.chokepoint = ctx.Gene(Genes.CombatChokepointWeight);
            w.indoors = ctx.Gene(Genes.CombatIndoorsWeight);
            return w;
        }

        /// <summary>Reads what is true of a cell, which is the half the genome cannot supply.</summary>
        Combat.PositionFeatures FeaturesOf(Map map, IntVec3 cell, IntVec3 threatAt)
        {
            var f = new Combat.PositionFeatures();

            try
            {
                // Cover measured towards the threat specifically: a wall is only cover from the
                // side the shooting comes from.
                f.cover = threatAt.IsValid
                    ? CoverUtility.CalculateOverallBlockChance(cell, threatAt, map)
                    : 0f;

                f.toThreat = threatAt.IsValid ? (cell - threatAt).LengthHorizontal : 999f;

                float nearest = 999f;
                for (int i = 0; i < taken.Count; i++)
                {
                    float d = (cell - taken[i]).LengthHorizontal;
                    if (d < nearest) nearest = d;
                }
                f.toNearestAlly = nearest;

                var room = cell.GetRoom(map);
                f.indoors = room != null && !room.PsychologicallyOutdoors;

                // A doorway is where attackers have to come one at a time.
                var edifice = cell.GetEdifice(map);
                f.chokepoint = edifice is Building_Door;
                if (!f.chokepoint)
                {
                    var adjacent = cell.GetThingList(map);
                    for (int i = 0; i < adjacent.Count; i++)
                        if (adjacent[i] is Building_Door) { f.chokepoint = true; break; }
                }
            }
            catch (Exception) { }

            return f;
        }

        /// <summary>Health each committed colonist had when the fight was joined.</summary>
        readonly Dictionary<int, float> healthAtEngage = new Dictionary<int, float>();
        readonly List<Pawn> committedThisFight = new List<Pawn>();
        Learning.ThreatKind fightKind = Learning.ThreatKind.Other;
        bool encounterOpen;

        /// <summary>
        /// Note who is in this fight and how healthy they are, so the cost can be read afterwards.
        ///
        /// Opened once and left alone until the threat is over — a fight that ebbs and flows is
        /// still one fight, and re-snapshotting mid-way would quietly forget the damage already
        /// taken, which is exactly the damage worth learning from.
        /// </summary>
        void OpenEncounter(DirectorContext ctx, Learning.ThreatKind kind, List<Pawn> fighters)
        {
            if (encounterOpen) return;

            encounterOpen = true;
            fightKind = kind;
            healthAtEngage.Clear();
            committedThisFight.Clear();

            for (int i = 0; i < fighters.Count; i++)
            {
                var pawn = fighters[i];
                if (pawn == null || pawn.health == null) continue;
                healthAtEngage[pawn.thingIDNumber] = Health(pawn);
                committedThisFight.Add(pawn);
            }
        }

        /// <summary>
        /// Read what the fight cost and hand it to the memory.
        ///
        /// Damage is summed across everyone committed rather than averaged here, because the
        /// memory divides by how many were sent — that ratio is the whole question. Sending one
        /// colonist against a wolf and sending three are both survivable; only one of them leaves
        /// somebody able to work afterwards.
        /// </summary>
        void CloseEncounter(DirectorContext ctx)
        {
            if (!encounterOpen) return;
            encounterOpen = false;

            if (committedThisFight.Count == 0) return;

            float damage = 0f;
            int casualties = 0;
            int leftBleeding = 0;

            for (int i = 0; i < committedThisFight.Count; i++)
            {
                var pawn = committedThisFight[i];
                if (pawn == null) continue;

                float before;
                if (!healthAtEngage.TryGetValue(pawn.thingIDNumber, out before)) continue;

                if (pawn.Dead) { damage += before; casualties++; continue; }
                if (pawn.Downed) casualties++;

                // Still on their feet and still losing blood. Neither a casualty nor a scrape,
                // and until now visible to the memory as neither — see ThreatMemory.RecordOutcome.
                if (Bleeding(pawn)) leftBleeding++;

                float now = Health(pawn);
                if (now < before) damage += before - now;
            }

            int committed = committedThisFight.Count;
            Learning.ThreatMemory.RecordOutcome(fightKind, committed, damage, casualties,
                                                leftBleeding,
                                                ctx.Gene(Genes.DefenseBleedingAsCasualty));
            Learning.ThreatMemory.Save();

            Chronicle.Record(ChronicleCategory.Threat, string.Format(
                "{0} cost {1:0.00} health across {2} sent{3}{5} — {4}",
                fightKind, damage, committed,
                casualties > 0 ? ", " + casualties + " down" : " and nobody went down",
                // "nobody went down" is true and was the whole story, beside a fight that had
                // just put two colonists on a blood-loss clock. A diagnostic that is accurate
                // and reads as reassuring is the kind this project has lost hours to.
                leftBleeding > 0 ? ", " + leftBleeding + " still bleeding" : "",
                Learning.ThreatMemory.Explain(fightKind)));

            healthAtEngage.Clear();
            committedThisFight.Clear();
        }

        static float Health(Pawn pawn)
        {
            try
            {
                return pawn.health != null && pawn.health.summaryHealth != null
                    ? pawn.health.summaryHealth.SummaryHealthPercent : 1f;
            }
            catch (Exception) { return 1f; }
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

            CloseEncounter(ctx);
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
