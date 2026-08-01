using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Puts a specific situation in front of the director on demand, rather than waiting for one.
    ///
    /// A colony reaches its first raid in ten real minutes and its first *losing* raid maybe
    /// never — the one that killed a test colony arrived on day twelve, after thirty-seven
    /// minutes, and could not be asked for again. Combat, capture and rescue were all effectively
    /// untestable at that rate.
    ///
    /// The game's own incident workers will fire on request, so the scenario is set up in
    /// seconds and the behaviour under test is left entirely to the director. Set
    /// <c>AUTOCOLONY_SCENARIO</c> to one of:
    ///
    ///   raid:&lt;points&gt;   an enemy raid of a chosen size — 50 is a lone tribal, 2000 a wave
    ///   downed           a downed neutral stranger, to be rescued
    ///   downedhostile    a downed raider, to be captured
    ///
    /// The reporting is the point as much as the trigger: it names what every colonist is
    /// actually *doing*, which is how "two were drafted and only one ever fought" was found.
    /// </summary>
    public class ScenarioHarness : GameComponent
    {
        static string Scenario
        {
            get { return Environment.GetEnvironmentVariable("AUTOCOLONY_SCENARIO"); }
        }

        /// <summary>Long enough for the director to have taken stock and settled.</summary>
        const int FireAtTick = 3000;

        bool fired;
        int lastReport = -9999;

        public ScenarioHarness(Game game) { }

        public override void GameComponentTick()
        {
            var scenario = Scenario;
            if (string.IsNullOrEmpty(scenario)) return;

            var map = Find.CurrentMap;
            if (map == null) return;

            int tick = Find.TickManager.TicksGame;
            if (!fired && tick >= FireAtTick)
            {
                fired = true;
                Fire(map, scenario);
            }

            if (!fired || tick - lastReport < 300) return;
            lastReport = tick;
            Report(map);
        }

        // ---------------------------------------------------------------- setting it up

        static void Fire(Map map, string scenario)
        {
            try
            {
                if (scenario.StartsWith("raid"))
                {
                    float points = 500f;
                    int colon = scenario.IndexOf(':');
                    if (colon >= 0) float.TryParse(scenario.Substring(colon + 1), out points);
                    FireRaid(map, points);
                }
                else if (scenario == "downed") SpawnDownedStranger(map, hostile: false);
                else if (scenario == "downedhostile") SpawnDownedStranger(map, hostile: true);
                else Chronicle.Record(ChronicleCategory.System, "SCENARIO: unknown '" + scenario + "'");
            }
            catch (Exception e)
            {
                Chronicle.Record(ChronicleCategory.System, "SCENARIO failed: " + e.Message);
            }
        }

        static void FireRaid(Map map, float points)
        {
            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.points = points;
            parms.forced = true;

            bool ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SCENARIO: raid at {0:0} points — {1}", points, ok ? "fired" : "REFUSED"));
        }

        /// <summary>
        /// Drops someone on the map already downed, which is the state both capture and rescue
        /// key off. Hostility decides which of the two is even available.
        /// </summary>
        static void SpawnDownedStranger(Map map, bool hostile)
        {
            var faction = hostile
                ? Find.FactionManager.RandomEnemyFaction(false, false, true)
                : Find.FactionManager.RandomNonHostileFaction(false, false, true);

            var kind = hostile ? PawnKindDefOf.Villager : PawnKindDefOf.Refugee;
            var pawn = PawnGenerator.GeneratePawn(kind, faction);

            var origin = map.mapPawns.FreeColonists.Count > 0
                ? map.mapPawns.FreeColonists[0].Position
                : map.Center;

            IntVec3 spot;
            if (!CellFinder.TryFindRandomSpawnCellForPawnNear(origin, map, out spot, 12))
                spot = origin;

            GenSpawn.Spawn(pawn, spot, map);

            // Downed, but not dying — the director should have time to decide.
            HealthUtility.DamageUntilDowned(pawn, false);

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SCENARIO: dropped {0} {1} at {2}, downed",
                hostile ? "hostile" : "neutral", pawn.LabelShortCap, spot));
        }

        // ---------------------------------------------------------------- what actually happens

        /// <summary>
        /// Names what each colonist is doing, not merely that they were drafted.
        ///
        /// Drafting and fighting are different things, and the gap between them is invisible in
        /// any summary that only counts how many were mobilised.
        /// </summary>
        static void Report(Map map)
        {
            var line = new System.Text.StringBuilder("SCENARIO: ");

            int hostiles = 0, downedHostiles = 0;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || !p.HostileTo(Faction.OfPlayer)) continue;
                if (p.Downed) downedHostiles++; else hostiles++;
            }

            line.Append(hostiles).Append(" hostiles up, ").Append(downedHostiles).Append(" down | ");

            var colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                var p = colonists[i];
                if (p == null || p.Dead) continue;

                line.Append(p.LabelShortCap).Append(p.Drafted ? "[drafted]" : "[free]");
                line.Append(p.Downed ? "(DOWN)" : "");
                line.Append('=').Append(p.CurJobDef != null ? p.CurJobDef.defName : "idle");
                line.Append("  ");
            }

            var report = line.ToString();
            if (report == lastReportLine) return;
            lastReportLine = report;
            Chronicle.Record(ChronicleCategory.System, report);
        }

        static string lastReportLine;
    }
}
