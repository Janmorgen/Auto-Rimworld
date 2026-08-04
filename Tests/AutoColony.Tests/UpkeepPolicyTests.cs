using AutoColony.Upkeep;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Labour, as distinct from material. A colony can be rich and still unable to finish
    /// anything, which is how one died with 842 units in the stockpile and no bed on the map.
    /// </summary>
    public class ConcurrentRoomsTests
    {
        [Fact]
        public void ASmallColonyBuildsOneThingAtATime()
        {
            Assert.Equal(1, AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(1));
            Assert.Equal(1, AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(2));
        }

        [Fact]
        public void MoreHandsAllowMoreAtOnce()
        {
            Assert.True(AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(6) >
                        AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(2));
            Assert.True(AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(12) >
                        AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(6));
        }

        [Fact]
        public void TwoColonistsAreNeverAllowedTheSixShellsThatKilledOne()
        {
            Assert.True(AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(2) < 6);
        }

        [Fact]
        public void AnEmptyColonyStillReturnsSomethingBuildable()
        {
            Assert.True(AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(0) >= 1);
        }
    }
}

namespace AutoColony.Tests
{
    /// <summary>
    /// The judgement half of the upkeep layer: what counts as a fault, and what to do about it.
    ///
    /// Worth testing offline because the mistake this code exists to avoid is a value judgement
    /// rather than a bug. Deciding a barracks is always wrong would have the director pull beds
    /// out of the one warm room a struggling colony owns; deciding it is always fine would leave
    /// a wealthy colony taking a large standing mood penalty for no reason. Neither shows up as
    /// an exception, and both would take an in-game season to notice.
    /// </summary>
    public class UpkeepPolicyTests
    {
        // ------------------------------------------------------------ means

        [Fact]
        public void ColonyWithNothingIsDestitute()
        {
            Assert.Equal(0f, BuildingMeans.Assess(0, 3));
            Assert.True(BuildingMeans.Destitute(BuildingMeans.Assess(30, 3)));
        }

        [Fact]
        public void ColonyWithARoomEachIsComfortable()
        {
            // Three colonists, three rooms' worth of material.
            float means = BuildingMeans.Assess(BuildingMeans.RoomCost * 3, 3);
            Assert.True(BuildingMeans.Comfortable(means));
        }

        [Fact]
        public void MeansFallAsTheColonyGrowsAgainstFixedMaterial()
        {
            int material = BuildingMeans.RoomCost * 3;
            Assert.True(BuildingMeans.Assess(material, 3) > BuildingMeans.Assess(material, 8));
        }

        [Fact]
        public void NoColonistsIsNotDestitute()
        {
            // Nothing to house, so nothing is short. Guards a divide by zero too.
            Assert.Equal(1f, BuildingMeans.Assess(0, 0));
        }

        // ------------------------------------------------------------ beds per room

        [Fact]
        public void ADestituteColonyPutsEveryoneInOneRoom()
        {
            Assert.Equal(5, BuildingMeans.BedsPerRoom(0f, preferred: 1, colonists: 5));
        }

        [Fact]
        public void AComfortableColonyHonoursThePreference()
        {
            Assert.Equal(1, BuildingMeans.BedsPerRoom(1f, preferred: 1, colonists: 5));
            Assert.Equal(2, BuildingMeans.BedsPerRoom(1f, preferred: 2, colonists: 5));
        }

        [Fact]
        public void SharingEasesOffAsTheColonyRecovers()
        {
            int poor = BuildingMeans.BedsPerRoom(0.3f, 1, 6);
            int better = BuildingMeans.BedsPerRoom(0.6f, 1, 6);
            Assert.True(poor >= better);
            Assert.True(better >= 1);
        }

        [Fact]
        public void BedsPerRoomIsNeverZeroOrNegative()
        {
            for (int colonists = 0; colonists < 6; colonists++)
                for (int preferred = -1; preferred < 5; preferred++)
                    Assert.True(BuildingMeans.BedsPerRoom(0.5f, preferred, colonists) >= 1);
        }

        // ------------------------------------------------------------ sharing as a fault

