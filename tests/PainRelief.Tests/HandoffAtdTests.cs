using System;
using Xunit;

namespace PainRelief.Tests
{
    public class HandoffAtdTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 11, 6, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Ten = TimeSpan.FromMinutes(10);

        [Theory]
        [InlineData("WAAF_BI_CTR", 10)]
        [InlineData("VTBB_CTR", 10)]
        [InlineData("wiii_app", 5)]
        [InlineData("WIII_N_APP", 5)]
        [InlineData("  WIII_APP  ", 5)]
        public void Lead_FromCentreAndApproachCallsigns(string callsign, int minutes)
        {
            Assert.Equal(TimeSpan.FromMinutes(minutes), HandoffAtd.LeadForCallsign(callsign));
        }

        [Theory]
        [InlineData("WIII_TWR")]
        [InlineData("WIII_DEL")]
        [InlineData("WIII_CTRL")]
        [InlineData("CTR")]
        [InlineData("")]
        [InlineData(null)]
        public void Lead_NullForOtherCallsigns(string callsign)
        {
            Assert.Null(HandoffAtd.LeadForCallsign(callsign));
        }

        [Fact]
        public void Boundary_PlacesAircraftLeadShortOfBoundary()
        {
            // Activated "now"; vatSys says the aircraft reaches our airspace 150 min later.
            // 10 min short of that is 140 min into the flight.
            DateTime? atd = HandoffAtd.AtdForBoundary(Now, Now, Now + TimeSpan.FromMinutes(150), Ten);
            Assert.Equal(Now - TimeSpan.FromMinutes(140), atd);
        }

        [Fact]
        public void Boundary_ApproachLeadIsShorter()
        {
            DateTime? atd = HandoffAtd.AtdForBoundary(Now, Now, Now + TimeSpan.FromMinutes(150), TimeSpan.FromMinutes(5));
            Assert.Equal(Now - TimeSpan.FromMinutes(145), atd);
        }

        [Fact]
        public void Boundary_DoesNotDependOnWhichAtdWasUsedToAsk()
        {
            // Same flight, but activated from an EET-based ATD of now - 150 min: vatSys's entry
            // time moves with it, so the answer is the same.
            DateTime activated = Now - TimeSpan.FromMinutes(150);
            DateTime? atd = HandoffAtd.AtdForBoundary(Now, activated, activated + TimeSpan.FromMinutes(150), Ten);
            Assert.Equal(Now - TimeSpan.FromMinutes(140), atd);
        }

        [Fact]
        public void Boundary_CorrectsAnEetThatDisagreesWithVatSysSpeed()
        {
            // EET said 150 min, but at vatSys's speed the boundary is 200 min from departure.
            // Activated from now - 150 min, so vatSys's entry time is 50 min from now.
            DateTime activated = Now - TimeSpan.FromMinutes(150);
            DateTime? atd = HandoffAtd.AtdForBoundary(Now, activated, Now + TimeSpan.FromMinutes(50), Ten);
            Assert.Equal(Now - TimeSpan.FromMinutes(190), atd);
        }

        [Fact]
        public void Boundary_CloserThanLead_PutsAircraftAtOrigin()
        {
            Assert.Equal(Now, HandoffAtd.AtdForBoundary(Now, Now, Now + TimeSpan.FromMinutes(8), Ten));
        }

        [Fact]
        public void Boundary_AlreadyInsideAirspace_PutsAircraftAtOrigin()
        {
            Assert.Equal(Now, HandoffAtd.AtdForBoundary(Now, Now, Now - TimeSpan.FromMinutes(5), Ten));
        }

        [Fact]
        public void Boundary_NoEntryTime_IsNull()
        {
            Assert.Null(HandoffAtd.AtdForBoundary(Now, Now, DateTime.MaxValue, Ten));
            Assert.Null(HandoffAtd.AtdForBoundary(Now, Now, DateTime.MinValue, Ten));
        }

        [Fact]
        public void Boundary_AbsurdlyLongFlight_IsNull()
        {
            Assert.Null(HandoffAtd.AtdForBoundary(Now, Now, Now + TimeSpan.FromHours(30), Ten));
        }
    }
}
