using System;
using System.Collections.Generic;
using AutoColony.Learning;
using AutoColony.Upkeep;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoColony.Modules
{
    /// <summary>
    /// Fixes what the colony has already built.
    ///
    /// Every other construction path in the director adds: it reserves a room and fills it. This
    /// one is the only thing that changes its mind about something standing — it roofs an
    /// exposed generator, lights a dark bedroom, pulls the surplus beds out of a barracks, and
    /// where a building simply cannot stay where it is, takes it down so the planner puts it
    /// back somewhere sensible.
    ///
    /// It acts on one defect per pass on purpose. Remedies queue colonist work, and a survey
    /// that found eleven problems and ordered all eleven at once would bury the construction the
    /// colony actually needs under a backlog of tidying.
    /// </summary>
    public class UpkeepModule : DirectorModule
    {
        public override string Name { get { return "Upkeep"; } }

        // Roughly twice an in-game day. These are slow-burning problems, and re-surveying often
        // costs a room walk per colonist for something that changes over days.
        public override int IntervalTicks { get { return 15000; } }

        // Deliberately *not* Discretionary. Most upkeep should wait while the colony is
        // burning, but "most" is not "all", and switching the whole module off during an
        // emergency is how a colony that lurched from one crisis to the next never got round to
        // burying anyone — carrying the largest penalty in the game for eleven days over a
        // building that costs nothing. The bar rises instead of the work stopping.

        /// <summary>How well a fix has to pay for itself to be worth doing mid-crisis.</summary>
        const float UrgentOnly = 0.8f;

        readonly List<UnmetComplaint> unhandled = new List<UnmetComplaint>();

        float[] kindWeights;

        /// <summary>
        /// This colony's own opinion of what each kind of fault is worth, read from its genome.
        /// Rebuilt each pass because a training trial swaps the genome underneath the module.
        /// </summary>
        float[] UpkeepWeights(DirectorContext ctx)
        {
            if (kindWeights == null) kindWeights = new float[DefectPolicy.KindCount];
            for (int i = 0; i < kindWeights.Length; i++)
                kindWeights[i] = ctx.Gene(DefectPolicy.WeightKey((DefectKind)i));
            return kindWeights;
        }

        protected override void Act(DirectorContext ctx)
        {

            // While something immediate is happening the colony only does what clearly pays for
            // itself — burying the dead does, decorating does not.
            bool crisis = ctx.state.EmergencyAtHome ||
                          (ctx.plan != null && ctx.plan.EmergencyActive);
            float bar = crisis ? UrgentOnly : DefectPolicy.ActionThreshold;

            // Withdraw anything the colony asked for and no longer wants, before asking for more.
            if (!crisis && CancelStaleOrders(ctx)) return;

            // Surgery outranks the defect survey and runs even in a crisis: a colonist losing to
            // an infection is a death on a timer, and every other remedy here is furniture.
            QueueLifesavingSurgery(ctx);

            // And let out anyone the colony has walled in. Same class of emergency as the
            // surgery above — a death on a timer — so it runs in a crisis too.
            FreeAnyoneWalledIn(ctx);

            // Before asking what the colony lacks, finish what it already owns. A chair against a
            // table it already paid for is the cheapest thing the director can buy, and until it
            // is there the survey below will keep asking for a second table.
            if (!crisis) SeatWhatNeedsSeating(ctx);

            NameWhatIsDry(ctx);

            unhandled.Clear();
            var defects = DefectSurvey.Survey(ctx.map, ctx.state, ctx.layout, unhandled,
                                              ctx.Gene(Genes.RoomEssentialWeight),
                                              ctx.Gene(Genes.RoomOccupancyWeight),
                                              UpkeepWeights(ctx),
                                              ctx.plan != null ? ctx.plan.RolesAnyGoalWants : null);

            Report(ctx, BuildingMeans.Assess(ctx.state.usableMaterial, ctx.state.colonists),
                   defects.Count);
            if (defects.Count == 0) return;

            for (int i = 0; i < defects.Count; i++)
            {
                var defect = defects[i];
                if (defect.Priority < bar) continue;
                if (!DefectPolicy.WorthActing(defect.kind, defect.severity)) continue;
                if (!Apply(ctx, defect)) continue;

                Chronicle.Record(ChronicleCategory.Build, string.Format(
                    "upkeep — {0}: {1} ({2}, severity {3:0.00})",
                    defect.remedy, defect.what, defect.kind, defect.severity));
                Note(defect.remedy + " for " + defect.kind);
                return;
            }
        }

        /// <summary>
        /// Withdraws standing orders whose reason has gone away.
        ///
        /// Orders outlive their justification. The case that matters is a colony that was
        /// comfortable when it decided to break up a barracks and is destitute by the time
        /// anyone gets to the job: pulling the beds out is now precisely the wrong move, and
        /// without this the order stands and the colony dismantles the one room everybody is
        /// sleeping in during the crisis that made it poor.
        ///
        /// Nothing else in the director ever cancelled anything it had asked for.
        /// </summary>
        bool CancelStaleOrders(DirectorContext ctx)
        {
            float means = BuildingMeans.Assess(ctx.state.usableMaterial, ctx.state.colonists);
            if (!BuildingMeans.Destitute(means)) return false;

            var lister = ctx.map.listerBuildings;
            if (lister == null) return false;

            foreach (var bed in lister.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == null || !bed.Spawned) continue;
                if (!bed.ForColonists || bed.Medical) continue;
                if (!PlacementUtil.CancelDesignation(ctx.map, bed, DesignationDefOf.Uninstall)) continue;

                Chronicle.Record(ChronicleCategory.Build, string.Format(
                    "upkeep — cancelling the order to move a bed out of its room: means have " +
                    "fallen to {0:0.00} and sharing is now the right answer", means));
                Note("cancelled a de-sharing order");
                return true;
            }
            return false;
        }

        string lastReport;

        /// <summary>
        /// A standing account of the colony's condition: what it can afford, what is wrong with
        /// it, and what it is unhappy about that the director has no answer for.
        ///
        /// Recorded on the chronicle rather than behind the test harness, because this is the
        /// line that makes a long unattended run diagnosable — whether upkeep is converging or
        /// oscillating is invisible from the remedies alone. Only written when it changes, or an
        /// established colony would repeat the same sentence four times a day forever.
        /// </summary>
        void Report(DirectorContext ctx, float means, int defectCount)
        {
            // Worst first: this list is read to decide what to teach the director next, and the
            // biggest single penalty is the answer to that question.
            unhandled.Sort(delegate(UnmetComplaint a, UnmetComplaint b)
            {
                return b.mood.CompareTo(a.mood);
            });

            // The same finding, handed to the scorer. The chronicle line is for whoever reads it
            // later; this is what lets the epoch's fitness know the colony spent a fortnight
            // miserable about something nobody had taught the director to fix.
            if (ctx.director != null && ctx.director.accumulator != null)
            {
                float total = 0f;
                for (int i = 0; i < unhandled.Count; i++) total += unhandled[i].mood;

                ctx.director.accumulator.NoteUnmetComplaints(
                    total,
                    unhandled.Count > 0 ? unhandled[0].thought : "",
                    unhandled.Count > 0 ? unhandled[0].mood : 0f);
            }

            string report = string.Format(
                "upkeep — means {0:0.00} ({1} material), {2} defects{3}",
                means, ctx.state.usableMaterial, defectCount,
                unhandled.Count > 0
                    ? "; cannot fix yet: " + string.Join(", ", unhandled.ToArray())
                    : "");

            if (report == lastReport) return;
            lastReport = report;
            Chronicle.Record(ChronicleCategory.Vitals, report);
        }


        bool Apply(DirectorContext ctx, ColonyDefect defect)
        {
            switch (defect.remedy)
            {
                case RemedyKind.RoofOver: return RoofOver(ctx, defect);
                case RemedyKind.Relocate: return Relocate(ctx, defect);
                case RemedyKind.AddLight: return AddLight(ctx, defect);
                case RemedyKind.RemoveSurplusBeds: return RemoveSurplusBeds(ctx, defect);
                case RemedyKind.AddBeauty:
                    return ShellsFirst(ctx, "decoration") ? false : AddBeauty(ctx, defect);
                case RemedyKind.Reclaim: return Reclaim(ctx, defect);
                case RemedyKind.BuryDead: return BuryDead(ctx, defect);
                case RemedyKind.AddHeater: return AddHeater(ctx);
                case RemedyKind.AddCooler: return AddCooler(ctx);
                case RemedyKind.AddTable:
                    return ShellsFirst(ctx, "a table") ? false : AddTable(ctx);
                case RemedyKind.AddRecreation:
                    return ShellsFirst(ctx, "recreation") ? false : AddRecreation(ctx);
                case RemedyKind.AddSeating:
                    return ShellsFirst(ctx, "seating") ? false : AddSeating(ctx);
                default: return false;
            }
        }

        // ------------------------------------------------------------ remedies

        /// <summary>
        /// Digs a grave near where the body is.
        ///
        /// A grave needs no research and costs nothing whatsoever, which makes this the best
        /// trade in the game: the single largest mood penalty, removed for free. Colonists haul
        /// their own dead into it once one exists — the director only has to provide the hole.
        /// </summary>
        /// <summary>The tomb, if one is planned and its walls are up. Null otherwise.</summary>
        static PlannedRoom PlannedTomb(DirectorContext ctx)
        {
            if (ctx.layout == null) return null;
            for (int i = 0; i < ctx.layout.rooms.Count; i++)
            {
                var room = ctx.layout.rooms[i];
                if (room.role == RoomRole.Tomb && room.wallsQueued) return room;
            }
            return null;
        }

        /// <summary>
        /// Set once the colony has been told a grave is dug and waiting. Cleared whenever the
        /// remedy does something, or when there is no unburied body left, so the next death
        /// gets its own line rather than inheriting this one's silence.
        /// </summary>
        static bool graveWaitNoted;

        /// <summary>What was dry last pass, so the chronicle speaks on change rather than every pass.</summary>
        static string fuelNoted = "";

        /// <summary>Reported surgeries, so each is chronicled once rather than every pass.</summary>
        static readonly HashSet<int> surgeryNoted = new HashSet<int>();

        /// <summary>
        /// Amputates what is killing a colonist, when tending has lost the race.
        ///
        /// An infection climbs towards lethalSeverity while the body builds immunity towards 1,
        /// and tending only speeds the immunity side. When the disease is ahead the answer is to
        /// remove the part it lives in — no amount of medicine substitutes once the race is
        /// lost, which is how colonists here have died of Infection (extreme) with twenty
        /// medicine in the cupboard.
        ///
        /// Guarded four ways, each from the design notes:
        ///  - only when actually losing, not merely infected — the race, not the diagnosis
        ///  - only parts the game itself would suggest amputating (canSuggestAmputation), which
        ///    excludes torsos and heads; whole-body diseases like plague have no part at all
        ///    and fall out naturally — for those, bed rest and cleanliness are all there is
        ///  - only in a clean room (cleanliness >= 0), because a filthy room cuts surgery to
        ///    0.60x and leaves post-operative infection at full odds — operating there to cure
        ///    an infection hands the colonist a fresh one. Overridden only when death is near
        ///    (past 85% of lethal), where a dirty table beats a grave
        ///  - queued once, on the pawn's own surgery bills, like a player would
        ///
        /// After the amputation, a peg leg. InstallPegLeg consumes one wood log directly —
        /// there is no prosthetic item to craft — so the whole aftermath is a second bill. The
        /// better rungs (simple prosthetic, bionic) sit behind Electricity and are queued by
        /// nothing here; a peg leg today, an upgrade when research pays the debt back.
        /// </summary>
        static void QueueLifesavingSurgery(DirectorContext ctx)
        {
            var remove = DefDatabase<RecipeDef>.GetNamedSilentFail("RemoveBodyPart");
            if (remove == null) return;

            for (int i = 0; i < ctx.state.allColonists.Count; i++)
            {
                var pawn = ctx.state.allColonists[i];
                if (pawn == null || pawn.Dead || pawn.health == null) continue;

                TryQueueAmputation(ctx, pawn, remove);
                TryQueuePegLeg(ctx, pawn);
            }
        }

        static void TryQueueAmputation(DirectorContext ctx, Verse.Pawn pawn, RecipeDef remove)
        {
            var hediffs = pawn.health.hediffSet.hediffs;
            for (int h = 0; h < hediffs.Count; h++)
            {
                var hediff = hediffs[h];
                if (hediff == null || hediff.def == null) continue;
                if (hediff.def.lethalSeverity <= 0f) continue;
                if (hediff.Part == null) continue;                        // whole-body: no surgery for it
                if (!hediff.Part.def.canSuggestAmputation) continue;

                var immunizable = hediff.TryGetComp<HediffComp_Immunizable>();
                if (immunizable == null || immunizable.FullyImmune) continue;

                float towardsDeath = hediff.Severity / hediff.def.lethalSeverity;
                if (towardsDeath <= immunizable.Immunity) continue;       // winning; leave the limb on

                if (HasBill(pawn, remove, hediff.Part)) continue;

                // A dirty theatre is its own infection. Hold the knife until the room is clean,
                // unless the race is nearly over — past 85% a dirty table beats a grave.
                //
                // 85% was far too late, because it measured the disease and not the clock. Leslie
                // went from 14% to 93% in a single day; the bill was queued at 93% and she died
                // two hours later, before anybody reached the table. The threshold has to leave
                // room for the surgery to actually happen — finding a doctor, walking there, and
                // cutting — not merely for the decision to be taken.
                //
                // Two fifths, and the room gets an emergency mop while the hold stands. If the
                // colony cannot get a floor clean in the time it takes a disease to cross from
                // 40% to lethal, the floor was never going to be the deciding factor.
                float cleanliness = RoomCleanlinessAround(pawn);
                if (cleanliness < 0f && towardsDeath < 0.4f)
                {
                    if (surgeryNoted.Add(pawn.thingIDNumber ^ 0x5A5A))
                        Chronicle.Record(ChronicleCategory.Health, string.Format(
                            "{0} is losing to {1} ({2:P0} towards lethal, immunity {3:P0}) and needs " +
                            "the {4} amputated — holding the surgery until the room is clean, because " +
                            "a filthy theatre cuts success to 0.6x and reinfects at full odds",
                            pawn.LabelShortCap, hediff.def.label, towardsDeath, immunizable.Immunity,
                            hediff.Part.Label));
                    continue;
                }

                var bill = new Bill_Medical(remove, null);
                pawn.health.surgeryBills.AddBill(bill);
                bill.Part = hediff.Part;

                Chronicle.Record(ChronicleCategory.Health, string.Format(
                    "amputating {0}\'s {1} — {2} is at {3:P0} of lethal against {4:P0} immunity, " +
                    "so tending has lost this race and the part goes before the colonist does",
                    pawn.LabelShortCap, hediff.Part.Label, hediff.def.label,
                    towardsDeath, immunizable.Immunity));
                return;   // one theatre booking per pass
            }
        }

        /// <summary>
        /// A peg leg for anyone missing a leg. The install consumes one wood log directly, so
        /// there is nothing to craft first — the bill is the whole aftermath.
        /// </summary>
        static void TryQueuePegLeg(DirectorContext ctx, Verse.Pawn pawn)
        {
            if (ctx.state.wood < 1) return;
            var install = DefDatabase<RecipeDef>.GetNamedSilentFail("InstallPegLeg");
            if (install == null || install.appliedOnFixedBodyParts == null) return;

            var missing = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            for (int i = 0; i < missing.Count; i++)
            {
                var part = missing[i].Part;
                if (part == null || !install.appliedOnFixedBodyParts.Contains(part.def)) continue;
                if (HasBill(pawn, install, part)) continue;

                var bill = new Bill_Medical(install, null);
                pawn.health.surgeryBills.AddBill(bill);
                bill.Part = part;

                Chronicle.Record(ChronicleCategory.Health, string.Format(
                    "fitting {0} with a peg leg for the missing {1} — one wood log, no research, " +
                    "and a slower colonist beats a bedridden one. Research buys this back later: " +
                    "prosthetic, then bionic, each replacing the last",
                    pawn.LabelShortCap, part.Label));
                return;
            }
        }

        static bool HasBill(Verse.Pawn pawn, RecipeDef recipe, BodyPartRecord part)
        {
            var bills = pawn.health.surgeryBills;
            if (bills == null) return false;
            for (int i = 0; i < bills.Count; i++)
            {
                var medical = bills[i] as Bill_Medical;
                if (medical != null && medical.recipe == recipe && medical.Part == part) return true;
            }
            return false;
        }

        /// <summary>
        /// Cleanliness where this pawn would be operated on — their bed\'s room if they are in
        /// one, the room they stand in otherwise. Live stat, because cleanliness is not a
        /// property of the building but of whether anybody swept.
        /// </summary>
        static float RoomCleanlinessAround(Verse.Pawn pawn)
        {
            try
            {
                var bed = pawn.CurrentBed() ?? pawn.ownership?.OwnedBed;
                var room = bed != null ? bed.GetRoom() : pawn.GetRoom();
                if (room == null || room.PsychologicallyOutdoors) return -1f;
                return room.GetStat(RoomStatDefOf.Cleanliness);
            }
            catch (Exception) { return -1f; }
        }

        static bool BuryDead(DirectorContext ctx, ColonyDefect defect)
        {
            var grave = AcDefs.Grave;
            if (grave == null) return false;

            // Digging the grave is not burying anybody.
            //
            // They are two separate actions, and only the first was ever taken: a colony would
            // build a grave, stand next to it with the body still lying where it fell, and go on
            // paying the -10 for an unburied colonist indefinitely. Carrying the corpse to the
            // grave is an ordinary hauling job, but only for a grave whose storage settings
            // accept that corpse and a corpse nobody has forbidden — and almost everything on a
            // RimWorld map arrives forbidden.
            if (EmptyGraveExists(ctx.map))
            {
                if (ReleaseTheDeadForBurial(ctx)) { graveWaitNoted = false; return true; }

                // A grave is on order and the body is still lying there. Nothing more this
                // remedy can do — the hole is dug when somebody digs it — but silence here is
                // indistinguishable from the remedy being broken, and the difference matters:
                // one is a colony short of hands and the other is a bug.
                //
                // Run 104 raised this defect once on day 4 and said nothing for the eleven days
                // that followed, while the largest single mood penalty in the game went on
                // being paid and the game's own "Colonist left unburied" alert stayed up.
                if (!graveWaitNoted)
                {
                    graveWaitNoted = true;
                    Chronicle.Record(ChronicleCategory.Build,
                        "a grave is on order and the body is still where it fell — nothing left to " +
                        "arrange, it wants a colonist with a shovel. If this line is old, the " +
                        "colony is too busy to bury its own dead");
                }
                return false;
            }
            graveWaitNoted = false;

            // Inside the tomb if there is one. Graves used to go wherever there was room within
            // eighteen cells of the body, which scatters them across the base and leaves them
            // out in the weather; a tomb keeps them together and multiplies what colonists get
            // from visiting by up to 1.4. Falls through to the old search when no tomb is
            // standing yet, because a hole today beats a room next week when the penalty for an
            // unburied colonist is the largest single mood hit in the game.
            var tomb = PlannedTomb(ctx);
            if (tomb != null)
            {
                foreach (var cell in tomb.Interior)
                {
                    if (!cell.InBounds(ctx.map)) continue;
                    if (PlacementUtil.TryPlace(ctx.map, grave, cell, Rot4.North, null))
                    {
                        PlacementUtil.MarkHome(ctx.map, cell);
                        return true;
                    }
                }
            }

            var near = defect.thing != null && defect.thing.Spawned
                ? defect.thing.Position : ctx.Origin;

            foreach (var cell in GenRadial.RadialCellsAround(near, 18, true))
            {
                if (PlacementUtil.TryPlace(ctx.map, grave, cell, Rot4.North, null))
                {
                    PlacementUtil.MarkHome(ctx.map, cell);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Makes an existing empty grave actually usable: the grave willing to take the body,
        /// and the body free to be carried.
        ///
        /// Returns true only when something was changed, so the caller does not report a remedy
        /// on a pass where nothing happened.
        /// </summary>
        static bool ReleaseTheDeadForBurial(DirectorContext ctx)
        {
            var map = ctx.map;
            bool changed = false;

            var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            for (int i = 0; i < corpses.Count; i++)
            {
                var corpse = corpses[i] as Corpse;
                if (corpse == null || !corpse.Spawned) continue;
                if (corpse.InnerPawn == null) continue;

                // Colonists and their friends. Raider bodies are not a mood problem and burying
                // them would spend the graves the colony dug for its own.
                if (corpse.InnerPawn.Faction != Faction.OfPlayer) continue;

                if (corpse.IsForbidden(Faction.OfPlayer))
                {
                    corpse.SetForbidden(false, false);
                    changed = true;
                }
            }

            // And a grave that will accept one. A grave's storage filter can exclude the very
            // corpse it was dug for, in which case the haul job never exists to be taken.
            var graveDef = AcDefs.Grave;
            var graves = graveDef != null ? map.listerThings.ThingsOfDef(graveDef) : null;
            for (int i = 0; graves != null && i < graves.Count; i++)
            {
                var building = graves[i] as Building_Grave;
                if (building == null || building.HasCorpse) continue;

                var settings = building.GetStoreSettings();
                if (settings == null || settings.filter == null) continue;
                if (settings.filter.AllowedDefCount > 0) continue;

                settings.filter.SetAllowAll(null);
                changed = true;
            }

            if (changed)
                Chronicle.Record(ChronicleCategory.Build,
                    "unforbade the dead and opened a grave to them — digging one and burying " +
                    "someone in it are two different jobs, and only the first was ever ordered");

            return changed;
        }

        static bool EmptyGraveExists(Map map)
        {
            var grave = AcDefs.Grave;
            if (grave == null || map.listerThings == null) return false;

            var graves = map.listerThings.ThingsOfDef(grave);
            for (int i = 0; i < graves.Count; i++)
            {
                var building = graves[i] as Building_Grave;
                if (building != null && !building.HasCorpse) return true;
            }

            // One already on order counts, or a grave is queued every pass until it is finished.
            var pending = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < pending.Count; i++)
                if (PlacementUtil.BuildTargetOf(pending[i]) == grave) return true;

            var frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
                if (PlacementUtil.BuildTargetOf(frames[i]) == grave) return true;

            return false;
        }

        /// <summary>
        /// Warmth, by whichever means the colony can actually manage today.
        ///
        /// A heater needs electricity and something generating it — a heater on a dead grid is
        /// as much use as the unpowered turrets that started all of this. But gating warmth on
        /// power alone meant a colony without a generator had *no* answer to cold at all, and
        /// EnvironmentCold and SleptInCold duly sat on the unfixable list at every survey, four
        /// mood each, for the whole of a colony's short life.
        ///
        /// A campfire needs neither power nor research and was already defined here, used only
        /// for cooking. It burns wood and it is a fire in a wooden room, which is a real cost —
        /// but freezing is not the safer option, and cold is measured in dead colonists rather
        /// than in mood once it passes ten degrees below what they can bear.
        /// </summary>
        static bool AddHeater(DirectorContext ctx)
        {
            // Only if a room is actually cold.
            //
            // Both of these fire on what colonists *feel*, and a colonist beside a campfire is
            // hot while one across the same room is cold — so the two complaints coexist and the
            // remedies alternate. Watched immediately after the heat survey was added: heater at
            // day 4 18h, cooler at 5 00h, cooler at 5 06h, heater at 5 12h, outdoors 12-18C the
            // whole time. Neither remedy was wrong about a colonist; both were wrong about the
            // room, which is the thing being altered.
            //
            // Reading the temperature is what tells them apart. The thought says somebody is
            // uncomfortable; only the thermometer says which way to move the room.
            if (!AnyRoomBeyond(ctx, ColonyState.ComfortableMin, true)) return false;

            if (ctx.state.workingGenerators > 0 &&
                PlaceInBase(ctx, AcDefs.Heater, 1, RoomPreference.Coldest)) return true;

            // A campfire the colony cannot keep fed heats nothing and eats the wood the stove
            // needs. See FuelUpkeep — the first one always passes; this stops the fourth.
            if (!Furniture.FuelUpkeep.CanKeepAnotherFed(ctx.state, ctx.map, AcDefs.Campfire))
            {
                NoteFuelRefusal(ctx, AcDefs.Campfire);
                return false;
            }
            return PlaceInBase(ctx, AcDefs.Campfire, 1, RoomPreference.Coldest);
        }

        /// <summary>
        /// Whether any planned room sits past a temperature bound — below it when
        /// <paramref name="below"/>, above it otherwise.
        /// </summary>
        static bool AnyRoomBeyond(DirectorContext ctx, float bound, bool below)
        {
            if (ctx.layout == null) return false;

            for (int i = 0; i < ctx.layout.rooms.Count; i++)
            {
                float t = RoomTemperature(ctx.map, ctx.layout.rooms[i]);
                if (below ? t < bound : t > bound) return true;
            }
            return false;
        }

        /// <summary>
        /// Cooling, by whichever means the colony can manage — and it can manage one without
        /// power, which an earlier version of this comment wrongly denied.
        ///
        /// A passive cooler costs fifty wood, needs no research and needs no electricity. It is
        /// the exact counterpart of the campfire on the cold side, and writing "heat has no
        /// low-technology answer" put EnvironmentHot back on the unfixable list for every colony
        /// without a grid — which is most of them, for most of their lives.
        ///
        /// The electric cooler is better where there is power to run it, so it is tried first;
        /// the passive one is what a pre-electricity colony actually gets.
        /// </summary>
        static bool AddCooler(DirectorContext ctx)
        {
            // The thermometer, for the same reason as AddHeater — see the note there.
            if (!AnyRoomBeyond(ctx, ColonyState.ComfortableMax, false)) return false;

            var cooler = AcDefs.Cooler;
            if (ctx.state.workingGenerators > 0 && cooler != null &&
                PlacementUtil.ResearchDone(cooler) &&
                PlaceInBase(ctx, cooler, 1, RoomPreference.Hottest))
                return true;

            var passive = AcDefs.Thing("PassiveCooler");
            if (passive == null || !PlacementUtil.ResearchDone(passive)) return false;

            // The one that cost run 110 its day 12. Seven hoppers stood dry in a heatstroke, the
            // passive coolers among them, while this remedy kept adding more of them — each new
            // cooler drawing on the same woodpile and the same hauling hours as the last.
            if (!Furniture.FuelUpkeep.CanKeepAnotherFed(ctx.state, ctx.map, passive))
            {
                NoteFuelRefusal(ctx, passive);
                return false;
            }
            if (!PlaceInBase(ctx, passive, 1, RoomPreference.Hottest)) return false;

            Chronicle.Record(ChronicleCategory.Build,
                "passive cooler placed — fifty wood, no research and no grid, which is the " +
                "answer a colony without electricity actually has to heat");
            return true;
        }

        /// <summary>
        /// Something to eat off.
        ///
        /// Placed in whatever room has space rather than waiting for a Dining room. The table
        /// was only ever queued as part of one, and that is a discretionary pick after storage,
        /// beds and a kitchen — so a colony that never got comfortable never got a table, and
        /// paid three mood per colonist at every meal indefinitely.
        /// </summary>
        /// <summary>
        /// Somewhere to eat that is not the floor.
        ///
        /// The big table is worth having and it is not worth waiting for. This asked only for
        /// the 2x2, at fifty units of material, and a colony that could not spare fifty units
        /// went on eating off the ground indefinitely — "nowhere to eat off a table" sat in the
        /// unfixable column of survey after survey while the colony had wood for a smaller one.
        ///
        /// So the small table is the fallback rather than nothing. It seats fewer people and
        /// costs twenty-eight, and eating at a bad table carries none of the penalty that eating
        /// off the floor does.
        /// </summary>
        static bool AddTable(DirectorContext ctx)
        {
            // A dining room is where a table belongs, and the planner furnishes one.
            //
            // Without this, the remedy drops a table into the first planned room with a free
            // cell and does it again on the next pass, because the complaint is about the
            // colony and not about that room. Run 53 fired it thirteen times: the Storage room
            // came out classified as a DiningRoom, and so did the Power room, because a table
            // and chairs is all it takes. The room the colony actually eats in is decided by
            // where the table is, so scattering them makes every room a worse version of the
            // one that was supposed to hold it.
            //
            // Same shape as the joy buildings, and the same answer: once the room exists, the
            // remedy stands down and lets the planner do it properly.
            if (ctx.layout != null && ctx.layout.HasRoom(RoomRole.Dining)) return false;

            if (PlaceInBase(ctx, AcDefs.Thing("Table2x2c"), 1)) return true;
            return PlaceInBase(ctx, AcDefs.SmallTable, 1);
        }

        /// <summary>
        /// Somewhere to sit, one stool per colonist.
        ///
        /// This complaint has been in the "cannot fix yet" column of essentially every survey
        /// this project has ever taken, on a belief that seating needed Complex Furniture — true
        /// of an armchair, and of nothing the colony needs. A stool is twenty-five units of
        /// whatever is in store and no research whatsoever.
        ///
        /// One each rather than one in total, because comfort is paid per colonist and a single
        /// stool answers it for whoever reaches it first.
        /// </summary>
        static bool AddSeating(DirectorContext ctx)
        {
            return PlaceInBase(ctx, AcDefs.Stool, ctx.state.colonists);
        }

        /// <summary>What the colony last refused to build for want of hands, so it says it once.</summary>
        static string fuelRefusalNoted = "";

        /// <summary>
        /// Speak the refusal, once per kind.
        ///
        /// A remedy that quietly declines looks exactly like one that was never reached, and
        /// this project has already lost days to that difference — an unexplained absence sends
        /// the next reader looking at the wrong subsystem. Every other gate here says why.
        /// </summary>
        static void NoteFuelRefusal(DirectorContext ctx, ThingDef def)
        {
            string key = def != null ? def.defName : "?";
            if (fuelRefusalNoted == key) return;
            fuelRefusalNoted = key;

            Chronicle.Record(ChronicleCategory.Build,
                "upkeep — " + Furniture.FuelUpkeep.Refusal(ctx.state, def));
        }

        /// <summary>
        /// Whether the colony has open walls that should have the hands instead.
        ///
        /// The planner already throttles wall-building when there are not enough colonists to
        /// finish what is open — run 120: "5 rooms open and only 1 that 1 colonists can finish,
        /// so the hands go to one of them instead of all of them". Upkeep had no such throttle,
        /// so on the same pass it queued three stools and a passive cooler, and the single
        /// surviving colonist furnished a room whose walls were still on the ground.
        ///
        /// Furniture in a room without walls is worse than nothing. It burns with the next
        /// fire — thirteen stools went into one open room in two in-game days — and every hour
        /// spent placing it is an hour not spent closing the shell that would have protected it.
        ///
        /// Comfort only. Heat, light and anything medical still go in: those answer measured
        /// conditions rather than a standing wish, and a colonist freezing in a half-built room
        /// is not helped by a rule about tidiness.
        /// </summary>
        static bool ShellsFirst(DirectorContext ctx, string what)
        {
            if (ctx.layout == null || ctx.map == null) return false;

            var rooms = ctx.layout.rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room == null || !room.wallsQueued) continue;
                if (BasePlannerModule.ShellIsComplete(ctx.map, room)) continue;

                if (shellsFirstNoted != room.role.ToString())
                {
                    shellsFirstNoted = room.role.ToString();
                    Chronicle.Record(ChronicleCategory.Build, string.Format(
                        "holding off on {0} — the {1} room still has no walls, and furniture in an " +
                        "open room burns with the next fire",
                        what, room.role));
                }
                return true;
            }

            shellsFirstNoted = null;
            return false;
        }

        static string shellsFirstNoted;

        /// <summary>
        /// Take down a wall the colony built across somebody's only way out.
        ///
        /// The planner sites rooms against rock on purpose — the Storage room's own explanation
        /// says it "wants to be near rock" — and a wall line run along a rock face can close the
        /// gap between the two. If a colonist is standing in that gap when the last segment goes
        /// up, they are sealed in, and nothing in the director noticed until they starved.
        ///
        /// Solomon died that way on the first biome of the first matrix: sealed at (136,118)
        /// between the Kitchen's west wall and the rock, starving at food 0.00 for a day with
        /// four days of cooked meals a few cells away, then a mental break, then heatstroke in
        /// the fires he set. Two colonists dead by day 6 on the gentlest map in the set.
        ///
        /// The colony undoes its own wall by preference. Mining through rock is the fallback,
        /// because rock is not the director's mistake and takes far longer to cut.
        /// </summary>
        static void FreeAnyoneWalledIn(DirectorContext ctx)
        {
            var trapped = ctx.state.cutOff;
            if (trapped == null || trapped.Count == 0) { walledInNoted = false; return; }

            var map = ctx.map;
            if (map == null) return;

            for (int i = 0; i < trapped.Count; i++)
            {
                var pawn = trapped[i];
                if (pawn == null || !pawn.Spawned) continue;

                if (!walledInNoted)
                {
                    walledInNoted = true;
                    Chronicle.Record(ChronicleCategory.Health, string.Format(
                        "{0} cannot reach any food on this map from {1} — walled in. Opening a " +
                        "way out; this is the colony's own wall, not the weather",
                        pawn.LabelShortCap, pawn.Position));
                }

                if (OpenAWayOut(ctx, pawn)) continue;

                Chronicle.Record(ChronicleCategory.Health, string.Format(
                    "{0} is walled in at {1} and nothing adjacent can be taken down or mined — " +
                    "the pocket has no edge the colony owns",
                    pawn.LabelShortCap, pawn.Position));
            }
        }

        static bool walledInNoted;

        /// <summary>
        /// Find the cell between where they are and where the food is, and order it removed.
        ///
        /// A bounded flood fill over what the pawn can actually stand on, collecting the solid
        /// things around the edge of that pocket. A candidate is worth removing when a cell on
        /// its far side can be reached by somebody who is not trapped — which is the definition
        /// of "this is the wall in the way" rather than a guess at which one it is.
        /// </summary>
        static bool OpenAWayOut(DirectorContext ctx, Pawn pawn)
        {
            var map = ctx.map;
            var seen = new HashSet<IntVec3>();
            var queue = new Queue<IntVec3>();
            var edge = new List<Thing>();

            queue.Enqueue(pawn.Position);
            seen.Add(pawn.Position);

            // Bounded: a pocket big enough to hold a colonist and no food is small. If the fill
            // runs past this the pawn is not in a pocket and something else is wrong.
            const int MaxPocket = 600;

            while (queue.Count > 0 && seen.Count < MaxPocket)
            {
                var cell = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    var next = cell + GenAdj.CardinalDirections[d];
                    if (!next.InBounds(map) || seen.Contains(next)) continue;

                    if (next.Walkable(map)) { seen.Add(next); queue.Enqueue(next); continue; }

                    var blocker = next.GetEdifice(map);
                    if (blocker != null && !edge.Contains(blocker)) edge.Add(blocker);
                }
            }

            // Player-built first: the colony taking down its own wall is cheap, immediate, and
            // is the mistake being corrected. Rock is somebody else's problem and slower to cut.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < edge.Count; i++)
                {
                    var thing = edge[i];
                    if (thing == null || thing.Destroyed) continue;

                    bool mine = thing.Faction != Faction.OfPlayer;
                    if (pass == 0 && mine) continue;
                    if (pass == 1 && !mine) continue;
                    if (mine && !(thing is Mineable)) continue;

                    if (!LeadsSomewhereUseful(ctx, pawn, thing, seen)) continue;

                    // Take the roof off first, if there is one over them.
                    //
                    // A wall holds up the roof beside it, so pulling one out of a sealed pocket
                    // drops that roof — onto the person being rescued. The first run of the
                    // walled scenario produced a Roof collapse incident doing exactly this; the
                    // colonist survived it, which is luck rather than design.
                    //
                    // Stripping a roof is a job a colonist does deliberately and safely, so the
                    // repair spends a pass on it and comes back for the wall once the pocket is
                    // open to the sky. Slower, and it cannot crush the colonist it is digging
                    // out. RoofCollapseUtility is the game's own support test, so this asks the
                    // same question the collapse itself will ask.
                    if (StripRoofFirst(ctx, pawn, seen)) return true;

                    bool ordered = mine ? Mine(map, thing) : PlacementUtil.TryDeconstruct(map, thing);
                    if (!ordered) continue;

                    Chronicle.Record(ChronicleCategory.Build, string.Format(
                        "{0} the {1} at {2} to let {3} out — it is the only thing between them " +
                        "and the rest of the colony",
                        mine ? "mining" : "taking down", thing.def.label ?? thing.def.defName,
                        thing.Position, pawn.LabelShortCap));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether removing this blocks opens onto ground somebody outside can already stand on.
        ///
        /// Without this the colony would cheerfully deconstruct a wall onto more of the same
        /// pocket, or into open rock, and report that it had freed somebody.
        /// </summary>
        static bool LeadsSomewhereUseful(DirectorContext ctx, Pawn trapped, Thing blocker,
                                         HashSet<IntVec3> pocket)
        {
            var map = ctx.map;
            var colonists = map.mapPawns.FreeColonistsSpawned;

            for (int d = 0; d < 4; d++)
            {
                var beyond = blocker.Position + GenAdj.CardinalDirections[d];
                if (!beyond.InBounds(map) || pocket.Contains(beyond)) continue;
                if (!beyond.Walkable(map)) continue;

                for (int i = 0; i < colonists.Count; i++)
                {
                    var other = colonists[i];
                    if (other == null || other == trapped || !other.Spawned) continue;
                    if (map.reachability.CanReach(other.Position, beyond,
                                                 PathEndMode.OnCell, TraverseParms.For(other)))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the pocket still has a roof that would come down on them, and asking for it
        /// to be taken off if so.
        ///
        /// Returns true when it has ordered roof work, which ends the pass — the wall stays up
        /// one more cycle and the colonist stays sealed, which is the safe way round. A pocket
        /// already open to the sky, or one whose roof is held by something other than the wall
        /// being removed, falls straight through to the deconstruct.
        /// </summary>
        static bool StripRoofFirst(DirectorContext ctx, Pawn pawn, HashSet<IntVec3> pocket)
        {
            var map = ctx.map;
            if (map == null || map.roofGrid == null) return false;

            bool ordered = false;
            foreach (var cell in pocket)
            {
                if (!cell.InBounds(map)) continue;
                if (!map.roofGrid.Roofed(cell)) continue;

                // Natural thick rock overhead is not something a colonist can strip, and mining
                // out from under a mountain is the rock branch's job rather than this one.
                var roof = map.roofGrid.RoofAt(cell);
                if (roof != null && roof.isNatural && !roof.isThickRoof) { /* strippable */ }
                else if (roof != null && roof.isThickRoof) continue;

                // Supported by something else anyway: no collapse to avoid.
                if (RoofCollapseUtility.WithinRangeOfRoofHolder(cell, map, false) &&
                    !pocket.Contains(cell)) continue;

                PlacementUtil.MarkNoRoof(map, cell);
                ordered = true;
            }

            if (ordered && !roofStripNoted)
            {
                roofStripNoted = true;
                Chronicle.Record(ChronicleCategory.Build, string.Format(
                    "taking the roof off around {0} before touching the wall — pulling a wall out " +
                    "from under a roof drops it, and they are standing under this one",
                    pawn.LabelShortCap));
            }
            if (!ordered) roofStripNoted = false;
            return ordered;
        }

        static bool roofStripNoted;

        static bool Mine(Map map, Thing rock)
        {
            try
            {
                if (map.designationManager.DesignationOn(rock, DesignationDefOf.Mine) != null)
                    return true;
                map.designationManager.AddDesignation(new Designation(rock, DesignationDefOf.Mine));
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Say which buildings are dry, not how many.
        ///
        /// The vitals line gained "2 DRY" and it is not enough: two dry things could be a stove
        /// and a generator, which is a colony about to stop eating and stop making power, or two
        /// torch lamps, which is a colony that will be slightly darker. Run 109 showed "2 DRY"
        /// standing for fourteen game-hours with Hauling already at 4.0 and no way to tell from
        /// the log whether that mattered. The list was captured for this and then only counted —
        /// the field's own doc comment says "so the chronicle can name the thing rather than
        /// count it", which is a note to somebody who did not then do it.
        ///
        /// Spoken on change, like the pen's enclosure verdict, so a long dry spell is one line
        /// rather than one per pass.
        /// </summary>
        static void NameWhatIsDry(DirectorContext ctx)
        {
            var dry = ctx.state.fuelStarved;
            if (dry == null || dry.Count == 0)
            {
                if (fuelNoted.Length > 0)
                {
                    Chronicle.Record(ChronicleCategory.Economy,
                        "fuel: everything that burns is loaded again (was " + fuelNoted + ")");
                    fuelNoted = "";
                }
                return;
            }

            var names = new List<string>();
            for (int i = 0; i < dry.Count; i++)
            {
                var def = dry[i].def;
                var label = def != null ? (def.label ?? def.defName) : "something";
                if (!names.Contains(label)) names.Add(label);
            }
            names.Sort(StringComparer.Ordinal);
            string now = string.Join(", ", names.ToArray());
            if (now == fuelNoted) return;

            fuelNoted = now;

            // Name the lever that actually applies. "Refuelling is Hauling work" is true and was
            // the wrong half of the answer for most of run 116: there was nothing to haul,
            // because nobody had cut it. Hauling is the lever once logs exist; before that it is
            // PlantCutting, and saying the first while the second is what is missing sends the
            // next reader — and the next fix — at the wrong subsystem.
            bool uncut = Furniture.FuelBudget.FuelUncut(ctx.state.buildingsWantingFuel,
                                                       ctx.state.fuelOnHand,
                                                       ctx.state.fuelStanding);
            Chronicle.Record(ChronicleCategory.Economy, string.Format(
                "fuel: {0} waiting on wood — {1}; {2}",
                dry.Count, now,
                uncut
                    ? "no logs cut and " + ctx.state.fuelStanding + " standing, so chopping is " +
                      "the lever, not hauling"
                    : "refuelling is Hauling work, so that is the lever, not whatever the bench does"));
        }

        /// <summary>
        /// Put a seat against everything that cannot be used without one.
        ///
        /// This is not a defect remedy and deliberately sits outside the survey, because the
        /// survey reads colonist thoughts and this fault produces none. A colonist who wants to
        /// play chess, finds no chair, and walks away is not unhappy about it in any way the game
        /// records — they simply take their joy somewhere worse, or take none. The only party
        /// that ever complains is RimWorld's own alert bar, which is where this was finally
        /// spotted: `Chess table needs chairs`, on screen, beside a colony at mood 0.15.
        ///
        /// On the eating side it is worse than a blind spot, because there the survey *does* see
        /// something and its remedy cannot work. `AteWithoutTable` raises `NoTable`, `AddTable`
        /// puts down a table, the pawn still cannot reach it — a pawn reaches a table only by
        /// finding a chair — and the thought recurs unchanged. Run 107 built eight tables that
        /// way. A remedy that does not clear its own complaint will run for ever, and the loop
        /// closes not because either rule is wrong but because neither knows what the other
        /// requires.
        ///
        /// Runs every pass rather than once at placement, because the requirement is a property
        /// of a standing arrangement and not of the moment something was built: chairs burn, get
        /// deconstructed, and arrive on the map inside ruins the colony claims. Same reasoning as
        /// asking every pass whether the pen is still enclosed.
        /// </summary>
        static void SeatWhatNeedsSeating(DirectorContext ctx)
        {
            var map = ctx.map;
            if (map == null) return;

            var seat = Furniture.SeatingRule.CheapestSeat();
            if (seat == null) return;

            var stuff = PlacementUtil.ChooseStuff(map, seat,
                FireRisk.StonePreference(ctx, FireRisk.Assess(map, ctx.state)));
            if (seat.MadeFromStuff && stuff == null) return;

            var things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing == null || thing.Faction != Faction.OfPlayer) continue;

                // The blueprint counts as the table. Waiting for it to finish leaves a window in
                // which the survey sees an unseatable colony and orders another table every pass.
                var subject = Furniture.SeatingRule.Subject(thing);
                if (subject == null) continue;

                // How many people could be sitting here at once. A dining table is for the
                // colony, so it is asked of the colony; a joy building only has to be *usable*,
                // and one seat is what usable means. Neither number is a guess about this
                // particular building.
                int wanted = subject.surfaceType == SurfaceType.Eat
                    ? Math.Max(1, ctx.state.colonists)
                    : 1;

                int have = 0;
                var free = new List<IntVec3>();
                foreach (var cell in Furniture.SeatingRule.Adjacent(thing))
                {
                    if (!cell.InBounds(map)) continue;

                    bool occupied = false;
                    var here = cell.GetThingList(map);
                    for (int t = 0; t < here.Count; t++)
                    {
                        // A seat already there or on its way counts. Treating a blueprint as
                        // absent is exactly how the colony ended up with eight tables.
                        if (Furniture.SeatingRule.IsSeat(here[t].def) ||
                            Furniture.SeatingRule.IsSeat(PlacementUtil.BuildTargetOf(here[t])))
                        { have++; occupied = true; break; }

                        if (here[t].def.passability != Traversability.Standable ||
                            PlacementUtil.HasAnyConstructionAt(map, cell))
                        { occupied = true; break; }
                    }
                    if (!occupied) free.Add(cell);
                }

                for (int f = 0; f < free.Count && have < wanted; f++)
                    if (PlacementUtil.TryPlace(map, seat, free[f], Rot4.North, stuff))
                    {
                        have++;
                        Chronicle.Record(ChronicleCategory.Build, string.Format(
                            "seating: a {0} beside the {1} — it cannot be used without one",
                            seat.label ?? seat.defName, subject.label ?? subject.defName));
                    }
            }
        }

        /// <summary>Somewhere to play. Horseshoes needs no research and barely any material.</summary>
        /// <summary>
        /// Candidate joy buildings, easiest to place first.
        ///
        /// A game of Ur needs no research and no clear ground; horseshoes needs a throwing lane
        /// and chess and poker need Complex Furniture. Ordered so the colony that most needs
        /// cheering up — young, unresearched, living in one small room — is served by the first
        /// entry rather than by none of them.
        /// </summary>
        static readonly string[] JoyBuildings =
        {
            "GameOfUrBoard", "ChessTable", "PokerTable", "HorseshoesPin", "BilliardsTable"
        };

        /// <summary>
        /// Something to do that is not work.
        ///
        /// This asked for a horseshoes pin and nothing else, which carries
        /// `PlaceWorker_WatchArea` — it needs a clear lane to throw down. Remedies are placed
        /// inside the planned rooms, and a seven-by-seven room has a five-by-five interior with
        /// beds in it, so every candidate cell was refused. The complaint was therefore attempted
        /// and failed on every pass for the whole of a colony's life: seven times in six-hour
        /// intervals in one run, with `Cheerless` at full severity throughout and the colony
        /// eventually dying at zero mood.
        ///
        /// Chosen on what will actually stand in the space available, not on a def existing —
        /// the same rule this codebase already applies to stoves and generators.
        /// </summary>
        static bool AddRecreation(DirectorContext ctx)
        {
            // One that is already coming is the whole remedy.
            //
            // The list above was itself the fix for a previous version of this, where the only
            // joy building asked for was a horseshoes pin that never fit — so falling through to
            // the next def on failure is deliberate. What it could not distinguish is *why* the
            // placement failed. "Nowhere it fits" should try the next one; "there is already one
            // of these on the way" should stop, because the colony is not short of a chess table,
            // it is short of a builder.
            //
            // Watched live in run 35: seven joy buildings queued between day 1 18h and day 3 06h,
            // one every six hours — Ur, Ur, chess, chess, poker, poker, horseshoes — with the
            // Cheerless complaint pinned at severity 1.00 throughout, because not one of them was
            // ever built. Three colonists cannot build a game table every six hours on top of
            // walls, so the construction queue filled with duplicates and the Bedroom sited on
            // day 1 was still standing open on day 4 with the colony sleeping on the ground.
            //
            // Only pending ones hold the remedy back. A joy building that gains finished status
            // stops blocking, so a colony that genuinely outgrows one table can still get a
            // second — it just cannot order five at once while none of them have been started.
            if (AnyJoyPending(ctx)) return false;

            // A rec room is where these belong, and once one *has something in it* the planner
            // is furnishing it. Scattering another game table through the kitchen would only
            // take the recreation out of the room the mood bonus is paid for — RimWorld scores
            // joy by where it was taken, so a chess table in a bedroom earns nothing the rec
            // room would have earned.
            //
            // Planned is not the same as standing, and the difference is a loop I put here
            // myself. Mood collapse now makes the planner site a recreation room, and this
            // stood down the moment it was sited — so a colony in the exact crisis the room was
            // ordered for lost its only immediate answer and waited days for walls. Watched in
            // run 55: mood 0.22 at two colonists, 0.08 at one, fed and uninjured throughout.
            //
            // The fast remedy therefore keeps working until the slow one has actually produced
            // something to use.
            if (RecRoomHasSomethingToDo(ctx)) return false;

            for (int i = 0; i < JoyBuildings.Length; i++)
            {
                var def = AcDefs.Thing(JoyBuildings[i]);
                if (def == null || !PlacementUtil.ResearchDone(def)) continue;
                if (!PlaceInBase(ctx, def, 1)) continue;

                Chronicle.Record(ChronicleCategory.Build,
                    "recreation: placed a " + (def.label ?? def.defName) +
                    " — the first joy building that would actually fit the space");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a planned recreation room actually holds a joy building yet, built or coming.
        ///
        /// The test the remedy stands down on. A room that exists on paper is not somewhere to
        /// play chess.
        /// </summary>
        static bool RecRoomHasSomethingToDo(DirectorContext ctx)
        {
            if (ctx.layout == null || ctx.map == null) return false;

            for (int r = 0; r < ctx.layout.rooms.Count; r++)
            {
                var room = ctx.layout.rooms[r];
                if (room.role != RoomRole.Recreation) continue;

                for (int i = 0; i < JoyBuildings.Length; i++)
                {
                    var def = AcDefs.Thing(JoyBuildings[i]);
                    if (def == null) continue;
                    if (CountIn(ctx.map, room, def) > 0) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether any joy building is already blueprinted or part-built anywhere in the base.
        ///
        /// Deliberately blind to finished ones: a table that exists is capacity the colony has,
        /// a table that is queued is capacity it is still waiting on, and only the second is a
        /// reason to refuse to order more.
        /// </summary>
        static bool AnyJoyPending(DirectorContext ctx)
        {
            if (ctx.map == null || ctx.layout == null) return false;

            for (int i = 0; i < JoyBuildings.Length; i++)
            {
                var def = AcDefs.Thing(JoyBuildings[i]);
                if (def == null) continue;

                for (int r = 0; r < ctx.layout.rooms.Count; r++)
                    if (PendingIn(ctx.map, ctx.layout.rooms[r], def)) return true;
            }
            return false;
        }

        /// <summary>Whether this def has a blueprint or a frame standing in the room.</summary>
        static bool PendingIn(Map map, PlannedRoom room, ThingDef def)
        {
            if (map == null || def == null) return false;

            foreach (var cell in room.Interior)
            {
                if (!cell.InBounds(map)) continue;

                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var blueprint = things[i] as Blueprint;
                    if (blueprint != null && blueprint.def.entityDefToBuild == def) return true;

                    var frame = things[i] as Frame;
                    if (frame != null && frame.def.entityDefToBuild == def) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Puts one of something into the first planned room with space for it.
        ///
        /// Some complaints belong to no particular room — nobody has anywhere to eat, nobody has
        /// anything to do — so the remedy chooses rather than the survey.
        /// </summary>
        static bool PlaceInBase(DirectorContext ctx, ThingDef def, int count)
        {
            return PlaceInBase(ctx, def, count, RoomPreference.Any);
        }

        /// <summary>
        /// How many of this thing already stand in the room, counting anything on its way — a
        /// blueprint or a frame is one that is coming, and treating it as absent is what queues
        /// the duplicate.
        /// </summary>
        static int CountIn(Map map, PlannedRoom room, ThingDef def)
        {
            if (map == null || def == null) return 0;

            int found = 0;
            foreach (var cell in room.Interior)
            {
                if (!cell.InBounds(map)) continue;

                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var thing = things[i];
                    if (thing == null) continue;
                    if (thing.def == def) { found++; continue; }

                    var blueprint = thing as Blueprint;
                    if (blueprint != null && blueprint.def.entityDefToBuild == def) { found++; continue; }

                    var frame = thing as Frame;
                    if (frame != null && frame.def.entityDefToBuild == def) found++;
                }
            }
            return found;
        }

        enum RoomPreference { Any, Hottest, Coldest }

        /// <summary>
        /// As above, but able to pick the room by its own temperature.
        ///
        /// Temperature is a property of a room, not of the map: one room can be baking while the
        /// one next door is fine, because heat is held by walls and moved by what is inside them.
        /// A cooler dropped into "the first room with space" is therefore as likely to cool a
        /// room nobody was complaining about as the one that drove the complaint, and the colony
        /// pays the wood either way.
        /// </summary>
        static bool PlaceInBase(DirectorContext ctx, ThingDef def, int count, RoomPreference prefer)
        {
            if (def == null || ctx.layout == null) return false;

            var stuff = PlacementUtil.ChooseStuff(ctx.map, def,
                FireRisk.StonePreference(ctx, FireRisk.Assess(ctx.map, ctx.state)));

            var rooms = new List<PlannedRoom>(ctx.layout.rooms);
            if (prefer != RoomPreference.Any)
            {
                var map = ctx.map;
                bool hottestFirst = prefer == RoomPreference.Hottest;
                rooms.Sort(delegate(PlannedRoom a, PlannedRoom b)
                {
                    float ta = RoomTemperature(map, a);
                    float tb = RoomTemperature(map, b);
                    return hottestFirst ? tb.CompareTo(ta) : ta.CompareTo(tb);
                });
            }

            for (int i = 0; i < rooms.Count; i++)
            {
                // A room still waiting for the thing it exists for is not a room with space.
                //
                // These remedies take whatever room has a free cell, and a research bench is
                // three cells by two — so a 2x2 table and a torch lamp dropped into a small
                // Research room leave nowhere its own bench will fit, and the planner then
                // re-queues that bench for ever. Watched live, with the diagnostic naming it
                // exactly: "SimpleResearchBench was refused at all 5 placeable cells in the
                // interior — its footprint does not fit anywhere among what is already there".
                //
                // Which makes this a second, independent reason research never happens: not only
                // does the plan rarely reach the long-term horizon, but when it does the room it
                // asks for has already been furnished with somebody else's answer to a different
                // complaint. The same discipline as not repurposing the room the plan wants.
                if (BasePlannerModule.KeyFurnitureMissing(ctx, rooms[i])) continue;

                // A room that already has one does not need a second.
                //
                // This placed into the first workable cell of the coldest room and never asked
                // what was already standing there, so every time the cold-room complaint came
                // back — which in a boreal winter is every few hours — another campfire went
                // down beside the last. Counted eleven of them in one colony at -15C.
                //
                // Three separate costs, none of them obvious from the remedy itself: eleven open
                // flames inside a wooden base, which is how run 5 burned to death; eleven work
                // tables, because a campfire is one, each collecting its own duplicate set of
                // cooking bills; and the wood. `PlaceMany` has topped up to a count rather than
                // adding to it since the duplicate-crafting-spot bug, and this is the same rule
                // arriving late in the other placement path.
                if (CountIn(ctx.map, rooms[i], def) >= count) continue;

                foreach (var cell in rooms[i].Interior)
                {
                    if ((cell - rooms[i].Door).LengthHorizontalSquared <= 2) continue;
                    if (PlacementUtil.TryPlace(ctx.map, def, cell, Rot4.North, stuff)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A planned room's actual temperature, or the outdoor reading when it has no walls yet.
        /// </summary>
        static float RoomTemperature(Map map, PlannedRoom planned)
        {
            try
            {
                var room = planned.Center.GetRoom(map);
                if (room != null && !room.UsesOutdoorTemperature) return room.Temperature;
                return map.mapTemperature.OutdoorTemp;
            }
            catch (Exception) { return 0f; }
        }

        /// <summary>Roofs every cell the building stands on, which is cheaper than moving it.</summary>
        static bool RoofOver(DirectorContext ctx, ColonyDefect defect)
        {
            if (defect.thing == null || !defect.thing.Spawned) return false;

            int marked = 0;
            foreach (var cell in defect.thing.OccupiedRect())
            {
                if (PlacementUtil.TryMarkRoofSupported(ctx.map, cell)) marked++;
                PlacementUtil.MarkHome(ctx.map, cell);
            }
            return marked > 0;
        }

        /// <summary>
        /// Moves the building somewhere it belongs.
        ///
        /// Three routes, cheapest first. Anything minifiable is carried to a sheltered spot
        /// intact, which costs nothing at all — the game has a reinstall job for exactly this.
        /// Failing a spot to put it, it is uninstalled and kept as an item for later. Only
        /// something that cannot be picked up is knocked down, and that is the expensive
        /// option: `resourcesFractionWhenDeconstructed` is per-def and several buildings return
        /// none of their cost.
        /// </summary>
        static bool Relocate(DirectorContext ctx, ColonyDefect defect)
        {
            var thing = defect.thing;
            if (thing == null || !thing.Spawned) return false;
            if (PlacementUtil.AlreadyOrdered(ctx.map, thing)) return false;

            // Never pull down the only thing generating, or the colony loses its grid to fix a
            // risk that has not happened yet.
            if (IsLastWorkingGenerator(ctx, thing)) return false;

            if (PlacementUtil.Movable(thing))
            {
                var shelter = FindShelteredSpot(ctx, thing);
                if (shelter.IsValid &&
                    PlacementUtil.TryReinstall(ctx.map, thing, shelter, thing.Rotation))
                {
                    defect.what += " — moving it under cover at " + shelter;
                    return true;
                }

                // Nowhere to put it yet. Lift it anyway rather than leaving it in the rain; it
                // keeps its quality and every unit of material as an item.
                if (PlacementUtil.TryUninstall(ctx.map, thing))
                {
                    defect.what += " — uninstalling it to place later";
                    return true;
                }
                return false;
            }

            return PlacementUtil.TryDeconstruct(ctx.map, thing);
        }

        /// <summary>A roofed cell inside one of the planner's rooms that will take this thing.</summary>
        static IntVec3 FindShelteredSpot(DirectorContext ctx, Thing thing)
        {
            if (ctx.layout == null) return IntVec3.Invalid;

            var rooms = ctx.layout.rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                foreach (var cell in rooms[i].Interior)
                {
                    if (!cell.InBounds(ctx.map)) continue;
                    if (ctx.map.roofGrid == null || !ctx.map.roofGrid.Roofed(cell)) continue;
                    if (PlacementUtil.HasAnyConstructionAt(ctx.map, cell)) continue;

                    var report = GenConstruct.CanPlaceBlueprintAt(thing.def, cell, thing.Rotation,
                                                                  ctx.map, false, thing, thing);
                    if (report.Accepted) return cell;
                }
            }
            return IntVec3.Invalid;
        }

        static bool IsLastWorkingGenerator(DirectorContext ctx, Thing thing)
        {
            var trader = thing.TryGetComp<CompPowerTrader>();
            if (trader == null || trader.Props == null) return false;
            if (trader.Props.PowerConsumption >= 0f) return false;

            return ctx.state.workingGenerators <= 1;
        }

        static bool AddLight(DirectorContext ctx, ColonyDefect defect)
        {
            var lamp = AcDefs.Torch;
            if (lamp == null || defect.room == null) return false;

            // A torch is the cheapest thing here and the easiest to over-place, and it burns the
            // same wood as everything else.
            if (!Furniture.FuelUpkeep.CanKeepAnotherFed(ctx.state, ctx.map, lamp))
            {
                NoteFuelRefusal(ctx, lamp);
                return false;
            }

            var stuff = PlacementUtil.ChooseStuff(ctx.map, lamp,
                FireRisk.StonePreference(ctx, FireRisk.Assess(ctx.map, ctx.state)));

            foreach (var cell in defect.room.Cells)
            {
                if (PlacementUtil.TryPlace(ctx.map, lamp, cell, Rot4.North, stuff)) return true;
            }
            return false;
        }

        /// <summary>
        /// Leaves one bed and takes the rest out, turning a barracks back into a bedroom.
        ///
        /// The planner then wants beds it no longer has and reserves another room for them,
        /// which is the outcome worth having: an awful barracks is -7 mood against an awful
        /// bedroom's -2, so the room count is what matters, not the decoration.
        /// </summary>
        static bool RemoveSurplusBeds(DirectorContext ctx, ColonyDefect defect)
        {
            if (defect.room == null) return false;

            // Affording to separate them is not the same as having beds to spare.
            //
            // The survey already declines to call sharing a fault when the colony is too poor to
            // fix it. That guard reads material, and material was never the binding constraint
            // here: a colony with 1,441 units of wood, two bedrooms and one bed pulled that bed
            // out of the barracks to cure a -3 SharedBedroom, and left two of three colonists on
            // the ground paying -4 SleptOnGround and -4 SleptOutside apiece. Richer made it
            // worse, because means is what unlocks this remedy.
            //
            // A bed is only surplus once everyone has one. Watched live: beds stuck at 1 for
            // three colonists across an entire epoch, with the other two beds lying in the room
            // as uninstalled crates.
            if (ctx.state.colonistBeds <= ctx.state.colonists) return false;

            // Counted over the planner's own footprint, not the game's room.
            //
            // The last round of this made the two sides agree on the target *number*. They still
            // disagreed about the region it applied to: the planner tops up beds inside
            // PlannedRoom.Rect and this counted them inside RimWorld's Room, which is bounded by
            // whatever walls actually exist. The two are the same only for a finished, sealed
            // room — so a room still going up, or one whose door leaks into the corridor, has one
            // count for adding and another for removing, and a bed comes out and goes back in
            // about twice a day for ever.
            //
            // Agreeing on the number was half of it; this is the other half. The survey above
            // still works in RimWorld rooms, and should: a SharedBedroom thought comes from the
            // room a pawn actually slept in, which is the game's notion, not the planner's.
            // It is only the arithmetic of how many to leave that has to match.
            var planned = PlannedRoomAt(ctx.layout, defect.cell);

            var beds = new List<Building_Bed>();
            int counted = 0;

            if (planned != null)
            {
                foreach (var cell in planned.Rect)
                {
                    if (!cell.InBounds(ctx.map)) continue;

                    var atCell = cell.GetThingList(ctx.map);
                    for (int i = 0; i < atCell.Count; i++)
                    {
                        var thing = atCell[i];
                        if (thing == null || thing.Position != cell) continue;

                        // Beds on the way count too, or the two sides disagree again the moment
                        // the planner queues one and this cannot see it yet.
                        if (PlacementUtil.BuildTargetOf(thing) != AcDefs.Bed) continue;
                        counted++;

                        var bed = thing as Building_Bed;
                        if (bed != null && bed.Spawned && bed.ForColonists && !bed.Medical)
                            beds.Add(bed);
                    }
                }
            }
            else
            {
                var things = defect.room.ContainedAndAdjacentThings;
                for (int i = 0; i < things.Count; i++)
                {
                    var bed = things[i] as Building_Bed;
                    if (bed == null || !bed.Spawned) continue;
                    if (bed.GetRoom() != defect.room) continue;
                    if (bed.ForColonists && !bed.Medical) beds.Add(bed);
                }
                counted = beds.Count;
            }

            // Down to the number the planner fills a bedroom to, not down to one.
            //
            // These two held different opinions about the same room. The planner tops a bedroom
            // up to BedsPerRoom; this drove it toward a single bed; and between them a bed came
            // out and went back in every couple of days, for ever. Reading the same number makes
            // them agree whichever runs first, which is the same discipline that keeps Reclaim
            // and the sharing rule from sawing at each other.
            int target = BuildingMeans.BedsPerRoom(
                BuildingMeans.Assess(ctx.state.usableMaterial, ctx.state.colonists),
                AcMath.Clamp(ctx.GeneInt(Genes.BaseBedsPerRoom), 1, 4),
                ctx.state.colonists);
            if (target < 1) target = 1;

            if (counted <= target) return false;

            // Uninstalled, not deconstructed. The colony wants this bed — just not here — and
            // uninstalling keeps it whole, quality included, ready to be set down in the room
            // the planner is about to reserve. Knocking it down would return a fraction of the
            // material and none of the workmanship.
            //
            // One at a time, so the colony is never left with nowhere to sleep while the
            // replacement rooms are still going up.
            // Only the beds beyond the target come out, and only ones actually standing — a
            // blueprint counts toward the total but there is nothing there to uninstall.
            int surplus = counted - target;
            int from = beds.Count - surplus;
            if (from < 0) from = 0;   // the surplus is blueprints, which nobody can uninstall

            for (int i = from; i < beds.Count; i++)
            {
                if (PlacementUtil.MarkedForDeconstruction(ctx.map, beds[i])) continue;
                if (PlacementUtil.TryUninstall(ctx.map, beds[i])) return true;
            }
            return false;
        }

        /// <summary>The planner's room containing this cell, or null if it is not in one.</summary>
        static PlannedRoom PlannedRoomAt(BaseLayout layout, IntVec3 cell)
        {
            if (layout == null) return null;

            for (int i = 0; i < layout.rooms.Count; i++)
            {
                if (layout.rooms[i].Rect.Contains(cell)) return layout.rooms[i];
            }
            return null;
        }

        /// <summary>
        /// Takes a surplus room apart and gets its material back.
        ///
        /// The room leaves the layout at the same time, which is what stops this from being a
        /// loop: the planner counts rooms it has reserved, so a room deconstructed but still on
        /// the books would simply be rebuilt, and the colony would saw away at itself forever.
        ///
        /// Everything goes — walls, door and whatever was inside — because the point is the
        /// material, and furniture standing in an unwalled square is just something else to
        /// deteriorate.
        /// </summary>
        /// <summary>
        /// Asks the colony to strip the roof off a room it is about to pull down.
        ///
        /// Only the room's own cells, and never one shared with a neighbour — the same rule the
        /// demolition itself follows, for the same reason. Taking the roof off the room next
        /// door is the problem this is meant to prevent, not a smaller version of it.
        /// </summary>
        static void MarkForNoRoof(DirectorContext ctx, PlannedRoom planned)
        {
            var area = ctx.map.areaManager != null ? ctx.map.areaManager.NoRoof : null;
            if (area == null) return;

            foreach (var cell in planned.Rect)
            {
                if (!cell.InBounds(ctx.map)) continue;
                if (SharedWithAnotherRoom(ctx.layout, planned, cell)) continue;
                if (ctx.map.roofGrid == null || !ctx.map.roofGrid.Roofed(cell)) continue;

                // Natural rock overhead is not this room's roof and cannot be taken off by
                // asking; ordering it would leave a standing job nobody can finish.
                var roof = ctx.map.roofGrid.RoofAt(cell);
                if (roof != null && roof.isNatural) continue;

                // And withdraw any standing request to roof it. The planner marked this room's
                // interior for BuildRoof when it went up, and that mark outlives the room —
                // leaving the cell in both areas, which is a colonist roofing and unroofing it
                // until something else kills them.
                PlacementUtil.ClearBuildRoof(ctx.map, cell);
                area[cell] = true;
            }
        }

        static bool Reclaim(DirectorContext ctx, ColonyDefect defect)
        {
            var planned = defect.plannedRoom;
            if (planned == null || ctx.layout == null) return false;

            // The roof comes off before the walls that hold it up.
            //
            // Reclaiming took the walls and left the roof, which is how a roof behaves when its
            // support disappears: it falls, on whatever is underneath. Run 61 reclaimed four
            // rooms across days 11 to 13, logged eight roof collapses, and lost Harvey at 0.63
            // health — the director killed its own colonist with its own demolition, in a
            // colony that was otherwise having the best run of the night.
            //
            // Marking the area no-roof is what a player does before pulling a building down: it
            // makes stripping the roof an ordinary construction job, so the supports are not
            // load-bearing by the time they go. It reduces the risk rather than removing it —
            // nothing here sequences the roof job ahead of the deconstruct orders — but a roof
            // nobody has asked to be removed is guaranteed to fall, and this at least asks.
            MarkForNoRoof(ctx, planned);

            int marked = 0;
            foreach (var cell in planned.Rect)
            {
                if (!cell.InBounds(ctx.map)) continue;

                // Neighbouring rooms share a wall by design — the layout budges them together
                // to keep the base cheap. Pulling one down cell by cell would therefore breach
                // the room next door and leave it open to the sky, which is a far worse problem
                // than the one being solved.
                if (SharedWithAnotherRoom(ctx.layout, planned, cell)) continue;

                // Anything still only ordered is withdrawn rather than built and then knocked
                // down again. Finishing a wall in order to demolish it spends the material twice
                // over, which is the exact opposite of what reclaiming is for.
                marked += PlacementUtil.CancelConstructionAt(ctx.map, cell);

                var things = cell.GetThingList(ctx.map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    var thing = things[i];
                    if (thing == null || thing.Faction != Faction.OfPlayer) continue;
                    if (thing.def == null || thing.def.category != ThingCategory.Building) continue;

                    // Furniture comes up whole and keeps its quality; only the shell is knocked
                    // down, because walls cannot be carried.
                    if (PlacementUtil.Movable(thing))
                    {
                        if (PlacementUtil.TryUninstall(ctx.map, thing)) marked++;
                        continue;
                    }

                    if (PlacementUtil.TryDeconstruct(ctx.map, thing)) marked++;
                }
            }

            if (marked == 0) return false;

            // Off the books, so the planner treats the slot as gone rather than as a room it
            // still owns and ought to finish.
            ctx.layout.rooms.Remove(planned);
            return true;
        }

        /// <summary>True when a cell also belongs to some other room the colony is keeping.</summary>
        static bool SharedWithAnotherRoom(BaseLayout layout, PlannedRoom keeping, IntVec3 cell)
        {
            if (layout == null) return false;

            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var other = layout.rooms[i];
                if (other == keeping) continue;
                if (other.Rect.Contains(cell)) return true;
            }
            return false;
        }

        /// <summary>
        /// Something worth looking at. A lamp first if the room somehow still has none, since it
        /// is both beauty and light; otherwise a plant pot, which is the cheapest beauty in the
        /// game that does not need a skilled crafter.
        /// </summary>
        /// <summary>How many plant pots one room is allowed to answer a beauty complaint with.</summary>
        const int PotsPerRoom = 2;

        static bool AddBeauty(DirectorContext ctx, ColonyDefect defect)
        {
            if (defect.room == null) return false;

            if (!DefectSurvey.HasLight(ctx.map, defect.room) && AddLight(ctx, defect)) return true;

            var pot = AcDefs.Thing("PlantPot");
            if (pot == null) return false;

            // A room has twenty-five cells and this walks them looking for a free one.
            //
            // TryPlace refuses a cell that already holds a pot, so the search simply moved to the
            // next cell and put another one down — one every six hours for as long as anybody was
            // unhappy about beauty, until the room was a nursery. The same shape as the joy
            // buildings above and the campfires before them: the remedy fires on a complaint, the
            // complaint clears only once the thing is *built*, and nothing was counting what was
            // already on its way.
            //
            // Two is the cap. Beauty from pots has sharply diminishing returns and the complaint
            // that drives this is usually really about floors and walls, which cost far more than
            // a remedy should spend without being asked.
            if (CountInRoom(ctx.map, defect.room, pot) >= PotsPerRoom) return false;

            var stuff = PlacementUtil.ChooseStuff(ctx.map, pot, 0.5f);
            foreach (var cell in defect.room.Cells)
            {
                if (PlacementUtil.TryPlace(ctx.map, pot, cell, Rot4.North, stuff)) return true;
            }
            return false;
        }

        /// <summary>
        /// How many of this thing stand in a game room, counting anything on its way.
        ///
        /// The <see cref="CountIn"/> above does this for a <see cref="PlannedRoom"/>; remedies
        /// that were handed a defect work from the game's own <see cref="Room"/> instead, and
        /// had no equivalent — which is how the duplicate rule ended up applying to one half of
        /// the remedies and not the other.
        /// </summary>
        static int CountInRoom(Map map, Room room, ThingDef def)
        {
            if (map == null || room == null || def == null) return 0;

            int found = 0;
            foreach (var cell in room.Cells)
            {
                if (!cell.InBounds(map)) continue;

                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var thing = things[i];
                    if (thing == null) continue;
                    if (thing.def == def) { found++; continue; }

                    var blueprint = thing as Blueprint;
                    if (blueprint != null && blueprint.def.entityDefToBuild == def) { found++; continue; }

                    var frame = thing as Frame;
                    if (frame != null && frame.def.entityDefToBuild == def) found++;
                }
            }
            return found;
        }
    }
}
