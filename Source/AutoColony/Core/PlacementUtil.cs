using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Blueprint placement helpers.
    ///
    /// Everything here validates before it acts and returns false rather than throwing: the
    /// planner speculatively tries a great many cells, and a rejected placement is a normal
    /// outcome, not an error.
    /// </summary>
    public static class PlacementUtil
    {
        /// <summary>
        /// What this thing is or is going to become: itself, or whatever a blueprint or frame is
        /// on its way to building. Null for anything that is neither.
        ///
        /// "Is there one of these here?" and "is there going to be?" are the same question almost
        /// everywhere in the director, and the three-case unwrap behind it was written out at
        /// four separate call sites.
        /// </summary>
        public static ThingDef BuildTargetOf(Thing thing)
        {
            if (thing == null) return null;

            var blueprint = thing as Blueprint;
            if (blueprint != null) return blueprint.def.entityDefToBuild as ThingDef;

            var frame = thing as Frame;
            if (frame != null) return frame.def.entityDefToBuild as ThingDef;

            return thing.def;
        }

        /// <summary>True if the cell already holds a building, blueprint, or frame.</summary>
        public static bool HasConstructionAt(Map map, IntVec3 cell, ThingDef def)
        {
            var things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (BuildTargetOf(things[i]) == def) return true;
            }
            return false;
        }

        /// <summary>
        /// What stands in this cell, or is on its way to standing there. Null for empty ground.
        ///
        /// Both have to count: furniture is scored against what is already in the room, and a
        /// blueprint placed a moment ago is exactly as real for that purpose as a finished one.
        /// </summary>
        public static ThingDef BuildTargetOfCell(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return null;

            var things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                var def = BuildTargetOf(things[i]);
                if (def == null) continue;
                if (def.category != ThingCategory.Building) continue;
                return def;
            }
            return null;
        }

        /// <summary>Any blueprint or frame at all, regardless of what it builds.</summary>
        public static bool HasAnyConstructionAt(Map map, IntVec3 cell)
        {
            var things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Blueprint || things[i] is Frame) return true;
            }
            return false;
        }

        /// <summary>
        /// Why the game would refuse this blueprint here, or null if it would take it.
        ///
        /// The same sequence of tests <see cref="TryPlace"/> runs, reported instead of collapsed
        /// to false. A placement that fails is the single most common thing to have to diagnose
        /// in this codebase and a bool says nothing about which of six causes it was — so the
        /// planner guessed in its logs instead, and told a colony it had "nothing to build from"
        /// while 1,422 units of wood sat unforbidden on the map.
        ///
        /// The last case defers to `CanPlaceBlueprintAt`, whose own reason string is written for
        /// a player looking at a red box on screen and is better than anything worth restating.
        /// </summary>
        public static string RefusalReason(Map map, ThingDef def, IntVec3 cell, Rot4 rot,
                                           ThingDef stuff)
        {
            if (map == null || def == null) return "no map or no def";
            if (!ResearchDone(def)) return def.defName + " is not researched yet";
            if (!cell.InBounds(map)) return "outside the map";
            if (HasConstructionAt(map, cell, def) || HasAnyConstructionAt(map, cell))
                return "something is already queued there";
            if (def.MadeFromStuff && stuff == null) return "no material to build it from";

            var report = GenConstruct.CanPlaceBlueprintAt(
                def, cell, rot, map, false, null, null, def.MadeFromStuff ? stuff : null);
            if (report.Accepted) return null;

            return string.IsNullOrEmpty(report.Reason)
                ? "the game refused it without saying why"
                : report.Reason;
        }

        /// <summary>
        /// Places a build blueprint if the game will accept it. Returns false when the spot is
        /// blocked, already queued, or the def cannot legally go there.
        /// </summary>
        public static bool TryPlace(Map map, ThingDef def, IntVec3 cell, Rot4 rot, ThingDef stuff)
        {
            if (map == null || def == null) return false;
            if (!ResearchDone(def)) return false;
            if (!cell.InBounds(map)) return false;
            if (HasConstructionAt(map, cell, def)) return false;
            if (HasAnyConstructionAt(map, cell)) return false;

            if (def.MadeFromStuff && stuff == null) return false;
            if (!def.MadeFromStuff) stuff = null;

            var report = GenConstruct.CanPlaceBlueprintAt(def, cell, rot, map, false, null, null, stuff);
            if (!report.Accepted) return false;

            // A "spot" is not built, it is designated.
            //
            // Crafting spots, butcher spots and their kin cost nothing and have WorkToBuild 0.
            // Queued as a blueprint they produce a frame with no work in it, which a colonist
            // walks to, cannot complete, and botches — so the spot never appears, the colonist's
            // trip is wasted, and the planner sees the furniture still missing and queues it
            // again. Spawning them outright is what the game itself does with a zero-work,
            // zero-cost building.
            if (IsSpot(def))
            {
                var spot = ThingMaker.MakeThing(def, stuff);
                spot.SetFaction(Faction.OfPlayer, null);
                GenSpawn.Spawn(spot, cell, map, rot);
                return true;
            }

            GenConstruct.PlaceBlueprintForBuild(def, cell, map, rot, Faction.OfPlayer, stuff);
            return true;
        }

        /// <summary>
        /// Whether this is a zero-work, zero-cost marker rather than something anybody builds.
        ///
        /// Tested on the def's own numbers rather than by listing names, so modded spots behave
        /// the same way and nothing has to be kept in sync.
        /// </summary>
        public static bool IsSpot(ThingDef def)
        {
            if (def == null) return false;
            if (def.costList != null && def.costList.Count > 0) return false;
            if (def.costStuffCount > 0) return false;

            try { return def.GetStatValueAbstract(StatDefOf.WorkToBuild) <= 0f; }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Whether the colony has unlocked this building.
        ///
        /// <c>GenConstruct.CanPlaceBlueprintAt</c> does not check research — the tech tree is
        /// enforced by the build menu, which the director does not go through. Without this the
        /// planner queues conduits and coolers a colony has no business owning yet, and the fact
        /// that it needs the research first never surfaces anywhere.
        /// </summary>
        public static bool ResearchDone(BuildableDef def)
        {
            return def != null && def.IsResearchFinished;
        }

        /// <summary>
        /// Picks a construction material the colony can actually afford right now.
        /// <paramref name="stonePreference"/> in [0,1] biases between wood and stone blocks —
        /// wood is fast and cheap, stone is slower but will not burn.
        /// </summary>
        public static ThingDef ChooseStuff(Map map, ThingDef def, float stonePreference)
        {
            bool ignored;
            return ChooseStuff(map, def, stonePreference, out ignored);
        }

        /// <summary>
        /// As above, reporting whether the preferred material was actually available.
        ///
        /// Worth surfacing: a colony on day one prefers stone and builds in wood because it has
        /// no cut blocks yet, which is correct but looks like the preference being ignored. A
        /// log that cannot tell those apart is a log that invites the wrong fix.
        /// </summary>
        public static ThingDef ChooseStuff(Map map, ThingDef def, float stonePreference,
                                           out bool gotPreferred)
        {
            gotPreferred = true;
            if (def == null || !def.MadeFromStuff) return null;

            // Both orderings are known at compile time; this is called several times per room.
            var order = stonePreference >= 0.5f ? StoneFirst : WoodFirst;

            int needed = def.CostStuffCount > 0 ? def.CostStuffCount : 1;
            bool preferStone = stonePreference >= 0.5f;

            for (int i = 0; i < order.Length; i++)
            {
                var stuff = AcDefs.Thing(order[i]);
                if (stuff == null || stuff.stuffProps == null) continue;
                if (!SharesAny(def.stuffCategories, stuff.stuffProps.categories)) continue;
                // Keep a reserve so building never consumes the last of a material.
                if (AvailableCount(map, stuff) < needed * 3) continue;

                bool isStone = System.Array.IndexOf(AcDefs.StoneBlockStuff, stuff.defName) >= 0;
                gotPreferred = isStone == preferStone;
                return stuff;
            }

            gotPreferred = false;

            // Nothing comfortably affordable; fall back to whatever the game would default to
            // so early colonies can still put up their first walls.
            return GenStuff.DefaultStuffFor(def);
        }

        static readonly string[] StoneFirst = Concat(AcDefs.StoneBlockStuff, AcDefs.WoodyStuff, AcDefs.MetalStuff);
        static readonly string[] WoodFirst = Concat(AcDefs.WoodyStuff, AcDefs.StoneBlockStuff, AcDefs.MetalStuff);

        static string[] Concat(params string[][] parts)
        {
            var all = new List<string>();
            for (int i = 0; i < parts.Length; i++) all.AddRange(parts[i]);
            return all.ToArray();
        }

        /// <summary>
        /// How much of a material the colony can actually build with.
        ///
        /// <c>ResourceCounter</c> only counts what is in a stockpile, which is nothing at all
        /// on the first day — so material preference was being silently ignored for the first
        /// few rooms and every choice fell through to the game's default. Colonists will haul
        /// from anywhere, so loose stacks count too.
        /// </summary>
        public static int AvailableCount(Map map, ThingDef stuff)
        {
            if (map == null || stuff == null) return 0;

            int total = map.resourceCounter != null ? map.resourceCounter.GetCount(stuff) : 0;
            if (total > 0) return total;

            var loose = map.listerThings != null ? map.listerThings.ThingsOfDef(stuff) : null;
            if (loose == null) return total;

            for (int i = 0; i < loose.Count; i++)
            {
                var thing = loose[i];
                if (thing == null || !thing.Spawned) continue;
                if (thing.IsForbidden(Faction.OfPlayer)) continue;
                total += thing.stackCount;
            }
            return total;
        }

        /// <summary>Marks a cell as part of the home area so colonists will tend and clean it.</summary>
        public static void MarkHome(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return;
            var home = map.areaManager.Home;
            if (home != null) home[cell] = true;
        }

        /// <summary>
        /// Requests a roof over a cell once its walls exist, and withdraws any standing request
        /// to take one off.
        ///
        /// The two areas are contradictory instructions and the game obeys both. A cell in
        /// BuildRoof with no roof gets one built; a cell in NoRoof with a roof gets it stripped;
        /// a cell in both is a colonist building and unbuilding the same roof for the rest of
        /// the colony's life. That is the "roof that already exists gets built again" loop, and
        /// it is a construction job's worth of labour a day, for ever, in a project where every
        /// colony that dies is short of hands.
        ///
        /// Both areas were write-only — set true in two places and never once set false — so a
        /// room demolished on day 6 left NoRoof on its ground permanently, and any room built
        /// over that ground afterwards inherited the fight.
        /// </summary>
        public static void MarkRoof(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return;
            ClearNoRoof(map, cell);
            var roof = map.areaManager.BuildRoof;
            if (roof != null) roof[cell] = true;
        }

        /// <summary>Withdraws a request to strip the roof off this cell.</summary>
        public static void ClearNoRoof(Map map, IntVec3 cell)
        {
            if (map == null || map.areaManager == null || !cell.InBounds(map)) return;
            var noRoof = map.areaManager.NoRoof;
            if (noRoof != null && noRoof[cell]) noRoof[cell] = false;
        }

        /// <summary>Withdraws a request to roof this cell, for when it is about to be pulled down.</summary>
        public static void ClearBuildRoof(Map map, IntVec3 cell)
        {
            if (map == null || map.areaManager == null || !cell.InBounds(map)) return;
            var build = map.areaManager.BuildRoof;
            if (build != null && build[cell]) build[cell] = false;
        }

        /// <summary>
        /// Asks for a roof over a cell only where one could actually be held up.
        ///
        /// Marking open ground far from any wall queues a job nobody can ever finish, and the
        /// area stays marked forever. `MarkRoof` is safe over a planned room because its walls
        /// are going up alongside; anywhere else needs this check first.
        /// </summary>
        public static bool TryMarkRoofSupported(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return false;
            if (map.roofGrid != null && map.roofGrid.Roofed(cell)) return false;
            if (!RoofCollapseUtility.WithinRangeOfRoofHolder(cell, map, false)) return false;

            var roof = map.areaManager.BuildRoof;
            if (roof == null) return false;

            ClearNoRoof(map, cell);
            roof[cell] = true;
            return true;
        }

        /// <summary>
        /// Marks a building for deconstruction, which is how the director moves anything.
        ///
        /// There is no "relocate" in the game's own terms for something already built: it comes
        /// down and goes back up elsewhere. Deconstruction returns most of the materials, so
        /// this is cheaper than it sounds — and the planner's own repair path notices the gap
        /// afterwards and re-places the building where it belongs.
        /// </summary>
        public static bool TryDeconstruct(Map map, Thing thing)
        {
            if (map == null || thing == null || !thing.Spawned) return false;
            if (thing.Faction != Faction.OfPlayer) return false;

            var def = DesignationDefOf.Deconstruct;
            if (def == null) return false;
            if (map.designationManager.DesignationOn(thing, def) != null) return false;

            map.designationManager.AddDesignation(new Designation(thing, def));
            return true;
        }

        /// <summary>
        /// True when something has already been ordered about this thing — knocked down, lifted,
        /// or carried elsewhere.
        ///
        /// The third case is easy to miss. A reinstall is a `Blueprint_Install` standing at the
        /// *destination*, not a designation on the building, so a check that looked only at
        /// designations kept reporting a stove as still needing moving while two colonists were
        /// already carrying it.
        /// </summary>
        public static bool AlreadyOrdered(Map map, Thing thing)
        {
            if (map == null || thing == null) return false;

            return HasDesignation(map, thing, DesignationDefOf.Deconstruct)
                || HasDesignation(map, thing, DesignationDefOf.Uninstall)
                || InstallBlueprintUtility.ExistingBlueprintFor(thing) != null;
        }

        /// <summary>True when this thing is already on its way out.</summary>
        public static bool MarkedForDeconstruction(Map map, Thing thing)
        {
            return HasDesignation(map, thing, DesignationDefOf.Deconstruct)
                || HasDesignation(map, thing, DesignationDefOf.Uninstall);
        }

        public static bool HasDesignation(Map map, Thing thing, DesignationDef def)
        {
            if (map == null || thing == null || def == null) return false;
            return map.designationManager.DesignationOn(thing, def) != null;
        }

        /// <summary>
        /// Whether this can be picked up and put down again rather than knocked down.
        ///
        /// Furniture — beds, chairs, tables, lamps — inherits <c>minifiedDef</c> from
        /// `FurnitureBase`, and so do batteries, heaters and turrets. Anything minifiable should
        /// be moved rather than deconstructed: it keeps every unit of material *and* its quality,
        /// where deconstruction returns only `resourcesFractionWhenDeconstructed` of the cost —
        /// a per-def figure that several vanilla buildings set to zero outright.
        /// </summary>
        public static bool Movable(Thing thing)
        {
            return thing != null && thing.def != null && thing.def.Minifiable;
        }

        /// <summary>
        /// Moves a building to another cell intact, by queueing the game's own reinstall job.
        /// Colonists uninstall it, carry it and set it down again — nothing is lost on the way.
        /// </summary>
        public static bool TryReinstall(Map map, Thing thing, IntVec3 target, Rot4 rot)
        {
            var building = thing as Building;
            if (map == null || building == null || !building.Spawned) return false;
            if (!Movable(building) || building.Faction != Faction.OfPlayer) return false;
            if (!target.InBounds(map) || target == building.Position) return false;
            if (AlreadyOrdered(map, building)) return false;

            var report = GenConstruct.CanPlaceBlueprintAt(building.def, target, rot, map,
                                                          false, building, building);
            if (!report.Accepted) return false;

            GenConstruct.PlaceBlueprintForReinstall(building, target, map, rot, Faction.OfPlayer);
            return true;
        }

        /// <summary>
        /// Takes a building up and leaves it as an item to be placed later. Worth it when the
        /// colony wants the thing but not where it currently stands, and has nowhere to put it
        /// yet — an uninstalled bed keeps its quality and all of its material.
        /// </summary>
        public static bool TryUninstall(Map map, Thing thing)
        {
            if (map == null || thing == null || !thing.Spawned) return false;
            if (!Movable(thing) || thing.Faction != Faction.OfPlayer) return false;

            var def = DesignationDefOf.Uninstall;
            if (def == null) return false;
            if (MarkedForDeconstruction(map, thing)) return false;

            map.designationManager.AddDesignation(new Designation(thing, def));
            return true;
        }

        /// <summary>
        /// Withdraws a standing order.
        ///
        /// Orders outlive the reason they were given. A bed marked to be pulled out of a barracks
        /// while the colony was comfortable is actively harmful once it is not, and nothing else
        /// in the director ever cancelled anything it had asked for.
        /// </summary>
        public static bool CancelDesignation(Map map, Thing thing, DesignationDef def)
        {
            if (map == null || thing == null || def == null) return false;

            var designation = map.designationManager.DesignationOn(thing, def);
            if (designation == null) return false;

            map.designationManager.RemoveDesignation(designation);
            return true;
        }

        /// <summary>
        /// Cancels construction that has not happened yet. A blueprint is simply removed; a frame
        /// is destroyed, which returns the material already carried to it.
        /// </summary>
        public static int CancelConstructionAt(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return 0;

            int cancelled = 0;
            var things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                var thing = things[i];
                if (!(thing is Blueprint) && !(thing is Frame)) continue;
                if (thing.Faction != Faction.OfPlayer) continue;

                thing.Destroy(DestroyMode.Cancel);
                cancelled++;
            }
            return cancelled;
        }

        /// <summary>
        /// Rough test that an area is worth building on: in bounds, mostly standable,
        /// and not water. Used to choose where the base goes.
        /// </summary>
        public static float BuildableFraction(Map map, CellRect rect)
        {
            int total = 0, good = 0;
            foreach (var c in rect)
            {
                if (!c.InBounds(map)) return 0f;
                total++;

                var terrain = c.GetTerrain(map);
                if (terrain == null) continue;
                if (terrain.passability == Traversability.Impassable) continue;
                if (!terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy)) continue;
                if (c.GetEdifice(map) != null) continue;

                good++;
            }
            return total > 0 ? good / (float)total : 0f;
        }

        static bool SharesAny<T>(List<T> a, List<T> b)
        {
            if (a == null || b == null) return false;
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                    if (Equals(a[i], b[j])) return true;
            return false;
        }
    }
}
