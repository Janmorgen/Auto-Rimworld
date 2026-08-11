namespace AutoColony.Rooms
{
    /// <summary>What is true of a place a room might go.</summary>
    public struct SiteFeatures
    {
        /// <summary>Cells from the base origin.</summary>
        public float fromOrigin;

        /// <summary>
        /// Cells to the nearest room already placed, or negative when there is no room to be
        /// near yet.
        ///
        /// The distinction matters now that this is scored rather than merely gathered. The
        /// first room of a colony has nothing to be far from, and a large number standing in for
        /// "nothing there" would make every first room look like an outpost and push it back
        /// towards an origin it is already sitting on.
        /// </summary>
        public float toNearestRoom;

        /// <summary>
        /// How unevenly this spot sits among the existing rooms: 0 when it is equidistant from
        /// all of them, rising as it favours one side. A store everything is hauled to wants
        /// this low; a bedroom does not care.
        /// </summary>
        public float unevenness;

        /// <summary>Cells to the nearest room of a role this one works with.</summary>
        public float toPartnerRoom;

        /// <summary>Cells to the nearest resource this room's purpose depends on.</summary>
        public float toResource;

        /// <summary>Share of the footprint that can actually be built on, 0 to 1.</summary>
        public float buildable;

        /// <summary>
        /// Share of the footprint that must be cleared first — trees, boulders, ruins — 0 to 1.
        ///
        /// Distinct from <see cref="buildable"/>, which asks whether a blueprint can be placed
        /// at all. A tree does not stop a blueprint; it stops the wall going up until somebody
        /// has cut it, and eighty-one cells of forest is fifty-six jobs before the first wall.
        /// </summary>
        public float toClear;
    }

    /// <summary>How much each of those matters to a given role. Every field is a gene.</summary>
    public struct SiteWeights
    {
        public float compactness;
        public float evenness;
        public float partnerAffinity;
        public float resourceAffinity;

        /// <summary>How much a site's clearing work counts against it.</summary>
        public float openGround;

        /// <summary>
        /// How much a gap between this room and its nearest neighbour counts against it.
        ///
        /// Distinct from compactness, which measures the base's centre and cannot see holes.
        /// A room thirty cells from the origin beside three other rooms and a room thirty cells
        /// from the origin with nothing within twenty of it score identically on compactness,
        /// and they are not the same base.
        /// </summary>
        public float isolation;
    }

    /// <summary>
    /// Where a room should go.
    ///
    /// Siting was a fixed pattern: slots alternating north and south of the origin, fanning
    /// left and right in order, first one that fits wins. Every room therefore got the same
    /// answer to a question that differs completely between them — and nothing in it could
    /// express that a store wants to be reachable from everywhere, that a workshop wants to be
    /// beside the store it draws material from, or that a rock store wants to be near the rock.
    ///
    /// Distances are all costs, so every term is a penalty and better sites score higher by
    /// being less bad. The weights are per role because the trade-offs genuinely invert: the
    /// spot that suits a freezer beside the kitchen is the wrong spot for a prison.
    ///
    /// Free of game types so the trade-offs can be tested offline.
    /// </summary>
    public static class RoomSiting
    {
        /// <summary>
        /// How good a site is, higher being better. Unbuildable ground is refused outright
        /// rather than scored, since no weighting makes a cliff into a bedroom.
        /// </summary>
        public static float Score(SiteFeatures f, SiteWeights w)
        {
            return Score(f, w, 1f);
        }

        /// <summary>
        /// The same, told how far a colony will let itself sprawl before distance dominates.
        ///
        /// A ceiling of 1 is the old behaviour exactly, so a genome that has never heard of this
        /// sites rooms as it always did.
        /// </summary>
        public static float Score(SiteFeatures f, SiteWeights w, float sprawlCeiling)
        {
            return Score(f, w, sprawlCeiling, DefaultGapReach);
        }

        /// <summary>
        /// The full scorer, told as well how big a gap between neighbouring rooms the colony
        /// will put up with.
        ///
        /// <paramref name="gapReach"/> is in cells, but it is not chosen in cells — the caller
        /// derives it from a walking time and the colony's own measured speed, via
        /// <c>Reach.Cells</c>. Nobody has an opinion about forty cells; they have an opinion
        /// about how much of a walk is worth putting between two rooms.
        /// </summary>
        public static float Score(SiteFeatures f, SiteWeights w, float sprawlCeiling, float gapReach)
        {
            if (f.buildable < 0.8f) return float.NegativeInfinity;

            float score = 0f;

            // Close to the rest of the colony: shorter walks, shared walls, less to defend.
            //
            // The only term here that keeps rising past its knee, because it is the only one
            // paid on every trip rather than being a benefit that is simply gone once you are
            // far enough away. See the three-argument Cost.
            score -= Cost(f.fromOrigin, 40f, sprawlCeiling) * w.compactness;

            // Evenly placed among what already exists. This is what a store room wants and
            // almost nothing else does.
            score -= f.unevenness * w.evenness;

            // Beside the room it works with. A workshop next to the store it draws from saves
            // every haul; a freezer next to the kitchen saves every meal.
            score -= Cost(f.toPartnerRoom, 30f) * w.partnerAffinity;

            // Beside what it is for. A rock store belongs where the rock is.
            score -= Cost(f.toResource, 40f) * w.resourceAffinity;

            // Ground that is already open.
            //
            // buildable answers whether a blueprint can be placed; this answers how much has to
            // be cut down first, which is a different question and the one that decides when the
            // room actually exists. Run 191 sited a 9x9 store on eighty-one cells of forest and
            // spent three days before "clearing 56 obstructions" — a room the colony had named
            // as its next need, waiting on nobody having noticed the trees.
            score -= f.toClear * w.openGround;

            // Beside *something*. A room dropped where nothing else stands is an outpost,
            // whatever the other terms say about it.
            //
            // This is the term that has to exist once sites stop being a line. A corridor could
            // not produce a hole: every slot was adjacent to the one before it, so "near the
            // origin" and "near the other rooms" were the same fact and one weight covered both.
            // Scoring free ground makes them different facts, and a site can now be forty cells
            // out in a direction the base has never gone while a perfectly good one sits against
            // an existing wall.
            //
            // Capped by the same sprawl ceiling as compactness and for the same reason: a gap is
            // paid on every trip between the two rooms, so it must keep costing past its knee,
            // but it must not be free to swamp every other term on its own.
            if (f.toNearestRoom >= 0f)
                score -= Cost(f.toNearestRoom, gapReach, sprawlCeiling) * w.isolation;

            return score;
        }

        /// <summary>
        /// The gap a colony tolerates when nobody has said, in cells.
        ///
        /// Only for the overloads that predate the tolerance being derived; the planner passes a
        /// measured one. Forty matches the other knees so a caller that does not care keeps the
        /// shape the rest of the scorer has.
        /// </summary>
        public const float DefaultGapReach = 40f;

        /// <summary>
        /// Distance as a cost in 0..1, flattening beyond the point where further distance stops
        /// making a practical difference.
        ///
        /// Correct for the terms that express a *benefit* of being near something. Once a
        /// workshop is forty cells from the store it draws on, it has lost the advantage of
        /// adjacency, and being four hundred cells away is not meaningfully worse than a
        /// hundred — the benefit was gone either way.
        ///
        /// Wrong for distance from the base itself, which is paid on every trip for the life of
        /// the colony. See the overload.
        /// </summary>
        public static float Cost(float distance, float far)
        {
            if (distance <= 0f) return 0f;
            if (far <= 0f) return 1f;
            return distance >= far ? 1f : distance / far;
        }

        /// <summary>
        /// Distance as a cost that keeps rising past the knee, bounded by a ceiling.
        ///
        /// Run 189 sited a 9x9 Storage room at x=34 while the rest of the base sat between x=133
        /// and x=146 — a hundred cells out, so every haul into it is a two-hundred-cell round
        /// trip, for ever. It was allowed because the flat version returns 1.0 at forty cells and
        /// 1.0 at four hundred: past the knee the scorer is perfectly indifferent to distance,
        /// and "wants to be near rock" then decided, the rock being a hundred cells away.
        ///
        /// A sprawling base is not only untidy. It is slower to build, because a wall a hundred
        /// cells out costs the walk there and back; and it is slower to reach a casualty across.
        /// Run 189 also lost Speedy to "0.4 hours of walking against 0.2 hours left" — a doctor
        /// twenty-four minutes away from a colonist with twelve minutes to live.
        ///
        /// The ceiling exists so one term cannot swamp the rest. Uncapped, a site four hundred
        /// cells out would score ten against every other term's one, and compactness would decide
        /// every room in every colony on its own. Where to put that ceiling is a strategy — how
        /// much sprawl a colony will tolerate to sit on a resource — so it is the genome's.
        /// </summary>
        public static float Cost(float distance, float far, float ceiling)
        {
            if (distance <= 0f) return 0f;
            if (far <= 0f) return 1f;
            if (ceiling < 1f) ceiling = 1f;

            float cost = distance / far;
            return cost > ceiling ? ceiling : cost;
        }

        /// <summary>
        /// How unevenly a spot sits among a set of distances: 0 when they are all equal, 1 when
        /// one dominates. Cheap to compute and enough to separate "in the middle of the base"
        /// from "tacked on one end".
        /// </summary>
        public static float Unevenness(float[] distances, int count)
        {
            if (distances == null || count <= 1) return 0f;

            float min = float.MaxValue;
            float max = 0f;
            for (int i = 0; i < count; i++)
            {
                if (distances[i] < min) min = distances[i];
                if (distances[i] > max) max = distances[i];
            }
            if (max <= 0f) return 0f;
            return (max - min) / max;
        }

        /// <summary>Gene name for one aspect of one role's siting preference.</summary>
        public static string GeneKey(string role, string aspect)
        {
            return "site." + role + "." + aspect;
        }

        public const string Compactness = "compactness";
        public const string Evenness = "evenness";
        public const string Partner = "partner";
        public const string Resource = "resource";
        public const string OpenGround = "openGround";
        public const string Isolation = "isolation";

        public static readonly string[] Aspects =
            { Compactness, Evenness, Partner, Resource, OpenGround, Isolation };

        /// <summary>Gene names for a role's dimensions, which differ hugely by purpose.</summary>
        public static string WidthKey(string role) { return "site." + role + ".width"; }
        public static string HeightKey(string role) { return "site." + role + ".height"; }
    }
}
