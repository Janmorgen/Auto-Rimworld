using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Decides which items on the map the colony is allowed to touch.
    ///
    /// RimWorld marks a great deal as forbidden: anything that arrives in a drop, anything
    /// dropped by raiders, loot lying in ruins, and — the case that actually matters at
    /// minute one — a scenario's starting resources. A colony that never unforbids them is
    /// standing next to its own steel and food unable to use either, which looks exactly like
    /// the director being incompetent rather than blocked.
    ///
    /// Both directions are handled. In peace it claims what is worth claiming within a
    /// learnable radius; under threat, a cautious strategy pushes everything outside the home
    /// area back to forbidden so haulers do not stroll into a firefight for a steel slag chunk.
    ///
    /// Two things are never claimed regardless of the genome: anything under fog, and anything
    /// inside a still-sealed structure. Unforbidding the contents of an unopened ancient danger
    /// is how a colony walks into a sealed room full of mechanoids and dies.
    /// </summary>
    public class ItemPolicyModule : DirectorModule
    {
        public override string Name { get { return "Item claiming"; } }
        public override int IntervalTicks { get { return 5000; } }

        /// <summary>Items touched per pass, so a loot-strewn map cannot stall a tick.</summary>
        const int MaxPerPass = 80;

        readonly List<Thing> buffer = new List<Thing>();

        protected override void Act(DirectorContext ctx)
        {
            var origin = ctx.Origin;
            bool threatened = ctx.state.danger != StoryDanger.None && ctx.state.hostilePawns > 0;

            if (threatened && ctx.Gene(Genes.ItemClaimDuringDanger) < 0.5f)
            {
                int locked = LockDownOutsideHome(ctx);
                if (locked > 0) Note("forbade " + locked + " items outside the home area during a threat");
                return;
            }

            int claimed = ClaimNearby(ctx, origin);
            if (claimed > 0) Note("allowed " + claimed + " forbidden items");
        }

        /// <summary>Unforbids worthwhile items within the strategy's claim radius.</summary>
        int ClaimNearby(DirectorContext ctx, IntVec3 origin)
        {
            var map = ctx.map;
            int radius = (int)ctx.Gene(Genes.ItemClaimRadius);
            int radiusSq = radius * radius;
            int done = 0;

            buffer.Clear();
            buffer.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver));
            buffer.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse));

            for (int i = 0; i < buffer.Count && done < MaxPerPass; i++)
            {
                var thing = buffer[i];
                if (!Claimable(thing, map)) continue;
                if ((thing.Position - origin).LengthHorizontalSquared > radiusSq) continue;

                ForbidUtility.SetForbidden(thing, false, false);
                done++;
            }

            return done;
        }

        /// <summary>
        /// Pushes haulables outside the home area back to forbidden while a fight is on, so
        /// colonists finish what is safe rather than walking out to meet the raid.
        /// </summary>
        int LockDownOutsideHome(DirectorContext ctx)
        {
            var map = ctx.map;
            var home = map.areaManager.Home;
            if (home == null) return 0;

            int done = 0;
            buffer.Clear();
            buffer.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver));

            for (int i = 0; i < buffer.Count && done < MaxPerPass; i++)
            {
                var thing = buffer[i];
                if (thing == null || !thing.Spawned || thing.Destroyed) continue;
                if (thing.def.category != ThingCategory.Item) continue;
                if (home[thing.Position]) continue;
                if (thing.IsForbidden(Faction.OfPlayer)) continue;

                ForbidUtility.SetForbidden(thing, true, false);
                done++;
            }

            return done;
        }

        static bool Claimable(Thing thing, Map map)
        {
            if (thing == null || !thing.Spawned || thing.Destroyed) return false;

            // Only actual items and corpses; buildings have their own ownership rules.
            if (thing.def.category != ThingCategory.Item && !(thing is Corpse)) return false;

            // Already allowed, so nothing to do.
            if (!thing.IsForbidden(Faction.OfPlayer)) return false;

            // Never reach into unexplored space: sealed structures are forbidden for a reason,
            // and opening one by sending a hauler is how a colony finds a mechanoid cluster.
            if (thing.Position.Fogged(map)) return false;

            return true;
        }
    }
}
