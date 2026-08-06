namespace AutoColony
{
    /// <summary>
    /// How many days of food the colony wants, in one place.
    ///
    /// The gene alone was the answer in nine call sites across seven files — the stock goal,
    /// the gatherer, the work priorities, the growing zones, and now the trade module. All nine
    /// asked for a flat number of days with no season in it, and the number they got was the
    /// same one whether the fields were about to produce for sixty days or stop in four.
    ///
    /// Run 159 is the whole argument. On day 23 it bought four days of food, which is what the
    /// gene asked for, four days before the growing season ended. On day 25 it was starving with
    /// six finished rooms, every bed sheltered, and Starvation on screen. Nothing was
    /// misweighted: the colony bought exactly what it had been told to want.
    ///
    /// So the target is the larger of what the genome likes and what the calendar demands. The
    /// gene still sets ordinary comfort — that is a real strategic preference and the search
    /// should keep tuning it — and the season raises it when there is a gap coming that food
    /// has to cross.
    ///
    /// One definition rather than nine, because "how much food" being answered differently in
    /// different modules is the duplicated-quantity fault this project keeps paying for: the
    /// roster that disagreed with the fieldable count, the gather circle that stood in for the
    /// world, a pen tally that reported history as state.
    ///
    /// Free of game types so the arithmetic can be tested offline.
    /// </summary>
    public static class FoodTarget
    {
        /// <summary>
        /// Days of food wanted, given the genome's preference and the barren stretch ahead.
        ///
        /// <paramref name="barrenDaysAhead"/> of zero means no gap is coming — a permanent
        /// summer map, where the fields never stop — and the genome's number stands unaltered.
        /// That is a real answer rather than a missing one, so it is not treated as unknown.
        ///
        /// The margin exists because crossing a winter on exactly the right amount assumes
        /// nothing spoils, nobody arrives, and no raid burns the larder, and all three happen.
        /// </summary>
        public static float Days(float geneDays, int growingDaysLeft, int barrenDaysAhead,
                                 float margin)
        {
            if (geneDays < 1f) geneDays = 1f;
            if (barrenDaysAhead <= 0) return geneDays;

            // What the gap itself costs, plus the slack that keeps a bad month from emptying it.
            float toCross = barrenDaysAhead * (margin < 1f ? 1f : margin);

            // Only wanted once the season is close enough to matter. A colony forty days from
            // winter should not be hoarding a winter's food while it still has fields to sow —
            // that is the same over-reaction as the flat target, in the other direction.
            if (growingDaysLeft > barrenDaysAhead) return geneDays;

            return toCross > geneDays ? toCross : geneDays;
        }
    }
}
