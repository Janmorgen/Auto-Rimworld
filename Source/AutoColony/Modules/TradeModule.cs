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

            // Food, which is the want I left out and the one that matters most.
            //
            // Run 156, day 8: a caravan standing on the map, the colony at 1.7 days of food
            // with Low food on screen and 800 silver in the bank, and this module said
            // "nothing the colony is short of". Medicine and the plan's materials were in the
            // list and the most basic want there is was not.
            //
            // Counted in nutrition rather than items, because a packaged meal and a bowl of
            // rice do not feed a colonist equally and "buy twenty food" means nothing. The
            // target is the same gene the rest of the director plans food against, so buying
            // stops where growing would have.
            float wantDays = ctx.FoodDaysWanted;
            // Buying against the gross figure means declining to buy on the strength of food
            // that will be compost by the time it was needed.
            if (ctx.DaysOfFoodKeeping < wantDays && s.colonists > 0)
            {
                // A colonist eats about 1.6 nutrition a day; the shortfall is the days missing
                // across everyone who has to eat.
                float missing = (wantDays - s.daysOfFood) * s.colonists * 1.6f;
                if (missing > 0f)
                    wants.Add(new Want("food", null, missing, true));
            }

            // Whatever the plan is blocked on, which it already declares.
            //
            // Reading plan.Needs rather than keeping a list here means the colony shops for
            // whatever it is currently short of, and a new goal costs nothing. It also reaches
            // the thing no colony in this project has ever obtained: components. The Power goal
            // wants six, they need Machining research and a machining table to make, and run
            // 152 sat at "components 0/6" with 811 steel and a finished research room, unable
            // to buy the one part it could not build.
            if (ctx.plan != null && ctx.plan.Needs != null)
            {
                foreach (var need in ctx.plan.Needs.All)
                {
                    var def = AcDefs.Thing(need.Key);
                    if (def == null) continue;

                    int held = HeldOf(s, need.Key);
                    int short_ = need.Value - held;
                    if (short_ <= 0) continue;

                    wants.Add(new Want(need.Key, new List<ThingDef> { def }, short_));
                }
            }

            return wants;
        }

        /// <summary>How much of a named material the colony already holds, where it counts it.</summary>
        static int HeldOf(ColonyState s, string defName)
        {
            switch (defName)
            {
                case "WoodLog": return s.wood;
                case "Steel": return s.steel;
                case "ComponentIndustrial": return s.components;
                case "Silver": return s.silver;
                default: return 0;
            }
        }

        /// <summary>
        /// What the colony can spare, so a shortfall can be met by selling rather than only by
        /// having silver.
        ///
        /// Trading is not shopping. Within one deal the colony can sell what it has too much of
        /// and buy what it has too little of, and the silver never has to exist — a colony
        /// stocked with cloth and short of medicine is not poor, it is holding the wrong goods.
        /// Every colony this project has lost to an infection was sitting on something.
        ///
        /// Surplus is measured, never listed. Only materials the colony actually counts are
        /// offered, and only above what the plan says it needs — so it will sell the wood it
        /// is not going to burn and never the wood a wall is waiting on. Anything the director
        /// does not track a need for is not sold at all, which is deliberately conservative:
        /// the failure mode of guessing here is selling the beds.
        /// </summary>
        static List<Want> Surpluses(DirectorContext ctx, List<Want> wants)
        {
            var spare = new List<Want>();
            var s = ctx.state;

            AddSpare(spare, ctx, wants, "WoodLog", s.wood, 100);
            AddSpare(spare, ctx, wants, "Steel", s.steel, 150);
            AddSpare(spare, ctx, wants, "Cloth", s.textiles, 50);

            return spare;
        }

        /// <summary>
        /// Offer what is left of a material after the plan's claim on it and a reserve.
        ///
        /// Never offers anything a want is asking for: selling medicine to buy medicine is the
        /// sort of thing that looks clever in a loop and is idiotic in a colony.
        /// </summary>
        static void AddSpare(List<Want> spare, DirectorContext ctx, List<Want> wants,
                             string defName, int held, int reserve)
        {
            var def = AcDefs.Thing(defName);
            if (def == null || held <= 0) return;
            if (WantFor(wants, def) != null) return;

            int claimed = ctx.plan != null && ctx.plan.Needs != null
                ? ctx.plan.Needs.For(defName) : 0;

            int free = held - claimed - reserve;
            if (free <= 0) return;

            spare.Add(new Want(defName, new List<ThingDef> { def }, free));
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
            public readonly List<ThingDef> acceptable;   // null means "anything that satisfies"
            public readonly bool byNutrition;
            public float outstanding;

            public Want(string label, List<ThingDef> acceptable, float outstanding,
                        bool byNutrition = false)
            {
                this.label = label;
                this.acceptable = acceptable;
                this.outstanding = outstanding;
                this.byNutrition = byNutrition;
            }

            public bool Accepts(ThingDef def)
            {
                if (def == null) return false;
                if (acceptable != null) return acceptable.Contains(def);
                return byNutrition && Feeds(def) > 0f;
            }

            /// <summary>
            /// How much of this need one of that item answers.
            ///
            /// One for a material — a steel is a steel. For food it is the item's own
            /// nutrition, read off the def, because a packaged meal and a bowl of rice do not
            /// feed a colonist equally and buying "twenty food" would mean nothing.
            /// </summary>
            public float Contribution(ThingDef def)
            {
                return byNutrition ? Feeds(def) : 1f;
            }

            /// <summary>How many of that item it would take to answer what is left.</summary>
            public int ItemsNeeded(ThingDef def)
            {
                float per = Contribution(def);
                if (per <= 0f) return 0;
                return (int)System.Math.Ceiling(outstanding / per);
            }

            /// <summary>Book what was actually taken, in the need's own units.</summary>
            public void Took(ThingDef def, int items)
            {
                outstanding -= items * Contribution(def);
                if (outstanding < 0f) outstanding = 0f;
            }

            static float Feeds(ThingDef def)
            {
                try
                {
                    if (def == null || def.ingestible == null) return 0f;
                    // Not everything edible is food for a colonist. Kibble, hay and corpses are
                    // ingestible and buying them to feed people is how a colony ends up eating
                    // something that costs it more mood than the hunger did.
                    if ((int)def.ingestible.preferability < (int)FoodPreferability.RawBad) return 0f;
                    if ((def.ingestible.foodType & FoodTypeFlags.Meal) == 0 &&
                        (def.ingestible.foodType & FoodTypeFlags.VegetableOrFruit) == 0 &&
                        (def.ingestible.foodType & FoodTypeFlags.Meat) == 0) return 0f;
                    return def.ingestible.CachedNutrition;
                }
                catch (System.Exception) { return 0f; }
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
                    int take = want.ItemsNeeded(line.ThingDef);
                    if (take > available) take = available;

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
                    want.Took(line.ThingDef, take);
                    bought.Add(take + " " + line.Label);
                }

                // Short of silver? Sell what the colony has too much of, in the same deal.
                //
                // This is the half that makes trading useful to a colony that has never been
                // rich. Silver is not the point — the point is that a colony holding two
                // hundred spare wood and no medicine is not poor, it is holding the wrong
                // goods, and one conversation fixes that. Sold only up to what the purchase
                // costs, because the aim is the medicine, not the money.
                var sold = new List<string>();
                string whyNoSale = "";
                if (unaffordable > 0 || bought.Count == 0)
                {
                    int stillNeeded = CostOfOutstanding(lines, wants, silver);
                    if (stillNeeded > 0)
                        stillNeeded = SellUpTo(lines, Surpluses(ctx, wants), stillNeeded,
                                               sold, out whyNoSale);

                    // With the proceeds in hand, try the purchases again.
                    if (sold.Count > 0)
                    {
                        offered = 0; unaffordable = 0; refused = 0;
                        for (int i = 0; i < lines.Count; i++)
                        {
                            var line = lines[i];
                            if (line == null || !line.TraderWillTrade || !line.HasAnyThing) continue;
                            if (line.ActionToDo == TradeAction.PlayerSells) continue;   // already selling this

                            var want = WantFor(wants, line.ThingDef);
                            if (want == null || want.outstanding <= 0) continue;

                            int available = line.CountHeldBy(Transactor.Trader);
                            if (available <= 0) continue;

                            offered++;
                            int take = want.ItemsNeeded(line.ThingDef);
                            if (take > available) take = available;
                            float each = line.GetPriceFor(TradeAction.PlayerBuys);
                            if (each > 0f)
                            {
                                int affordable = (int)((silver + Proceeds(lines)) * 0.7f / each);
                                if (take > affordable) take = affordable;
                            }
                            if (take <= 0) { unaffordable++; continue; }
                            if (!AskFor(line, take)) { refused++; continue; }

                            want.Took(line.ThingDef, take);
                            bought.Add(take + " " + line.Label);
                        }
                    }
                }

                if (bought.Count == 0)
                {
                    NoteTrader(trader, WhyNothing(offered, unaffordable, refused,
                                                  dearest, ctx.state.silver, wants) +
                                       (string.IsNullOrEmpty(whyNoSale)
                                            ? "" : "; and could not sell to cover it — " + whyNoSale));
                    return;
                }

                bool traded;
                deal.TryExecute(out traded);

                if (traded)
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "bought {0} from {1}{2} — {3} was negotiating at Social {4}, on {5} silver",
                        string.Join(", ", bought.ToArray()), trader.TraderName ?? trader.LabelShortCap,
                        sold.Count > 0 ? ", paying with " + string.Join(", ", sold.ToArray()) : "",
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

        /// <summary>Silver still needed to finish the outstanding wants, beyond what is spendable.</summary>
        static int CostOfOutstanding(List<Tradeable> lines, List<Want> wants, int silver)
        {
            float cost = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null || !line.TraderWillTrade || !line.HasAnyThing) continue;

                var want = WantFor(wants, line.ThingDef);
                if (want == null || want.outstanding <= 0) continue;

                int available = line.CountHeldBy(Transactor.Trader);
                if (available <= 0) continue;

                int take = want.ItemsNeeded(line.ThingDef);
                if (take > available) take = available;
                cost += line.GetPriceFor(TradeAction.PlayerBuys) * take;
            }

            int spendable = (int)(silver * 0.7f);
            int shortfall = (int)cost - spendable;
            return shortfall > 0 ? shortfall : 0;
        }

        /// <summary>
        /// Sell spare goods until the shortfall is covered. Returns what is still missing.
        ///
        /// Stops the moment the purchase is affordable rather than emptying the store — a
        /// colony that sells everything it can is a colony that will be short of it next week.
        /// </summary>
        static int SellUpTo(List<Tradeable> lines, List<Want> spare, int needed,
                            List<string> sold, out string why)
        {
            // Why the colony could not raise the money, because "sold nothing" has three
            // causes and only one of them is a shortage. Built with no diagnostic at all the
            // first time, which is the fourth instrument this session that reported less than
            // it appeared to.
            int wouldSell = 0, traderRefused = 0, apiRefused = 0;

            for (int i = 0; i < lines.Count && needed > 0; i++)
            {
                var line = lines[i];
                if (line == null) continue;

                var have = WantFor(spare, line.ThingDef);
                if (have == null || have.outstanding <= 0) continue;

                int mine = line.CountHeldBy(Transactor.Colony);
                if (mine <= 0) continue;

                wouldSell++;
                if (!line.TraderWillTrade) { traderRefused++; continue; }

                float each = line.GetPriceFor(TradeAction.PlayerSells);
                if (each <= 0f) continue;

                int give = (int)(needed / each) + 1;
                if (give > (int)have.outstanding) give = (int)have.outstanding;
                if (give > mine) give = mine;
                if (give <= 0) continue;

                if (!Offer(line, give)) { apiRefused++; continue; }

                needed -= (int)(each * give);
                have.outstanding -= give;   // surplus is always counted in items
                sold.Add(give + " " + line.Label);
            }

            why = sold.Count > 0 ? ""
                : wouldSell == 0
                    ? "the colony has nothing spare it is willing to part with"
                    : traderRefused == wouldSell
                        ? "this trader will not take what the colony has spare"
                        : apiRefused > 0
                            ? apiRefused + " sale line(s) the game would not accept — a fault here"
                            : "spare goods offered but they raise nothing worth having";

            return needed > 0 ? needed : 0;
        }

        /// <summary>What the sell side of the deal is currently worth.</summary>
        static int Proceeds(List<Tradeable> lines)
        {
            float total = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null || line.ActionToDo != TradeAction.PlayerSells) continue;
                int n = line.CountToTransfer;
                if (n < 0) n = -n;
                total += line.GetPriceFor(TradeAction.PlayerSells) * n;
            }
            return (int)total;
        }

        /// <summary>The mirror of AskFor: put a line in as a sale, and confirm the game agrees.</summary>
        static bool Offer(Tradeable line, int count)
        {
            try
            {
                line.ForceToSource(count);
                if (line.ActionToDo == TradeAction.PlayerSells) return true;

                line.ForceToDestination(count);
                if (line.ActionToDo == TradeAction.PlayerSells) return true;

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
        /// <summary>
        /// What the colony was asking for, for the refusal that says nobody stocked it.
        ///
        /// "This trader stocks none of what the colony is short of" is true and unusable. Run 196
        /// spent nine days deadlocked on wood, watched two traders arrive and leave, and this
        /// line is what the record holds about both — so whether wood was ever on the list, or
        /// whether the colony walked past its one way out of the deadlock without asking, is a
        /// question the chronicle cannot answer either way.
        ///
        /// The same fix this file's four-way split already made once: a refusal has to name the
        /// thing refused, or it sends the reader to look in the wrong place.
        /// </summary>
        static string Naming(List<Want> wants)
        {
            if (wants == null || wants.Count == 0) return "and it wanted nothing";

            var sb = new System.Text.StringBuilder("wanted ");
            for (int i = 0; i < wants.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(wants[i].label);
            }
            return sb.ToString();
        }

        static string WhyNothing(int offered, int unaffordable, int refused,
                                 float dearest, int silver, List<Want> wants)
        {
            if (offered == 0)
                return "this trader stocks none of what the colony is short of (" +
                       Naming(wants) + ")";

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
