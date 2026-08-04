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

            // How far to walk for something depends on what it is.
            //
            // Claiming was radius alone, so a slag chunk ten cells out was worth fetching and a
            // component forty cells out was not — while the plan sat on "needs 6
            // ComponentIndustrial" and could not build a generator without them.
            //
            // What an item is worth is not a property of the item. It is a property of the item
            // and the colony together: components are what a generator, an electric stove and a
            // cooler are made of and what repairs them; a dead raider's rifle is a colonist who
            // hunts better and survives the next raid; medicine off a corpse is the difference
            // between tending an infection and watching it. The plan already states which of
            // these the colony is short of, in exactly these terms, and nothing was reading it.
            //
            // So the radius is the default and an outstanding need overrides it. Deliberately a
            // wider reach rather than an unlimited one — a colonist crossing the whole map for
            // one component is an afternoon spent, and the point is to value the walk, not to
            // ignore its cost.
            int wantedRadiusSq = radius * NeededReach * radius * NeededReach;
            int wanted = 0;

            for (int i = 0; i < buffer.Count && done < MaxPerPass; i++)
            {
                var thing = buffer[i];
                if (!Claimable(thing, map)) continue;

                bool needed = Needed(ctx, thing);
                int reachSq = needed ? wantedRadiusSq : radiusSq;
                if ((thing.Position - origin).LengthHorizontalSquared > reachSq) continue;

                ForbidUtility.SetForbidden(thing, false, false);
                done++;
                if (needed) wanted++;
            }

            if (wanted > 0)
                Note("claimed " + wanted + " items the plan is short of, beyond the usual radius");

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

        /// <summary>How much further the colony will walk for something it actually needs.</summary>
        const int NeededReach = 2;

        /// <summary>
        /// Whether the plan is currently short of this.
        ///
        /// Asked of the goal layer rather than of a list here, so what counts as valuable
        /// changes as the colony's situation does — steel while it is building, components while
        /// it wants power, and neither once those are met.
        /// </summary>
        static bool Needed(DirectorContext ctx, Thing thing)
        {
            if (ctx.plan == null || thing == null || thing.def == null) return false;
            if (!ctx.plan.Needs.Any) return false;

            return ctx.plan.Needs.For(thing.def.defName) > 0;
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