        [Fact]
        public void SharingIsNotAFaultWhenTheColonyCannotAffordToSplit()
        {
            // The correction that matters: a barracks in a destitute colony is the right answer,
            // not a defect to be fixed.
            Assert.Equal(0f, BuildingMeans.SharingSeverity(0f, 0.7f));
            Assert.Equal(0f, BuildingMeans.SharingSeverity(0.1f, 0.7f));
        }

        [Fact]
        public void SharingIsAFaultOnceTheColonyCanAffordToSplit()
        {
            Assert.True(BuildingMeans.SharingSeverity(1f, 0.7f) > 0f);
        }

        [Fact]
        public void SharingMattersMoreTheRicherTheColony()
        {
            Assert.True(BuildingMeans.SharingSeverity(0.9f, 0.7f) >
                        BuildingMeans.SharingSeverity(0.4f, 0.7f));
        }

        // ------------------------------------------------------------ reclaiming

        [Fact]
        public void NothingIsReclaimedWhileTheColonyIsComfortable()
        {
            Assert.Equal(0f, BuildingMeans.ReclaimSeverity(1f, 3));
            Assert.Equal(0f, BuildingMeans.ReclaimSeverity(0.5f, 3));
        }

        [Fact]
        public void NothingIsReclaimedWithNothingSpare()
        {
            Assert.Equal(0f, BuildingMeans.ReclaimSeverity(0f, 0));
        }

        [Fact]
        public void ADestituteColonyWithSurplusRoomsWantsThemBack()
        {
            Assert.True(BuildingMeans.ReclaimSeverity(0.02f, 3) > 0.5f);
        }

        [Fact]
        public void ReclaimingAndSharingNeverBothApply()
        {
            // They are opposite moves. If the colony is poor enough to be pulling rooms down it
            // must not simultaneously be told to spread out, or it would oscillate forever.
            for (float means = 0f; means <= 1f; means += 0.05f)
            {
                bool reclaiming = BuildingMeans.ReclaimSeverity(means, 2) > 0f;
                bool splitting = BuildingMeans.SharingSeverity(means, 0.7f) > 0f;
                Assert.False(reclaiming && splitting);
            }
        }

        // ------------------------------------------------------------ complaints

        [Fact]
        public void KnownComplaintsMapToSomethingActionable()
        {
            DefectKind kind;
            Assert.True(Complaints.TryMap("EnvironmentDark", out kind));
            Assert.Equal(DefectKind.DarkRoom, kind);

            Assert.True(Complaints.TryMap("SleptInBarracks", out kind));
            Assert.Equal(DefectKind.SharedBedroom, kind);
        }

        [Fact]
        public void UnknownComplaintsAreReportedNotMapped()
        {
            DefectKind ignored;
            Assert.False(Complaints.TryMap("SomeThoughtNobodyTaughtItAbout", out ignored));
            Assert.False(Complaints.TryMap(null, out ignored));
            Assert.False(Complaints.TryMap("", out ignored));
        }

        [Fact]
        public void OnlyNegativeMoodCountsAsSeverity()
        {
            Assert.Equal(0f, Complaints.Severity(3f));
            Assert.Equal(0f, Complaints.Severity(0f));
            Assert.True(Complaints.Severity(-5f) > 0f);
        }

        [Fact]
        public void SeverityIsBoundedForAnAbsurdMoodPenalty()
        {
            Assert.Equal(1f, Complaints.Severity(-500f));
        }

        [Fact]
        public void AWorseMoodIsAWorseComplaint()
        {
            Assert.True(Complaints.Severity(-7f) > Complaints.Severity(-2f));
        }

        // ------------------------------------------------------------ what to do first

        [Fact]
        public void EveryKindHasARemedy()
        {
            foreach (DefectKind kind in System.Enum.GetValues(typeof(DefectKind)))
                Assert.NotEqual(RemedyKind.None, DefectPolicy.RemedyFor(kind));
        }

        [Fact]
        public void ReclaimingOutranksSpendingWhenBothAreEquallySevere()
        {
            // It frees the very material the other remedies are about to spend.
            Assert.True(DefectPolicy.Priority(DefectKind.Overbuilt, 0.5f) >
                        DefectPolicy.Priority(DefectKind.ExposedPowered, 0.5f));
        }

