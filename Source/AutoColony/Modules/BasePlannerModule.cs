using System;
using System.Collections.Generic;
using AutoColony.Learning;
using AutoColony.Upkeep;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Lays out and builds the colony.
    ///
    /// The plan is a corridor with rooms budding off both sides, which is deliberately dull:
    /// it tiles indefinitely, shares walls between neighbours so it stays cheap, and every
    /// room is reachable and roofable without pathing tricks. Room dimensions, wall material
    /// and beds per room come from the genome, and which room to build next is a bandit
    /// choice, so the interesting decisions stay learnable while the geometry stays reliable.
    ///
    /// Construction is queued as blueprints and left to the colonists' own job system — the
    /// director never spawns anything directly, so the colony still has to earn what it builds.
    /// </summary>
    public class BasePlannerModule : DirectorModule
    {
        public const string BanditId = "build";

        public override string Name { get { return "Base planner"; } }
        /// <summary>
        /// Only for the one thing this module does that cannot wait: putting a bed under a
        /// colonist who is on the floor with nowhere to be carried to. Ordinary building is
        /// exactly the sort of work that should keep its place in the rotation.
        /// </summary>
        public override bool Urgent(DirectorContext ctx)
        {
            return NeedsAnEmergencyBed(ctx);
        }

        /// <summary>
        /// Whether somebody is on the floor with nowhere to be carried to.
        ///
        /// Counting beds against colonists answers that most of the time and not when there is a
        /// fire. A colony can own four beds, have none of them occupied, and still have nowhere
        /// to put anybody, because a bed the fire is about to reach is not a rescue — it is a
        /// slower way to the same end. Every bed count in the director read as "no bed needed"
        /// in exactly that situation.
        /// </summary>
        static bool NeedsAnEmergencyBed(DirectorContext ctx)
        {
            if (ctx.state.colonistsDowned == 0) return false;

            if (ctx.state.colonistBeds < ctx.state.colonists) return true;

            return ctx.state.fires > 0 && ctx.state.freeBedsAwayFromFire == 0;
        }

        public override int IntervalTicks { get { return 3750; } }

        /// <summary>Stop queueing work when this much construction is already outstanding.</summary>
        const int MaxPendingConstruction = 60;

        /// <summary>Blueprints placed in a single pass, to spread the cost over time.</summary>
        const int MaxPlacementsPerPass = 30;

        /// <summary>How far from the starting position the base may be sited.</summary>
        const int OriginSearchRadius = 45;

        /// <summary>Total room slots along the corridor before the base is considered full.</summary>
        const int MaxSlots = 40;

        int placedThisPass;

        /// <summary>
        /// Whether the "no ground left" line has already been said for the current dry spell.
        /// Cleared as soon as a room is sited again, so a base that gets unblocked — a boulder
        /// mined out, a surplus room reclaimed — says so again if it blocks a second time.
        /// </summary>
        bool reportedNoSite;

        /// <summary>Whether the "nothing to build walls from" line has been said this dry spell.</summary>
        bool reportedNoShellMaterial;

        /// <summary>
        /// How many passes running the same room has had its furniture re-queued, and which room
        /// that is. Consecutive by room rather than a total, because the fault this catches is a
        /// single room cycling — a re-queue elsewhere is ordinary work and resets the count.
        /// </summary>
        int requeueRun;
        string requeueRoom;

        /// <summary>
        /// Why the last furniture placement put nothing down, or null if the last one worked.
        /// Carried so the loop report can name its own cause instead of only its symptom.
        /// </summary>
        string lastPlacementFailure;

        /// <summary>
        /// Cells the game refused this item in, so the next-best is tried instead of the same one
        /// for ever. Per call, and a field only to keep it off the per-pass allocation path.
        /// </summary>
        readonly HashSet<IntVec3> refusedCells = new HashSet<IntVec3>();

        /// <summary>
        /// How many different cells one item may be offered before the room is left for a later
        /// pass. Bounded because every attempt rescores the whole interior.
        /// </summary>
        const int MaxCellAttempts = 8;

        /// <summary>
        /// How many times a room's walls may be started over before the site is given up.
        ///
        /// Generous, because the ordinary reason for restarting is that something destroyed the
        /// walls and rebuilding is exactly right. It is the site that never finishes however
        /// often it is tried that this is meant to catch.
        /// </summary>
        const int MaxShellAttempts = 6;

        /// <summary>
        /// Whether the plan wants a room the colony has not reserved, and the spare slot is free.
        ///
        /// Three conditions, all of them narrowing. The plan has to be asking for a room at all;
        /// the colony must not already have one of that kind, or this would open a second
        /// bedroom rather than the first; and the allowance must not already have been stretched,
        /// which is what holds the extra to exactly one however many passes run.
        /// </summary>
        RoomRole? previouslyWanted;

        bool FocusRoomWouldUseTheSpareSlot(DirectorContext ctx, int unfinished, int allowed)
        {
            var asking = ctx.plan != null && ctx.plan.Focus != null
                ? ctx.plan.Focus.WantsRoom
                : null;

            var lastTime = previouslyWanted;
            previouslyWanted = asking;

            if (unfinished > allowed) return false;   // the spare slot is already in use
            if (!asking.HasValue) return false;
            if (ctx.layout == null || ctx.layout.HasRoom(asking.Value)) return false;

            // The plan has to have wanted this room for more than an instant.
            //
            // Long-term goals are separated by urgency alone and several of them read theirs off
            // the map, so the ordering among them can flip on the weather — three of them sat
            // within six hundredths of a point of each other in the self-test. That is harmless
            // while they share a prerequisite and it is not harmless here, because they do not
            // all want the same room: one pass in which Masonry beat "Somewhere to research"
            // opened a Workshop, and a colony of three then spent days splitting its builders
            // between that and the research room it had been asking for all along.
            //
            // Scoring a tie one way or the other is cheap. Committing a shell to it is not, so
            // the extra slot waits for the plan to say the same thing twice.
            if (!lastTime.HasValue || lastTime.Value != asking.Value) return false;

            // Not while the colony cannot afford the rooms it has already started.
            //
            // The slot exists because a colony can be building the wrong thing first and have no
            // way to start the right one. It does not help a colony that cannot finish anything:
            // watched one reach day four with three shells open, none finished, and its material
            // falling from 164 to 134 while it opened nothing new and completed nothing old.
            //
            // Adding a fourth outline to that is the exact failure the concurrency limit was
            // written for. Being blocked on labour and being blocked on material look identical
            // from the plan's side and want opposite answers, and this is the one that can tell
            // them apart.
            return !Upkeep.BuildingMeans.Destitute(Means(ctx));
        }

        /// <summary>
        /// Ticks after a withdrawal before another may happen. Half an in-game day.
        ///
        /// Withdrawing is cheap and withdrawing repeatedly is a loop, which this codebase has
        /// produced three times already from rules that were individually correct. One room at a
        /// time, with long enough between for the colony to visibly get on with what is left.
        /// </summary>
        const int ConsolidateCooldownTicks = 30000;

        /// <summary>How long a set-aside room stays set aside. One in-game day.</summary>
        const int DeferralTicks = 60000;

        int lastConsolidatedTick = -999999;

        /// <summary>
        /// Withdraws the blueprints of a room the colony cannot currently finish.
        ///
        /// Chooses the room furthest from being done and least wanted: never the focus room,
        /// never one whose walls are complete, and never one with a frame standing in it. A
        /// frame holds real work and material; a blueprint holds neither, so taking one back
        /// costs the colony nothing but the intention.
        /// </summary>
        void ConsolidateOntoWhatCanBeFinished(DirectorContext ctx, int unfinished, int allowed)
        {
            if (unfinished <= allowed) return;

            int now = Find.TickManager.TicksGame;
            if (now - lastConsolidatedTick < ConsolidateCooldownTicks) return;

            var layout = ctx.layout;
            var wanted = ctx.plan != null && ctx.plan.Focus != null
                ? ctx.plan.Focus.WantsRoom
                : null;

            for (int i = layout.rooms.Count - 1; i >= 0; i--)
            {
                var room = layout.rooms[i];
                if (wanted.HasValue && room.role == wanted.Value) continue;

                // Never a room whose walls are already up.
                //
                // This asked for a finished shell *and* furniture queued, and requiring both was
                // wrong twice over. Withdrawing from a standing room does not free anybody from
                // wall-building, which is the entire point; and what it takes back is the
                // furniture that makes the room worth having.
                //
                // Watched it take the research bench out of a research room four in-game hours
                // after the room was finished — the first research room any colony here has ever
                // completed — and set the room aside for a day. The long-term tiebreak had
                // flipped the focus that pass, so the room was not protected as the focus room
                // either.
                if (ShellComplete(ctx.map, room)) continue;

                if (AnyFrameIn(ctx.map, room)) continue;      // somebody has already spent work here

                if (room.deferredUntilTick > now) continue;   // already set aside

                int withdrawn = WithdrawBlueprints(ctx.map, room);
                if (withdrawn == 0) continue;

                // Set aside rather than forgotten. It keeps its site, so nothing else is sited
                // over the walls it already has, and it is picked up again when the deferral
                // lapses and the colony has hands to spare.
                room.wallsQueued = false;
                room.deferredUntilTick = now + DeferralTicks;
                lastConsolidatedTick = now;

                Chronicle.Record(ChronicleCategory.Build, string.Format(
                    "took back {0} unstarted blueprints from the {1} room and set it aside for a " +
                    "day — {2} rooms open and only {3} that {4} colonists can finish, so the hands " +
                    "go to one of them instead of all of them. Nothing built was lost",
                    withdrawn, room.role, unfinished, allowed, ctx.state.ableColonists.Count));
                Note("withdrew the " + room.role + " room to concentrate on the rest");
                return;
            }
        }

        static bool AnyFrameIn(Map map, PlannedRoom room)
        {
            foreach (var cell in room.Rect)
            {
                if (!cell.InBounds(map)) continue;

                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Frame) return true;
                }
            }
            return false;
        }

        static int WithdrawBlueprints(Map map, PlannedRoom room)
        {
            var doomed = new List<Thing>();
            foreach (var cell in room.Rect)
            {
                if (!cell.InBounds(map)) continue;

                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Blueprint) doomed.Add(things[i]);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                if (!doomed[i].Destroyed) doomed[i].Destroy(DestroyMode.Cancel);
            }
            return doomed.Count;
        }

        bool roomsHeldNoted;

        /// <summary>
        /// Records the whole base being held on the concurrency limit.
        ///
        /// This return is the single most consequential thing the planner does and it was silent,
        /// so every colony that never got a bedroom had to be diagnosed by inferring it from the
        /// absence of BUILD lines. One unfinished room stops all the others, and when that room
        /// is slow for ordinary reasons — a raid, a fire, forty-one boulders in its footprint —
        /// the colony can spend days with no bed and nothing in the record saying why.
        ///
        /// Says what is holding it and what the colony would build next, because "held" and
        /// "held while it needs a bedroom" are different facts.
        /// </summary>
        void NoteRoomsHeld(DirectorContext ctx, int unfinished, int allowed)
        {
            if (roomsHeldNoted) return;
            roomsHeldNoted = true;

            string first = "nothing";
            var layout = ctx.layout;
            for (int i = 0; i < layout.rooms.Count; i++)
            {
                if (layout.rooms[i].wallsQueued && ShellComplete(ctx.map, layout.rooms[i])) continue;
                first = layout.rooms[i].role.ToString();
                break;
            }

            var wanted = ctx.plan != null && ctx.plan.Focus != null ? ctx.plan.Focus.WantsRoom : null;

            Chronicle.Record(ChronicleCategory.Build, string.Format(
                "not opening another room — {0} unfinished and only {1} allowed for {2} able " +
                "colonists; waiting on the {3} room{4}",
                unfinished, allowed, ctx.state.ableColonists.Count, first,
                wanted.HasValue ? ", while the plan is asking for a " + wanted.Value : ""));
        }

        protected override void Act(DirectorContext ctx)
        {
            var layout = ctx.layout;
            if (layout == null) return;

            if (!layout.established && !TryEstablish(ctx)) return;

            var s = ctx.state;
            if (s.pendingBlueprints + s.pendingFrames > MaxPendingConstruction) return;

            placedThisPass = 0;

            // Finish what is already reserved before opening a new room.
            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var room = layout.rooms[i];

                // A room set aside is not worked on until its deferral runs out. Without this the
                // withdrawal below undoes itself on the very next pass: the room reads as having
                // no walls queued, and the planner obligingly queues them all again.
                if (room.deferredUntilTick > Find.TickManager.TicksGame) continue;

                if (!room.wallsQueued)
                {
                    // Walls already going up from an earlier pass are walls already queued.
                    //
                    // Without this the planner re-ran the whole shell every pass while its own
                    // blueprints stood waiting, placed nothing because every cell was taken by
                    // those blueprints, and reported that it could not start the room — "28 ×
                    // something is already queued there", which is what a room being built looks
                    // like. The rule below wants something *going up*, and pending construction
                    // is exactly that; it just could not see anything it had not placed itself
                    // on this very pass.
                    if (HasPendingConstructionIn(ctx.map, room))
                    {
                        room.wallsQueued = true;
                        continue;
                    }

                    // Only claim the room once something is actually going up. Marking it queued
                    // on a pass that placed nothing is what made the planner un-queue and re-queue
                    // it every three in-game hours, and burn the pass doing it.
                    if (QueueShell(ctx, room) > 0)
                    {
                        room.wallsQueued = true;
                        Note("queued walls for " + room.role + " room");
                        return;
                    }
                    continue;
                }
            }

            // Furniture that has been destroyed is never noticed otherwise. A kitchen whose
            // stove burned down is still "a kitchen" by room, so the colony keeps starving next
            // to a room that cannot cook -- observed as a standing "need meal source" alert on
            // a colony with a kitchen.
            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var room = layout.rooms[i];
                if (!room.furnitureQueued) continue;
                if (!KeyFurnitureMissing(ctx, room)) continue;
                // The whole room, not just its walls: furniture stands in the interior, so a
                // generator or bed still waiting to be built reads as one that was destroyed.
                if (HasPendingConstructionAnywhereIn(ctx.map, room)) continue;

                // Nor is a room whose floor is still being dug out a room that lost its
                // furniture. Mining is work the colony has already been told to do, and a mine
                // designation is not a blueprint, so this check could not see it.
                if (StillBeingCleared(ctx.map, room)) continue;

                room.furnitureQueued = false;
                Chronicle.Record(ChronicleCategory.Build,
                    room.role + " room is missing its key furniture — re-queuing it");
                Note("re-queuing lost furniture in " + room.role + " room");

                // Re-queuing the same room over and over is not upkeep, it is a loop.
                //
                // This path exists for furniture that was destroyed, and one pass should end it.
                // Twice now it has run instead as a metronome — a kitchen, then a bedroom,
                // alternating "furnished" and "missing" every three in-game hours for days,
                // while both branches return early and starve the rest of the planner. Each time
                // the cause was different and each time it was invisible, because every
                // individual line looked like ordinary work.
                //
                // The loop is easier to recognise than any of its causes, so this names the loop.
                if (requeueRoom == room.role.ToString()) requeueRun++;
                else { requeueRoom = room.role.ToString(); requeueRun = 1; }

                if (requeueRun == 4 || (requeueRun > 4 && requeueRun % 20 == 0))
                {
                    Chronicle.Record(ChronicleCategory.Build, string.Format(
                        "the {0} room's key furniture has been re-queued {1} times running and " +
                        "still has not appeared — the planner is looping on it instead of getting " +
                        "on with the base; last placement said: {2}",
                        room.role, requeueRun,
                        lastPlacementFailure ?? "nothing, which means it placed something and it " +
                        "went away again"));
                }
                return;
            }

            // Somebody is on the floor and there is nowhere to carry them.
            //
            // This test already existed, but only inside the "walls are not up yet" branch and
            // behind `furnitureQueued`, so it could not reach the case that actually keeps
            // killing people: a finished, furnished bedroom holding fewer beds than the colony
            // has colonists. A rescue needs a *free* bed, and bed rest is what decides whether a
            // wound beats the infection.
            //
            // Watched live, and this is the clearest the record has ever been about it: Sanchez
            // spent nine in-game hours down with no threat, no fire and eight days of food in
            // store, while the director used those hours to place a games table twice.
            if (NeedsAnEmergencyBed(ctx) && TryAddEmergencyBed(ctx)) return;

            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var room = layout.rooms[i];
                if (room.furnitureQueued) continue;

                if (!ShellComplete(ctx.map, room))
                {
                    // Walls were queued but neither finished nor still pending: a raid levelled
                    // them, or the blueprints were cancelled. Re-queue on the next pass rather
                    // than leaving a half-built room abandoned forever.
                    if (!HasPendingConstructionIn(ctx.map, room))
                    {
                        // A site the colony keeps starting and never finishes is the wrong site.
                        //
                        // This path assumes the walls were lost — levelled by a raid, or the
                        // blueprints cancelled — and that starting them again will work. When
                        // the site itself is the problem that assumption never comes true, and
                        // because an unfinished room counts against the concurrency limit, the
                        // planner stops opening any room at all. One colony sat in that state
                        // for five days: no second room, no bedroom, no bed, two colonists dead
                        // on open ground.
                        //
                        // Giving the site up costs the walls already standing on it. Keeping it
                        // costs every room the colony would otherwise have built, which is not a
                        // close comparison.
                        if (++room.shellAttempts > MaxShellAttempts)
                        {
                            Chronicle.Record(ChronicleCategory.Build, string.Format(
                                "giving up on the {0} room at {1} — its walls have been started " +
                                "{2} times and never finished, so the site is the problem rather " +
                                "than the building; freeing it and choosing somewhere else",
                                room.role, room.Center, room.shellAttempts - 1));
                            layout.rooms.RemoveAt(i);
                            Note("abandoned an unbuildable " + room.role + " site");
                            return;
                        }

                        room.wallsQueued = false;
                        Note("re-queuing lost walls for " + room.role + " room");
                        return;
                    }

                    // A bed is also the only way a downed colonist gets off the floor.
                    //
                    // RimWorld's Rescue job needs a free bed to carry someone to; with none, a
                    // downed colonist stays where they fell. That matters far more than the mood
                    // penalty, because resting in a bed multiplies immunity gain and heals about
                    // 14 HP a day, and an untended infection races immunity to 100% — the first
                    // to arrive decides whether the pawn lives. Colonies in this session died
                    // exactly there: everyone down, nobody rescuable, `beds 0` in the record.
                    // A bed somebody else is asleep in is not a bed to be rescued into, so the
                    // test is how many are spare, not how many exist.
                    //
                    // This asked for zero beds, and zero is the one number a colony stops being
                    // at the moment the first bed goes up. Watched live: Belle went down at 12h
                    // in a raid with `beds 1` for three colonists, the doctor was correctly held
                    // back to tend her, and she died on the ground at 16h — four hours in which
                    // this branch could have placed the bed that would have carried her off it,
                    // and declined to because one bed already existed.
                    if (ctx.state.colonistsDowned > 0 && room.role == RoomRole.Bedroom &&
                        ctx.state.colonistBeds < ctx.state.colonists)
                    {
                        QueueFurniture(ctx, room);
                        room.furnitureQueued = true;
                        Chronicle.Record(ChronicleCategory.Health,
                            ctx.state.colonistsDowned + " colonists down and no bed to carry them " +
                            "to — placing beds now; a rescue needs somewhere to rescue them to");
                        return;
                    }

                    // Somewhere to sleep does not need walls.
                    //
                    // Everything else here waits for the shell for a reason — a stove or a
                    // generator standing in the rain is a fire, which is a trap this codebase
                    // has already paid for once. A bed is not. Meanwhile the wait costs about
                    // -11 mood a survey in SleptOnGround, SleptOutside and NeedComfort, from the
                    // first night until the walls close, which measured one to two full days
                    // across these runs and killed a colony outright at 0.00 mood.
                    //
                    // Beds are a bedroom's only furniture, so furnishing it early leaves nothing
                    // to add later and needs no extra bookkeeping: the shell carries on being
                    // built around them.
                    if (room.role == RoomRole.Bedroom &&
                        ctx.state.colonistBeds < ctx.state.colonists)
                    {
                        QueueFurniture(ctx, room);
                        room.furnitureQueued = true;
                        Chronicle.Record(ChronicleCategory.Build,
                            "beds queued in the bedroom before its walls are up — sleeping on the " +
                            "ground costs mood every night and a bed does not need a roof to help");
                        return;
                    }

                    continue;
                }

                QueueFurniture(ctx, room);
                room.furnitureQueued = true;
                ReportRoomStats(ctx, room);
                Note("furnished " + room.role + " room");
                return;
            }

            // Everything a room needs, not just the one item it is named for.
            //
            // The missing-furniture check above asks only about the *key* item, so a kitchen
            // with a stove is a finished kitchen however much else failed to land. Anything
            // secondary therefore gets exactly one attempt, on one pass, and if that attempt
            // fails it is never made again. Watched live: a kitchen with a stove and no butcher
            // table, and a colony that hunted for ten days with 51 corpses on the map, one meal
            // in the larder and 0.0 days of food — "a corpse is not food until something
            // butchers it", arriving by a route the goal layer cannot see, because the goal was
            // satisfied the moment the kitchen existed.
            if (TopUpFurniture(ctx)) return;

            MarkPrisonBeds(ctx);

            // Opening a building project is not how a fire or a raid is answered, and the labour
            // it claims is the labour the emergency needs. Everything already reserved carries on
            // above; this only stops the colony taking on something new.
            //
            // Deliberately keyed on something physically happening at the colony rather than on
            // `plan.EmergencyActive`, which is true for *any* immediate goal — "Feed the colony"
            // among them. Written that way first, it deadlocked outright: a hungry colony was
            // barred from building, including from building the kitchen its own hunger goal was
            // asking for, so it stayed hungry and stayed barred. Three colonists to one, no room
            // ever queued, food at 0.0 for the whole run.
            if (ctx.state.firesNearBase > 0 || ctx.state.hostilesNearBase > 0) return;

            // Only as many rooms at once as there are hands to finish them.
            //
            // This used to be guarded solely by the focus room below, which is a narrower rule
            // than it looks: the focus moves. A raid makes the plan want no room at all, so the
            // guard passed and the planner opened another shell — six of them in three days,
            // none finished, with the colonists sleeping on wet ground the whole time and a
            // bedroom among the things they never got round to.
            int unfinished = UnfinishedRooms(ctx);
            int allowed = Upkeep.BuildingMeans.ConcurrentRooms(ctx.state.ableColonists.Count);

            // One slot is kept for the room the plan is actually asking for.
            //
            // The limit exists for a good reason — six shells at once, none finished, colonists
            // on wet ground for three days — and it is not being relaxed in general. But it
            // counts rooms without asking what they are for, so a colony building its kitchen
            // cannot start the bedroom the plan is asking for, and beds queue behind the store
            // and the workshop. Run 19 spent its whole life at `beds 0` and lost three
            // colonists; run 26 reached day 2 with no bed while a kitchen crawled through
            // forty-one boulders, a raid and a fire.
            //
            // Bounded at exactly one extra, and only for the focus room, so the failure the
            // limit was written for cannot come back: the colony can be building at most one
            // thing more than its hands can finish, and that thing is the one it most needs.
            if (unfinished >= allowed && FocusRoomWouldUseTheSpareSlot(ctx, unfinished, allowed))
                allowed = unfinished + 1;

            if (unfinished >= allowed)
            {
                NoteRoomsHeld(ctx, unfinished, allowed);

                // More open than the colony can now carry: give one back.
                //
                // The limit only ever refused to *open* rooms, so a commitment made when the
                // colony was larger outlives the colony. Watched one go from three colonists to
                // two with three shells standing, 989 units of material in store and not one
                // room finished in five days — its people sleeping outside, because the beds
                // were in a bedroom whose walls were never going to go up while the same hands
                // were also building a workshop and a research room.
                //
                // Nothing here is destroyed. Only blueprints are withdrawn, and a blueprint is a
                // note about intent rather than work done, so the room can be started again the
                // moment the colony can carry it.
                ConsolidateOntoWhatCanBeFinished(ctx, unfinished, allowed);
                return;
            }
            roomsHeldNoted = false;

            // Finish the room the plan actually asked for before opening another.
            //
            // Reserving a room aims at the focus, but only at the moment of reserving. After
            // that the planner would happily reserve a hospital while the power room it asked
            // for stood half-built.
            if (FocusRoomUnfinished(ctx)) return;

            // Everything reserved is done; decide what the colony needs next. A null answer
            // means the base is complete for the current population — the planner then stops
            // rather than tiling bedrooms across the map forever.
            RoomRole role;
            if (!TryChooseNextRole(ctx, out role)) return;

            // A room the colony already owns, before any new ground is opened.
            if (TryRepurpose(ctx, role)) return;

            var reserved = ReserveRoom(ctx, role);
            if (reserved != null)
            {
                ctx.Credit(BanditId, role.ToString());
                Note("reserved a new " + role + " room");
            }
        }

        /// <summary>
        /// Whether the colony would ask for a room of this role again the moment it lost one.
        ///
        /// Deliberately the same question <see cref="TryChooseNextRole"/> answers, from the other
        /// side: it offers the planner every role the layout does not have, so any role on that
        /// list is one the colony wants back. A prison is the only conditional case — the colony
        /// stops wanting one once nothing has been captured and no raid has landed — and that is
        /// exactly the shape of room repurposing was introduced for, a shell whose purpose has
        /// genuinely lapsed rather than one still in demand.
        /// </summary>
        static bool RoleStillWanted(DirectorContext ctx, RoomRole role)
        {
            if (role == RoomRole.Prison)
                return ctx.state.raidsSurvived > 0 || ctx.state.downedStrangers > 0;

            return true;
        }

        /// <summary>
        /// How long a room keeps a new job before the plan may reconsider it. One in-game day,
        /// which is about what the colony needs to furnish a room it has just been given.
        /// </summary>
        const int RepurposeCooldownTicks = 60000;

        readonly HashSet<int> repurposeHeldNoted = new HashSet<int>();

        /// <summary>Records a conversion being declined, once per room, so inaction explains itself.</summary>
        void NoteRepurposeHeld(PlannedRoom room)
        {
            if (!repurposeHeldNoted.Add(room.minX * 10000 + room.minZ)) return;
            Chronicle.Record(ChronicleCategory.Build, string.Format(
                "left the {0} room as it is — it was given that job less than a day ago, and a " +
                "shell that changes purpose faster than it can be furnished is never any of them",
                room.role));
        }

        /// <summary>
        /// Gives an existing, finished room a new job instead of building another one.
        ///
        /// The walls are the expensive part — roughly a hundred and twenty units a shell, and
        /// this codebase already treats them as stored material when it reclaims a surplus room.
        /// A room that has stopped being needed for what it was built for is a complete shell
        /// standing empty, and the only thing between it and being a workshop is the furniture
        /// inside it.
        ///
        /// Without this the two halves of the director worked against each other. The planner
        /// opened new ground for the role it wanted; the extra walls pushed the colony into
        /// being destitute; the upkeep layer then reclaimed the *old* room for its material —
        /// so a colony with a perfectly good empty room built a second one beside it and tore
        /// the first down to pay for it. Observed in a running colony, blueprints going up
        /// alongside finished rooms with nothing wrong with them.
        /// </summary>
        bool TryRepurpose(DirectorContext ctx, RoomRole role)
        {
            var layout = ctx.layout;
            if (layout == null) return false;

            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var room = layout.rooms[i];
                if (room.role == role) continue;

                // Never take the room the plan is currently asking for.
                //
                // Repurposing judges a room on whether anything needs it for what it is *now*,
                // and a room that has not been furnished yet always looks spare. But a Research
                // room with no bench in it is not a spare room — it is the room the plan is in
                // the middle of asking for. So wanting a workshop quietly ate the research room,
                // the plan asked for research again, and the two traded the same shell back and
                // forth: four conversions in seven in-game hours, and no bench ever built.
                //
                // The same guard the planner already applies to its own focus before opening new
                // ground; it simply never applied it to taking a room away.
                if (ctx.plan != null && ctx.plan.Focus != null)
                {
                    var wanted = ctx.plan.Focus.WantsRoom;
                    if (wanted.HasValue && room.role == wanted.Value) continue;
                }

                // Giving up the last room of a role the colony still wants does not gain a room,
                // it moves the shortage — and the planner will ask for the old role again on the
                // very next pass, because what it picks from is the set of roles the layout does
                // not have. Converting the only Workshop into a Research room takes Workshop out
                // of the layout and therefore puts it straight back into the options.
                //
                // That is a closed loop with nothing random about its outcome, and it ran: one
                // shell went Workshop, Research, Workshop, Dining, Hospital, Workshop in fourteen
                // in-game hours, six conversions ending exactly where it started. Because every
                // conversion strips the furniture that made the room what it was, it was never
                // any of those things long enough to be furnished — the colony had a workshop on
                // paper for two days and never owned a workbench.
                //
                // `Expendable` was meant to be this test and stops one step short: it protects
                // the last Kitchen, Storage, Bedroom and Power room, which is the same idea with
                // an incomplete list. The rest of the enumeration belongs here rather than there,
                // because tearing a room down for material when the colony is destitute is a
                // fair use of the last Dining room and swapping its label is not.
                if (layout.CountRooms(room.role) <= 1 && RoleStillWanted(ctx, room.role)) continue;

                // A room that has just been given a job keeps it for a while.
                //
                // The guard above stops the plan eating the room it is currently asking for, and
                // that was enough while only two roles were in contention. It cannot stop a
                // cycle, because in a cycle every single conversion is locally correct: the plan
                // wants a workshop and the research room is spare, so it takes it; an hour later
                // the plan wants a dining room and the workshop is spare, so it takes that. The
                // want rotates and no individual step ever looks wrong.
                //
                // Watched one shell go Workshop, Research, Workshop, Dining, Hospital, Workshop
                // in fourteen in-game hours — six conversions ending exactly where it started,
                // and because every conversion strips the furniture that made the room what it
                // was, it was never any of them for long enough to be furnished at all.
                //
                // The saving that justifies repurposing is real once and a pure loss repeated:
                // the shell is reused but the fittings are paid for again every time. So a room
                // has to have been left alone for a day before its job is reconsidered, which is
                // also roughly how long the colony needs to furnish one.
                if (room.roleChangedTick >= 0 &&
                    Find.TickManager.TicksGame - room.roleChangedTick < RepurposeCooldownTicks)
                {
                    NoteRepurposeHeld(room);
                    continue;
                }

                // Only a shell that is actually finished. A half-built room offers no saving
                // over starting one where the plan wants it.
                if (!room.wallsQueued || !ShellComplete(ctx.map, room)) continue;

                // And only one nobody needs for what it currently is — the same test used
                // before taking a room down, since the question is the same one.
                if (!Upkeep.DefectSurvey.Expendable(ctx.map, layout, room)) continue;

                var was = room.role;
                ClearFurnitureFor(ctx, room, was);

                room.role = role;
                room.furnitureQueued = false;   // furnished for its new purpose on a later pass
                room.roleChangedTick = Find.TickManager.TicksGame;

                ctx.Credit(BanditId, role.ToString());
                Chronicle.Record(ChronicleCategory.Build, string.Format(
                    "repurposed the {0} room as a {1} — the shell is already standing, and " +
                    "building another one would have cost about {2} units of material it did not need",
                    was, role, Upkeep.BuildingMeans.RoomCost));
                Note("repurposed a " + was + " room as " + role);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Takes out the furniture that made the room what it was, keeping the material.
        ///
        /// Uninstalled rather than deconstructed wherever the game allows it, which is the rule
        /// this codebase already applies to moving anything: deconstruction returns only
        /// `resourcesFractionWhenDeconstructed`, and several vanilla defs set that to zero.
        /// Beds especially have to go, or the room still counts as somewhere to sleep and the
        /// shelter goal reads it as one.
        /// </summary>
        void ClearFurnitureFor(DirectorContext ctx, PlannedRoom room, RoomRole was)
        {
            if (was != RoomRole.Bedroom && was != RoomRole.Hospital && was != RoomRole.Prison)
                return;

            foreach (var cell in room.Interior)
            {
                if (!cell.InBounds(ctx.map)) continue;

                var things = cell.GetThingList(ctx.map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    var bed = things[i] as Building_Bed;
                    if (bed == null || !bed.Spawned) continue;
                    if (bed.OwnersForReading != null && bed.OwnersForReading.Count > 0) continue;

                    if (bed.def.Minifiable)
                        ctx.map.designationManager.AddDesignation(
                            new Designation(bed, DesignationDefOf.Uninstall));
                    else
                        ctx.map.designationManager.AddDesignation(
                            new Designation(bed, DesignationDefOf.Deconstruct));
                }
            }
        }

        /// <summary>
        /// Rooms reserved but not yet standing with their furniture queued — the same bar
        /// <see cref="FocusRoomUnfinished"/> uses, applied to every room rather than one.
        /// </summary>
        static int UnfinishedRooms(DirectorContext ctx)
        {
            var rooms = ctx.layout.rooms;
            int count = 0;
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room.furnitureQueued && ShellComplete(ctx.map, room)) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// True when the plan wants a room the colony has reserved but not yet finished. Walls
        /// up and furniture queued is the bar — the colonists' own job system takes it from
        /// there, and waiting for every last blueprint to be built would stall the base whenever
        /// one item was unreachable.
        /// </summary>
        static bool FocusRoomUnfinished(DirectorContext ctx)
        {
            if (ctx.plan == null || ctx.plan.Focus == null) return false;

            var wanted = ctx.plan.Focus.WantsRoom;
            if (!wanted.HasValue) return false;

            var rooms = ctx.layout.rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room.role != wanted.Value) continue;
                if (room.furnitureQueued && ShellComplete(ctx.map, room)) return false;
                return true;
            }
            return false;   // not reserved yet, which is what reserving one is for
        }

        // ------------------------------------------------------------ siting

        bool TryEstablish(DirectorContext ctx)
        {
            var map = ctx.map;
            int size = RoomSize(ctx);

            IntVec3 seed = ColonistCentroid(ctx);
            if (!seed.IsValid) seed = map.Center;

            // The base needs a corridor plus a room on each side, with slack for growth.
            foreach (var cell in GenRadial.RadialCellsAround(seed, OriginSearchRadius, true))
            {
                if (!cell.InBounds(map)) continue;
                if (!SiteIsViable(map, cell, size)) continue;

                ctx.layout.origin = cell;
                ctx.layout.established = true;
                AcLog.Message("Base site chosen at " + cell + " (room size " + size + ").");
                Note("established base at " + cell);
                return true;
            }

            AcLog.WarningOnce("noBaseSite", "Could not find a buildable base site near " + seed + ".");
            return false;
        }

        /// <summary>Checks that a candidate origin has room for the corridor and first rooms.</summary>
        static bool SiteIsViable(Map map, IntVec3 origin, int size)
        {
            // Corridor is 2 cells tall; allow two rooms north and two south, three slots wide.
            var footprint = new CellRect(
                origin.x - 2,
                origin.z - size - 1,
                (size - 1) * 3 + 2,
                size * 2 + 4);

            if (!footprint.InBounds(map)) return false;
            return PlacementUtil.BuildableFraction(map, footprint) > 0.85f;
        }

        static IntVec3 ColonistCentroid(DirectorContext ctx)
        {
            var pawns = ctx.state.allColonists;
            int n = 0, x = 0, z = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (!pawns[i].Spawned) continue;
                x += pawns[i].Position.x;
                z += pawns[i].Position.z;
                n++;
            }
            return n > 0 ? new IntVec3(x / n, 0, z / n) : IntVec3.Invalid;
        }

        /// <summary>
        /// Makes the beds in a prison actually prisoner beds.
        ///
        /// Placing a bed in a room the planner calls a prison does not make it one — `ForPrisoners`
        /// is a flag on the built bed, and until it is set the game sees an ordinary bed, offers
        /// nobody the option to capture anyone, and the room does nothing at all. Done here
        /// rather than at placement time because the flag lives on the finished building, which
        /// does not exist when the blueprint goes down.
        /// </summary>
        void MarkPrisonBeds(DirectorContext ctx)
        {
            var layout = ctx.layout;
            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var room = layout.rooms[i];
                if (room.role != RoomRole.Prison) continue;

                foreach (var cell in room.Rect)
                {
                    if (!cell.InBounds(ctx.map)) continue;
                    var things = cell.GetThingList(ctx.map);
                    for (int t = 0; t < things.Count; t++)
                    {
                        var bed = things[t] as Building_Bed;
                        if (bed == null || !bed.Spawned || bed.ForPrisoners) continue;

                        // ForOwnerType directly, not SetBedOwnerTypeByInterface. The "ByInterface"
                        // name is the tell: it is the guarded path the player's own UI goes
                        // through, and it declines quietly. The bed came out unmarked, which
                        // left the entire prisoner chain dead — a prison room with an ordinary
                        // bed in it, and no way for anyone ever to be captured into it.
                        bed.ForOwnerType = BedOwnerType.Prisoner;

                        // And the room has to be told. `IsPrisonCell` is cached on the room, not
                        // derived on demand, so a bed that says it is for prisoners inside a room
                        // that still believes it holds none is refused by the game with "no
                        // enclosed prisoner-marked bed" — every clause of which looks satisfied.
                        var bedRoom = bed.GetRoom();
                        if (bedRoom != null)
                        {
                            bedRoom.Notify_BedTypeChanged();
                            bedRoom.Notify_ContainedThingSpawnedOrDespawned(bed);
                        }
                        Chronicle.Record(ChronicleCategory.Build,
                            "marked a bed for prisoners — the colony can now take them");
                        Note("marked a prisoner bed");
                    }
                }
            }
        }

        /// <summary>How comfortably the colony can afford to build right now.</summary>
        static float Means(DirectorContext ctx)
        {
            return BuildingMeans.Assess(ctx.state.usableMaterial, ctx.state.colonists);
        }

        /// <summary>
        /// Beds to a room, which is a question about wealth rather than taste.
        ///
        /// The genome supplies a preference, but a preference for private rooms is worth nothing
        /// to a colony that cannot afford the walls. When material is short everyone shares, and
        /// the room count comes back down as the colony recovers. Privacy is bought, not owed.
        /// </summary>
        static int BedsPerRoom(DirectorContext ctx)
        {
            int preferred = AcMath.Clamp(ctx.GeneInt(Genes.BaseBedsPerRoom), 1, 4);
            return BuildingMeans.BedsPerRoom(Means(ctx), preferred, ctx.state.colonists);
        }

        /// <summary>A dimension gene, clamped to what is actually buildable and useful.</summary>
        static int SizeGene(DirectorContext ctx, string key, int fallback)
        {
            int size = ctx.GeneInt(key);
            if (size < 5) size = fallback < 5 ? 5 : fallback;
            if (size > 13) size = 13;
            return size;
        }

        static int RoomSize(DirectorContext ctx)
        {
            int size = ctx.GeneInt(Genes.BaseRoomSize);
            if (size < 5) size = 5;      // 3x3 interior is the smallest useful room
            if (size > 11) size = 11;
            return size;
        }

        // ------------------------------------------------------------ room reservation

        /// <summary>
        /// Which room the colony most needs next. Hard prerequisites come first — a colony
        /// with nowhere to sleep or store food has no business building a research bench —
        /// and among the genuinely optional rooms the choice is left to the bandit.
        /// </summary>
        bool TryChooseNextRole(DirectorContext ctx, out RoomRole role)
        {
            var layout = ctx.layout;
            var s = ctx.state;
            role = RoomRole.Storage;

            // The plan already worked out what the colony is short of, including the chain of
            // prerequisites behind it. Building anything else first would be second-guessing it.
            if (ctx.plan != null && ctx.plan.Focus != null)
            {
                var wanted = ctx.plan.Focus.WantsRoom;
                if (wanted.HasValue && !layout.HasRoom(wanted.Value))
                {
                    role = wanted.Value;
                    return true;
                }
            }

            if (!layout.HasRoom(RoomRole.Storage)) return true;

            int bedsPerRoom = BedsPerRoom(ctx);
            int bedsWanted = s.colonists + ctx.GeneInt(Genes.BaseSpareBeds);
            int bedsPlanned = layout.CountRooms(RoomRole.Bedroom) * bedsPerRoom;
            if (bedsPlanned < bedsWanted)
            {
                role = RoomRole.Bedroom;
                return true;
            }

            if (!layout.HasRoom(RoomRole.Kitchen))
            {
                role = RoomRole.Kitchen;
                return true;
            }

            // A colony that cannot afford walls has no business starting a dining room. Below
            // this it builds only what the plan is actually blocked on, which is handled above.
            if (BuildingMeans.Destitute(Means(ctx))) return false;

            // Everything past this point is discretionary, so let experience decide.
            var options = new List<string>();
            if (!layout.HasRoom(RoomRole.Workshop)) options.Add(RoomRole.Workshop.ToString());
            if (!layout.HasRoom(RoomRole.Research)) options.Add(RoomRole.Research.ToString());
            if (!layout.HasRoom(RoomRole.Dining)) options.Add(RoomRole.Dining.ToString());
            if (!layout.HasRoom(RoomRole.Hospital)) options.Add(RoomRole.Hospital.ToString());
            // A prison has to exist *before* anyone can be taken prisoner: the game will not let
            // a colonist capture someone without a prisoner bed to carry them to. Gating this on
            // already having prisoners was a deadlock with no way in — no prison, so no capture,
            // so no prisoners, so no prison.
            //
            // Any downed stranger is the real signal, not raids specifically: a crashed transport
            // pod puts one on the tile with no raid anywhere in sight. Raids still count, because
            // a colony that has seen one will see more, and a prison takes days to build — the
            // body on the ground today will not wait for it.
            if (!layout.HasRoom(RoomRole.Prison) && (s.raidsSurvived > 0 || s.downedStrangers > 0))
                options.Add(RoomRole.Prison.ToString());

            // Nothing outstanding: the base matches the colony's current size.
            if (options.Count == 0) return false;

            var bandit = ctx.director.BanditFor(BanditId);
            string pick = bandit.Select(options, ctx.Gene(Genes.ResearchExplore));
            role = pick != null
                ? (RoomRole)System.Enum.Parse(typeof(RoomRole), pick)
                : RoomRole.Workshop;
            return true;
        }

        /// <summary>
        /// Claims the next free slot along the corridor. Slots alternate north and south and
        /// march outward from the origin, and neighbouring rooms share a wall.
        /// </summary>
        PlannedRoom ReserveRoom(DirectorContext ctx, RoomRole role)
        {
            var layout = ctx.layout;
            var map = ctx.map;

            // Dimensions are per role now, not one number shared by every room. A store that
            // fills up stops being a store; a bedroom is better small, because RimWorld rewards
            // a tidy little room over a large bare one and every extra cell is wall to build.
            var profile = Rooms.RoomProfiles.For(role.ToString());
            int width = SizeGene(ctx, Rooms.RoomSiting.WidthKey(role.ToString()), profile.width);
            int height = SizeGene(ctx, Rooms.RoomSiting.HeightKey(role.ToString()), profile.height);

            var weights = new Rooms.SiteWeights();
            weights.compactness = ctx.Gene(Rooms.RoomSiting.GeneKey(role.ToString(), Rooms.RoomSiting.Compactness));
            weights.evenness = ctx.Gene(Rooms.RoomSiting.GeneKey(role.ToString(), Rooms.RoomSiting.Evenness));
            weights.partnerAffinity = ctx.Gene(Rooms.RoomSiting.GeneKey(role.ToString(), Rooms.RoomSiting.Partner));
            weights.resourceAffinity = ctx.Gene(Rooms.RoomSiting.GeneKey(role.ToString(), Rooms.RoomSiting.Resource));

            float bestScore = float.NegativeInfinity;
            var bestRect = CellRect.Empty;
            bool bestNorth = true;
            System.Func<bool> bestFound = delegate { return bestScore > float.NegativeInfinity; };

            // Surveying a slot is not the same as spending it.
            //
            // While this loop took the first slot that fitted, advancing the cursor per slot
            // examined was right: everything it stepped over had just been rejected. Scoring
            // changed that — the loop now runs all the way to the end on every call, so the
            // cursor ran to its ceiling on the *first* room and `ReserveRoom` returned null in
            // silence for the rest of the colony's life. Watched live: one kitchen, nextSlot
            // pinned at 40 in the save, three colonists sleeping outside for five days beside a
            // plan that kept asking for beds, and no line anywhere saying why.
            //
            // The cursor is only allowed past slots that can never come good — off the map, or
            // already under a room. The winner does not advance it, because the room about to
            // be added there makes it overlap on the next call anyway.
            int firstUsable = -1;

            // Slots are capped so a hemmed-in base stops searching instead of marching rooms
            // off across the map.
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int slot = layout.nextSlot + attempt;
                if (slot >= MaxSlots) break;
                bool north = (slot % 2) == 0;
                int index = slot / 2;

                // Fan out alternately left and right of the origin.
                int lateral = ((index % 2) == 0 ? 1 : -1) * ((index + 1) / 2);
                int xMin = layout.origin.x + lateral * (width - 1);
                int zMin = north
                    ? layout.origin.z + 2
                    : layout.origin.z - 1 - (height - 1);

                var rect = new CellRect(xMin, zMin, width, height);
                if (!rect.InBounds(map)) continue;
                if (OverlapsExisting(layout, rect)) continue;

                // Nor anywhere a wall could never stand: on somebody else's building, or on
                // terrain that will not carry one.
                //
                // Overlap was only ever checked against the planner's own rooms, so the map
                // itself never got a vote — and a map has ruins, abandoned settlements and water
                // on it. One colony sited its kitchen squarely inside another faction's building:
                // 20 of the 24 perimeter cells already held that faction's walls, and a cell with
                // a wall in it will not take a wall blueprint.
                //
                // What made it fatal rather than untidy is that the shell could then never be
                // finished, and an unfinished room holds the whole planner. Four free cells took
                // blueprints, the other twenty never could, so the room stayed unfinished for
                // ever; the concurrency limit counts it as the one room in progress and refuses
                // to open another. That colony built nothing at all for five days, never had a
                // bed, and lost two colonists on open ground with 1,422 units of wood in store.
                if (PerimeterUnbuildable(map, rect)) continue;

                if (firstUsable < 0) firstUsable = slot;

                // Scored rather than taken. Every slot used to give the same answer to a
                // question that differs completely by role — a store wants the middle of the
                // base, a workshop wants to be beside that store, a prison wants to be nowhere
                // near either — so the first slot that fitted was as good as the planner could
                // ever say.
                float score = Rooms.RoomSiting.Score(SiteFeaturesFor(ctx, rect, profile), weights);
                if (score <= bestScore) continue;

                bestScore = score;
                bestRect = rect;
                bestNorth = north;
            }

            if (!bestFound())
            {
                // Running out of ground is a legitimate answer; being unable to tell it apart
                // from the planner having quietly broken is not. This is the line whose absence
                // cost a whole colony to diagnose, so it is said once per dry spell rather than
                // suppressed for tidiness.
                if (!reportedNoSite)
                {
                    reportedNoSite = true;
                    Chronicle.Record(ChronicleCategory.Build, string.Format(
                        "nowhere to put a {0} room {1}x{2} — 24 slots from {3} of {4} are all off " +
                        "the map or already built on; the base has run out of ground",
                        role, width, height, layout.nextSlot, MaxSlots));
                }
                return null;
            }

            layout.nextSlot = firstUsable;
            reportedNoSite = false;

            var room = new PlannedRoom();
            room.minX = bestRect.minX;
            room.minZ = bestRect.minZ;
            room.width = width;
            room.height = height;
            room.role = role;
            room.doorX = bestRect.minX + width / 2;
            room.doorZ = bestNorth ? bestRect.minZ : bestRect.minZ + height - 1;

            layout.rooms.Add(room);

            Chronicle.Record(ChronicleCategory.Build, string.Format(
                "sited the {0} room {1}x{2} at {3} — {4}",
                role, width, height, room.Center, DescribeSiting(profile)));

            return room;
        }

        /// <summary>What the siting model needs to know about a candidate footprint.</summary>
        Rooms.SiteFeatures SiteFeaturesFor(DirectorContext ctx, CellRect rect,
                                           Rooms.RoomProfiles.Profile profile)
        {
            var f = new Rooms.SiteFeatures();
            var map = ctx.map;
            var centre = rect.CenterCell;

            f.buildable = PlacementUtil.BuildableFraction(map, rect);
            f.fromOrigin = (centre - ctx.layout.origin).LengthHorizontal;

            var rooms = ctx.layout.rooms;
            float nearest = 999f;
            float partner = 999f;

            if (siteDistances == null || siteDistances.Length < rooms.Count)
                siteDistances = new float[rooms.Count + 8];

            for (int i = 0; i < rooms.Count; i++)
            {
                float d = (centre - rooms[i].Center).LengthHorizontal;
                siteDistances[i] = d;
                if (d < nearest) nearest = d;
                if (profile.partner != null && rooms[i].role.ToString() == profile.partner && d < partner)
                    partner = d;
            }

            f.toNearestRoom = nearest;
            f.toPartnerRoom = partner;
            f.unevenness = Rooms.RoomSiting.Unevenness(siteDistances, rooms.Count);
            f.toResource = profile.resource != null
                ? DistanceToResource(map, centre, profile.resource)
                : 0f;

            return f;
        }

        float[] siteDistances;

        /// <summary>
        /// How far the nearest of a kind of resource is. Sampled outward rather than scanning
        /// the map, since this runs once per candidate footprint.
        /// </summary>
        static float DistanceToResource(Map map, IntVec3 from, string resource)
        {
            const int Limit = 40;

            foreach (var cell in GenRadial.RadialCellsAround(from, Limit, true))
            {
                if (!cell.InBounds(map)) continue;

                if (resource == "rock")
                {
                    var edifice = cell.GetEdifice(map);
                    if (edifice != null && edifice.def.mineable)
                        return (cell - from).LengthHorizontal;
                }
                else if (resource == "wood")
                {
                    var plant = cell.GetPlant(map);
                    if (plant != null && plant.def.plant != null && plant.def.plant.IsTree)
                        return (cell - from).LengthHorizontal;
                }
                else if (resource == "soil")
                {
                    if (map.fertilityGrid.FertilityAt(cell) >= 0.7f)
                        return (cell - from).LengthHorizontal;
                }
            }
            return Limit;
        }

        static string DescribeSiting(Rooms.RoomProfiles.Profile profile)
        {
            var reasons = new List<string>();
            if (profile.evenness >= 1f) reasons.Add("wants to sit evenly among the other rooms");
            if (profile.partner != null) reasons.Add("wants to be near the " + profile.partner);
            if (profile.resource != null) reasons.Add("wants to be near " + profile.resource);
            if (profile.compactness <= 0.3f) reasons.Add("wants distance from the rest");
            return reasons.Count > 0 ? string.Join(", ", reasons.ToArray()) : "no strong preference";
        }

        /// <summary>
        /// Whether this footprint has a perimeter cell no wall will ever stand in.
        ///
        /// Two causes, and what they share is that neither goes away by waiting or by working:
        /// the shell stays unfinished for the life of the colony, and an unfinished room holds
        /// the concurrency limit closed against every other room.
        ///
        /// A building somebody else owns. Overlap was only ever tested against the planner's own
        /// rooms, so the map itself never got a vote, and maps have ruins and abandoned
        /// settlements on them. Unowned and player-owned edifices still pass — those can be
        /// deconstructed or built around, and refusing every site with an old wall on it would
        /// rule out most of a map.
        ///
        /// Terrain that will not carry a wall. Water and marsh are the ordinary cases. Watched a
        /// Storage room sited across three such cells: "Wall (when made of wood) requires terrain
        /// that supports: Light", repeated every pass, until the site was abandoned six attempts
        /// later having paid for the walls that did go up.
        ///
        /// The terrain question is asked without a material, which is the weaker form of it —
        /// affordance varies by stuff, so a site this accepts can still refuse the particular
        /// material the colony picks later. It catches the terrain that carries nothing at all,
        /// which is the case that was actually costing colonies.
        /// </summary>
        static bool PerimeterUnbuildable(Map map, CellRect rect)
        {
            var player = Faction.OfPlayer;
            var wallDef = AcDefs.Wall;

            foreach (var cell in rect.EdgeCells)
            {
                if (!cell.InBounds(map)) continue;

                var edifice = cell.GetEdifice(map);
                if (edifice != null && edifice.Faction != null && edifice.Faction != player)
                    return true;

                if (wallDef == null) continue;

                try
                {
                    if (!GenConstruct.CanBuildOnTerrain(wallDef, cell, map, Rot4.North, null, null))
                        return true;
                }
                catch (Exception) { }   // a def the game will not answer for is not a reason to refuse the site
            }
            return false;
        }

        static bool OverlapsExisting(BaseLayout layout, CellRect rect)
        {
            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var other = layout.rooms[i].Rect;
                // Shared walls are fine; genuine interior overlap is not.
                if (rect.ContractedBy(1).Overlaps(other.ContractedBy(1))) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ construction

        /// <summary>
        /// Clears what is standing where the room is going before anything is queued on top.
        ///
        /// Nothing did this, and the consequences ran in both directions. A tree in the wall
        /// line is not an edifice, so placement was simply refused there and the room could
        /// never finish — it sat half-built forever while the planner counted it as outstanding
        /// work. A boulder in the wall line *is* an edifice, so it read as a finished wall and
        /// the room was declared complete around an obstruction the colony had not built and
        /// could not heat.
        ///
        /// Both stop being possible if the ground is cleared first. Rock is mined, which also
        /// returns material; plants are cut, which returns wood. Neither is wasted work — it is
        /// work the colony was going to be blocked by otherwise.
        /// </summary>
        /// <summary>
        /// True while the room's own floor is still being cleared out from under it.
        ///
        /// Natural rock is an edifice, and <see cref="PlaceMany"/> skips any cell holding one —
        /// so a room sited on rock has nowhere to put its furniture until the mining is done.
        /// That is a wait, not a loss, but nothing could tell the two apart: the re-queue check
        /// looked for blueprints, and mining leaves a designation instead. A kitchen sited over
        /// 22 obstructions re-queued its stove every three in-game hours for a day while the
        /// colonists dug, and because both that branch and the furnish branch return, the whole
        /// planner idled behind it with the colony at 0.0 days of food.
        /// </summary>
        /// <summary>
        /// Which room the top-up sweep looks at next. One room per pass, in rotation, because
        /// every check rescans the room and the planner already runs at most once a tick.
        /// </summary>
        int topUpCursor;

        /// <summary>
        /// Puts back anything a finished room was meant to have and has not got.
        ///
        /// Safe to run repeatedly: <see cref="PlaceMany"/> tops up to a count rather than adding
        /// to it, so this places what is missing and nothing else. It reports only when it
        /// actually placed something, so a base with nothing wrong stays silent.
        /// </summary>
        /// <summary>
        /// Adds one bed, anywhere it will go, because somebody is down and every bed is spoken
        /// for. Deliberately indifferent to whether the room is finished or already furnished:
        /// the ordinary furnishing rules are about building a base tidily, and this is not that.
        /// </summary>
        bool TryAddEmergencyBed(DirectorContext ctx)
        {
            var layout = ctx.layout;
            if (layout == null || AcDefs.Bed == null) return false;

            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var room = layout.rooms[i];
                if (room.role != RoomRole.Bedroom && room.role != RoomRole.Hospital) continue;

                // A bed already on its way is not a reason to queue a second. A *wall* on its way
                // is no reason at all.
                //
                // This asked whether anything at all was under construction in the room, which is
                // true of every bedroom that is still being built — so the one case this path
                // exists for, an early colony where nobody has a bed yet, was the one case it
                // refused to act on. Watched live: `beds 0` for all 118 vitals samples across
                // nine days, a Bedroom sited on day 0 and never furnished, two colonists dead on
                // the floor, and this branch never once firing.
                //
                // ExistingCount counts blueprints and frames as well as built beds, so asking
                // about the bed specifically is both the correct guard and an idempotent one.
                if (ExistingCount(ctx.map, room, AcDefs.Bed) > 0) continue;

                int before = placedThisPass;
                PlaceMany(ctx, room, AcDefs.Bed, 1);
                if (placedThisPass <= before) continue;

                Chronicle.Record(ChronicleCategory.Health, string.Format(
                    "{0} down with {1} beds for {2} colonists — putting another bed in the {3} " +
                    "now, because a rescue needs a free bed to carry someone to",
                    ctx.state.colonistsDowned, ctx.state.colonistBeds, ctx.state.colonists,
                    room.role));
                Note("emergency bed for a downed colonist in the " + room.role + " room");
                return true;
            }

            // Nothing could take a bed. Put down a sleeping spot instead.
            //
            // A bed costs 45 of a material the colony may not have, and it costs *time* it
            // certainly does not have: queued as a blueprint it is somewhere to lie down in
            // several hours, and the colonist bleeding on the floor is being asked to wait for
            // hauling and construction to happen first. Every bed this branch has ever placed
            // was a promise rather than a bed.
            //
            // A sleeping spot is a patch of floor with a label on it. It costs nothing, needs no
            // research, and has zero work to build — so `PlacementUtil` spawns it outright
            // instead of queuing it, and it exists the instant it is placed. The game counts it
            // as a bed, which is the only thing that matters here: `FindBedFor` will return it
            // and a rescue will carry somebody to it.
            //
            // It is a bad bed. Comfort 0.4, no quality, and it does none of the things a real bed
            // does for a wound. It is also the difference between being tended on the floor where
            // you fell and being tended somewhere, and colonies in this session died on the wrong
            // side of that. The real bed still gets built by the ordinary furnishing path; this
            // only guarantees the colony is never in a state where a rescue has nowhere to go.
            return TryAddSleepingSpot(ctx);
        }

        /// <summary>
        /// Puts a sleeping spot somewhere a casualty can actually be carried to.
        ///
        /// Inside a room if the colony has one, because a roof and walls are most of what a bed
        /// is for. Failing that, next to the casualty — a spot on open ground is a poor place to
        /// be tended and it is still off the floor, reachable, and reservable, which is three
        /// more things than where they are now.
        ///
        /// Fire is the one thing that disqualifies a location outright. Carrying somebody into
        /// the path of a fire is not a rescue, and this runs in exactly the situations where
        /// there is one.
        /// </summary>
        bool TryAddSleepingSpot(DirectorContext ctx)
        {
            var def = AcDefs.SleepingSpot;
            if (def == null) return false;

            var layout = ctx.layout;

            // With something burning, a room is not automatically the safer choice — it may be
            // the thing that is on fire, and this path exists partly to answer that case. So the
            // rooms are skipped entirely and the spot goes down beside the casualty, which is
            // somewhere the fire demonstrably has not reached yet.
            bool avoidRooms = ctx.state.fires > 0;

            for (int i = 0; !avoidRooms && layout != null && i < layout.rooms.Count; i++)
            {
                var room = layout.rooms[i];
                if (room.role != RoomRole.Bedroom && room.role != RoomRole.Hospital) continue;
                if (ExistingCount(ctx.map, room, def) > 0) continue;

                int before = placedThisPass;
                PlaceMany(ctx, room, def, 1);
                if (placedThisPass <= before) continue;

                NoteSleepingSpot(ctx, "the " + room.role + " room");
                return true;
            }

            // No room will take one, so put it beside whoever needs it.
            var casualty = FirstDownedColonist(ctx);
            var origin = casualty != null ? casualty.Position : ctx.Origin;

            foreach (var cell in GenRadial.RadialCellsAround(origin, 12, true))
            {
                if (!cell.InBounds(ctx.map)) continue;
                if (PlacementUtil.TryPlace(ctx.map, def, cell, Rot4.North, null))
                {
                    PlacementUtil.MarkHome(ctx.map, cell);
                    NoteSleepingSpot(ctx, "open ground beside them");
                    return true;
                }
            }
            return false;
        }

        static Pawn FirstDownedColonist(DirectorContext ctx)
        {
            var colonists = ctx.map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                var pawn = colonists[i];
                if (pawn != null && !pawn.Dead && pawn.Downed && !pawn.InBed()) return pawn;
            }
            return null;
        }

        bool sleepingSpotNoted;

        void NoteSleepingSpot(DirectorContext ctx, string where)
        {
            if (sleepingSpotNoted) return;
            sleepingSpotNoted = true;

            Chronicle.Record(ChronicleCategory.Health, string.Format(
                "{0} down and no bed would go up — laid a sleeping spot in {1}. It costs nothing " +
                "and appears at once, where a bed is 45 material and several hours of hauling " +
                "and building that nobody bleeding on the floor has",
                ctx.state.colonistsDowned, where));
            Note("sleeping spot for a downed colonist — " + where);
        }

        bool TopUpFurniture(DirectorContext ctx)
        {
            var rooms = ctx.layout.rooms;
            if (rooms.Count == 0) return false;

            var room = rooms[topUpCursor % rooms.Count];
            topUpCursor++;

            // Only somewhere settled enough that a gap means a failure rather than work in
            // progress — the same three tests the missing-furniture check uses.
            if (!room.furnitureQueued) return false;
            if (!ShellComplete(ctx.map, room)) return false;
            if (StillBeingCleared(ctx.map, room)) return false;
            if (HasPendingConstructionAnywhereIn(ctx.map, room)) return false;

            int before = placedThisPass;
            QueueFurniture(ctx, room);
            if (placedThisPass <= before) return false;

            Chronicle.Record(ChronicleCategory.Build, string.Format(
                "topped up the {0} room — it was standing without furniture it was meant to have",
                room.role));
            Note("topped up furniture in " + room.role + " room");
            return true;
        }

        static bool StillBeingCleared(Map map, PlannedRoom room)
        {
            foreach (var cell in room.Interior)
            {
                if (!cell.InBounds(map)) continue;

                var edifice = cell.GetEdifice(map);
                if (edifice != null && edifice.def.mineable && edifice.Faction == null) return true;

                var plant = cell.GetPlant(map);
                if (plant == null) continue;
                if (map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null)
                    return true;
                if (map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null)
                    return true;
            }
            return false;
        }

        int ClearFootprint(DirectorContext ctx, PlannedRoom room)
        {
            var map = ctx.map;
            int ordered = 0;

            foreach (var cell in room.Rect)
            {
                if (!cell.InBounds(map)) continue;

                var edifice = cell.GetEdifice(map);
                if (edifice != null)
                {
                    // Natural rock only. Anything the colony built is its own business, and
                    // tearing down a neighbouring room's shared wall is how a base gets opened
                    // to the sky.
                    if (edifice.def.mineable && edifice.Faction == null &&
                        map.designationManager.DesignationOn(edifice, DesignationDefOf.Mine) == null)
                    {
                        map.designationManager.AddDesignation(
                            new Designation(edifice, DesignationDefOf.Mine));
                        ordered++;
                    }
                    continue;
                }

                var plant = cell.GetPlant(map);
                if (plant == null) continue;
                if (map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null) continue;
                if (map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null) continue;

                // Harvest anything worth taking, cut the rest; both clear the cell.
                var how = plant.def.plant != null && plant.def.plant.harvestedThingDef != null
                    ? DesignationDefOf.HarvestPlant
                    : DesignationDefOf.CutPlant;

                map.designationManager.AddDesignation(new Designation(plant, how));
                ordered++;
            }

            return ordered;
        }

        /// <summary>
        /// Lays out a room's walls and door, and reports how many blueprints actually landed.
        ///
        /// The count is the point. This used to return nothing and record "walls queued" either
        /// way, so a colony with no wood was told six times in twelve hours that it had queued
        /// the storage room's walls while placing not one blueprint — and because the caller
        /// then set `wallsQueued`, the next pass found no pending construction, un-queued the
        /// room and started again. The walls-path twin of the furniture metronome, and it hid
        /// for the same reason: every line looked like ordinary work.
        /// </summary>
        int QueueShell(DirectorContext ctx, PlannedRoom room)
        {
            var map = ctx.map;
            int before = placedThisPass;

            // Ground first. A wall cannot be placed through a tree, and a boulder left in the
            // wall line would be mistaken for one.
            int cleared = ClearFootprint(ctx, room);
            if (cleared > 0)
                Chronicle.Record(ChronicleCategory.Build, string.Format(
                    "clearing {0} obstructions from the {1} room's footprint before building it",
                    cleared, room.role));

            // Material is a reading of the conditions, not a fixed taste. Storage leans harder
            // toward stone than the rest: it is where the colony's value ends up, so a fire
            // there is not an inconvenience but the loss of everything worth hauling indoors.
            float risk = FireRisk.Assess(map, ctx.state);
            float stonePref = room.role == RoomRole.Storage
                ? FireRisk.StorageStonePreference(ctx, risk)
                : FireRisk.StonePreference(ctx, risk);

            var wallDef = AcDefs.Wall;
            var doorDef = AcDefs.Door;
            if (wallDef == null) return 0;

            bool gotPreferred;
            var wallStuff = PlacementUtil.ChooseStuff(map, wallDef, stonePref, out gotPreferred);
            var doorStuff = doorDef != null ? PlacementUtil.ChooseStuff(map, doorDef, stonePref) : null;

            var rect = room.Rect;
            var door = room.Door;

            foreach (var cell in rect.EdgeCells)
            {
                if (placedThisPass >= MaxPlacementsPerPass) return placedThisPass - before;

                if (cell.x == door.x && cell.z == door.z)
                {
                    if (doorDef != null && PlacementUtil.TryPlace(map, doorDef, cell, Rot4.North, doorStuff))
                        placedThisPass++;
                    continue;
                }

                if (PlacementUtil.TryPlace(map, wallDef, cell, Rot4.North, wallStuff))
                    placedThisPass++;
            }

            if (placedThisPass == before)
            {
                // Nothing landed. Said once per dry spell rather than every pass, because the
                // colony will keep asking until it has something to build with.
                //
                // This used to name the cause rather than measure it: any dry pass was reported
                // as having nothing to build from. That was wrong in the only run where it
                // mattered — a colony that built nothing for five days, lost two colonists on
                // open ground with no bed, and had 1,422 units of unforbidden wood the whole
                // time. A message that guesses is worse than one that says nothing, because it
                // ends the investigation it should have started.
                if (!reportedNoShellMaterial)
                {
                    reportedNoShellMaterial = true;
                    Chronicle.Record(ChronicleCategory.Build, string.Format(
                        "cannot start the {0} room's walls in {1} — not one of its {2} edge cells " +
                        "would take a blueprint: {3}",
                        room.role, wallStuff != null ? wallStuff.label : "no material at all",
                        CountEdgeCells(rect), DescribeShellRefusals(ctx, room, wallStuff)));
                }
                return 0;
            }
            reportedNoShellMaterial = false;

            Chronicle.Record(ChronicleCategory.Build, string.Format(
                "{0} room walls queued in {1} (fire risk {2:0.00}, stone preference {3:0.00}){4}",
                room.role, wallStuff != null ? wallStuff.label : "default", risk, stonePref,
                gotPreferred ? "" : " — preferred material unavailable, used what was in store"));

            // Claim the room for housekeeping and ask for a roof over the interior.
            foreach (var cell in rect)
            {
                PlacementUtil.MarkHome(map, cell);
            }
            foreach (var cell in room.Interior)
            {
                PlacementUtil.MarkRoof(map, cell);
            }
            return placedThisPass - before;
        }

        static int CountEdgeCells(CellRect rect)
        {
            int n = 0;
            foreach (var cell in rect.EdgeCells) n++;
            return n;
        }

        /// <summary>
        /// Why each wall cell was refused, tallied so one line explains the whole perimeter.
        ///
        /// Counted rather than listed: twenty-four cells refused for the same reason is one fact,
        /// and the interesting case is the perimeter that splits — half of it blocked by somebody
        /// else's building and half of it fine — which a tally shows and a list buries.
        /// </summary>
        static string DescribeShellRefusals(DirectorContext ctx, PlannedRoom room, ThingDef stuff)
        {
            var map = ctx.map;
            var wallDef = AcDefs.Wall;
            if (wallDef == null) return "there is no wall def at all";

            var tally = new Dictionary<string, int>();
            foreach (var cell in room.Rect.EdgeCells)
            {
                string why = PlacementUtil.RefusalReason(map, wallDef, cell, Rot4.North, stuff)
                             ?? "the game would have accepted it";

                int seen;
                tally[why] = tally.TryGetValue(why, out seen) ? seen + 1 : 1;
            }

            var ordered = new List<KeyValuePair<string, int>>(tally);
            ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

            var text = new System.Text.StringBuilder();
            for (int i = 0; i < ordered.Count && i < 3; i++)
            {
                if (i > 0) text.Append("; ");
                text.Append(ordered[i].Value).Append(" × ").Append(ordered[i].Key);
            }
            return text.ToString();
        }

        /// <summary>True while any blueprint or frame is still outstanding on the room's walls.</summary>
        static bool HasPendingConstructionIn(Map map, PlannedRoom room)
        {
            foreach (var cell in room.Rect.EdgeCells)
            {
                if (!cell.InBounds(map)) continue;
                if (PlacementUtil.HasAnyConstructionAt(map, cell)) return true;
            }
            return false;
        }

        /// <summary>
        /// True while anything at all is still outstanding anywhere in the room, interior
        /// included.
        ///
        /// The furniture check needs this rather than the walls-only version above. Furniture
        /// stands inside the room, so a generator or bed that is merely *not built yet* was
        /// being read as one that had been destroyed: the room was un-queued, re-queued on the
        /// next pass, and a second blueprint placed beside the first. Two passes per cycle went
        /// to that, and because both paths return early, no other room made progress while it
        /// continued — observed as a duplicate generator and a base that built at a crawl.
        /// </summary>
        static bool HasPendingConstructionAnywhereIn(Map map, PlannedRoom room)
        {
            foreach (var cell in room.Rect)
            {
                if (!cell.InBounds(map)) continue;
                if (PlacementUtil.HasAnyConstructionAt(map, cell)) return true;
            }
            return false;
        }

        /// <summary>True once no wall cell is still missing a finished building.</summary>
        /// <summary>
        /// Whether the room is actually a room.
        ///
        /// The old test asked whether every edge cell held an edifice, which answers a different
        /// question. A natural rock formation is an edifice, so a boulder sitting in the wall
        /// line counted as a finished wall — and the game, which decides enclosure by whether
        /// air can escape rather than by what is in the cells, disagreed. Rooms therefore
        /// reported themselves complete while standing open: no insulation, no protection from
        /// weather, and every temperature and roof decision built on top of it was wrong.
        ///
        /// So it asks the game instead. Every interior cell has to belong to one enclosed room
        /// that does not reach the map edge, which is the same judgement RimWorld makes when it
        /// decides whether a heater is heating anything.
        /// </summary>
        static bool ShellComplete(Map map, PlannedRoom room)
        {
            var door = room.Door;
            foreach (var cell in room.Rect.EdgeCells)
            {
                if (cell.x == door.x && cell.z == door.z) continue;
                if (!cell.InBounds(map)) continue;
                if (cell.GetEdifice(map) == null) return false;
            }

            return Enclosed(map, room);
        }

        /// <summary>
        /// Asks the game whether the interior is one sealed space rather than part of outdoors.
        ///
        /// Deliberately tolerant of an unroofed room — walls go up before roofs, and a shell
        /// with its walls closed is complete for the planner's purposes — but not of one that
        /// leaks into the map, which is the case the edge test could not see.
        /// </summary>
        static bool Enclosed(Map map, PlannedRoom room)
        {
            try
            {
                Room first = null;
                foreach (var cell in room.Interior)
                {
                    if (!cell.InBounds(map)) return false;

                    var here = cell.GetRoom(map);
                    if (here == null || here.TouchesMapEdge) return false;

                    if (first == null) first = here;
                    else if (here != first) return false;   // split by something standing inside
                }
                return first != null;
            }
            catch (Exception) { return false; }
        }

        void QueueFurniture(DirectorContext ctx, PlannedRoom room)
        {
            switch (room.role)
            {
                case RoomRole.Bedroom:
                    PlaceMany(ctx, room, AcDefs.Bed, BedsPerRoom(ctx));
                    break;
                case RoomRole.Kitchen:
                    PlaceOne(ctx, room, StoveFor(ctx));

                    // A butcher table, and a butcher spot regardless.
                    //
                    // The table is better in every way except the one that has repeatedly
                    // mattered: it costs material and hours, and until it exists a corpse is not
                    // food. Colonies here have hunted thirteen gazelles and starved at 0.0 days
                    // with the meat lying in the field, and one carried 11.6 days of nutrition
                    // around as carcasses nobody could process.
                    //
                    // The spot is free and has zero work, so it is placed outright rather than
                    // queued, and butchering can start immediately. Both go in: the spot covers
                    // the gap until the table lands, and costs nothing to leave afterwards.
                    PlaceOne(ctx, room, AcDefs.ButcherTable);
                    PlaceOne(ctx, room, AcDefs.ButcherSpot);
                    break;
                case RoomRole.Research:
                    PlaceOne(ctx, room, AcDefs.ResearchBench);
                    break;
                case RoomRole.Workshop:
                    PlaceOne(ctx, room, AcDefs.StonecuttersTable);
                    PlaceOne(ctx, room, AcDefs.CraftingSpot);
                    // A shelf, so the benches have something to reach into. A worktable works
                    // measurably faster with its materials to hand, and the placement layer now
                    // knows to put the two together — but only if the shelf exists at all.
                    PlaceOne(ctx, room, AcDefs.Thing("Shelf"));
                    // Somewhere to make clothes. The production module has always been willing
                    // to keep bills on any table it finds; there was simply never a tailor bench
                    // on the map for it to find, so nothing was ever sewn.
                    PlaceOne(ctx, room, AcDefs.TailorBench);
                    break;
                case RoomRole.Dining:
                    PlaceOne(ctx, room, AcDefs.Thing("Table2x2c"));
                    PlaceMany(ctx, room, AcDefs.Thing("DiningChair"), 2);
                    break;
                case RoomRole.Hospital:
                    PlaceMany(ctx, room, AcDefs.Bed, 2);
                    break;
                case RoomRole.Prison:
                    PlaceMany(ctx, room, AcDefs.Bed, 1);
                    // Marked when it is actually built — see MarkPrisonBeds. A bed in a room the
                    // planner calls a prison is still an ordinary bed until someone says so, and
                    // an unmarked one makes the whole room useless for its purpose.
                    break;
                case RoomRole.Power:
                    // A wood-fired generator, not a solar panel, because this room gets roofed
                    // like every other one and a roofed solar panel produces nothing — the game
                    // scales its output by CompPowerPlantSolar.RoofedPowerOutputFactor. It also
                    // fits: 2x2 against the panel's 4x4, in an interior that can be as small as
                    // three cells. Wood costs hauling, but a generator that runs beats one that
                    // is merely built. A battery carries the colony through the night.
                    PlaceOne(ctx, room, AcDefs.WoodFiredGenerator);
                    PlaceMany(ctx, room, AcDefs.Battery, 2);
                    break;
                case RoomRole.Freezer:
                    // The cooler goes in a wall, not the floor: it moves heat from one side to
                    // the other, so it has to span inside and outside.
                    PlaceCoolerInWall(ctx, room);
                    break;
            }

            // A light in every room; unlit rooms tank mood and slow work.
            PlaceOne(ctx, room, AcDefs.Torch);
        }

        /// <summary>
        /// Puts a cooler on the room's wall, facing outward. Placed on an edge cell rather than
        /// inside, because a cooler spans the wall by design.
        /// </summary>
        void PlaceCoolerInWall(DirectorContext ctx, PlannedRoom room)
        {
            var cooler = AcDefs.Cooler;
            if (cooler == null) return;

            var map = ctx.map;
            var door = room.Door;

            foreach (var cell in room.Rect.EdgeCells)
            {
                if (cell.x == door.x && cell.z == door.z) continue;
                // Corners have no clean inside/outside, so skip them.
                bool corner = (cell.x == room.minX || cell.x == room.minX + room.width - 1)
                           && (cell.z == room.minZ || cell.z == room.minZ + room.height - 1);
                if (corner) continue;

                var facing = cell.z == room.minZ ? Rot4.South
                           : cell.z == room.minZ + room.height - 1 ? Rot4.North
                           : cell.x == room.minX ? Rot4.West : Rot4.East;

                if (PlacementUtil.TryPlace(map, cooler, cell, facing, null))
                {
                    placedThisPass++;
                    Chronicle.Record(ChronicleCategory.Build, "cooler queued in the freezer wall at " + cell);
                    return;
                }
            }
        }

        /// <summary>
        /// The best cooking station the colony can build and actually run today.
        ///
        /// This was `ElectricStove ?? FueledStove ?? Campfire`, which reads like a fallback chain
        /// and is not one: `??` gives way only on null, and every one of these defs resolves in
        /// any vanilla install whatever the colony has researched. So the electric stove was
        /// chosen always, and the other two were unreachable.
        ///
        /// The cost of that is a colony waiting on Electricity, a generator and conduit before it
        /// can cook, when a fuelled stove needs no research at all and burns wood it already has.
        /// A `-quicktest` colony hides it completely, because those start with Electricity done.
        ///
        /// A def resolving is not a building the colony can use — the same distinction the power
        /// goals draw, one level further down.
        /// </summary>
        static ThingDef StoveFor(DirectorContext ctx)
        {
            var electric = AcDefs.ElectricStove;
            if (electric != null && electric.IsResearchFinished && ctx.state.workingGenerators > 0)
                return electric;

            var fueled = AcDefs.FueledStove;
            if (fueled != null && fueled.IsResearchFinished) return fueled;

            return AcDefs.Campfire;
        }

        /// <summary>
        /// The one thing a room exists for, so its loss can be detected. A bedroom without a
        /// bed is not a bedroom; a kitchen without a stove cannot feed anyone.
        /// </summary>
        static ThingDef KeyFurnitureFor(DirectorContext ctx, RoomRole role)
        {
            switch (role)
            {
                case RoomRole.Bedroom:
                case RoomRole.Hospital:
                case RoomRole.Prison: return AcDefs.Bed;
                case RoomRole.Kitchen: return StoveFor(ctx);
                case RoomRole.Research: return AcDefs.ResearchBench;
                case RoomRole.Workshop: return AcDefs.StonecuttersTable;
                case RoomRole.Freezer: return AcDefs.Cooler;
                case RoomRole.Power: return AcDefs.WoodFiredGenerator;
                default: return null;   // storage and dining have nothing essential
            }
        }

        /// <summary>
        /// Public so the upkeep layer can ask it before dropping a table or a lamp into a room:
        /// a room that has not got the thing it exists for is not a room with spare space.
        /// </summary>
        public static bool KeyFurnitureMissing(DirectorContext ctx, PlannedRoom room)
        {
            var def = KeyFurnitureFor(ctx, room.role);
            if (def == null) return false;

            // Nothing counts as missing that the colony could not have built in the first place.
            // A stonecutter's table before Stonecutting, or a cooler before Air Conditioning,
            // would otherwise be re-queued on every single pass — and because that path returns
            // early, it would starve the rest of the base of any construction at all.
            if (!def.IsResearchFinished) return false;

            foreach (var cell in room.Rect)
            {
                if (!cell.InBounds(ctx.map)) continue;
                var things = cell.GetThingList(ctx.map);
                for (int i = 0; i < things.Count; i++)
                {
                    var thing = things[i];
                    if (thing == null) continue;
                    // A campfire counts for a kitchen even if a stove was the original plan.
                    if (thing.def == def) return false;
                    if (room.role == RoomRole.Kitchen && thing.def != null &&
                        thing.def.IsWorkTable) return false;
                }
            }
            return true;
        }

        void PlaceOne(DirectorContext ctx, PlannedRoom room, ThingDef def)
        {
            PlaceMany(ctx, room, def, 1);
        }

        void PlaceMany(DirectorContext ctx, PlannedRoom room, ThingDef def, int count)
        {
            if (def == null || count <= 0) return;

            var map = ctx.map;

            // Top up to the count rather than adding that many more. Furniture is queued again
            // whenever a room's key item goes missing, and without this each pass dropped a
            // fresh copy of everything else in the first free cell — so a workshop whose
            // stonecutter could not be afforded accumulated crafting spots indefinitely.
            //
            // It shows up as an unending stream of "construction botched": a crafting spot has
            // WorkToBuild 0, so a failed attempt retries in the same instant rather than after
            // any work, and with a pile of them queued there is always another one to fail at.
            count -= ExistingCount(map, room, def);
            if (count <= 0) return;

            var stuff = PlacementUtil.ChooseStuff(map, def,
                FireRisk.StonePreference(ctx, FireRisk.Assess(map, ctx.state)));
            var kind = KindOf(def);
            var weights = PlacementWeightsFor(ctx, kind);

            // Scored rather than first-fit.
            //
            // Everything used to go into the first legal cell of the interior, so a room's
            // furniture piled into one corner in iteration order — blocking its own access,
            // leaving the rest of the floor bare, and costing the room the Space rating RimWorld
            // scores it on. Each item now takes the best cell for *that* item, which is a
            // different cell for a bed than for a workbench.
            // Nothing below can succeed if the item is unbuildable in principle, and retrying it
            // in another cell only wastes the pass. Tested once, up front, and said plainly.
            if (!def.IsResearchFinished)
            {
                lastPlacementFailure = def.defName + " is not researched yet";
                return;
            }
            if (def.MadeFromStuff && stuff == null)
            {
                lastPlacementFailure = def.defName + " has no material the colony can spare";
                return;
            }

            refusedCells.Clear();

            for (int n = 0; n < count; n++)
            {
                if (placedThisPass >= MaxPlacementsPerPass) return;

                bool placed = false;

                // The best cell is not always one the game will accept.
                //
                // Furniture is bigger than the cell it is scored on — a bed is two cells long, a
                // butcher table two by two — and `CanPlaceBlueprintAt` judges the whole
                // footprint. So the highest-scoring cell is regularly one whose second cell runs
                // into a wall, a rock or another item. Taking the best cell and giving up on
                // refusal is a deterministic dead end: same map, same scores, same best cell,
                // same refusal, for ever. Watched live as a bedroom re-queuing its bed twenty
                // times running, reporting "Bed was refused at (137, 0, 129)" every single pass.
                //
                // A refused cell is therefore struck off and the next best one tried.
                for (int attempt = 0; attempt < MaxCellAttempts && !placed; attempt++)
                {
                    float bestScore = float.NegativeInfinity;
                    var best = IntVec3.Invalid;

                    foreach (var cell in room.Interior)
                    {
                        if (!cell.InBounds(map)) continue;
                        if (refusedCells.Contains(cell)) continue;
                        if (PlacementUtil.HasAnyConstructionAt(map, cell)) continue;
                        if (cell.GetEdifice(map) != null) continue;

                        float score = Rooms.FurniturePlacement.Score(
                            PlacementFeaturesAt(map, room, cell, kind), weights);
                        if (score > bestScore) { bestScore = score; best = cell; }
                    }

                    if (!best.IsValid)
                    {
                        // Which of the two this is decides the fix entirely, and the first
                        // version of this line could not tell them apart: an interior with
                        // nothing placeable in it at all is a siting problem, whereas one whose
                        // every candidate the game refused is a fit problem — a 2x2 table in a
                        // room that has the cells but not four of them together.
                        lastPlacementFailure = refusedCells.Count == 0
                            ? def.defName + " found no placeable cell in the interior at all " +
                              "(every cell holds rock, a building or pending construction)"
                            : def.defName + " was refused at all " + refusedCells.Count +
                              " placeable cells in the interior — its footprint does not fit " +
                              "anywhere among what is already there";
                        return;
                    }

                    // Orientation is part of whether a footprint fits at all, so a long item
                    // gets asked both ways before the cell is written off.
                    if (PlacementUtil.TryPlace(map, def, best, Rot4.North, stuff) ||
                        PlacementUtil.TryPlace(map, def, best, Rot4.East, stuff))
                    {
                        placed = true;
                        lastPlacementFailure = null;
                        placedThisPass++;
                    }
                    else
                    {
                        refusedCells.Add(best);
                        lastPlacementFailure = def.defName + " was refused at " + best +
                            " in both orientations (the game's own placement rules)";
                    }
                }

                if (!placed) return;
            }
        }

        /// <summary>
        /// Records what the game itself thinks of the finished room.
        ///
        /// RimWorld scores a room on space, beauty and cleanliness and turns those into the
        /// impressiveness a colonist actually feels, and none of it was ever read — so how a
        /// room turned out was invisible and furniture placement could not be judged at all.
        /// The stats are only meaningful for an enclosed room, which is the other reason the
        /// completeness test had to start asking the game whether the walls actually close.
        /// </summary>
        static void ReportRoomStats(DirectorContext ctx, PlannedRoom planned)
        {
            try
            {
                var room = planned.Center.GetRoom(ctx.map);
                if (room == null || room.TouchesMapEdge || room.PsychologicallyOutdoors) return;

                Chronicle.Record(ChronicleCategory.Build, string.Format(
                    "{0} room finished — space {1:0.0}, beauty {2:0.0}, cleanliness {3:0.00}, " +
                    "impressiveness {4:0.0}",
                    planned.role,
                    room.GetStat(RoomStatDefOf.Space),
                    room.GetStat(RoomStatDefOf.Beauty),
                    room.GetStat(RoomStatDefOf.Cleanliness),
                    room.GetStat(RoomStatDefOf.Impressiveness)));
            }
            catch (Exception) { }
        }

        /// <summary>What this def wants from a cell, which is not the same for all of them.</summary>
        static Rooms.FurnitureKind KindOf(ThingDef def)
        {
            if (def == null) return Rooms.FurnitureKind.Other;
            if (typeof(Building_Bed).IsAssignableFrom(def.thingClass)) return Rooms.FurnitureKind.Bed;
            if (typeof(Building_WorkTable).IsAssignableFrom(def.thingClass))
                return Rooms.FurnitureKind.WorkTable;
            if (def.surfaceType == SurfaceType.Eat) return Rooms.FurnitureKind.Surface;
            if (typeof(Building_Storage).IsAssignableFrom(def.thingClass))
                return Rooms.FurnitureKind.Storage;
            if (def.HasComp(typeof(CompGlower))) return Rooms.FurnitureKind.Light;
            return Rooms.FurnitureKind.Other;
        }

        Rooms.PlacementWeights PlacementWeightsFor(DirectorContext ctx, Rooms.FurnitureKind kind)
        {
            var w = new Rooms.PlacementWeights();
            w.doorClearance = ctx.Gene(
                Rooms.FurniturePlacement.GeneKey(kind, Rooms.FurniturePlacement.DoorClearance));
            w.access = ctx.Gene(
                Rooms.FurniturePlacement.GeneKey(kind, Rooms.FurniturePlacement.Access));
            w.wallHugging = ctx.Gene(
                Rooms.FurniturePlacement.GeneKey(kind, Rooms.FurniturePlacement.WallHugging));
            w.spacing = ctx.Gene(
                Rooms.FurniturePlacement.GeneKey(kind, Rooms.FurniturePlacement.Spacing));
            w.partnerAffinity = ctx.Gene(
                Rooms.FurniturePlacement.GeneKey(kind, Rooms.FurniturePlacement.Partner));
            w.purity = ctx.Gene(
                Rooms.FurniturePlacement.GeneKey(kind, Rooms.FurniturePlacement.Purity));
            return w;
        }

        /// <summary>Reads the cell: how open it is, what it backs onto, what is already near.</summary>
        static Rooms.PlacementFeatures PlacementFeaturesAt(Map map, PlannedRoom room, IntVec3 cell,
                                                           Rooms.FurnitureKind kind)
        {
            var f = new Rooms.PlacementFeatures();
            f.fromDoor = (cell - room.Door).LengthHorizontal;

            int free = 0;
            bool wall = false;
            for (int i = 0; i < 4; i++)
            {
                var side = cell + GenAdj.CardinalDirections[i];
                if (!side.InBounds(map)) { wall = true; continue; }

                var edifice = side.GetEdifice(map);
                if (edifice != null) { wall = true; continue; }
                if (PlacementUtil.HasAnyConstructionAt(map, side)) continue;
                if (side.Standable(map)) free++;
            }
            f.freeSides = free;
            f.againstWall = wall;

            // What is nearby, and what *kind* it is. Knowing only that something occupies a cell
            // is enough to space things out and nothing else — it cannot tell a bench that the
            // shelf it reaches into is on the far side of the room.
            float nearest = 99f;
            float nearestPartner = 99f;
            int furniture = 0;
            int samePurpose = 0;
            var partner = Rooms.FurniturePlacement.PartnerOf(kind);

            foreach (var other in room.Interior)
            {
                if (other == cell) continue;

                var standing = PlacementUtil.BuildTargetOfCell(map, other);
                if (standing == null) continue;

                float d = (cell - other).LengthHorizontal;
                if (d < nearest) nearest = d;

                var otherKind = KindOf(standing);
                furniture++;
                if (SharesPurpose(kind, otherKind)) samePurpose++;

                if (partner.HasValue && otherKind == partner.Value && d < nearestPartner)
                    nearestPartner = d;
            }

            f.fromOtherFurniture = nearest;
            f.fromPartnerFurniture = nearestPartner;
            f.roomPurity = furniture > 0 ? samePurpose / (float)furniture : 1f;

            return f;
        }

        /// <summary>
        /// Whether two kinds belong to the same sort of room.
        ///
        /// A bench and a shelf are both workshop things; a bed among them is not. RimWorld
        /// decides a room's role the same way — by what is in it — and a workshop with a bed in
        /// the corner stops being read as a workshop at all.
        /// </summary>
        static bool SharesPurpose(Rooms.FurnitureKind a, Rooms.FurnitureKind b)
        {
            if (a == b) return true;
            if (a == Rooms.FurnitureKind.Light || b == Rooms.FurnitureKind.Light) return true;

            bool aWork = a == Rooms.FurnitureKind.WorkTable || a == Rooms.FurnitureKind.Storage;
            bool bWork = b == Rooms.FurnitureKind.WorkTable || b == Rooms.FurnitureKind.Storage;
            return aWork && bWork;
        }

        /// <summary>
        /// How many of this thing the room already has, counting what is built and what is still
        /// only ordered. Both have to count, or a second copy goes down every pass until the
        /// first one is finished.
        /// </summary>
        static int ExistingCount(Map map, PlannedRoom room, ThingDef def)
        {
            int found = 0;
            foreach (var cell in room.Rect)
            {
                if (!cell.InBounds(map)) continue;

                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var thing = things[i];
                    if (thing == null) continue;

                    // Only at its own anchor cell. A three-cell table appears in three cells'
                    // thing lists, and counting it once per cell would report a single bench as
                    // three and quietly stop the room ever being furnished.
                    if (thing.Position != cell) continue;

                    if (PlacementUtil.BuildTargetOf(thing) == def) found++;
                }
            }
            return found;
        }

    }
}
