using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Connects things that need electricity to things that make it.
    ///
    /// Building a generator and building a turret are not the same as powering the turret, and
    /// nothing in the director previously bridged the two — a colony could own a generator, own
    /// a turret, and have a turret that did not fire. Worse, the defence model counted that
    /// turret as if it worked, so the colony looked fortified while owning a wall decoration.
    ///
    /// Conduits are cheap and the routing does not need to be clever: an L-shaped run from the
    /// consumer to the nearest source is enough, because RimWorld joins anything that touches
    /// the same net.
    /// </summary>
    public class PowerModule : DirectorModule
    {
        public override string Name { get { return "Power"; } }
        public override int IntervalTicks { get { return 7500; } }

        /// <summary>Conduit blueprints per pass, so a long run is spread over several.</summary>
        const int MaxConduitsPerPass = 60;

        readonly List<Thing> sources = new List<Thing>();
        readonly List<Thing> orphans = new List<Thing>();

        protected override void Act(DirectorContext ctx)
        {
            var map = ctx.map;
            var conduitDef = AcDefs.PowerConduit;
            if (conduitDef == null) return;

            CollectPowerThings(map);
            if (sources.Count == 0)
            {
                if (orphans.Count > 0)
                    Chronicle.Record(ChronicleCategory.Build, string.Format(
                        "{0} buildings need power and the colony has no generator yet",
                        orphans.Count));
                return;
            }
            if (orphans.Count == 0) return;

            int placed = 0;
            for (int i = 0; i < orphans.Count && placed < MaxConduitsPerPass; i++)
            {
                var orphan = orphans[i];
                var source = NearestTo(orphan.Position);
                if (source == null) continue;

                placed += RunConduit(map, conduitDef, orphan.Position, source.Position,
                                     MaxConduitsPerPass - placed);

                Chronicle.Record(ChronicleCategory.Build, string.Format(
                    "wiring {0} to {1}, {2} cells away",
                    orphan.LabelCap, source.LabelCap,
                    (orphan.Position - source.Position).LengthHorizontal.ToString("0")));
            }

            if (placed > 0) Note("laid " + placed + " conduit blueprints");
        }

        /// <summary>
        /// Splits the map's power-related buildings into things that generate or store, and
        /// things that want power but are not on any net.
        /// </summary>
        void CollectPowerThings(Map map)
        {
            sources.Clear();
            orphans.Clear();

            var buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                if (building == null || !building.Spawned) continue;

                var trader = building.TryGetComp<CompPowerTrader>();
                if (trader != null)
                {
                    // Positive base consumption means it draws; negative means it generates.
                    bool generates = trader.Props != null && trader.Props.PowerConsumption < 0f;
                    if (generates) { sources.Add(building); continue; }

                    if (trader.PowerNet == null) orphans.Add(building);
                    continue;
                }

                if (building.TryGetComp<CompPowerBattery>() != null) sources.Add(building);
            }
        }

        Thing NearestTo(IntVec3 cell)
        {
            Thing best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < sources.Count; i++)
            {
                float d = (sources[i].Position - cell).LengthHorizontalSquared;
                if (d < bestSq) { bestSq = d; best = sources[i]; }
            }
            return best;
        }

        /// <summary>
        /// Lays an L-shaped conduit run between two points. Conduits sit under other things, so
        /// this does not need to route around the base.
        /// </summary>
        static int RunConduit(Map map, ThingDef conduit, IntVec3 from, IntVec3 to, int budget)
        {
            int placed = 0;
            int x = from.x, z = from.z;

            while (x != to.x && placed < budget)
            {
                x += x < to.x ? 1 : -1;
                if (TryConduit(map, conduit, new IntVec3(x, 0, z))) placed++;
            }
            while (z != to.z && placed < budget)
            {
                z += z < to.z ? 1 : -1;
                if (TryConduit(map, conduit, new IntVec3(x, 0, z))) placed++;
            }
            return placed;
        }

        static bool TryConduit(Map map, ThingDef conduit, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return false;
            if (PlacementUtil.HasConstructionAt(map, cell, conduit)) return false;

            var report = GenConstruct.CanPlaceBlueprintAt(conduit, cell, Rot4.North, map);
            if (!report.Accepted) return false;

            GenConstruct.PlaceBlueprintForBuild(conduit, cell, map, Rot4.North, Faction.OfPlayer, null);
            return true;
        }
    }
}
