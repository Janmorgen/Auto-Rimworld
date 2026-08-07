namespace AutoColony
{
    /// <summary>
    /// How far the colony will walk for a thing it is short of.
    ///
    /// The colony would range a hundred and fifteen cells for a meal and exactly fifty-five for
    /// a log, and nothing said so. Hunting widened its search under urgency; chopping and mining
    /// read a flat constant. Three gatherers, one of them with a lever.
    ///
    /// Run 196 demonstrated it rather than implying it, because all three ran in the same pass
    /// from the same origin and disagreed: **[live, run 196]**
    ///
    ///     day 6 16h  gathering: marked 0 trees, 0 rock, 1 animals within 55 cells of the base
    ///     day 6 21h  gathering: still wanting wood, food and nothing within 55 cells to mark
    ///                — this is not a shortage of hands and no work priority answers it
    ///
    /// The animal was found because the hunt had already stretched past 55. The trees were not,
    /// because chopping never does. Meanwhile four construction jobs waited on wood, two of two
    /// colonists could build, and the colony sat idle for six days on a map whose walls need wood
    /// and whose stone needs a research bench that needs wood.
    ///
    /// Worth being plain about the earlier mistake, because it is the more interesting half.
    /// `ResourceModule` had already considered this and ruled it out in a comment:
    ///
    ///     "This was left off deliberately when the message was split, because 'nothing in
    ///      range' might have been a radius that widens under pressure rather than a real
    ///      absence. Run 184 answered within a day ... That is not transient."
    ///
    /// Run 184 could not have answered it. Persistence distinguishes a transient absence from a
    /// standing one; it says nothing at all about whether the radius would have widened, because
    /// no radius ever widened. The evidence was consistent with the conclusion and equally
    /// consistent with its opposite, which is the failure this file exists to correct.
    ///
    /// Free of game types so the arithmetic can be argued with in a test.
    /// </summary>
    public static class GatherReach
    {
        /// <summary>
        /// How badly the colony wants more of something, from what it holds against what it
        /// wants. 0 when stocked, 1 when it has none.
        ///
        /// The same shape <see cref="FoodTiming"/> already gives food, applied to the other two
        /// stores so that all three gatherers stretch on the same terms. A target of zero means
        /// nothing is wanted, which is not the same as wanting it infinitely.
        /// </summary>
        public static float Shortfall(float held, float target)
        {
            if (target <= 0f) return 0f;
            if (held <= 0f) return 1f;
            if (held >= target) return 0f;
            return (target - held) / target;
        }

        /// <summary>
        /// The radius to search, given how short the colony is.
        ///
        /// Linear rather than clever: the honest claim is only that emptier means further, and
        /// any particular curve would be a guess dressed as a derivation. `stretch` is the gene,
        /// so the shape of that trade is something evolution prices rather than something chosen
        /// here — a colony that walks too far spends the day walking.
        /// </summary>
        public static int Radius(int baseRadius, float shortfall, float stretch)
        {
            if (baseRadius < 0) baseRadius = 0;
            if (shortfall <= 0f) return baseRadius;
            if (shortfall > 1f) shortfall = 1f;
            if (stretch <= 0f) return baseRadius;
            return baseRadius + (int)(shortfall * stretch);
        }
    }
}
