using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// What the network layer reports about itself, and the property the whole
    /// adaptive buffer rests on.
    ///
    /// The property is this: the input delay is not part of the simulation. It only
    /// decides how far ahead each side generates its own commands, so a session whose
    /// two ends disagree about it does not desynchronise - it runs at the pace of the
    /// smaller value. That distinction matters because it is what allows the host to
    /// resize the buffer mid-session at all; if the delay shifted tic numbering, as
    /// the old comment claimed, changing it would split the simulation in two.
    /// </summary>
    [TestClass]
    public class NetStatsTest
    {
        private const int Port = 5420;

        private sealed class Participant
        {
            public NetSession Session;
            public NetDriver Driver;
            public bool Pause;

            /// <summary>Per-tic hash of what was applied, so two ends can be compared.</summary>
            public uint[] Hashes = new uint[4096];
        }

        private static Participant Prepare(NetSession session)
        {
            var driver = new NetDriver(session);
            driver.DeterministicInput = true;

            return new Participant { Session = session, Driver = driver };
        }

        private static bool Step(Participant participant)
        {
            var cmds = new TicCmd[Player.MaxPlayerCount];
            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            var tic = participant.Driver.CurrentTic;

            if (!participant.Driver.TryAdvance(cmds, null, ref participant.Pause))
            {
                return false;
            }

            participant.Driver.Commit();

            var previous = tic > 0 ? participant.Hashes[tic - 1] : 0u;
            var hash = previous * 31u + (uint)tic;

            for (var i = 0; i < participant.Driver.ExpectedPlayers; i++)
            {
                hash = hash * 31u + (byte)cmds[i].ForwardMove;
                hash = hash * 31u + (byte)cmds[i].SideMove;
                hash = hash * 31u + (ushort)cmds[i].AngleTurn;
                hash = hash * 31u + cmds[i].Buttons;
            }

            participant.Hashes[tic] = hash;
            return true;
        }

        private static void WaitForGuest(NetSession host)
        {
            var watch = Stopwatch.StartNew();

            while (host.PlayerCount < 2 && watch.ElapsedMilliseconds < 5000)
            {
                Thread.Sleep(20);
            }

            Assert.AreEqual(2, host.PlayerCount, "the guest joined");
        }

        private static void RunUpTo(Participant host, Participant guest, int target)
        {
            var watch = Stopwatch.StartNew();

            while ((host.Driver.CurrentTic < target || guest.Driver.CurrentTic < target) &&
                   watch.Elapsed < TimeSpan.FromSeconds(60))
            {
                Step(host);
                Step(guest);
                Thread.Sleep(1);
            }

            Assert.IsTrue(host.Driver.CurrentTic >= target && guest.Driver.CurrentTic >= target,
                "both ends reached tic " + target + " (host " + host.Driver.CurrentTic +
                ", guest " + guest.Driver.CurrentTic + ")");
        }

        private static void CheckNoDivergence(Participant host, Participant guest)
        {
            var common = Math.Min(host.Driver.CurrentTic, guest.Driver.CurrentTic);

            Assert.IsTrue(common > 0, "something was simulated");

            for (var tic = 0; tic < common; tic++)
            {
                Assert.AreEqual(host.Hashes[tic], guest.Hashes[tic],
                    "tic " + tic + " applied the same commands on both ends");
            }
        }

        [TestMethod]
        public void BothEndsAgreeOnTheBufferSizeAtJoin()
        {
            // The welcome carries the number, so a guest cannot measure the same link
            // and arrive somewhere else.
            using (var hostSession = NetSession.StartHost(Port, 2, "hostname", new SessionSettings(), 3))
            using (var guestSession = NetSession.Join("127.0.0.1", Port, "guestname", 5000))
            {
                WaitForGuest(hostSession);

                Assert.AreEqual(3, hostSession.Delay, "the host honours its own override");
                Assert.AreEqual(hostSession.Delay, guestSession.Delay,
                    "and the guest starts with the host's number, not its own measurement");
            }
        }

        [TestMethod]
        public void TheHostCanResizeTheBufferMidSession()
        {
            // The guest has to apply it. If it did not, the two ends would keep
            // different buffers and the session would run at the pace of whichever is
            // smaller - a slowdown nobody could see the cause of.
            using (var hostSession = NetSession.StartHost(Port + 1, 2, "hostname", new SessionSettings(), 0))
            using (var guestSession = NetSession.Join("127.0.0.1", Port + 1, "guestname", 5000))
            {
                WaitForGuest(hostSession);

                var guest = Prepare(guestSession);
                var host = Prepare(hostSession);

                RunUpTo(host, guest, 10);

                hostSession.SetDelayForTest(5);

                // The frame travels on the read loop, so give it a tic to arrive.
                var watch = Stopwatch.StartNew();
                while (guestSession.Delay != 5 && watch.ElapsedMilliseconds < 5000)
                {
                    Step(host);
                    Step(guest);
                    Thread.Sleep(5);
                }

                Assert.AreEqual(5, guestSession.Delay, "the guest applied the new buffer size");
            }
        }

        [TestMethod]
        public void ResizingMidSessionDoesNotDesynchronise()
        {
            // The reason the buffer may change at all. Its whole effect is how far
            // ahead commands are generated; the tics themselves are identical either
            // side of the change.
            using (var hostSession = NetSession.StartHost(Port + 2, 2, "hostname", new SessionSettings(), 0))
            using (var guestSession = NetSession.Join("127.0.0.1", Port + 2, "guestname", 5000))
            {
                WaitForGuest(hostSession);

                var host = Prepare(hostSession);
                var guest = Prepare(guestSession);

                RunUpTo(host, guest, 20);

                hostSession.SetDelayForTest(6);

                RunUpTo(host, guest, 60);

                hostSession.SetDelayForTest(0);

                RunUpTo(host, guest, 100);

                CheckNoDivergence(host, guest);
            }
        }

        [TestMethod]
        public void DifferentBuffersStillAgreeOnTheWorld()
        {
            // The documented invariant is that both ends use the same delay. It is
            // true that they must - but for pacing, not for correctness. Adding an
            // unmeasured end would run at the smaller buffer's speed, not drift apart.
            // This pins the mechanism the adaptive buffer depends on, so that a future
            // change that really does couple the delay to the simulation fails here
            // instead of in someone's game.
            using (var hostSession = NetSession.StartHost(Port + 3, 2, "hostname", new SessionSettings(), 6))
            using (var guestSession = NetSession.Join("127.0.0.1", Port + 3, "guestname", 5000))
            {
                WaitForGuest(hostSession);

                var host = Prepare(hostSession);
                var guest = Prepare(guestSession);

                // Drive the guest's buffer down behind the host's back. This is not a
                // supported state - it is a probe of what the state costs.
                guestSession.SetDelayForTest(0);

                Assert.AreEqual(6, hostSession.Delay);
                Assert.AreEqual(0, guestSession.Delay, "the two ends now disagree on purpose");

                RunUpTo(host, guest, 80);

                CheckNoDivergence(host, guest);
            }
        }

        // ------------------------------------------------------------ the report

        [TestMethod]
        public void AShortWaitIsNotCountedAsAStall()
        {
            // The distinction the adaptation depends on. On loopback with no buffer
            // every tic waits a moment for the peer, and the session is perfectly
            // smooth; counting those would raise the buffer to the cap for nothing.
            using (var session = NetSession.StartHost(Port + 4, 2, "hostname", new SessionSettings(), 0))
            {
                session.NoteStall(0);
                session.NoteStall(DelayPolicy.TicMs - 1);

                Assert.AreEqual(0, session.StallCount, "waits inside the tic budget are not stalls");

                session.NoteStall(DelayPolicy.TicMs);

                Assert.AreEqual(1, session.StallCount);
                Assert.AreEqual(DelayPolicy.TicMs, session.StallTotalMs);
            }
        }

        [TestMethod]
        public void TheReportSaysWhatTheSessionDid()
        {
            using (var hostSession = NetSession.StartHost(Port + 5, 2, "hostname", new SessionSettings(), 2))
            using (var guestSession = NetSession.Join("127.0.0.1", Port + 5, "guestname", 5000))
            {
                WaitForGuest(hostSession);

                var host = Prepare(hostSession);
                var guest = Prepare(guestSession);

                RunUpTo(host, guest, 40);

                var text = hostSession.Report();

                StringAssert.Contains(text, "role host", text);
                StringAssert.Contains(text, "player 1 of 2", text);
                StringAssert.Contains(text, "input delay 2 tics = 57 ms", text);
                StringAssert.Contains(text, "reconnects 0", text);

                // Every line the player can compare against the other screen.
                StringAssert.Contains(text, "ping ", text);
                StringAssert.Contains(text, "jitter ", text);
                StringAssert.Contains(text, "stalls ", text);

                Assert.IsTrue(hostSession.TicsSimulated > 0, "the session counted its tics");
                Assert.IsTrue(hostSession.TicsPerSecond > 0, "and the rate it actually ran at");
            }
        }
    }
}
