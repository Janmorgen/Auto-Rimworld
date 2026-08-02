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
                    p.partner = "Workshop";
                    p.partnerAffinity = 0.6f;
                    break;
            }

            return p;
        }
    }
}