        [Fact]
        public void AFireHazardOutranksAMoodPenalty()
        {
            Assert.True(DefectPolicy.Priority(DefectKind.ExposedPowered, 0.5f) >
                        DefectPolicy.Priority(DefectKind.DrearyRoom, 0.5f));
        }

        [Fact]
        public void BuryingTheDeadOutranksEverything()
        {
            // The best trade available: the largest single mood penalty in the game, removed by
            // a building that needs no research and costs nothing at all.
            foreach (DefectKind kind in System.Enum.GetValues(typeof(DefectKind)))
            {
                if (kind == DefectKind.UnburiedDead) continue;
                Assert.True(DefectPolicy.Priority(DefectKind.UnburiedDead, 0.5f) >
                            DefectPolicy.Priority(kind, 0.5f),
                            "burial should outrank " + kind);
            }
        }

        [Fact]
        public void ColdOutranksTheComfortsBecauseColdKills()
        {
            Assert.True(DefectPolicy.Priority(DefectKind.ColdRoom, 0.5f) >
                        DefectPolicy.Priority(DefectKind.NoTable, 0.5f));
            Assert.True(DefectPolicy.Priority(DefectKind.ColdRoom, 0.5f) >
                        DefectPolicy.Priority(DefectKind.Cheerless, 0.5f));
        }

        [Fact]
        public void TheComplaintsThatKilledAColonyAllMapToSomething()
        {
            // Every one of these was reported by a real colony's own survey as unfixable, and
            // the accumulation is what ground it to a mood of zero.
            var wereUnfixable = new[]
            {
                "ColonistLeftUnburied", "ObservedLayingCorpse", "EnvironmentCold",
                "AteWithoutTable", "NeedJoy", "NeedBeauty"
            };

            foreach (var thought in wereUnfixable)
            {
                DefectKind kind;
                Assert.True(Complaints.TryMap(thought, out kind), thought + " still has no answer");
                Assert.NotEqual(RemedyKind.None, DefectPolicy.RemedyFor(kind));
            }
        }

        [Fact]
        public void TrivialFaultsAreLeftAlone()
        {
            Assert.False(DefectPolicy.WorthActing(DefectKind.DrearyRoom, 0.01f));
            Assert.False(DefectPolicy.WorthActing(DefectKind.SharedBedroom, 0f));
        }

        [Fact]
        public void ASeriousFaultIsWorthActingOn()
        {
            Assert.True(DefectPolicy.WorthActing(DefectKind.ExposedPowered, 0.8f));
        }

        [Fact]
        public void PriorityIsZeroForSomethingCostingNothing()
        {
            Assert.Equal(0f, DefectPolicy.Priority(DefectKind.DarkRoom, 0f));
            Assert.Equal(0f, DefectPolicy.Priority(DefectKind.DarkRoom, -1f));
        }
    }
}

namespace AutoColony.Tests
{
    /// <summary>
    /// Temperature complaints recurred on the unfixable list in every run — cold because the
    /// only remedy needed a generator the colony did not have, heat because it was mapped to
    /// nothing at all.
    /// </summary>
    public class TemperatureComplaintTests
    {
        [Fact]
        public void ColdComplaintsAskForHeat()
        {
            AutoColony.Upkeep.DefectKind kind;
            Assert.True(AutoColony.Upkeep.Complaints.TryMap("EnvironmentCold", out kind));
            Assert.Equal(AutoColony.Upkeep.DefectKind.ColdRoom, kind);

            Assert.True(AutoColony.Upkeep.Complaints.TryMap("SleptInCold", out kind));
            Assert.Equal(AutoColony.Upkeep.DefectKind.ColdRoom, kind);
        }

        [Fact]
        public void HeatComplaintsAreNoLongerInvisible()
        {
            // EnvironmentHot was the single largest complaint in one run's survey and mapped to
            // nothing, so no part of the director could see it.
            AutoColony.Upkeep.DefectKind kind;
            Assert.True(AutoColony.Upkeep.Complaints.TryMap("EnvironmentHot", out kind));
            Assert.Equal(AutoColony.Upkeep.DefectKind.HotRoom, kind);
        }

