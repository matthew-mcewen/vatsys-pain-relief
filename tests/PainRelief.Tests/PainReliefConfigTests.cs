using System;
using System.IO;
using Xunit;

namespace PainRelief.Tests
{
    public class PainReliefConfigTests
    {
        private const string Folder = @"C:\Plugins\PainRelief";

        [Fact]
        public void MissingFileMeansEverythingOn()
        {
            PainReliefConfig c = PainReliefConfig.Parse(null);
            Assert.True(c.ScalingFixes);
            Assert.True(c.HandoffAutoActivate);
            Assert.True(c.HandoffChime);
            Assert.True(c.FsdLog);
            Assert.Equal(10, c.FsdLogMaxFiles);
            Assert.Equal(Path.Combine(Folder, "incoming.wav"), c.HandoffIncomingSound(Folder));
            Assert.Equal(Path.Combine(Folder, "nudge.wav"), c.HandoffNudgeSound(Folder));
            Assert.Equal(Path.Combine(Folder, "refused.wav"), c.HandoffRefusedSound(Folder));
        }

        [Fact]
        public void ShippedFileReadsAsTheDefaults()
        {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PainRelief.conf"));
            PainReliefConfig shipped = PainReliefConfig.Parse(text);
            PainReliefConfig none = PainReliefConfig.Parse(null);
            Assert.Equal("true", shipped.GetString(PainReliefConfig.ScalingFixesKey));
            Assert.Equal("true", shipped.GetString(PainReliefConfig.HandoffAutoActivateKey));
            Assert.Equal("true", shipped.GetString(PainReliefConfig.HandoffChimeKey));
            Assert.Equal("true", shipped.GetString(PainReliefConfig.FsdLogKey));
            Assert.Equal(none.FsdLogMaxFiles, shipped.FsdLogMaxFiles);
            Assert.Equal(none.HandoffIncomingSound(Folder), shipped.HandoffIncomingSound(Folder));
            Assert.Equal(none.HandoffNudgeSound(Folder), shipped.HandoffNudgeSound(Folder));
            Assert.Equal(none.HandoffRefusedSound(Folder), shipped.HandoffRefusedSound(Folder));
        }

        [Theory]
        [InlineData("false", false)]
        [InlineData(" Off ", false)]
        [InlineData("0", false)]
        [InlineData("no", false)]
        [InlineData("TRUE", true)]
        [InlineData("on", true)]
        [InlineData("maybe", true)]
        [InlineData("", true)]
        public void ReadsToggles(string value, bool expected)
        {
            PainReliefConfig c = PainReliefConfig.Parse("# comment\r\nhandoffchime=" + value + "\r\n");
            Assert.Equal(expected, c.HandoffChime);
        }

        [Fact]
        public void TogglesAreIndependent()
        {
            PainReliefConfig c = PainReliefConfig.Parse("ScalingFixes=false\nFsdLog=false");
            Assert.False(c.ScalingFixes);
            Assert.False(c.FsdLog);
            Assert.True(c.HandoffAutoActivate);
            Assert.True(c.HandoffChime);
        }

        [Fact]
        public void CommentedOutLinesAreIgnored()
        {
            Assert.True(PainReliefConfig.Parse("#ScalingFixes=false\n;ScalingFixes=false").ScalingFixes);
        }

        [Theory]
        [InlineData("FsdLogMaxFiles=25", 25)]
        [InlineData("FsdLogMaxFiles=0", 0)]
        [InlineData("FsdLogMaxFiles=-2", 0)]
        [InlineData("FsdLogMaxFiles=lots", 10)]
        public void ReadsMaxFiles(string config, int expected)
        {
            Assert.Equal(expected, PainReliefConfig.Parse(config).FsdLogMaxFiles);
        }

        [Fact]
        public void DefaultRefusedSoundIsVatSysTone3()
        {
            Assert.Equal(@"C:\Program Files (x86)\vatSys\wav\Tone3.wav",
                PainReliefConfig.Parse("HandoffRefusedSound=default").HandoffRefusedSound(Folder));
            Assert.Equal(@"C:\Program Files (x86)\vatSys\wav\Tone3.wav",
                PainReliefConfig.Parse("HandoffRefusedSound=DEFAULT").HandoffRefusedSound(Folder));
        }

        [Fact]
        public void DefaultIncomingAndNudgeAreTheSystemSound()
        {
            Assert.Null(PainReliefConfig.Parse("HandoffIncomingSound=default").HandoffIncomingSound(Folder));
            Assert.Null(PainReliefConfig.Parse("HandoffNudgeSound=default").HandoffNudgeSound(Folder));
        }

        [Fact]
        public void SoundPathsAreTakenFromThePluginFolderUnlessFull()
        {
            Assert.Equal(Path.Combine(Folder, "mine.wav"),
                PainReliefConfig.Parse("HandoffNudgeSound=mine.wav").HandoffNudgeSound(Folder));
            Assert.Equal(@"D:\Sounds\ding.wav",
                PainReliefConfig.Parse(@"HandoffNudgeSound=""D:\Sounds\ding.wav""").HandoffNudgeSound(Folder));
        }

        [Fact]
        public void NoneMeansSilent()
        {
            Assert.Equal(PainReliefConfig.NoSound, PainReliefConfig.Parse("HandoffIncomingSound=none").HandoffIncomingSound(Folder));
            Assert.Equal(PainReliefConfig.NoSound, PainReliefConfig.Parse("HandoffNudgeSound=None").HandoffNudgeSound(Folder));
            Assert.Equal(PainReliefConfig.NoSound, PainReliefConfig.Parse("HandoffRefusedSound=NONE").HandoffRefusedSound(Folder));
        }

        [Fact]
        public void ReadsNudgeTimings()
        {
            ChimeSchedule s = PainReliefConfig.Parse("HandoffNudgeAfter=5\nHandoffNudgeEvery=2.5\nHandoffNudgeFor=20").NudgeSchedule;
            Assert.Equal(TimeSpan.FromSeconds(5), s.FirstNudgeAfter);
            Assert.Equal(TimeSpan.FromSeconds(2.5), s.NudgeEvery);
            Assert.Equal(TimeSpan.FromSeconds(20), s.NudgeFor);
        }

        [Fact]
        public void BadNudgeTimingsUseDefaults()
        {
            ChimeSchedule s = PainReliefConfig.Parse("HandoffNudgeAfter=soon\nHandoffNudgeEvery=\nHandoffNudgeFor=1e99").NudgeSchedule;
            Assert.Equal(ChimeSchedule.DefaultFirstNudgeAfter, s.FirstNudgeAfter);
            Assert.Equal(ChimeSchedule.DefaultNudgeEvery, s.NudgeEvery);
            Assert.Equal(ChimeSchedule.DefaultNudgeFor, s.NudgeFor);
        }

        [Fact]
        public void ShippedTimingsAreTheDefaults()
        {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PainRelief.conf"));
            ChimeSchedule s = PainReliefConfig.Parse(text).NudgeSchedule;
            Assert.Equal(ChimeSchedule.Default.FirstNudgeAfter, s.FirstNudgeAfter);
            Assert.Equal(ChimeSchedule.Default.NudgeEvery, s.NudgeEvery);
            Assert.Equal(ChimeSchedule.Default.NudgeFor, s.NudgeFor);
        }
    }
}
