using System.Collections.Generic;
using AutoColony.Learning;
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
                if (!room.wallsQueued)
                {
                    QueueShell(ctx, room);
                    room.wallsQueued = true;
                    Note("queued walls for " + room.role + " room");
                    return;
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

                room.furnitureQueued = false;
                Chronicle.Record(ChronicleCategory.Build,
                    room.role + " room is missing its key furniture — re-queuing it");
                Note("re-queuing lost furniture in " + room.role + " room");
                return;
            }

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
                        room.wallsQueued = false;
                        Note("re-queuing lost walls for " + room.role + " room");
                        return;
                    }
                    continue;
                }

                QueueFurniture(ctx, room);
                room.furnitureQueued = true;
                Note("furnished " + room.role + " room");
                return;
            }

            // Finish the room the plan actually asked for before opening another.
            //
            // Reserving a room aims at the focus, but only at the moment of reserving. After
            // that the planner would happily reserve a hospital while the power room it asked
            // for stood half-built — and since each reservation adds another shell to the
            // queue, a small colony spreads itself across all of them and completes none.
            // Observed with two colonists and seven outstanding shells, the power room among
            // them, at a standstill.
            if (FocusRoomUnfinished(ctx)) return;

            // Everything reserved is done; decide what the colony needs next. A null answer
            // means the base is complete for the current population — the planner then stops
            // rather than tiling bedrooms across the map forever.
            RoomRole role;
            if (!TryChooseNextRole(ctx, out role)) return;

            var reserved = ReserveRoom(ctx, role);
            if (reserved != null)
            {
                ctx.Credit(BanditId, role.ToString());
                Note("reserved a new " + role + " room");
            }
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

            int bedsPerRoom = Clamp(ctx.GeneInt(Genes.BaseBedsPerRoom), 1, 4);
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

            // Everything past this point is discretionary, so let experience decide.
            var options = new List<string>();
            if (!layout.HasRoom(RoomRole.Workshop)) options.Add(RoomRole.Workshop.ToString());
            if (!layout.HasRoom(RoomRole.Research)) options.Add(RoomRole.Research.ToString());
            if (!layout.HasRoom(RoomRole.Dining)) options.Add(RoomRole.Dining.ToString());
            if (!layout.HasRoom(RoomRole.Hospital)) options.Add(RoomRole.Hospital.ToString());
            if (s.prisoners > 0 && !layout.HasRoom(RoomRole.Prison)) options.Add(RoomRole.Prison.ToString());

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
            int size = RoomSize(ctx);

            // Try successive slots until one lands somewhere buildable. Slots are capped so a
            // hemmed-in base stops searching instead of marching rooms off across the map.
            for (int attempt = 0; attempt < 24; attempt++)
            {
                if (layout.nextSlot >= MaxSlots) return null;
                int slot = layout.nextSlot++;
                bool north = (slot % 2) == 0;
                int index = slot / 2;

                // Fan out alternately left and right of the origin.
                int lateral = ((index % 2) == 0 ? 1 : -1) * ((index + 1) / 2);
                int xMin = layout.origin.x + lateral * (size - 1);
                int zMin = north
                    ? layout.origin.z + 2
                    : layout.origin.z - 1 - (size - 1);

                var rect = new CellRect(xMin, zMin, size, size);
                if (!rect.InBounds(map)) continue;
                if (PlacementUtil.BuildableFraction(map, rect) < 0.8f) continue;
                if (OverlapsExisting(layout, rect)) continue;

                var room = new PlannedRoom();
                room.minX = xMin;
                room.minZ = zMin;
                room.width = size;
                room.height = size;
                room.role = role;
                room.doorX = xMin + size / 2;
                room.doorZ = north ? zMin : zMin + size - 1;

                layout.rooms.Add(room);
                return room;
            }

            return null;
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

        void QueueShell(DirectorContext ctx, PlannedRoom room)
        {
            var map = ctx.map;

            // Material is a reading of the conditions, not a fixed taste. Storage leans harder
            // toward stone than the rest: it is where the colony's value ends up, so a fire
            // there is not an inconvenience but the loss of everything worth hauling indoors.
            float risk = FireRisk.Assess(map, ctx.state);
            float stonePref = room.role == RoomRole.Storage
                ? FireRisk.StorageStonePreference(ctx, risk)
                : FireRisk.StonePreference(ctx, risk);

            var wallDef = AcDefs.Wall;
            var doorDef = AcDefs.Door;
            if (wallDef == null) return;

            bool gotPreferred;
            var wallStuff = PlacementUtil.ChooseStuff(map, wallDef, stonePref, out gotPreferred);
            var doorStuff = doorDef != null ? PlacementUtil.ChooseStuff(map, doorDef, stonePref) : null;

            var rect = room.Rect;
            var door = room.Door;

            foreach (var cell in rect.EdgeCells)
            {
                if (placedThisPass >= MaxPlacementsPerPass) return;

                if (cell.x == door.x && cell.z == door.z)
                {
                    if (doorDef != null && PlacementUtil.TryPlace(map, doorDef, cell, Rot4.North, doorStuff))
                        placedThisPass++;
                    continue;
                }

                if (PlacementUtil.TryPlace(map, wallDef, cell, Rot4.North, wallStuff))
                    placedThisPass++;
            }

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
        static bool ShellComplete(Map map, PlannedRoom room)
        {
            var door = room.Door;
            foreach (var cell in room.Rect.EdgeCells)
            {
                if (cell.x == door.x && cell.z == door.z) continue;
                if (!cell.InBounds(map)) continue;
                if (cell.GetEdifice(map) == null) return false;
            }
            return true;
        }

        void QueueFurniture(DirectorContext ctx, PlannedRoom room)
        {
            switch (room.role)
            {
                case RoomRole.Bedroom:
                    PlaceMany(ctx, room, AcDefs.Bed, Clamp(ctx.GeneInt(Genes.BaseBedsPerRoom), 1, 4));
                    break;
                case RoomRole.Kitchen:
                    PlaceOne(ctx, room, StoveFor(ctx));
                    PlaceOne(ctx, room, AcDefs.ButcherTable);
                    break;
                case RoomRole.Research:
                    PlaceOne(ctx, room, AcDefs.ResearchBench);
                    break;
                case RoomRole.Workshop:
                    PlaceOne(ctx, room, AcDefs.StonecuttersTable);
                    PlaceOne(ctx, room, AcDefs.CraftingSpot);
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

        static bool KeyFurnitureMissing(DirectorContext ctx, PlannedRoom room)
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
            var stuff = PlacementUtil.ChooseStuff(map, def,
                FireRisk.StonePreference(ctx, FireRisk.Assess(map, ctx.state)));
            int placed = 0;

            foreach (var cell in room.Interior)
            {
                if (placed >= count || placedThisPass >= MaxPlacementsPerPass) return;
                // Keep the cell in front of the door clear so nothing blocks the entrance.
                if ((cell - room.Door).LengthHorizontalSquared <= 2) continue;

                if (PlacementUtil.TryPlace(map, def, cell, Rot4.North, stuff))
                {
                    placed++;
                    placedThisPass++;
                }
            }
        }

        static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
