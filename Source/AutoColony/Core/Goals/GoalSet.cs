using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Goals
{
    // ------------------------------------------------------------------ immediate

    /// <summary>Something is burning close enough to matter.</summary>
    public class ExtinguishFireGoal : ColonyGoal
    {
        public const string Id = "Extinguish fire";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.Immediate; } }
        public override bool Satisfied(DirectorContext ctx) { return ctx.state.firesNearBase == 0; }
        public override float Urgency(DirectorContext ctx) { return 1f; }
        public override string Explain(DirectorContext ctx)
        {
            return ctx.state.firesNearBase + " fires at the colony";
        }
    }

    /// <summary>Hostiles have reached the colony.</summary>
    public class RepelRaidGoal : ColonyGoal
    {
        public const string Id = "Repel raid";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.Immediate; } }
        public override bool Satisfied(DirectorContext ctx) { return ctx.state.hostilesNearBase == 0; }
        public override float Urgency(DirectorContext ctx) { return 1f; }
        public override string Explain(DirectorContext ctx)
        {
            return ctx.state.hostilesNearBase + " hostiles at the colony";
        }
    }

    /// <summary>The larder is empty enough that people will start starving.</summary>
    public class FeedColonyGoal : ColonyGoal
    {
        public const string Id = "Feed the colony";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.Immediate; } }

        /// <summary>
        /// A kitchen, because hunting on its own does not feed anyone. Game comes back as
        /// corpses, and a corpse is not food until something butchers it — observed in-game
        /// with a colony that killed thirteen gazelles and still starved at 0.0 days.
        /// </summary>
        public override RoomRole? WantsRoom { get { return RoomRole.Kitchen; } }

        public override bool Satisfied(DirectorContext ctx)
        {
            return ctx.state.daysOfFood >= 2f;
        }

        public override float Urgency(DirectorContext ctx)
        {
            return 1f - AcMath.Clamp01(ctx.state.daysOfFood / 2f);
        }

        public override string Explain(DirectorContext ctx)
        {
            return ctx.state.daysOfFood.ToString("0.0") + " days of food";
        }

    }

    // ------------------------------------------------------------------ short term

    /// <summary>Everyone has somewhere to sleep.</summary>
    public class ShelterGoal : ColonyGoal
    {
        public const string Id = "Shelter everyone";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.ShortTerm; } }
        public override RoomRole? WantsRoom { get { return RoomRole.Bedroom; } }

        public override bool Satisfied(DirectorContext ctx)
        {
            return ctx.state.colonistBeds >= ctx.state.colonists;
        }

        public override float Urgency(DirectorContext ctx)
        {
            if (ctx.state.colonists == 0) return 0f;
            return 1f - AcMath.Clamp01(ctx.state.colonistBeds / (float)ctx.state.colonists);
        }

        public override string Explain(DirectorContext ctx)
        {
            return ctx.state.colonistBeds + " beds for " + ctx.state.colonists + " colonists";
        }

    }

    /// <summary>Somewhere covered to put things, so they stop rotting in the rain.</summary>
    public class StorageGoal : ColonyGoal
    {
        public const string Id = "Roofed storage";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.ShortTerm; } }
        public override RoomRole? WantsRoom { get { return RoomRole.Storage; } }

        public override bool Satisfied(DirectorContext ctx)
        {
            return ctx.layout != null && ctx.layout.HasRoom(RoomRole.Storage);
        }

        public override float Urgency(DirectorContext ctx)
        {
            // Items rotting under open sky are what makes this urgent rather than tidy.
            return 0.4f + AcMath.Clamp01(ctx.state.itemsOutdoors / 60f) * 0.6f;
        }

        public override string Explain(DirectorContext ctx)
        {
            return ctx.state.itemsOutdoors + " items outdoors";
        }

    }

    /// <summary>
    /// Land under cultivation, enough of it, and not all of it the same crop.
    ///
    /// Food arrives one of two ways and they are not equivalent. Hunting is faster and answers
    /// an empty larder today, but it spends the colonists themselves to do it: nearly every
    /// combat death across this project's test runs began with a colony reaching for meat
    /// because nothing was planted, and the last-resort rule exists precisely because that
    /// reach sometimes has to be made against an animal nobody should fight. A field carries no
    /// such risk. It is slower, which is exactly why it belongs to the short term rather than
    /// the immediate one — the time to plant is while there is still food in the store.
    ///
    /// Variety is part of the goal rather than a refinement of it. Blight takes an entire crop
    /// at once, so a colony living off one large field is a single event from having nothing,
    /// and staggered growing times spread the harvest instead of banking it all on one week.
    /// </summary>
    public class FarmGoal : ColonyGoal
    {
        public const string Id = "Plant fields";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.ShortTerm; } }

        /// <summary>Distinct crops worth having before variety stops mattering.</summary>
        public const int WantedCrops = 2;

        static int WantedCells(DirectorContext ctx)
        {
            return (int)(ctx.state.colonists * ctx.Gene(Genes.GrowingCellsPerColonist));
        }

        public override bool Satisfied(DirectorContext ctx)
        {
            int wanted = WantedCells(ctx);
            if (wanted <= 0) return true;
            return ctx.state.growingCells >= wanted &&
                   ctx.state.distinctCrops >= WantedCrops;
        }

        public override float Urgency(DirectorContext ctx)
        {
            int wanted = WantedCells(ctx);
            if (wanted <= 0) return 0f;

            // Mostly about how much land is missing, but a colony that is also short of food
            // wants the field sooner — that is the situation where hunting would otherwise be
            // the only answer available.
            float landShortfall = 1f - AcMath.Clamp01(ctx.state.growingCells / (float)wanted);
            float hunger = 1f - AcMath.Clamp01(
                ctx.state.daysOfFood / AcMath.Clamp(ctx.Gene(Genes.FoodDaysPerColonist), 1f, 30f));

            return AcMath.Clamp01(landShortfall * 0.7f + hunger * 0.3f);
        }

        public override string Explain(DirectorContext ctx)
        {
            return ctx.state.growingCells + " of " + WantedCells(ctx) + " growing cells, " +
                   ctx.state.distinctCrops + " of " + WantedCrops + " crops";
        }
    }

    /// <summary>A comfortable buffer of food rather than hand to mouth.</summary>
    public class FoodStockGoal : ColonyGoal
    {
        public const string Id = "Stock food";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.ShortTerm; } }
        public override RoomRole? WantsRoom { get { return RoomRole.Kitchen; } }

        /// <summary>
        /// Fields first. A buffer built by hunting is a buffer bought with the colonists' own
        /// safety, and it has to be re-bought every time it runs down; a field keeps paying.
        /// The prerequisite walk therefore turns "we want more food in store" into "plant
        /// something" rather than into another hunt.
        /// </summary>
        static readonly string[] NeedsFields = { FarmGoal.Id };
        public override string[] Requires { get { return NeedsFields; } }

        public override bool Satisfied(DirectorContext ctx)
        {
            return ctx.state.daysOfFood >= ctx.Gene(Genes.FoodDaysPerColonist);
        }

        public override float Urgency(DirectorContext ctx)
        {
            float target = ctx.Gene(Genes.FoodDaysPerColonist);
            if (target <= 0f) return 0f;
            return 1f - AcMath.Clamp01(ctx.state.daysOfFood / target);
        }

        public override string Explain(DirectorContext ctx)
        {
            return ctx.state.daysOfFood.ToString("0.0") + " of " +
                   ctx.Gene(Genes.FoodDaysPerColonist).ToString("0") + " days";
        }

    }

    // ------------------------------------------------------------------ long term

    /// <summary>
    /// A workshop, which is what turns rock into blocks. Without it a colony in a dry biome
    /// cannot act on any preference for stone however strongly it holds it.
    /// </summary>
    /// <summary>
    /// Clothes against the weather the colony is actually in.
    ///
    /// Clothing is the cheapest answer to temperature and the only one that travels with the
    /// colonist. A heater warms a room nobody can stay in all day, there is no portable cooler
    /// at all, and past about ten degrees outside what a colonist can bear the cost stops being
    /// mood and becomes hypothermia or heatstroke — both fatal at full severity.
    ///
    /// The good garments — parkas above all, which are far and away the most insulating thing in
    /// the game — sit behind Complex Clothing, so wanting them has to reach back into research
    /// the same way wanting a freezer reaches back into electricity. That is the whole reason
    /// this is a goal rather than a rule inside the production module: the module can only make
    /// what the colony has already unlocked.
    /// </summary>
    public class WeatherClothingGoal : ColonyGoal
    {
        public const string Id = "Clothe the colony";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.ShortTerm; } }
        public override RoomRole? WantsRoom { get { return RoomRole.Workshop; } }

        public override string[] RequiresResearch { get { return Research; } }
        static readonly string[] Research = { "ComplexClothing" };

        /// <summary>
        /// Whether everyone is dressed for the weather they are actually in.
        ///
        /// Asked of the colonists rather than of the buildings. The first version of this asked
        /// whether a workshop existed, which is satisfied by every colony that has one — so the
        /// goal could never fire in exactly the situation it was written for, and a probe at
        /// minus twenty degrees calmly reported that the colony should work on power. A bench is
        /// a means; being warm is the end, and only the end belongs in a satisfaction test.
        /// </summary>
        public override bool Satisfied(DirectorContext ctx)
        {
            return ctx.state.colonistsUnderdressed == 0;
        }

        public override float Urgency(DirectorContext ctx)
        {
            if (ctx.state.colonistsUnderdressed == 0) return 0f;

            // Ten degrees past what a colonist can bear is where hypothermia and heatstroke
            // begin, so that is full urgency rather than an arbitrary ceiling. Scaled by how
            // much of the colony is exposed, so one unlucky pawn does not outrank the larder.
            float depth = AcMath.Clamp01(ctx.state.worstClothingGap / 10f);
            float spread = ctx.state.colonists > 0
                ? ctx.state.colonistsUnderdressed / (float)ctx.state.colonists
                : 1f;
            return AcMath.Clamp01(depth * 0.7f + spread * 0.3f);
        }

        public override string Explain(DirectorContext ctx)
        {
            return ctx.state.colonistsUnderdressed + " of " + ctx.state.colonists +
                   " dressed for neither, " + ctx.state.outdoorTemperature.ToString("0") +
                   "C outdoors, worst " + ctx.state.worstClothingGap.ToString("0") +
                   " degrees past bearing";
        }
    }

    /// <summary>
    /// Somewhere to actually do the research.
    ///
    /// Every research-gated goal in this file walks its prerequisites back through
    /// <see cref="ResearchChain"/> until it finds something the colony could start studying —
    /// and then nothing, anywhere, asks for the bench the studying has to happen at. The
    /// research module selects a project on day zero regardless, so a colony reports a project
    /// in progress from its first hour and finishes none of it, ever.
    ///
    /// Six colonies scored exactly 0.00 on research, across two biomes, for this reason. The
    /// whole power chain sits behind it: Electricity gates the conduits, the generator and the
    /// electric stove, and none of that can be studied at a bench nobody built. One run reached
    /// "LongTerm: Power (towards Refrigeration)" on day 8 and could not have advanced a single
    /// project from there.
    ///
    /// <c>hasResearchBench</c> was already measured. Its only reader weighted the research work
    /// priority by it — how hard to work at a bench, never whether to have one.
    ///
    /// A bench is the end here rather than a means, which is what separates this from the
    /// clothing goal's workshop: being warm is the end there and a bench is incidental to it,
    /// whereas nothing else in the colony makes research possible at all.
    /// </summary>
    public class ResearchCapacityGoal : ColonyGoal
    {
        public const string Id = "Somewhere to research";
        public override string Name { get { return Id; } }

        /// <summary>
        /// Long term, and reached by being a prerequisite rather than by pre-empting.
        ///
        /// Written as ShortTerm first, which was wrong in a way the power self-test caught
        /// immediately: a nearer horizon pre-empts a further one outright, and no probe fixture
        /// has a bench, so *every* long-term probe in the file came back
        /// <c>focus=Somewhere to research</c> — power, refrigeration, crop diversity, the lot.
        /// That is the same fault the handoff records against the last batch of short-term
        /// goals, and it hides itself: the probes all still pass, they simply stop testing
        /// anything. A bench is a compounding investment, not a this-season necessity, so it
        /// belongs beside power and refrigeration and reaches the plan the way they do.
        /// </summary>
        public override GoalHorizon Horizon { get { return GoalHorizon.LongTerm; } }
        public override RoomRole? WantsRoom { get { return RoomRole.Research; } }

        public override bool Satisfied(DirectorContext ctx)
        {
            return ctx.state.hasResearchBench;
        }

        public override float Urgency(DirectorContext ctx)
        {
            if (ctx.state.hasResearchBench) return 0f;

            // Deliberately below a short larder or a colony with no beds, and above anything
            // discretionary. It is never the most pressing thing in the colony and it is always
            // in the way of everything that compounds.
            return 0.45f;
        }

        public override string Explain(DirectorContext ctx)
        {
            return "no research bench, so nothing the colony studies can ever finish";
        }
    }

    public class MasonryGoal : ColonyGoal
    {
        public const string Id = "Masonry";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.LongTerm; } }
        public override RoomRole? WantsRoom { get { return RoomRole.Workshop; } }
        public override string[] RequiresResearch { get { return Research; } }
        static readonly string[] Research = { "Stonecutting" };

        public override bool Satisfied(DirectorContext ctx)
        {
            return ctx.layout != null && ctx.layout.HasRoom(RoomRole.Workshop);
        }

        public override float Urgency(DirectorContext ctx)
        {
            // Only pressing where the environment actually punishes wooden walls.
            return FireRisk.Assess(ctx.map, ctx.state);
        }

        public override string Explain(DirectorContext ctx)
        {
            return "fire risk " + FireRisk.Assess(ctx.map, ctx.state).ToString("0.00") +
                   ", no stonecutting yet";
        }
    }

    /// <summary>Electricity, which everything mechanical downstream depends on.</summary>
    public class PowerGoal : ColonyGoal
    {
        public const string Id = "Power";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.LongTerm; } }
        public override RoomRole? WantsRoom { get { return RoomRole.Power; } }
        public override string[] RequiresResearch { get { return Research; } }
        static readonly string[] Research = { "Electricity" };

        /// <summary>
        /// Electricity is a hard gate on every building this goal wants, and it cannot be
        /// studied without somewhere to study. Naming the bench as a prerequisite is what makes
        /// wanting power resolve into wanting a bench, exactly as wanting a freezer resolves
        /// into wanting power — rather than the plan sitting on "Power (towards Refrigeration)"
        /// while the one project it needs cannot advance by a single point. Refrigeration and
        /// fortification inherit this through their own dependency on power.
        /// </summary>
        public override string[] Requires { get { return NeedsBench; } }
        static readonly string[] NeedsBench = { ResearchCapacityGoal.Id };

        /// <summary>
        /// A generator that is actually producing, not a room with the word Power on it.
        ///
        /// This previously read <c>layout.HasRoom(RoomRole.Power)</c>, which was true the instant
        /// the room was *reserved* — before a wall existed, let alone a generator. Power stopped
        /// being the focus immediately, and refrigeration and fortification both unblocked against
        /// a colony with no electricity at all.
        /// </summary>
        public override bool Satisfied(DirectorContext ctx)
        {
            return ctx.state.workingGenerators > 0;
        }

        public override float Urgency(DirectorContext ctx) { return 0.5f; }

        public override void DeclareNeeds(DirectorContext ctx, MaterialNeeds needs)
        {
            // A generator and a battery, with enough left over not to strip the stockpile.
            needs.Need("Steel", 220);
            needs.Need("ComponentIndustrial", 6);
            // The generator is wood-fired, so it is not built and finished with — it burns fuel
            // for as long as the colony wants power.
            needs.Need("WoodLog", 300);
        }

        public override string Explain(DirectorContext ctx)
        {
            int idle = ctx.state.generators - ctx.state.workingGenerators;
            return ctx.state.workingGenerators + " generators running" +
                   (idle > 0 ? " (" + idle + " built but producing nothing)" : "") +
                   ", steel " + ctx.state.steel + "/220, components " + ctx.state.components + "/6";
        }
    }

    /// <summary>
    /// Refrigeration. The canonical example of a goal that is useless to want directly: it
    /// needs a cooler, which needs power, which needs components and steel, which needs mining
    /// — and none of that can happen while the colony is on fire.
    /// </summary>
    public class RefrigerationGoal : ColonyGoal
    {
        public const string Id = "Refrigeration";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.LongTerm; } }
        public override RoomRole? WantsRoom { get { return RoomRole.Freezer; } }
        public override string[] Requires { get { return NeedsPower; } }
        static readonly string[] NeedsPower = { PowerGoal.Id };
        public override string[] RequiresResearch { get { return Research; } }
        static readonly string[] Research = { "AirConditioning" };

        /// <summary>
        /// A cooler with power. A freezer room whose cooler is unpowered is a warm room, and
        /// the food in it spoils exactly as fast as it did outside.
        /// </summary>
        public override bool Satisfied(DirectorContext ctx)
        {
            return ctx.state.workingCoolers > 0;
        }

        public override float Urgency(DirectorContext ctx)
        {
            // Worth more the more food there is to spoil, and in a hot climate.
            float heat = 0.3f;
            if (ctx.map.mapTemperature != null)
                heat = AcMath.Clamp01((ctx.map.mapTemperature.OutdoorTemp - 5f) / 30f);
            return AcMath.Clamp01(0.3f + heat * 0.7f);
        }

        public override void DeclareNeeds(DirectorContext ctx, MaterialNeeds needs)
        {
            needs.Need("Steel", 280);
            needs.Need("ComponentIndustrial", 9);
        }

        public override string Explain(DirectorContext ctx)
        {
            return "outdoor temp " +
                   (ctx.map.mapTemperature != null
                       ? ctx.map.mapTemperature.OutdoorTemp.ToString("0")
                       : "?") + "C";
        }

    }

    /// <summary>Static defences, so the next raid is met by more than bodies.</summary>
    public class FortifyGoal : ColonyGoal
    {
        public const string Id = "Fortify";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.LongTerm; } }
        public override string[] Requires { get { return NeedsPower; } }
        static readonly string[] NeedsPower = { PowerGoal.Id };
        public override string[] RequiresResearch { get { return Research; } }
        static readonly string[] Research = { "GunTurrets" };

        public override bool Satisfied(DirectorContext ctx)
        {
            // Only turrets that can actually fire count towards being fortified.
            return ctx.state.poweredTurrets >= ctx.GeneInt(Genes.DefenseTurretCount);
        }

        public override float Urgency(DirectorContext ctx)
        {
            // Against the raid the colony's own size is summoning, rather than off raw wealth
            // on a straight line. Wealth drives raid points, so defence is a race and not a
            // threshold: the question is whether the colony is keeping up with what it is
            // building, and a bare wealth ramp cannot answer that at all.
            float strength = CombatAssessment.ColonyStrength(ctx.state);
            float expected = ThreatForecast.ExpectedRaidPoints(
                ctx.state.wealthTotal, ctx.state.colonists);

            return 1f - ThreatForecast.Readiness(strength, expected);
        }

        public override void DeclareNeeds(DirectorContext ctx, MaterialNeeds needs)
        {
            needs.Need("Steel", 170);
            needs.Need("ComponentIndustrial", 4);
        }

        public override string Explain(DirectorContext ctx)
        {
            int dead = ctx.state.turrets - ctx.state.poweredTurrets;
            float strength = CombatAssessment.ColonyStrength(ctx.state);
            float expected = ThreatForecast.ExpectedRaidPoints(
                ctx.state.wealthTotal, ctx.state.colonists);

            return ctx.state.poweredTurrets + " working turrets" +
                   (dead > 0 ? " (" + dead + " unpowered)" : "") +
                   ", wealth " + ctx.state.wealthTotal.ToString("N0") +
                   " is summoning about " + expected.ToString("0") +
                   " raid points against strength " + strength.ToString("0") +
                   " (readiness " + ThreatForecast.Readiness(strength, expected).ToString("0.00") + ")";
        }

    }
}
