using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// What happens when a player is gone for good.
    ///
    /// It used to end the session for everybody: one player's flat or their ISP's
    /// maintenance window closed the game for the rest. That is a phone call, not a
    /// co-op game.
    ///
    /// The slot cannot simply be removed, because a world built for three players and
    /// a world built for two are different worlds - the count decides who spawns, who
    /// is counted in the intermission and what the monsters do. So the slot stays and
    /// the host fills it with empty commands: an inert player standing where they
    /// were. Every end applies the same thing, so nothing drifts.
    ///
    /// The test therefore checks the property that matters, not the message: the two
    /// surviving ends must have applied identical commands for every tic, including
    /// the ones where the departed player's slot had to be invented.
    /// </summary>
    [TestClass]
    public class NetLeaveTest
    {
        private const int Port = 5450;

        private sealed class Participant
        {
            public NetSession Session;
            public NetDriver Driver;
            public bool Pause;
            /// <summary>Per-tic hash of what was applied. A list, not an array: a
            /// reconnecting guest can simulate thousands of tics in the seconds these
            /// tests wait, and a fixed size would overflow in the test instead of the
            /// code.
            public readonly List<uint> Hashes = new List<uint>();
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

            participant.Hashes.Add(hash);
            return true;
        }

        private static void WaitFor(NetSession host, int players)
        {
            var watch = Stopwatch.StartNew();

            while (host.PlayerCount < players && watch.ElapsedMilliseconds < 5000)
            {
                Thread.Sleep(20);
            }

            Assert.AreEqual(players, host.PlayerCount, "every guest joined");
        }

        private static void CheckNoDivergence(Participant a, Participant b, string what)
        {
            var common = Math.Min(a.Driver.CurrentTic, b.Driver.CurrentTic);

            Assert.IsTrue(common > 0, "the surviving ends have tics to compare");

            for (var tic = 0; tic < common; tic++)
            {
                Assert.AreEqual(a.Hashes[tic], b.Hashes[tic],
                    what + ": both ends applied the same commands up to tic " + tic);
            }
        }

        [TestMethod]
        public void OnePlayerLeavingDoesNotEndTheGame()
        {
            using (var hostSession = NetSession.StartHost(Port, 3, "hostname", new SessionSettings(), 0))
            using (var guestA = NetSession.Join("127.0.0.1", Port, "guestA", 5000))
            using (var guestB = NetSession.Join("127.0.0.1", Port, "guestB", 5000))
            {
                WaitFor(hostSession, 3);

                // The host gives up on a lost player after a second instead of the
                // thirty a real session waits: the point here is the decision, not the
                // patience.
                hostSession.SetReconnectDialer(null, 1);

                var host = Prepare(hostSession);
                var a = Prepare(guestA);
                var b = Prepare(guestB);

                var dropped = false;
                var dropAt = 40;
                var target = 260;
                var watch = Stopwatch.StartNew();

                while ((host.Driver.CurrentTic < target || a.Driver.CurrentTic < target) &&
                       watch.Elapsed < TimeSpan.FromSeconds(60))
                {
                    Assert.IsFalse(host.Driver.Faulted,
                        "the host kept the session: " + host.Driver.FaultReason);

                    var moved = Step(host);
                    if (Step(a))
                    {
                        moved = true;
                    }

                    if (!dropped && host.Driver.CurrentTic >= dropAt)
                    {
                        dropped = true;

                        // Slot 2 is guestB: slot 0 is the host, slot 1 is guestA.
                        host.Session.DropPeerForTest(2);
                    }

                    // guestB has no dialer of its own, so its side ends. That is the
                    // point: the session must survive a machine that simply went away.
                    Step(b);

                    if (!moved)
                    {
                        Thread.Sleep(1);
                    }
                }

                Assert.IsTrue(dropped, "the link was broken during the run");

                Assert.IsTrue(host.Driver.CurrentTic >= target,
                    "the host kept playing after the player left (reached tic " + host.Driver.CurrentTic + ")");
                Assert.IsTrue(a.Driver.CurrentTic >= target,
                    "the remaining guest kept playing (reached tic " + a.Driver.CurrentTic + ")");

                // The slot is given up on and the session goes on. Without this the
                // rest of the assertions could pass on a session that never noticed.
                Assert.AreEqual(1, host.Session.LeftCount, "one slot was abandoned");
                Assert.AreEqual(2, host.Session.PlayerCount, "two players are still connected");

                CheckNoDivergence(host, a, "after a player left");
            }
        }

        [TestMethod]
        public void TheRemainingGuestIsToldWhoLeft()
        {
            // The world cannot tell: the departed player's slot looks exactly like
            // somebody standing still. Without the notice a player would see a
            // teammate freeze and have no idea why.
            using (var hostSession = NetSession.StartHost(Port + 1, 3, "hostname", new SessionSettings(), 0))
            using (var guestA = NetSession.Join("127.0.0.1", Port + 1, "guestA", 5000))
            using (var guestB = NetSession.Join("127.0.0.1", Port + 1, "guestB", 5000))
            {
                WaitFor(hostSession, 3);

                hostSession.SetReconnectDialer(null, 1);

                var host = Prepare(hostSession);
                var a = Prepare(guestA);
                var b = Prepare(guestB);

                var watch = Stopwatch.StartNew();
                var dropped = false;

                while (watch.Elapsed < TimeSpan.FromSeconds(30) && guestA.LeftCount == 0)
                {
                    Step(host);
                    Step(a);
                    Step(b);

                    if (!dropped && host.Driver.CurrentTic >= 30)
                    {
                        dropped = true;
                        host.Session.DropPeerForTest(2);
                    }

                    Thread.Sleep(1);
                }

                Assert.IsTrue(dropped, "the link was broken during the run");
                Assert.AreEqual(1, guestA.LeftCount, "the surviving guest learned that a player left");
                Assert.IsTrue(guestA.IsAbandoned(2), "and which slot it was");
            }
        }

        [TestMethod]
        public void TheOtherGuestsAreToldTheGameIsHeldAndWhy()
        {
            // On a surviving guest's side nothing is wrong: its link is healthy and no
            // command it could ask for is missing. The tic simply never completes, so
            // without the notice a held session is indistinguishable from a hung one -
            // on screen and to the headless harness, which counted it as a stall and
            // stopped the run.
            //
            // Three players, not two. With two, the guest whose link dies is the one
            // who is gone; the survivor is the one who has no idea what happened.
            using (var hostSession = NetSession.StartHost(Port + 4, 3, "hostname", new SessionSettings(), 0))
            using (var guestA = NetSession.Join("127.0.0.1", Port + 4, "guestA", 5000))
            using (var guestB = NetSession.Join("127.0.0.1", Port + 4, "guestB", 5000))
            {
                WaitFor(hostSession, 3);

                var host = Prepare(hostSession);
                var survivor = Prepare(guestA);
                var doomed = Prepare(guestB);

                var dropped = false;
                var watch = Stopwatch.StartNew();

                while (watch.Elapsed < TimeSpan.FromSeconds(20) && !guestA.Reconnecting)
                {
                    Step(host);
                    Step(survivor);
                    Step(doomed);

                    if (!dropped && host.Driver.CurrentTic >= 20)
                    {
                        dropped = true;
                        hostSession.DropPeerForTest(2);
                    }

                    Thread.Sleep(1);
                }

                Assert.IsTrue(dropped, "the link was broken during the run");
                Assert.IsTrue(guestA.Reconnecting, "the surviving guest knows the game is held");
                Assert.IsTrue(hostSession.Reconnecting, "and so does the host");

                var status = guestA.ReconnectStatus;

                Assert.IsNotNull(status, "the guest has something to put on screen");
                StringAssert.Contains(status, "WAITING FOR", "and it says what for: " + status);
            }
        }

        [TestMethod]
        public void AnAbandonedSlotIsNotWaitingForReconnect()
        {
            // The game is held while a slot is being waited for. If giving up did not
            // clear that wait the session would stop for good, which is the failure
            // this whole mechanism replaced.
            using (var hostSession = NetSession.StartHost(Port + 2, 2, "hostname", new SessionSettings(), 0))
            using (var guest = NetSession.Join("127.0.0.1", Port + 2, "guest", 5000))
            {
                WaitFor(hostSession, 2);

                hostSession.SetReconnectDialer(null, 1);

                var host = Prepare(hostSession);
                var survivor = Prepare(guest);

                var dropped = false;
                var watch = Stopwatch.StartNew();

                while (watch.Elapsed < TimeSpan.FromSeconds(30) && host.Driver.CurrentTic < 120)
                {
                    Step(host);
                    Step(survivor);

                    if (!dropped && host.Driver.CurrentTic >= 20)
                    {
                        dropped = true;
                        hostSession.DropPeerForTest(1);
                    }

                    Thread.Sleep(1);
                }

                Assert.IsTrue(dropped, "the link was broken during the run");
                Assert.IsTrue(host.Driver.CurrentTic >= 120,
                    "the host carried on alone (reached tic " + host.Driver.CurrentTic + ")");
                Assert.IsFalse(host.Driver.Reconnecting, "nothing is being waited for any more");
            }
        }

        [TestMethod]
        public void AGuestReturningAfterBeingGivenUpOnIsToldWhy()
        {
            // The slot was given up on and the game moved on without it. The returning
            // machine is by then further behind than the replay window, and catching up
            // would need a world snapshot, which lockstep does not have.
            //
            // So it is refused - with a reason. The failure this replaces is worse than
            // a refusal: a guest that reconnects into a slot nobody is waiting for, then
            // waits for commands that will never be replayed, and hangs without a word.
            using (var hostSession = NetSession.StartHost(Port + 3, 2, "hostname", new SessionSettings(), 0))
            using (var guest = NetSession.Join("127.0.0.1", Port + 3, "guest", 5000))
            {
                WaitFor(hostSession, 2);

                hostSession.SetReconnectDialer(null, 1);

                // This guest keeps trying for longer than the host's patience, so the
                // slot really is given up on before the redial succeeds. The first
                // attempt fails on purpose: the reconnect loop waits 1.2 s between
                // attempts, which puts the successful one past the host's one second.
                var attempts = 0;

                guest.SetReconnectDialer(
                    () =>
                    {
                        if (Interlocked.Increment(ref attempts) == 1)
                        {
                            throw new Exception("not yet");
                        }

                        System.Net.Sockets.TcpClient unused = null;
                        return NetSession.DialDirect("127.0.0.1", Port + 3, 3000, out unused);
                    },
                    15);

                var host = Prepare(hostSession);
                var survivor = Prepare(guest);

                var dropped = false;
                var watch = Stopwatch.StartNew();

                while (watch.Elapsed < TimeSpan.FromSeconds(30) && hostSession.LeftCount == 0)
                {
                    Step(host);
                    Step(survivor);

                    if (!dropped && host.Driver.CurrentTic >= 10)
                    {
                        dropped = true;
                        hostSession.DropPeerForTest(1);
                    }

                    Thread.Sleep(1);
                }

                Assert.IsTrue(dropped, "the link was broken during the run");
                Assert.AreEqual(1, hostSession.LeftCount, "the slot was given up on");

                // The guest redials, is told the slot is gone, and ends. No hang.
                var ended = Stopwatch.StartNew();

                while (ended.Elapsed < TimeSpan.FromSeconds(20) && !survivor.Driver.Faulted)
                {
                    Step(host);
                    Step(survivor);
                    Thread.Sleep(5);
                }

                Assert.IsTrue(survivor.Driver.Faulted, "the returning guest was told and stopped");
                StringAssert.Contains(survivor.Driver.FaultReason, "given up on",
                    "and the reason says what happened: " + survivor.Driver.FaultReason);

                // And the host is still playing, which is the whole point.
                var run = Stopwatch.StartNew();

                while (run.Elapsed < TimeSpan.FromSeconds(30) && host.Driver.CurrentTic < 140)
                {
                    Step(host);
                    Thread.Sleep(1);
                }

                Assert.IsTrue(host.Driver.CurrentTic >= 140,
                    "the host carried on to tic 140 (reached " + host.Driver.CurrentTic + ")");
            }
        }
    }
}
