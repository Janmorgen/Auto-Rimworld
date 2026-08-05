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
            return string.Format("{0} items deteriorating outdoors, worth {1:N0}",
                ctx.state.itemsOutdoors, ctx.state.valueOutdoors);
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
        /// Somewhere to research, because there is no warm garment a colony can make without it.
        ///
        /// This was left out on the argument that clothing does not always need research —
        /// tribalwear is ungated, so surely a bare colony can dress itself. The defs say
        /// otherwise, and the gate is one hop from where it looks. Apparel_Parka carries no
        /// research prerequisite at all, and neither does Apparel_TribalA; what needs
        /// ComplexClothing is the *tailoring bench*, both of them, the hand bench included. So
        /// the garment is ungated and the only place to make it is not, and a colony without
        /// that research can produce a war mask at a crafting spot and nothing that keeps
        /// anybody warm.
        ///
        /// Run 104 proved it at -11C: seventy-one cloth in store, two of three colonists
        /// dressed for neither, and "Clothe the colony" sitting at the top of the plan for days
        /// with nothing the colony could do about it. Material was never the problem.
        ///
        /// Stating the dependency lets the planner walk back to the room, the same way wanting
        /// a freezer resolves into wanting power. Where a bench already stands
        /// ResearchCapacityGoal is satisfied and this costs nothing.
        /// </summary>
        public override string[] Requires { get { return NeedsBench; } }
        static readonly string[] NeedsBench = { ResearchCapacityGoal.Id };

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
                   " degrees past bearing, " + ctx.state.textiles + " cloth to sew with";
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

        /// <summary>
        /// A bench, and somebody who can use it.
        ///
        /// Satisfied on the bench alone at first, which is the mistake this codebase keeps
        /// making in new clothes: a kitchen with no stove cannot cook, an unpowered turret is a
        /// wall decoration, and a research bench in a colony where every colonist has
        /// Intellectual work disabled is a table. Each time, the object existed and the
        /// capability did not.
        ///
        /// Treated as satisfied when nobody can research, rather than left outstanding forever.
        /// The colony cannot fix it by building — a researcher arrives with a new colonist or
        /// not at all — and an unsatisfiable goal would sit in the plan blocking the rest.
        /// </summary>
        public override bool Satisfied(DirectorContext ctx)
        {
            if (!ctx.state.canResearch) return true;
            return ctx.state.hasResearchBench;
        }

        public override float Urgency(DirectorContext ctx)
        {
            if (ctx.state.hasResearchBench || !ctx.state.canResearch) return 0f;

            // Deliberately below a short larder or a colony with no beds, and above anything
            // discretionary. It is never the most pressing thing in the colony and it is always
            // in the way of everything that compounds.
            return 0.45f;
        }

        public override string Explain(DirectorContext ctx)
        {
            if (!ctx.state.canResearch)
                return "nobody here can do intellectual work, so a bench would be furniture";

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

    /// <summary>
    /// Wood that grows back, on the maps where it does not.
    ///
    /// Everything this colony burns runs on wood — the stove it cooks at, the campfire it warms
    /// by, the passive cooler that is a pre-electric colony's only answer to heat — and the
    /// director had no concept of where wood comes from. It read a woodpile, chopped when the
    /// pile was low, and on a map with almost no trees that is a plan with no second step.
    ///
    /// Run 110 was that map. Eight hoppers stood dry with the woodpile at zero, the colony
    /// idle rather than busy, and every remedy the director had reached for — another cooler,
    /// another torch — made it worse. Nothing in sixteen goals was about the supply.
    ///
    /// TreeSowing is 1000 points, Neolithic, and needs no prerequisite, which puts it inside
    /// what a surviving colony reaches. It is the only renewable answer in the game before
    /// electricity.
    ///
    /// Satisfied wherever wood is not the constraint, so on an ordinary forested map this never
    /// surfaces and costs nothing. It pulls only where the map is actually poor in it.
    /// </summary>
    public class WoodSupplyGoal : ColonyGoal
    {
        public const string Id = "Wood that grows back";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.LongTerm; } }
        public override string[] RequiresResearch { get { return Research; } }
        static readonly string[] Research = { "TreeSowing" };

        /// <summary>
        /// Standing wood below which the colony is living off a finite pile.
        ///
        /// Set against what the colony's own fires would consume rather than against a flat
        /// number: a fuelled stove holds fifty, so a few hundred units of standing timber is the
        /// difference between a forest and a handful of trees.
        /// </summary>
        const int ComfortableStandingWood = 400;

        public override bool Satisfied(DirectorContext ctx)
        {
            var s = ctx.state;

            // Nothing burns anything: this colony has no wood problem to solve.
            if (s.burners <= 0) return true;

            // Plenty standing, or already growing more.
            return s.fuelOnHand >= ComfortableStandingWood || s.growingWood;
        }

        public override float Urgency(DirectorContext ctx)
        {
            var s = ctx.state;
            if (s.burners <= 0) return 0f;

            // Sharpest when the fires are already going out. A colony with a dry stove and no
            // tree to cut is not short of hands, and no work priority answers it.
            float scarcity = 1f - AcMath.Clamp01(s.fuelOnHand / (float)ComfortableStandingWood);
            float goingOut = s.buildingsWantingFuel > 0 ? 1f : 0.5f;
            return AcMath.Clamp01(scarcity * goingOut);
        }

        public override string Explain(DirectorContext ctx)
        {
            var s = ctx.state;
            // "The only wood that grows back" is a claim about the world, and until now it was
            // only ever true of the gather circle. Run 137 felled all seventeen trees within 55
            // cells by day 10 and then argued for a thousand research points on that basis,
            // standing in a forest. Say which of the two situations this actually is, because
            // they want opposite answers: one is research, the other is a longer walk.
            string beyond = s.fuelStanding <= 0 && s.fuelBeyondReach > 0
                ? string.Format(
                    " — but {0} wood stands {1} cells out, past the {2} the gatherer works in, " +
                    "so this is a reach problem before it is a research one",
                    s.fuelBeyondReach, s.nearestFuelDistance, Modules.ResourceModule.GatherRadius)
                : " — tree sowing is 1000 points and the only wood that grows back before " +
                  "electricity";

            return string.Format(
                "{0} wood standing or stacked for {1} things that burn it{2}{3}",
                s.fuelOnHand, s.burners,
                s.buildingsWantingFuel > 0 ? ", " + s.buildingsWantingFuel + " already dry" : "",
                beyond);
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
    /// <summary>
    /// Food that keeps — the answer a colony without electricity actually has.
    ///
    /// Measured on run 55: an unprovisioned colony on a hot map lost 3.6 days of food in two
    /// in-game hours, hunted successfully all week, and finished with Food security 0.35 having
    /// spent 67% of the epoch answering a food emergency it could never get ahead of. Nothing in
    /// that circle breaks on its own — food rots, so days-of-food never rises, so the colony
    /// firefights, so it builds no rooms, so it gets no research bench, so it never researches
    /// the thing that would stop the food rotting.
    ///
    /// <see cref="RefrigerationGoal"/> is the other half of this and cannot answer it: it is
    /// LongTerm, needs a power grid, and needs AirConditioning. Pemmican is 500 points,
    /// Neolithic, with no prerequisites at all — among the cheapest projects in the game — and
    /// it keeps for **70 days** against a simple meal's 1.4. It is made at a campfire or butcher
    /// table, both of which the planner already builds, so nothing new has to be constructed.
    ///
    /// Nothing else was needed to make the colony cook it. ProductionModule keeps a bill for any
    /// recipe whose product has a stock target, and pemmican is preferability MealSimple, so it
    /// is already treated as a meal the moment the recipe becomes available. The gap was only
    /// ever that nobody asked for the research.
    /// </summary>
    public class PreservedFoodGoal : ColonyGoal
    {
        public const string Id = "Food that keeps";
        public override string Name { get { return Id; } }

        /// <summary>
        /// ShortTerm, deliberately. It is not an emergency — a colony with a rotting larder and
        /// no food today is answered by FeedColonyGoal, which is Immediate and stays above this.
        /// But it is not a luxury either: it is the thing that stops next week looking like this
        /// week, and left LongTerm it sits behind every room and never happens.
        /// </summary>
        public override GoalHorizon Horizon { get { return GoalHorizon.ShortTerm; } }

        public override string[] RequiresResearch { get { return Research; } }
        static readonly string[] Research = { "Pemmican" };

        /// <summary>
        /// Somewhere to research, because this goal is *only* research — it wants no room and no
        /// building of its own, so as a focus with no bench on the map it would leave the colony
        /// with nothing to do about it and research that cannot progress. Stating the dependency
        /// lets the planner walk back to the room by itself, the same way wanting a freezer
        /// resolves into wanting power. "Food that keeps, via somewhere to research" is also
        /// exactly what is happening, which is what the chronicle should say.
        /// </summary>
        public override string[] Requires { get { return NeedsBench; } }
        static readonly string[] NeedsBench = { ResearchCapacityGoal.Id };

        /// <summary>
        /// Either way of keeping food counts. A colony that got refrigeration working does not
        /// also need pemmican, and one that can make pemmican does not need a freezer to stop
        /// starving — which is the whole point, since the freezer is what it cannot afford.
        /// </summary>
        public override bool Satisfied(DirectorContext ctx)
        {
            if (ctx.state.workingCoolers > 0) return true;
            return IsResearchFinished("Pemmican");
        }

        /// <summary>
        /// Scaled by heat, because rot is. Food keeps by itself below freezing, so a colony on a
        /// cold map is not being told to spend 500 research points solving a problem it does not
        /// have — and at low urgency this cannot pull the research room forward either.
        /// </summary>
        public override float Urgency(DirectorContext ctx)
        {
            // Measured spoilage first, temperature only as a stand-in for it.
            //
            // Temperature was always a proxy for "is the food going to survive", and the colony
            // can now answer the real question: TicksUntilRotAtCurrentTemp, summed over the
            // larder. That accounts for a freezer without this needing to know what a cooler
            // is, and for the case temperature gets wrong — a warm colony whose food is all
            // pemmican is not in trouble, and a cool one whose meals are three days old is.
            float rot;
            if (ctx.state.daysOfFood > 0.1f)
            {
                rot = AcMath.Clamp01(ctx.state.daysOfFoodSpoiling / ctx.state.daysOfFood);
            }
            else
            {
                float temp = ctx.map.mapTemperature != null ? ctx.map.mapTemperature.OutdoorTemp : 15f;
                rot = AcMath.Clamp01((temp - 0f) / 30f);
            }

            // Worth more when there is actually a larder to lose. A colony with nothing in store
            // has a hunger problem, not a preservation problem, and FeedColonyGoal owns that.
            float stock = AcMath.Clamp01(ctx.state.daysOfFood / 4f);

            return AcMath.Clamp01(rot * (0.35f + 0.65f * stock));
        }

        public override string Explain(DirectorContext ctx)
        {
            return string.Format("{0:0}C, {1:0.0} days in store of which {2:0.0} is spoiling — " +
                                 "a simple meal keeps 1.4 days, pemmican keeps 70",
                ctx.map.mapTemperature != null ? ctx.map.mapTemperature.OutdoorTemp : 0f,
                ctx.state.daysOfFood, ctx.state.daysOfFoodSpoiling);
        }
    }

    /// <summary>
    /// More hands, when the colony can carry them.
    ///
    /// The ultra-long layer that was missing entirely: fourteen goals and none about growing
    /// the colony, while every run this project has driven peaked at three or four colonists
    /// and died on losing two. Labour is the binding constraint in almost every postmortem, and
    /// the only lasting answer to a labour shortage is people.
    ///
    /// Deliberately wants nothing directly buildable. Its force runs through the levers that
    /// already exist — the incident module accepting joiners, the prisoner module preferring
    /// recruitment — which read <c>PopulationWanted</c> off the plan. Growing past what the
    /// colony can feed is how colonies die, so it is satisfied unless there is a spare bed AND
    /// a food margin: the colony votes with its own larder on whether it can carry another
    /// mouth.
    /// </summary>
    public class GrowColonyGoal : ColonyGoal
    {
        public const string Id = "Grow the colony";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.LongTerm; } }

        public override bool Satisfied(DirectorContext ctx)
        {
            // Room for one more, literally: a built bed nobody sleeps in, and food enough that
            // another mouth is margin rather than risk.
            bool spareBed = ctx.state.colonistBeds > ctx.state.colonists;
            bool fedWithMargin = ctx.state.daysOfFood >= ctx.Gene(Genes.GrowthFoodMargin);
            return !(spareBed && fedWithMargin);
        }

        public override float Urgency(DirectorContext ctx)
        {
            // Scales with how comfortably another colonist would fit. Never pressing — this is
            // the layer that pulls when nothing nearer is pulling harder.
            float slack = AcMath.Clamp01((ctx.state.daysOfFood - ctx.Gene(Genes.GrowthFoodMargin)) / 12f);
            return 0.2f + slack * 0.4f;
        }

        public override string Explain(DirectorContext ctx)
        {
            return ctx.state.colonists + " colonists, " + ctx.state.colonistBeds + " beds, " +
                   ctx.state.daysOfFood.ToString("0.0") + "d food — room for more hands";
        }
    }

    /// <summary>
    /// Something for the evenings — psychite tea, the labour-free mood lever.
    ///
    /// The mood note's own conclusion: the colonies dying of mood need answers that cost no
    /// hands, because hands are what they are out of. Tea is that lever. PsychoidBrewing is 500
    /// points, Neolithic, no prerequisites; the recipe runs at a campfire or stove the colony
    /// already has — not the drug lab it looks like it needs — and the leaves come off a plot
    /// the social crop already plants once this research lands. Joy 0.40 at addictiveness 0.02,
    /// the safest ratio in the game.
    ///
    /// Consumption is gated where the game gates it: the drug policy allows tea for joy only
    /// below a mood floor, so a content colony never touches it and a miserable one has
    /// something in the cupboard. The AlcoholWithdrawal postmortem is the reason for the gate.
    /// </summary>
    public class ComfortGoal : ColonyGoal
    {
        public const string Id = "Comfort";
        public override string Name { get { return Id; } }
        public override GoalHorizon Horizon { get { return GoalHorizon.LongTerm; } }

        public override string[] Requires { get { return NeedsBench; } }
        static readonly string[] NeedsBench = { ResearchCapacityGoal.Id };

        public override string[] RequiresResearch { get { return Research; } }
        static readonly string[] Research = { "PsychoidBrewing" };

        public override bool Satisfied(DirectorContext ctx)
        {
            return IsResearchFinished("PsychoidBrewing");
        }

        public override float Urgency(DirectorContext ctx)
        {
            // Keyed on the worst colonist, because breaks are individual — and only when fed,
            // because a hungry colony has sharper problems than joyless evenings.
            float misery = AcMath.Clamp01((0.45f - ctx.state.minMood) * 2f);
            float fed = AcMath.Clamp01(ctx.state.daysOfFood / 5f);
            return misery * fed * 0.8f;
        }

        public override string Explain(DirectorContext ctx)
        {
            return "worst mood " + ctx.state.minMood.ToString("0.00") +
                   " — tea is joy 0.40 at addictiveness 0.02, brewed at a campfire";
        }
    }

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
