using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoColony.Modules
{
    /// <summary>
    /// Buys what the colony cannot make, from whoever is standing on the map offering it.
    ///
    /// The director could not trade at all. Fifteen modules and not one of them about it; the
    /// only mention of a trader anywhere in the codebase was a doc comment in IncidentModule
    /// listing "traders offering deals" among the kinds of thing an incident can be. A caravan
    /// walked onto the map, was answered like any other incident, and walked off again.
    ///
    /// Run 148, day 33, is the whole argument in one screen: a slaver caravan standing on the
    /// map, "Low medicine" in the alert column, med 0 in the vitals, and 817 silver in the
    /// stockpile. Nothing in the director could connect those three facts. Medicine is what
    /// these colonies run out of, and untreated bleeding has killed more colonists here than
    /// anything else — runs 132, 134, 135, 138, 144, 145 and 147 all lost people to it, several
    /// with medicine sitting at zero.
    ///
    /// Trading with a visiting caravan needs no research whatsoever. It needs a colonist who can
    /// walk to the trader and some silver. The colony had both, every time.
    ///
    /// What to buy is deliberately not a list. It is whatever the colony is short of by a
    /// measure it already keeps, so adding a second want later needs no new branch here — see
    /// <see cref="Shortfalls"/>.
    /// </summary>
    public class TradeModule : DirectorModule
    {
        public override string Name { get { return "Trade"; } }

        /// <summary>
        /// Traders do not linger. A caravan is on the map for around a day, and the check is
        /// cheap when there is nobody to trade with — the first thing it does is look for a
        /// trader and give up.
        /// </summary>
        public override int IntervalTicks { get { return 2500; } }

        /// <summary>What the colony last said about a trader, so it says it once per visit.</summary>
        string lastTraderNoted = "";

        protected override void Act(DirectorContext ctx)
        {
            var map = ctx.map;
            if (map == null) return;

            var trader = FindTrader(map);
            if (trader == null) { lastTraderNoted = ""; return; }

            var wants = Shortfalls(ctx);
            if (wants.Count == 0)
            {
                NoteTrader(trader, "nothing the colony is short of");
                return;
            }

            var negotiator = ChooseNegotiator(ctx, trader);
            if (negotiator == null)
            {
                NoteTrader(trader, "nobody who can reach them and talk");
                return;
            }

            Buy(ctx, trader, negotiator, wants);
        }

        /// <summary>
        /// A trader standing on the map, not hostile, with stock.
        ///
        /// Orbital ships are deliberately out of scope: they need a comms console and a powered
        /// beacon, which is Microelectronics away, and the case that keeps killing colonies is
        /// the caravan that walks past a colony with no electricity at all.
        /// </summary>
        static Pawn FindTrader(Map map)
        {
            try
            {
                var pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var pawn = pawns[i];
                    if (pawn == null || pawn.Dead || pawn.Downed) continue;
                    if (pawn.TraderKind == null) continue;
                    if (pawn.HostileTo(Faction.OfPlayer)) continue;
                    return pawn;
                }
            }
            catch (System.Exception) { }
            return null;
        }

        /// <summary>
        /// What the colony is short of, by a threshold it already keeps.
        ///
        /// Medicine only, for now, and the number is not invented here: it is the same two per
        /// colonist that RimWorld's own Alert_LowMedicine uses, read out of the game rather
        /// than chosen. A colony that has enough by the game's own reckoning is not short.
        ///
        /// Shaped as a list so a second want costs a line rather than a branch.
        /// </summary>
        static List<Want> Shortfalls(DirectorContext ctx)
        {
            var wants = new List<Want>();
            var s = ctx.state;

            // medicineCount, not medicineStored, and the difference matters. A doctor fetches
            // medicine from anywhere reachable, so the stockpile is irrelevant to whether a
            // wound gets treated — ColonyState says so where it captures both, having been
            // caught by the stockpile version in run 84. Reading the stored figure here would
            // have the colony buying medicine it already owns and cannot be bothered to haul.
            //
            // Any tier will do, and it has to. The shortfall is counted across herbal,
            // industrial and ultratech together — that is what medicineCount is — so asking
            // for one specific def measured a need in one currency and shopped in another.
            // Run 153: a trader stood on the map on day 25 with the colony short, the deal
            // opened, and nothing was bought; Jet died of an infection on day 27. Most traders
            // stock industrial medicine and the want named herbal.
            int wantedMedicine = s.colonists * 2;
            if (s.medicineCount < wantedMedicine)
            {
                var tiers = new List<ThingDef>();
                foreach (var name in new[] { "MedicineIndustrial", "MedicineHerbal", "MedicineUltratech" })
                {
                    var def = AcDefs.Thing(name);
                    if (def != null) tiers.Add(def);
                }
                if (tiers.Count > 0)
                    wants.Add(new Want("medicine", tiers, wantedMedicine - s.medicineCount));
            }

            return wants;
        }

        /// <summary>
        /// Something the colony is short of, and every def that would answer it.
        ///
        /// A want names a need, not a product. "Medicine" is satisfied by any of three tiers,
        /// and a want that named one of them would go unfilled beside a trader carrying the
        /// other two.
        /// </summary>
        class Want
        {
            public readonly string label;
            public readonly List<ThingDef> acceptable;
            public int outstanding;

            public Want(string label, List<ThingDef> acceptable, int outstanding)
            {
                this.label = label;
                this.acceptable = acceptable;
                this.outstanding = outstanding;
            }

            public bool Accepts(ThingDef def)
            {
                return def != null && acceptable.Contains(def);
            }
        }

        /// <summary>
        /// Who does the talking. Social decides the price, so it is worth choosing.
        ///
        /// Must be able to reach the trader: a negotiator who cannot walk there is the same
        /// failure as a bed nobody can carry a casualty to, and this codebase has been caught
        /// by that shape often enough to check first.
        /// </summary>
        static Pawn ChooseNegotiator(DirectorContext ctx, Pawn trader)
        {
            Pawn best = null;
            float bestSkill = -1f;

            var able = ctx.state.ableColonists;
            for (int i = 0; i < able.Count; i++)
            {
                var pawn = able[i];
                if (pawn == null || pawn.Downed || pawn.Dead) continue;
                if (pawn.skills == null) continue;
                if (pawn.WorkTagIsDisabled(WorkTags.Social)) continue;

                try
                {
                    if (!pawn.CanReach(trader, PathEndMode.Touch, Danger.Some)) continue;
                }
                catch (System.Exception) { continue; }

                var social = pawn.skills.GetSkill(SkillDefOf.Social);
                float level = social != null && !social.TotallyDisabled ? social.Level : 0f;
                if (level > bestSkill) { bestSkill = level; best = pawn; }
            }
            return best;
        }

        /// <summary>
        /// Open the deal, take what is wanted and can be afforded, close it.
        ///
        /// Direction is named rather than signed. CountToTransfer has a non-public setter and
        /// its sign convention is not something to guess at — Transferable offers
        /// ForceToDestination and ForceToSource, which say which way the goods move instead of
        /// leaving it to be inferred. Even then the result is checked against ActionToDo before
        /// it is trusted, because "which side is the destination" is exactly the sort of
        /// unverified assumption that has cost this project colonies.
        /// </summary>
        void Buy(DirectorContext ctx, Pawn trader, Pawn negotiator, List<Want> wants)
        {
            var traderInterface = trader as ITrader;
            if (traderInterface == null) return;

            bool opened = false;
            try
            {
                TradeSession.SetupWith(traderInterface, negotiator, false);
                opened = true;

                var deal = TradeSession.deal;
                if (deal == null) return;

                int silver = ctx.state.silver;
                var bought = new List<string>();

                // Why nothing was bought, kept apart rather than lumped.
                //
                // The first version of this said "nothing affordable that the colony wants" for
                // three different failures, and on run 153 that line appeared two days before a
                // colonist died of an infection — with no way to tell whether the trader had no
                // medicine, the colony could not afford it, or the trade API had not done what
                // was asked. A diagnostic that cannot separate its own causes is the fault this
                // director keeps finding elsewhere.
                int offered = 0, unaffordable = 0, refused = 0;
                float dearest = 0f;

                var lines = deal.AllTradeables;
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (line == null || !line.TraderWillTrade || !line.HasAnyThing) continue;

                    var want = WantFor(wants, line.ThingDef);
                    if (want == null || want.outstanding <= 0) continue;

                    int available = line.CountHeldBy(Transactor.Trader);
                    if (available <= 0) continue;

                    offered++;
                    int take = want.outstanding < available ? want.outstanding : available;

                    // Never spend the colony down to nothing. Silver is also what a colony
                    // buys its next emergency with.
                    float each = line.GetPriceFor(TradeAction.PlayerBuys);
                    if (each > 0f)
                    {
                        if (each > dearest) dearest = each;
                        int affordable = (int)(silver * 0.7f / each);
                        if (take > affordable) take = affordable;
                    }
                    if (take <= 0) { unaffordable++; continue; }

                    if (!AskFor(line, take)) { refused++; continue; }

                    silver -= (int)(each * take);
                    want.outstanding -= take;
                    bought.Add(take + " " + line.Label);
                }

                if (bought.Count == 0)
                {
                    NoteTrader(trader, WhyNothing(offered, unaffordable, refused,
                                                  dearest, ctx.state.silver));
                    return;
                }

                bool traded;
                deal.TryExecute(out traded);

                if (traded)
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "bought {0} from {1} — {2} was negotiating at Social {3}, and the colony " +
                        "had {4} silver against a shortfall it could not make itself",
                        string.Join(", ", bought.ToArray()), trader.TraderName ?? trader.LabelShortCap,
                        negotiator.LabelShortCap, SocialOf(negotiator), ctx.state.silver));
                else
                    NoteTrader(trader, "the deal would not execute");
            }
            catch (System.Exception e)
            {
                AcLog.Warning("trade failed: " + e.Message);
            }
            finally
            {
                if (opened)
                {
                    try { TradeSession.Close(); } catch (System.Exception) { }
                }
            }
        }

        /// <summary>
        /// Put a line into the deal as a purchase, and confirm the game agrees that is what it
        /// is. Returns false if neither direction produced a buy, leaving the line untouched.
        /// </summary>
        static bool AskFor(Tradeable line, int count)
        {
            try
            {
                line.ForceToDestination(count);
                if (line.ActionToDo == TradeAction.PlayerBuys) return true;

                line.ForceToSource(count);
                if (line.ActionToDo == TradeAction.PlayerBuys) return true;

                line.ForceTo(0);
            }
            catch (System.Exception) { }
            return false;
        }

        static Want WantFor(List<Want> wants, ThingDef def)
        {
            for (int i = 0; i < wants.Count; i++)
                if (wants[i].Accepts(def)) return wants[i];
            return null;
        }

        /// <summary>
        /// Which of the three ways this failed, said separately, because they want different
        /// answers: stock nothing can fix, price a richer colony could, and a refusal that
        /// means the trade code itself is wrong.
        /// </summary>
        static string WhyNothing(int offered, int unaffordable, int refused,
                                 float dearest, int silver)
        {
            if (offered == 0)
                return "this trader stocks none of what the colony is short of";

            if (refused > 0)
                return refused + " line(s) the game would not let the colony buy — the trade " +
                       "code asked for something it did not accept, which is a fault here " +
                       "rather than a shortage";

            if (unaffordable > 0)
                return string.Format(
                    "it is stocked but too dear — {0:0} silver each against {1} in the colony, " +
                    "of which only 70% may be spent",
                    dearest, silver);

            return "stocked, affordable, and still nothing went through";
        }

        static int SocialOf(Pawn pawn)
        {
            if (pawn == null || pawn.skills == null) return 0;
            var skill = pawn.skills.GetSkill(SkillDefOf.Social);
            return skill != null ? skill.Level : 0;
        }

        /// <summary>
        /// Say why a trader was not traded with, once per visit.
        ///
        /// A caravan that leaves untraded looks identical to one that was never noticed, and
        /// this project has lost days to that difference before — every other refusal in the
        /// director says why.
        /// </summary>
        void NoteTrader(Pawn trader, string why)
        {
            string key = (trader.TraderName ?? trader.LabelShortCap) + "/" + why;
            if (lastTraderNoted == key) return;
            lastTraderNoted = key;

            Chronicle.Record(ChronicleCategory.Economy, string.Format(
                "a trader is here ({0}) and nothing was bought — {1}",
                trader.TraderName ?? trader.LabelShortCap, why));
        }
    }
}
