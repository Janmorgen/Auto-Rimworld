using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Applies the per-colonist settings a player would otherwise tweak by hand: medical care
    /// level, self-tending, how to react to hostiles, and what to do with prisoners.
    ///
    /// Medicine policy is a genuine economic choice — glitterworld medicine on every scratch
    /// wins fights and loses winters — so the care level is a gene rather than a constant.
    /// </summary>
    public class ColonistPolicyModule : DirectorModule
    {
        public override string Name { get { return "Colonist policy"; } }
        /// <summary>
        /// Medical care level and self-tending are settings about being wounded, and this runs
        /// every eight in-game hours by default — long enough that a casualty can be treated
        /// under the wrong policy from start to finish, or die under it.
        /// </summary>
        public override bool Urgent(DirectorContext ctx)
        {
            return ctx.state.colonistsDowned > 0 || ctx.state.hostilesNearBase > 0;
        }

        public override int IntervalTicks { get { return 20000; } }

        // Prisoners used to be handled here too, as a straight recruit-or-not read off the
        // genome. They now belong to PrisonerModule, which weighs the person against the
        // colony's situation — and two modules writing the same setting on alternate passes
        // would simply have fought each other.
        protected override void Act(DirectorContext ctx)
        {
            EnsureComfortPolicy(ctx);

            ApplyColonistSettings(ctx);
            ApplyShelterRestriction(ctx);
        }

        /// <summary>The area colonists are confined to while the open sky is dangerous.</summary>
        Area_Allowed shelterArea;
        bool confined;

        /// <summary>
        /// Keeps everyone under a roof while a condition makes being outdoors the hazard.
        ///
        /// Toxic fallout gives toxic buildup to any pawn *not under a roof*, at 40% a day, and a
        /// roof is the whole of the protection — so refusing to designate hunts is not enough on
        /// its own. Colonists go outside for a hundred other reasons, and each errand costs them
        /// poison. RimWorld's own answer to this is the allowed area, so that is what the
        /// director uses: a region of roofed cells inside the home area, applied while the
        /// condition lasts and released the moment it passes.
        ///
        /// Releasing matters as much as applying. A colony left confined after the fallout ends
        /// would quietly starve inside its own base, having stopped farming, hauling and hunting
        /// for good — which is a worse failure than the one being prevented, and precisely the
        /// kind of standing order this codebase already learned to withdraw elsewhere.
        /// </summary>
        void ApplyShelterRestriction(DirectorContext ctx)
        {
            bool wantConfined = Conditions.ConditionResponse.OutsideIsDangerous(ctx.state.conditions);

            if (!wantConfined)
            {
                if (!confined) return;

                confined = false;
                for (int i = 0; i < ctx.state.allColonists.Count; i++)
                {
                    var ps = ctx.state.allColonists[i].playerSettings;
                    if (ps != null) ps.AreaRestrictionInPawnCurrentMap = null;
                }
                Chronicle.Record(ChronicleCategory.Health,
                    "conditions passed — colonists released from shelter, free to work outdoors again");
                return;
            }

            var area = EnsureShelterArea(ctx);
            if (area == null) return;

            int moved = 0;
            for (int i = 0; i < ctx.state.allColonists.Count; i++)
            {
                var ps = ctx.state.allColonists[i].playerSettings;
                if (ps == null) continue;
                if (ps.AreaRestrictionInPawnCurrentMap == area) continue;
                ps.AreaRestrictionInPawnCurrentMap = area;
                moved++;
            }

            if (moved > 0 || !confined)
            {
                confined = true;
                Chronicle.Record(ChronicleCategory.Health, string.Format(
                    "{0} — confining {1} colonists to {2} roofed cells; a roof is the whole of the " +
                    "protection and every errand outside costs them poison",
                    Conditions.ConditionResponse.Describe(ctx.state.conditions),
                    moved, area.TrueCount));
            }
        }

        /// <summary>
        /// Builds or refreshes the roofed area. Rebuilt each time the condition starts, because
        /// the base grows and an area drawn around last season's walls would strand people
        /// outside the rooms built since.
        /// </summary>
        Area_Allowed EnsureShelterArea(DirectorContext ctx)
        {
            if (shelterArea != null && shelterArea.TrueCount > 0 && confined) return shelterArea;

            var map = ctx.map;
            if (map.areaManager == null || map.roofGrid == null) return null;

            if (shelterArea == null || !map.areaManager.AllAreas.Contains(shelterArea))
            {
                if (!map.areaManager.TryMakeNewAllowed(out shelterArea)) return null;
                shelterArea.SetLabel("Under cover");
            }

            int roofed = 0;
            var home = map.areaManager.Home;
            foreach (var cell in map.AllCells)
            {
                bool under = map.roofGrid.Roofed(cell) && cell.Walkable(map) &&
                             (home == null || home[cell]);
                shelterArea[cell] = under;
                if (under) roofed++;
            }

            // Nowhere to shelter is worth saying rather than silently confining people to an
            // empty area, which would stop them working without protecting them from anything.
            if (roofed == 0)
            {
                Chronicle.Record(ChronicleCategory.Health,
                    "conditions call for shelter but the colony has no roofed walkable space — " +
                    "leaving everyone free rather than penning them into nothing");
                return null;
            }

            return shelterArea;
        }

        void ApplyColonistSettings(DirectorContext ctx)
        {
            var care = (MedicalCareCategory)AcMath.Clamp(ctx.GeneInt(Genes.ColonistMedCare), 0, 4);
            bool selfTend = ctx.Gene(Genes.ColonistSelfTend) >= 0.5f;
            int changed = 0;

            for (int i = 0; i < ctx.state.allColonists.Count; i++)
            {
                var pawn = ctx.state.allColonists[i];
                var ps = pawn.playerSettings;
                if (ps == null) continue;

                if (ps.medCare != care)
                {
                    ps.medCare = care;
                    changed++;
                }

                if (ps.selfTend != selfTend)
                {
                    ps.selfTend = selfTend;
                    changed++;
                }

                // Non-combatants should run rather than trade shots they cannot win.
                var wanted = pawn.WorkTagIsDisabled(WorkTags.Violent)
                    ? HostilityResponseMode.Flee
                    : HostilityResponseMode.Attack;

                if (ps.hostilityResponse != wanted)
                {
                    ps.hostilityResponse = wanted;
                    changed++;
                }
            }

            if (changed > 0) Note("updated policy on " + changed + " settings");
        }

        /// <summary>Set once the tea entry has been written, so the policy is not rewritten hourly.</summary>
        static bool teaPolicyEnsured;

        /// <summary>
        /// Lets colonists drink psychite tea when they are miserable, and only then.
        ///
        /// The default SocialDrugs policy covers beer and smokeleaf and not tea, so a colony
        /// could brew tea it would never drink. The entry added here is the game's own gating,
        /// not a rule of ours: allowedForJoy with onlyIfMoodBelow means a content colonist
        /// never touches it and one at 0.35 has something between them and a break.
        ///
        /// The mood floor is the addiction guard. Tea is joy 0.40 at addictiveness 0.02 — the
        /// safest ratio in the game — but the AlcoholWithdrawal postmortem is what a standing
        /// habit looks like, so the policy hands it out as medicine rather than as routine.
        /// </summary>
        static void EnsureComfortPolicy(DirectorContext ctx)
        {
            if (teaPolicyEnsured) return;
            var tea = AcDefs.PsychiteTea;
            if (tea == null) return;

            try
            {
                var pawns = ctx.map.mapPawns.FreeColonists;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var pawn = pawns[i];
                    if (pawn == null || pawn.drugs == null) continue;
                    var policy = pawn.drugs.CurrentPolicy;
                    if (policy == null) continue;

                    for (int e = 0; e < policy.Count; e++)
                    {
                        var entry = policy[e];
                        if (entry == null || entry.drug != tea) continue;
                        entry.allowedForJoy = true;
                        entry.onlyIfMoodBelow = 0.35f;
                    }
                }
                teaPolicyEnsured = true;
                Chronicle.Record(ChronicleCategory.Health,
                    "psychite tea allowed for joy below mood 0.35 — handed out as medicine, not " +
                    "routine, which is the difference between a comfort and a habit");
            }
            catch (System.Exception) { }
        }

    }
}
