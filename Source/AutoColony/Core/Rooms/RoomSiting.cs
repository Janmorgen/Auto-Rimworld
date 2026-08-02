namespace AutoColony.Rooms
{
    /// <summary>What is true of a place a room might go.</summary>
    public struct SiteFeatures
    {
        /// <summary>Cells from the base origin.</summary>
        public float fromOrigin;

        /// <summary>Cells to the nearest room already placed.</summary>
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
    }

    /// <summary>How much each of those matters to a given role. Every field is a gene.</summary>
    public struct SiteWeights
    {
        public float compactness;
        public float evenness;
        public float partnerAffinity;
        public float resourceAffinity;
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
            if (f.buildable < 0.8f) return float.NegativeInfinity;

            float score = 0f;

            // Close to the rest of the colony: shorter walks, shared walls, less to defend.
            score -= Cost(f.fromOrigin, 40f) * w.compactness;

            // Evenly placed among what already exists. This is what a store room wants and
            // almost nothing else does.
            score -= f.unevenness * w.evenness;

            // Beside the room it works with. A workshop next to the store it draws from saves
            // every haul; a freezer next to the kitchen saves every meal.
            score -= Cost(f.toPartnerRoom, 30f) * w.partnerAffinity;

            // Beside what it is for. A rock store belongs where the rock is.
            score -= Cost(f.toResource, 40f) * w.resourceAffinity;

            return score;
        }

        /// <summary>
        /// Distance as a cost in 0..1, flattening beyond the point where further distance stops
        /// making a practical difference.
        /// </summary>
        public static float Cost(float distance, float far)
        {
            if (distance <= 0f) return 0f;
            if (far <= 0f) return 1f;
            return distance >= far ? 1f : distance / far;
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

        public static readonly string[] Aspects = { Compactness, Evenness, Partner, Resource };

        /// <summary>Gene names for a role's dimensions, which differ hugely by purpose.</summary>
        public static string WidthKey(string role) { return "site." + role + ".width"; }
        public static string HeightKey(string role) { return "site." + role + ".height"; }
    }
}
