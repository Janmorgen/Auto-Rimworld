using AutoColony.Rooms;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Siting was a fixed slot pattern: every role got the same answer to a question that
    /// differs completely between them.
    /// </summary>
    public class RoomSitingTests
    {
        static SiteFeatures Site(float fromOrigin, float uneven, float toPartner, float toResource)
        {
            var f = new SiteFeatures();
            f.buildable = 1f;
            f.fromOrigin = fromOrigin;
            f.unevenness = uneven;
            f.toPartnerRoom = toPartner;
            f.toResource = toResource;
            return f;
        }

        static SiteWeights WeightsFor(string role)
        {
            var p = RoomProfiles.For(role);
            var w = new SiteWeights();
            w.compactness = p.compactness;
            w.evenness = p.evenness;
            w.partnerAffinity = p.partnerAffinity;
            w.resourceAffinity = p.resourceAffinity;
            return w;
        }

        [Fact]
        public void UnbuildableGroundIsRefusedWhateverElseIsTrue()
        {
            var f = Site(0f, 0f, 0f, 0f);
            f.buildable = 0.5f;
            Assert.Equal(float.NegativeInfinity, RoomSiting.Score(f, WeightsFor("Storage")));
        }

        [Fact]
        public void AStoreWantsToSitEvenlyAmongTheOtherRooms()
        {
            var w = WeightsFor("Storage");
            float even = RoomSiting.Score(Site(20f, 0.1f, 30f, 10f), w);
            float lopsided = RoomSiting.Score(Site(20f, 0.9f, 30f, 10f), w);
            Assert.True(even > lopsided);
        }

        [Fact]
        public void ABedroomCaresFarLessAboutEvennessThanAStore()
        {
            // The distinction a single pattern could not express. Every room mildly prefers not
            // to be tacked on the end, but only the store everything is hauled to really wants
            // the middle — so this is a comparison rather than an absence.
            float storePenalty =
                RoomSiting.Score(Site(20f, 0.1f, 30f, 10f), WeightsFor("Storage")) -
                RoomSiting.Score(Site(20f, 0.9f, 30f, 10f), WeightsFor("Storage"));
            float bedroomPenalty =
                RoomSiting.Score(Site(20f, 0.1f, 30f, 0f), WeightsFor("Bedroom")) -
                RoomSiting.Score(Site(20f, 0.9f, 30f, 0f), WeightsFor("Bedroom"));

            Assert.True(storePenalty > bedroomPenalty * 4f);
        }

        [Fact]
        public void AWorkshopWantsToBeBesideTheStore()
        {
            var w = WeightsFor("Workshop");
            Assert.True(RoomSiting.Score(Site(20f, 0.5f, 4f, 10f), w) >
                        RoomSiting.Score(Site(20f, 0.5f, 40f, 10f), w));
        }

        [Fact]
        public void AWorkshopWantsToBeNearRock()
        {
            var w = WeightsFor("Workshop");
            Assert.True(RoomSiting.Score(Site(20f, 0.5f, 10f, 3f), w) >
                        RoomSiting.Score(Site(20f, 0.5f, 10f, 39f), w));
        }

        [Fact]
        public void APrisonPrefersDistanceWhereEverythingElsePrefersCloseness()
        {
            var prison = WeightsFor("Prison");
            var bedroom = WeightsFor("Bedroom");

            float near = 5f, far = 38f;
            float prisonGain = RoomSiting.Score(Site(far, 0.5f, 0f, 0f), prison) -
                               RoomSiting.Score(Site(near, 0.5f, 0f, 0f), prison);
            float bedroomGain = RoomSiting.Score(Site(far, 0.5f, 0f, 0f), bedroom) -
                                RoomSiting.Score(Site(near, 0.5f, 0f, 0f), bedroom);

            // Both still mildly prefer closeness, but the prison gives up far less for distance.
            Assert.True(prisonGain > bedroomGain);
        }

        [Fact]
        public void RoomsAreSizedForTheirPurpose()
        {
            Assert.True(RoomProfiles.For("Storage").width > RoomProfiles.For("Bedroom").width);
            Assert.True(RoomProfiles.For("Dining").width > RoomProfiles.For("Bedroom").width);
        }

        [Fact]
        public void EvennessIsZeroWhenEverythingIsEquidistant()
        {
            var equal = new float[] { 10f, 10f, 10f };
            Assert.Equal(0f, RoomSiting.Unevenness(equal, 3), 3);

            var lopsided = new float[] { 2f, 40f, 20f };
            Assert.True(RoomSiting.Unevenness(lopsided, 3) > 0.8f);
        }

        [Fact]
        public void TheFirstRoomHasNothingToBeUnevenAbout()
        {
            Assert.Equal(0f, RoomSiting.Unevenness(new float[] { 5f }, 1), 3);
            Assert.Equal(0f, RoomSiting.Unevenness(null, 0), 3);
        }
    }
}
