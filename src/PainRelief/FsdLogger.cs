using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using RossCarlson.Vatsim.Network;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Logs every FSD packet vatSys sends and receives, in EuroScope's log format:
    ///
    ///   [15:16:40 >>>> WIII_APP]
    ///   %WILL_ATIS:27700:4:50:5:-5.24056:105.17556:0
    ///
    ///   [15:16:41 &lt;&lt;&lt;2 WIII_APP]
    ///   #DAWIII_APP:SERVER
    ///
    /// ">>>>" is a packet received, "&lt;&lt;&lt;2" one sent; the callsign is ours; times are UTC.
    ///
    /// Each connection gets its own file in the plugin's folder, named
    /// [Position]_[UTC date]_[UTC start time].txt. It is started by the first packet of the
    /// connection and closed (saved) when vatSys disconnects. When a new log starts, the oldest
    /// logs are deleted so that at most FsdLogMaxFiles (PainRelief.conf, default 10) remain.
    /// FsdLog and FsdLogMaxFiles are read as each connection starts, so a change applies from
    /// the next one.
    ///
    /// The FSD session's RawDataSent and RawDataReceived events are public, but the session
    /// itself (Network.VATSIM) is private and is created as vatSys connects. A short timer looks
    /// for a new session so it is hooked before the login packets go out. The ATIS connection is
    /// a separate session and isn't logged. If a vatSys update renames the members used, one error
    /// is raised and nothing is logged.
    /// </summary>
    internal sealed class FsdLogger : IDisposable
    {
        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

        // Network.Instance (internal static) -> the Network singleton
        private static readonly FieldInfo NetworkInstanceField = typeof(Network).GetField("Instance", NonPublicStatic);
        // Network.VATSIM (private) -> the FSD session
        private static readonly FieldInfo SessionField = typeof(Network).GetField("VATSIM", NonPublicInstance);

        private readonly string folder;
        private readonly string configPath;
        private readonly EventHandler<RawDataEventArgs> sentHandler;
        private readonly EventHandler<RawDataEventArgs> receivedHandler;
        private readonly EventHandler<NetworkEventArgs> sessionDisconnectedHandler;
        private readonly Timer timer;

        private readonly object hookLock = new object();
        private FSDSession hookedSession;
        private bool hookFailureReported;

        private readonly object logLock = new object();
        private StreamWriter writer;
        private string logCallsign;
        private bool dirty;
        // Set when this connection won't be logged (logging off, or the file couldn't be opened),
        // so that isn't re-decided on every packet. Cleared on disconnect.
        private bool skipConnection;
        private bool writeFailureReported;

        public FsdLogger(string folder, string configPath)
        {
            this.folder = folder;
            this.configPath = configPath;

            sentHandler = (sender, e) => Log(sender as FSDSession, true, e?.Data);
            receivedHandler = (sender, e) => Log(sender as FSDSession, false, e?.Data);
            sessionDisconnectedHandler = (sender, e) => CloseLog();

            Network.Connected += (sender, e) => EnsureHooked();
            Network.Disconnected += (sender, e) => CloseLog();
            EnsureHooked();
            timer = new Timer(_ => Tick(), null, PollInterval, PollInterval);
        }

        private void Tick()
        {
            EnsureHooked();
            lock (logLock)
            {
                if (writer == null || !dirty)
                    return;
                try
                {
                    writer.Flush();
                    dirty = false;
                }
                catch (Exception ex)
                {
                    ReportWriteFailure(ex);
                }
            }
        }

        /// <summary>Subscribes to the current FSD session if it's one we haven't seen yet.</summary>
        public void EnsureHooked()
        {
            try
            {
                if (NetworkInstanceField == null || SessionField == null)
                    throw new MissingFieldException("This version of vatSys has changed internally.");

                Network network = NetworkInstanceField.GetValue(null) as Network;
                FSDSession session = network == null ? null : SessionField.GetValue(network) as FSDSession;
                if (session == null || ReferenceEquals(session, hookedSession))
                    return;

                lock (hookLock)
                {
                    if (ReferenceEquals(session, hookedSession))
                        return;

                    if (hookedSession != null)
                    {
                        hookedSession.RawDataSent -= sentHandler;
                        hookedSession.RawDataReceived -= receivedHandler;
                        hookedSession.NetworkDisconnected -= sessionDisconnectedHandler;
                    }
                    session.RawDataSent -= sentHandler;
                    session.RawDataSent += sentHandler;
                    session.RawDataReceived -= receivedHandler;
                    session.RawDataReceived += receivedHandler;
                    session.NetworkDisconnected -= sessionDisconnectedHandler;
                    session.NetworkDisconnected += sessionDisconnectedHandler;
                    hookedSession = session;
                }
            }
            catch (Exception ex)
            {
                if (hookFailureReported)
                    return;
                hookFailureReported = true;
                Errors.Add(new Exception("FSD logging is not working: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
            }
        }

        private void Log(FSDSession session, bool sent, string data)
        {
            DateTime now = DateTime.UtcNow;
            lock (logLock)
            {
                try
                {
                    if (writer == null)
                    {
                        // Only a live connection starts a log, so a straggling packet after a
                        // disconnect can't create a file (and push a real log out).
                        if (skipConnection || session == null || !session.Connected)
                            return;
                        skipConnection = true; // until StartLog succeeds
                        if (!StartLog(now))
                            return;
                        skipConnection = false;
                    }

                    foreach (string packet in FsdLogFormat.Packets(data))
                        writer.Write(FsdLogFormat.Entry(now, sent, logCallsign, packet));
                    dirty = true;
                }
                catch (Exception ex)
                {
                    ReportWriteFailure(ex);
                }
            }
        }

        /// <summary>Opens a new log file, after deleting old ones. False if logging is off.</summary>
        private bool StartLog(DateTime startUtc)
        {
            PainReliefConfig config = PainReliefConfig.Load(configPath);
            int maxFiles = config.FsdLogMaxFiles;
            if (!config.FsdLog || maxFiles <= 0)
                return false;

            logCallsign = Network.Callsign;
            if (string.IsNullOrWhiteSpace(logCallsign))
                logCallsign = "UNKNOWN";
            string path = Path.Combine(folder, FsdLogFormat.FileName(logCallsign, startUtc));

            // Make room for the new log: keep maxFiles - 1 of the existing ones.
            try
            {
                string[] existing = Directory.GetFiles(folder, "*.txt").Select(Path.GetFileName).ToArray();
                foreach (string name in FsdLogFormat.FilesToDelete(existing, maxFiles - 1))
                {
                    try
                    {
                        File.Delete(Path.Combine(folder, name));
                    }
                    catch
                    {
                        // A log that can't be deleted (open elsewhere) is tried again next time.
                    }
                }
            }
            catch
            {
            }

            writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false));
            return true;
        }

        private void CloseLog()
        {
            lock (logLock)
            {
                skipConnection = false;
                if (writer == null)
                    return;
                try
                {
                    writer.Dispose();
                }
                catch (Exception ex)
                {
                    ReportWriteFailure(ex);
                }
                writer = null;
                dirty = false;
            }
        }


        private void ReportWriteFailure(Exception ex)
        {
            if (writeFailureReported)
                return;
            writeFailureReported = true;
            Errors.Add(new Exception("Could not write the FSD log: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
        }

        public void Dispose()
        {
            timer.Dispose();
            CloseLog();
        }
    }
}
