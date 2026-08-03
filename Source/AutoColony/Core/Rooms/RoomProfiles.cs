namespace AutoColony.Rooms
{
    /// <summary>
    /// The opening opinion about each kind of room: how big, what it wants to be near, and what
    /// its purpose depends on being close to.
    ///
    /// These are starting points for the search rather than answers. The *pairings* are facts
    /// about the game — a workshop draws material from a store, a freezer feeds a kitchen, both
    /// are true in every colony — while how much that closeness is worth against compactness or
    /// even spacing is a judgement, and that part is a gene.
    ///
    /// Kept as plain data so nothing has to be edited in two places when a role is added.
    /// </summary>
    public static class RoomProfiles
    {
        public struct Profile
        {
            public int width;
            public int height;

            /// <summary>The role this one most wants to stand beside, or null.</summary>
            public string partner;

            /// <summary>What its purpose depends on being near: "rock", "wood", "soil", or null.</summary>
            public string resource;

            public float compactness;
            public float evenness;
            public float partnerAffinity;
            public float resourceAffinity;
        }

        public static Profile For(string role)
        {
            var p = new Profile();

            // Reasonable across the board; overridden below where the role differs.
            p.width = 7;
            p.height = 7;
            p.compactness = 1.0f;
            p.evenness = 0.2f;
            p.partnerAffinity = 0.5f;
            p.resourceAffinity = 0f;

            switch (role)
            {
                case "Storage":
                    // Everything is hauled here from everywhere, so what it wants is to be in
                    // the middle rather than merely close to the origin. Large, because a store
                    // that fills up stops being a store.
                    p.width = 9;
                    p.height = 9;
                    p.evenness = 2.0f;
                    p.compactness = 0.6f;
                    p.resource = "rock";
                    p.resourceAffinity = 0.3f;
                    break;

                case "Workshop":
                    // Beside the store it draws from; every haul to a bench is a walk saved.
                    p.width = 9;
                    p.height = 7;
                    p.partner = "Storage";
                    p.partnerAffinity = 1.8f;
                    p.resource = "rock";
                    p.resourceAffinity = 0.8f;
                    break;

                case "Kitchen":
                    p.partner = "Storage";
                    p.partnerAffinity = 1.2f;
                    break;

                case "Freezer":
                    // Next to the kitchen, which is where its contents are going.
                    p.partner = "Kitchen";
                    p.partnerAffinity = 2.0f;
                    break;

                case "Dining":
                    p.width = 9;
                    p.height = 9;
                    p.partner = "Kitchen";
                    p.partnerAffinity = 1.5f;
                    break;

                case "Bedroom":
                    // Small on purpose: RimWorld rewards a small tidy room over a large bare
                    // one, and every extra cell is wall to build and floor to keep clean.
                    p.width = 6;
                    p.height = 6;
                    p.compactness = 1.2f;
                    p.partnerAffinity = 0f;
                    break;

                case "Hospital":
                    p.partner = "Bedroom";
                    p.partnerAffinity = 0.8f;
                    p.compactness = 1.4f;   // carried patients should not be carried far
                    break;

                case "Prison":
                    // Away from everything, which is the one role that wants distance.
                    p.compactness = 0.2f;
                    p.partnerAffinity = 0f;
                    break;

                case "Power":
                    // Fuel comes from wood, and conduit runs are shorter near the middle.
                    p.width = 6;
                    p.height = 6;
                    p.resource = "wood";
                    p.resourceAffinity = 0.6f;
                    break;

                case "Research":
                    // Same footprint as the workshop, and for the same reason: a research bench
                    // is three cells by two, and a 7x7 room leaves a 5x5 interior that also has
                    // to hold a light. That was tight enough that the game refused the bench at
                    // every cell the scorer preferred, and the room stood empty for the whole of
                    // thirty-seven colonies. The retry cap was the fault; this is the margin.
                    p.width = 9;
                    p.height = 7;
                    p.partner = "Workshop";
                    p.partnerAffinity = 0.6f;
                    break;

                case "Recreation":
                    // Large, because impressiveness is the whole point of the room and space is
                    // one of the four things it is scored on — and because a horseshoes pin
                    // needs a clear lane to throw down, which is what defeated the old remedy
                    // when it tried to fit one into a bedroom.
                    //
                    // Beside the dining room: colonists eat and then look for something to do,
                    // and the two rooms are used in the same trip.
                    p.width = 9;
                    p.height = 9;
                    p.partner = "Dining";
                    p.partnerAffinity = 1.4f;
                    break;

                case "Tomb":
                    // Away from where people live and work, like the prison, for the same
                    // reason: nothing else wants to be near it. Small — graves are 1x1 and a
                    // colony that needs a large tomb has worse problems.
                    p.width = 6;
                    p.height = 6;
                    p.compactness = 0.3f;
                    p.partnerAffinity = 0f;
                    break;

                case "Barn":
                    // Big, because animals need floor, and near the store the feed comes out of.
                    // Kept off the middle of the base: a barn is filth, and filth spreads to
                    // whatever room is next door.
                    p.width = 9;
                    p.height = 9;
                    p.compactness = 0.4f;
                    p.partner = "Storage";
                    p.partnerAffinity = 1.0f;
                    p.resource = "soil";
                    p.resourceAffinity = 0.5f;
                    break;
            }

            return p;
        }
    }
}
