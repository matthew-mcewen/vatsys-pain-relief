using System;
using Xunit;

namespace PainRelief.Tests
{
    public class ChimeScheduleTests
    {
        private static readonly DateTime Arrived = new DateTime(2026, 10, 11, 6, 0, 0, DateTimeKind.Utc);

        private static ChimeAction Decide(double secondsSinceArrival, int nudgesPlayed) =>
            ChimeSchedule.Default.Decide(Arrived, nudgesPlayed, Arrived.AddSeconds(secondsSinceArrival));

        private static int Due(double secondsSinceArrival) =>
            ChimeSchedule.Default.NudgesDue(Arrived, Arrived.AddSeconds(secondsSinceArrival));

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(9.9)]
        public void NoNudgeBeforeTenSeconds(double age)
        {
            Assert.Equal(ChimeAction.Wait, Decide(age, 0));
        }

        [Fact]
        public void FirstNudgeAtTenSeconds()
        {
            Assert.Equal(ChimeAction.Nudge, Decide(10, 0));
            Assert.Equal(ChimeAction.Nudge, Decide(10.2, 0));
        }

        [Fact]
        public void NudgesEveryTenSeconds()
        {
            Assert.Equal(ChimeAction.Wait, Decide(19.9, 1));
            Assert.Equal(ChimeAction.Nudge, Decide(20, 1));
            Assert.Equal(ChimeAction.Wait, Decide(29.9, 2));
            Assert.Equal(ChimeAction.Nudge, Decide(30.1, 2));
        }

        [Fact]
        public void TenNudgesOverTheNextNinetySeconds()
        {
            Assert.Equal(10, ChimeSchedule.Default.MaxNudges);
            Assert.Equal(ChimeAction.Nudge, Decide(100, 9));    // the last one, 90 s after the first
            Assert.Equal(ChimeAction.GiveUp, Decide(100.1, 10));
            Assert.Equal(ChimeAction.GiveUp, Decide(600, 10));
        }

        [Fact]
        public void PlaysEveryNudgeOnTime()
        {
            // Simulate the 250 ms timer and record when each nudge plays.
            var played = new System.Collections.Generic.List<double>();
            int nudges = 0;
            for (double t = 0; t <= 200; t += 0.25)
            {
                ChimeAction action = Decide(t, nudges);
                if (action == ChimeAction.GiveUp)
                    break;
                if (action == ChimeAction.Nudge)
                {
                    played.Add(t);
                    nudges = Due(t);
                }
            }
            Assert.Equal(new double[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }, played);
        }

        [Fact]
        public void LateTickDoesNotBurst()
        {
            // A tick that comes 35 s in, with no nudge yet, plays one nudge and counts the three
            // that were due, so the next one is at 40 s rather than on the following tick.
            Assert.Equal(ChimeAction.Nudge, Decide(35, 0));
            Assert.Equal(3, Due(35));
            Assert.Equal(ChimeAction.Wait, Decide(35.25, 3));
            Assert.Equal(ChimeAction.Nudge, Decide(40, 3));
        }

        [Fact]
        public void NudgesDueIsCapped()
        {
            Assert.Equal(0, Due(9.9));
            Assert.Equal(1, Due(10));
            Assert.Equal(10, Due(100));
            Assert.Equal(10, Due(1000));
        }

        [Fact]
        public void CustomTimings()
        {
            var s = new ChimeSchedule(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
            Assert.Equal(3, s.MaxNudges); // 5, 20, 35
            Assert.Equal(ChimeAction.Wait, s.Decide(Arrived, 0, Arrived.AddSeconds(4.9)));
            Assert.Equal(ChimeAction.Nudge, s.Decide(Arrived, 0, Arrived.AddSeconds(5)));
            Assert.Equal(ChimeAction.Nudge, s.Decide(Arrived, 2, Arrived.AddSeconds(35)));
            Assert.Equal(ChimeAction.GiveUp, s.Decide(Arrived, 3, Arrived.AddSeconds(36)));
        }

        [Fact]
        public void NudgeForZeroMeansOneNudge()
        {
            Assert.Equal(1, new ChimeSchedule(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.Zero).MaxNudges);
        }

        [Fact]
        public void OutOfRangeTimingsAreClamped()
        {
            var s = new ChimeSchedule(TimeSpan.FromSeconds(-3), TimeSpan.Zero, TimeSpan.FromSeconds(-1));
            Assert.Equal(TimeSpan.Zero, s.FirstNudgeAfter);
            Assert.Equal(ChimeSchedule.MinNudgeEvery, s.NudgeEvery);
            Assert.Equal(1, s.MaxNudges);
        }
    }
}