        [Fact]
        public void ColdAndHeatAskForOppositeRemedies()
        {
            Assert.Equal(AutoColony.Upkeep.RemedyKind.AddHeater,
                         AutoColony.Upkeep.DefectPolicy.RemedyFor(AutoColony.Upkeep.DefectKind.ColdRoom));
            Assert.Equal(AutoColony.Upkeep.RemedyKind.AddCooler,
                         AutoColony.Upkeep.DefectPolicy.RemedyFor(AutoColony.Upkeep.DefectKind.HotRoom));
        }
    }
}

namespace AutoColony.Tests
{
    /// <summary>
    /// What each kind of fault is worth was a hardcoded table — the director's entire opinion
    /// about what to do next, fixed for every colony in every situation.
    /// </summary>
    public class LearnedUpkeepWeightTests
    {
        [Fact]
        public void TheDefaultTableIsSizedForEveryKind()
        {
            // A zero-length table silently made every weight fall back to a constant, which is
            // how the hardcoded behaviour would have survived the change to remove it.
            Assert.Equal(AutoColony.Upkeep.DefectPolicy.KindCount,
                         AutoColony.Upkeep.DefectPolicy.DefaultWeights.Length);
            foreach (var w in AutoColony.Upkeep.DefectPolicy.DefaultWeights) Assert.True(w > 0f);
        }

        [Fact]
        public void AColonyCanValueAFaultDifferentlyFromTheDefault()
        {
            float asShipped = AutoColony.Upkeep.DefectPolicy.Priority(
                AutoColony.Upkeep.DefectKind.Cheerless, 1f, 1f);
            float caresMore = AutoColony.Upkeep.DefectPolicy.Priority(
                AutoColony.Upkeep.DefectKind.Cheerless, 1f, 1f, 3f);

            Assert.True(caresMore > asShipped);
        }

        [Fact]
        public void AWeightOfZeroMeansTheColonyIgnoresThatFault()
        {
            Assert.Equal(0f, AutoColony.Upkeep.DefectPolicy.Priority(
                AutoColony.Upkeep.DefectKind.DrearyRoom, 1f, 1f, 0f), 3);
        }

        [Fact]
        public void EveryKindHasItsOwnGeneKey()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (AutoColony.Upkeep.DefectKind kind in
                     System.Enum.GetValues(typeof(AutoColony.Upkeep.DefectKind)))
            {
                Assert.True(seen.Add(AutoColony.Upkeep.DefectPolicy.WeightKey(kind)));
            }
        }

        [Fact]
        public void RoomImportanceAndKindWeightCompose()
        {
            float plain = AutoColony.Upkeep.DefectPolicy.Priority(
                AutoColony.Upkeep.DefectKind.DarkRoom, 1f, 1f, 1f);
            float importantRoom = AutoColony.Upkeep.DefectPolicy.Priority(
                AutoColony.Upkeep.DefectKind.DarkRoom, 1f, 2f, 1f);

            Assert.Equal(plain * 2f, importantRoom, 3);
        }
    }
}

namespace AutoColony.Tests
{
    /// <summary>
    /// How much building may be in flight at once.
    ///
    /// Two constraints bind it and only one used to be modelled. Run 107 reached day nineteen
    /// with four colonists, two hundred material, three shells open and one room finished — the
    /// labour gate satisfied, the colony spread across sites it could afford one of.
    /// </summary>
    public class ConcurrentRoomTests
    {
        [Xunit.Fact]
        public void MaterialCanBindTighterThanHands()
        {
            // Plenty of hands, barely enough for one shell.
            Xunit.Assert.Equal(1, AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(9, 200));
        }

        [Xunit.Fact]
        public void HandsStillBindWhenMaterialIsPlentiful()
        {
            // A warehouse of steel does not give two colonists more arms.
            Xunit.Assert.Equal(1, AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(2, 100000));
        }

        [Xunit.Fact]
        public void OneRoomIsAlwaysAllowedSoWorkNeverStops()
        {
            // Destitute and mid-build: the room already going up must not be forbidden.
            Xunit.Assert.Equal(1, AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(6, 0));
        }

        [Xunit.Fact]
        public void BothPlentifulGivesTheLabourLimit()
        {
            Xunit.Assert.Equal(3, AutoColony.Upkeep.BuildingMeans.ConcurrentRooms(6, 100000));
        }
    }
}
