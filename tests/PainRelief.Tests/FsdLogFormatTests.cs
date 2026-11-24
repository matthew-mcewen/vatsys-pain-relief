using System;
using System.Linq;
using Xunit;

namespace PainRelief.Tests
{
    public class FsdLogFormatTests
    {
        private static readonly DateTime At = new DateTime(2026, 11, 8, 15, 16, 41, DateTimeKind.Utc);

        [Fact]
        public void ReceivedEntryMatchesEuroScope()
        {
            Assert.Equal("[15:16:41 >>>> WIII_APP]\r\n%WILL_ATIS:27700:4:50:5:-5.24056:105.17556:0\r\n\r\n",
                FsdLogFormat.Entry(At, false, "WIII_APP", "%WILL_ATIS:27700:4:50:5:-5.24056:105.17556:0"));
        }

        [Fact]
        public void SentEntryMatchesEuroScope()
        {
            Assert.Equal("[15:16:41 <<<2 WIII_APP]\r\n#DAWIII_APP:SERVER\r\n\r\n",
                FsdLogFormat.Entry(At, true, "WIII_APP", "#DAWIII_APP:SERVER"));
        }

        [Fact]
        public void SplitsChunkIntoPackets()
        {
            Assert.Equal(new[] { "#DAA:B", "$CQA:B:C" }, FsdLogFormat.Packets("#DAA:B\r\n$CQA:B:C\r\n").ToArray());
            Assert.Empty(FsdLogFormat.Packets(null));
            Assert.Empty(FsdLogFormat.Packets("\r\n"));
        }

        [Fact]
        public void FileNameIsPositionDateTime()
        {
            Assert.Equal("WIII_APP_20261108_151641.txt", FsdLogFormat.FileName("WIII_APP", At));
            Assert.Equal("UNKNOWN_20261108_151641.txt", FsdLogFormat.FileName("", At));
            Assert.Equal("A_B_20261108_151641.txt", FsdLogFormat.FileName("A/B", At));
        }

        [Fact]
        public void DeletesOldestLogsOnly()
        {
            string[] files =
            {
                "WIII_APP_20261108_151641.txt",
                "WAAF_CTR_20261107_090000.txt",
                "WIII_APP_20261108_080000.txt",
                "notes.txt",
                "StealthPlugins.txt",
            };
            Assert.Equal(new[] { "WIII_APP_20261108_080000.txt", "WAAF_CTR_20261107_090000.txt" },
                FsdLogFormat.FilesToDelete(files, 1).ToArray());
            Assert.Empty(FsdLogFormat.FilesToDelete(files, 3));
            Assert.Equal(3, FsdLogFormat.FilesToDelete(files, 0).Count());
        }
    }
}
