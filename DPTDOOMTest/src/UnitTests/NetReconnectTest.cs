using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Reconnection after the link drops mid-game.
    ///
    /// This exists because the property it checks is the whole reason the feature
    /// is possible at all: in lockstep both ends stall at the same tic when the
    /// connection dies, so nothing has to be synchronised - only the commands in
    /// between have to be re-sent. If that reasoning were wrong, the session would
    /// resume and then quietly desynchronise, which is worse than not resuming.
    ///
    /// So the test does not settle for "the session came back". It runs both ends
    /// with deterministic input, hashes every command it applies, and compares the
    /// two hashes after the drop. A single missing or duplicated command anywhere
    /// in the session changes the hash.
    /// </summary>
    [TestClass]
    public class NetReconnectTest
    {
        private const int Port = 5390;

        private sealed class Participant
        {
            public NetSession Session;
            public NetDriver Driver;
            public bool Pause;

            /// <summary>
            /// A running hash of everything applied so far, one entry per tic.
            ///
            /// Per tic rather than one total, because the two ends do not stop at
            /// the same moment - one of them always applies the last tic first -
            /// and comparing totals would report that harmless difference as a
            /// divergence. Prefixes can be compared; totals cannot.
            /// </summary>
            public uint[] Hashes = new uint[4096];
        }

        private static Participant Prepare(NetSession session)
        {
            var driver = new NetDriver(session);
            driver.DeterministicInput = true;

            return new Participant { Session = session, Driver = driver };
        }

        /// <summary>
        /// Applies one tic if it can. Returns false when the driver is waiting,
        /// which is normal while reconnecting.
        /// </summary>
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
            participant.Hashes[tic] = Mix(previous, tic, cmds, participant.Driver.ExpectedPlayers);
            return true;
        }

        private static uint Mix(uint hash, int tic, TicCmd[] cmds, int players)
        {
            hash = hash * 31u + (uint)tic;

            for (var i = 0; i < players; i++)
            {
                hash = hash * 31u + (byte)cmds[i].ForwardMove;
                hash = hash * 31u + (byte)cmds[i].SideMove;
                hash = hash * 31u + (ushort)cmds[i].AngleTurn;
                hash = hash * 31u + cmds[i].Buttons;
            }

            return hash;
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

        /// <summary>
        /// Runs both ends, breaks the link at <paramref name="dropAt"/>, and
        /// returns once both have passed <paramref name="target"/>.
        /// </summary>
        private static void RunThroughDrop(Participant host, Participant guest, int dropAt, int target)
        {
            var dropped = false;
            var hostDrops = host.Session.ReconnectCount;
            var guestDrops = guest.Session.ReconnectCount;
            var watch = Stopwatch.StartNew();

            while ((host.Driver.CurrentTic < target || guest.Driver.CurrentTic < target) &&
                   watch.Elapsed < TimeSpan.FromSeconds(60))
            {
                Assert.IsFalse(host.Driver.Faulted, "the host did not fault: " + host.Driver.FaultReason);
                Assert.IsFalse(guest.Driver.Faulted, "the guest did not fault: " + guest.Driver.FaultReason);

                var moved = Step(host);
                if (Step(guest))
                {
                    moved = true;
                }

                if (!dropped && host.Driver.CurrentTic >= dropAt)
                {
                    dropped = true;

                    // The guest's slot is 1: slot 0 is the host.
                    host.Session.DropPeerForTest(1);
                }

                if (!moved)
                {
                    Thread.Sleep(1);
                }
            }

            Assert.IsTrue(dropped, "the link was broken during the run");

            // Without this the test would happily pass on a session that never
            // noticed the drop at all.
            //
            // Counted rather than observed through the Reconnecting flag: on
            // loopback the whole reconnect finishes in a millisecond or two, so a
            // test polling the flag loses the race most of the time. That is how
            // this assertion failed in CI while the log showed a clean reconnect.
            Assert.IsTrue(host.Session.ReconnectCount > hostDrops,
                "the host handled the drop as a reconnect (" + hostDrops + " -> " + host.Session.ReconnectCount + ")");

            Assert.IsTrue(guest.Session.ReconnectCount > guestDrops,
                "the guest handled the drop as a reconnect (" + guestDrops + " -> " + guest.Session.ReconnectCount + ")");

            Assert.IsTrue(host.Driver.CurrentTic >= target,
                "the host kept playing after the link came back (reached tic " + host.Driver.CurrentTic + ")");
            Assert.IsTrue(guest.Driver.CurrentTic >= target,
                "the guest kept playing after the link came back (reached tic " + guest.Driver.CurrentTic + ")");

            Assert.IsFalse(host.Driver.Reconnecting, "the host is no longer waiting");
            Assert.IsFalse(guest.Driver.Reconnecting, "the guest is no longer reconnecting");
        }

        private static void CheckNoDivergence(Participant host, Participant guest)
        {
            // The commands applied at tic N are the same on both ends or they are
            // not: nothing about the network can legitimately change them.
            var common = Math.Min(host.Driver.CurrentTic, guest.Driver.CurrentTic);

            Assert.IsTrue(common > 0, "the two ends have tics to compare");

            for (var tic = 0; tic < common; tic++)
            {
                Assert.AreEqual(host.Hashes[tic], guest.Hashes[tic],
                    "both ends applied the same commands up to tic " + tic + " across the drop " +
                    "(host reached tic " + host.Driver.CurrentTic +
                    ", guest " + guest.Driver.CurrentTic + ")");
            }
        }

        [TestMethod]
        public void SessionSurvivesADroppedLink()
        {
            using (var hostSession = NetSession.StartHost(Port, 2, "hostname", new SessionSettings(), 0))
            {
                using (var guestSession = NetSession.Join("127.0.0.1", Port, "guestname", 5000))
                {
                    WaitForGuest(hostSession);

                    guestSession.SetReconnectDialer(
                        () =>
                        {
                            System.Net.Sockets.TcpClient unused = null;
                            return NetSession.DialDirect("127.0.0.1", Port, 3000, out unused);
                        },
                        20);

                    var host = Prepare(hostSession);
                    var guest = Prepare(guestSession);

                    RunThroughDrop(host, guest, 60, 220);
                    CheckNoDivergence(host, guest);
                }
            }
        }

        [TestMethod]
        public void SessionSurvivesADropWithInputAheadInFlight()
        {
            // With a real input delay both ends keep a few generated commands in
            // hand, so at the moment of the drop the two sides are not at the same
            // tic and one of them has sent commands the other has not seen. That is
            // the case the replay buffer exists for, and the case the no-delay test
            // cannot reach.
            using (var hostSession = NetSession.StartHost(Port + 1, 2, "hostname", new SessionSettings(), 3))
            {
                using (var guestSession = NetSession.Join("127.0.0.1", Port + 1, "guestname", 5000))
                {
                    WaitForGuest(hostSession);

                    guestSession.SetReconnectDialer(
                        () =>
                        {
                            System.Net.Sockets.TcpClient unused = null;
                            return NetSession.DialDirect("127.0.0.1", Port + 1, 3000, out unused);
                        },
                        20);

                    var host = Prepare(hostSession);
                    var guest = Prepare(guestSession);

                    RunThroughDrop(host, guest, 60, 220);
                    CheckNoDivergence(host, guest);
                }
            }
        }

        [TestMethod]
        public void TheSlotIsKeptAcrossTheDrop()
        {
            // Re-issuing the slot would give the returning guest somebody else's
            // commands and inventory, and both ends would already disagree about
            // who player 2 is.
            using (var hostSession = NetSession.StartHost(Port + 2, 2, "hostname", new SessionSettings(), 0))
            {
                using (var guestSession = NetSession.Join("127.0.0.1", Port + 2, "guestname", 5000))
                {
                    WaitForGuest(hostSession);

                    var wasPlayer = guestSession.LocalPlayer;

                    guestSession.SetReconnectDialer(
                        () =>
                        {
                            System.Net.Sockets.TcpClient unused = null;
                            return NetSession.DialDirect("127.0.0.1", Port + 2, 3000, out unused);
                        },
                        20);

                    var host = Prepare(hostSession);
                    var guest = Prepare(guestSession);

                    RunThroughDrop(host, guest, 40, 120);

                    Assert.AreEqual(wasPlayer, guestSession.LocalPlayer,
                        "the guest came back on the same slot");

                    // And the host still knows who that is.
                    Assert.AreEqual(2, hostSession.PlayerCount, "the host still counts two players");
                    Assert.AreEqual(2, hostSession.Names.Length, "no duplicate player was added");
                }
            }
        }

        [TestMethod]
        public void WithoutADialerTheSessionStillEnds()
        {
            // Not every session can come back: a host has nothing to dial, and an
            // older build has no token. Those must behave exactly as they did
            // before, by ending with a reason rather than hanging forever.
            using (var hostSession = NetSession.StartHost(Port + 3, 2, "hostname", new SessionSettings(), 0))
            {
                using (var guestSession = NetSession.Join("127.0.0.1", Port + 3, "guestname", 5000))
                {
                    WaitForGuest(hostSession);

                    var host = Prepare(hostSession);
                    var guest = Prepare(guestSession);

                    var watch = Stopwatch.StartNew();

                    while (watch.Elapsed < TimeSpan.FromSeconds(20) && !guest.Driver.Faulted)
                    {
                        Step(host);
                        Step(guest);

                        if (watch.ElapsedMilliseconds > 100)
                        {
                            hostSession.DropPeerForTest(1);
                        }

                        Thread.Sleep(1);
                    }

                    Assert.IsTrue(guest.Driver.Faulted, "the guest gave up instead of waiting forever");
                    Assert.IsFalse(guest.Driver.Reconnecting, "and it is not still trying");
                }
            }
        }
    }
}
