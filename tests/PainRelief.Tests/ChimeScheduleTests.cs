using System;
using Xunit;

namespace PainRelief.Tests
{
    public class ChimeScheduleTests
    {
        private static readonly DateTime Arrived = new DateTime(2026, 10, 11, 6, 0, 0, DateTimeKind.Utc);

        private static ChimeAction Decide(double secondsSinceArrival, double? secondsSinceLastChime = null)
        {
            DateTime now = Arrived.AddSeconds(secondsSinceArrival);
            DateTime? last = secondsSinceLastChime == null ? (DateTime?)null : now.AddSeconds(-secondsSinceLastChime.Value);
            return ChimeSchedule.Decide(Arrived, last, now);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(4.9)]
        public void NothingBeforeFiveSeconds(double age)
        {
            Assert.Equal(ChimeAction.Wait, Decide(age));
        }

        [Fact]
        public void FirstChimeAtFiveSeconds()
        {
            Assert.Equal(ChimeAction.Chime, Decide(5));
            Assert.Equal(ChimeAction.Chime, Decide(5.2));
        }

        [Fact]
        public void RepeatsEveryTwoSeconds()
        {
            Assert.Equal(ChimeAction.Wait, Decide(6, 1.9));
            Assert.Equal(ChimeAction.Chime, Decide(7, 2.0));
            Assert.Equal(ChimeAction.Chime, Decide(30, 2.25));
            Assert.Equal(ChimeAction.Wait, Decide(30, 0.25));
        }

        [Fact]
        public void GivesUpAfterTwoMinutes()
        {
            Assert.Equal(ChimeAction.Chime, Decide(119.9, 2));
            Assert.Equal(ChimeAction.GiveUp, Decide(120, 2));
            Assert.Equal(ChimeAction.GiveUp, Decide(600));
        }
    }
}
