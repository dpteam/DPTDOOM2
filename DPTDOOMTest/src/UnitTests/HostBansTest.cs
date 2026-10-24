using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Refusing a banned guest, and remembering who came and went.
    ///
    /// Both halves are checked through a real loopback session rather than by calling
    /// the matcher: the rule that matters is not "the matcher works" but "a banned
    /// address never gets a slot", and there is a whole handshake between those two
    /// statements. The log is checked the same way, because the interesting part is
    /// whether the events are written at all - a log with the right format and no lines
    /// looks perfect and is useless.
    /// </summary>
    [TestClass]
    public class HostBansTest
    {
        private const int Port = 5490;

        private static string TempPath(string suffix)
        {
            return Path.Combine(
                Path.GetTempPath(), "dptdoom-test-" + Guid.NewGuid().ToString("N") + suffix);
        }

        private static void Delete(string path)
        {
            try
            {
                if (path != null && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }

        private static void WaitForGuest(NetSession host)
        {
            var watch = Stopwatch.StartNew();

            while (host.PlayerCount < 2 && watch.ElapsedMilliseconds < 5000)
            {
                Thread.Sleep(20);
            }
        }

        private static void Step(NetDriver driver, ref bool pause)
        {
            var cmds = new TicCmd[Player.MaxPlayerCount];

            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            if (driver.TryAdvance(cmds, null, ref pause))
            {
                driver.Commit();
            }
        }

        [TestMethod]
        public void ABannedAddressNeverGetsASlot()
        {
            var banPath = TempPath(".txt");

            try
            {
                // 127.0.0.1 is where the test's own guest comes from, which is exactly
                // the point: the ban has to be checked before the slot, not after.
                File.WriteAllLines(banPath, new[] { "127.0.0.1/32" });

                var warnings = new List<string>();
                var bans = BanList.Load(banPath, warnings);
                Assert.AreEqual(0, warnings.Count);

                using (var host = NetSession.StartHost(
                    Port, 2, "hostname", new SessionSettings(), 0,
                    new HostGuard { Bans = bans }))
                {
                    try
                    {
                        using (var guest = NetSession.Join("127.0.0.1", Port, "guestname", 5000))
                        {
                            Assert.Fail("a banned address joined anyway");
                        }
                    }
                    catch (NetRefusalException refusal)
                    {
                        StringAssert.Contains(refusal.Message, "banned", refusal.Message);
                    }

                    Assert.AreEqual(1, host.PlayerCount, "the slot was never taken");
                }
            }
            finally
            {
                Delete(banPath);
            }
        }

        [TestMethod]
        public void AnUnbannedAddressGetsInWhileAnotherIsBanned()
        {
            // The other half of the check: a ban list that refuses everybody would pass
            // the test above and be useless.
            var banPath = TempPath(".txt");

            try
            {
                File.WriteAllLines(banPath, new[] { "203.0.113.0/24" });

                var warnings = new List<string>();
                var bans = BanList.Load(banPath, warnings);

                using (var host = NetSession.StartHost(
                    Port + 1, 2, "hostname", new SessionSettings(), 0,
                    new HostGuard { Bans = bans }))
                using (var guest = NetSession.Join("127.0.0.1", Port + 1, "guestname", 5000))
                {
                    WaitForGuest(host);
                    Assert.AreEqual(2, host.PlayerCount, "an address that is not banned still plays");
                }
            }
            finally
            {
                Delete(banPath);
            }
        }

        [TestMethod]
        public void ABannedNicknameNeverGetsASlot()
        {
            // A nickname can only be checked after hello, since that is the first moment
            // it exists. The test is that the slot is still not issued.
            var banPath = TempPath(".txt");

            try
            {
                File.WriteAllLines(banPath, new[] { "name unwanted*" });

                var warnings = new List<string>();
                var bans = BanList.Load(banPath, warnings);

                using (var host = NetSession.StartHost(
                    Port + 2, 2, "hostname", new SessionSettings(), 0,
                    new HostGuard { Bans = bans }))
                {
                    try
                    {
                        using (var guest = NetSession.Join("127.0.0.1", Port + 2, "unwanted_guest", 5000))
                        {
                            Assert.Fail("a banned nickname joined anyway");
                        }
                    }
                    catch (NetRefusalException refusal)
                    {
                        StringAssert.Contains(refusal.Message, "banned", refusal.Message);
                    }

                    Assert.AreEqual(1, host.PlayerCount, "the slot was never taken");
                }
            }
            finally
            {
                Delete(banPath);
            }
        }

        [TestMethod]
        public void TheLogSaysWhoJoinedAndWhoLeft()
        {
            var banPath = TempPath(".txt");
            var logPath = TempPath(".log");

            try
            {
                File.WriteAllLines(banPath, new[] { "203.0.113.0/24" });

                var warnings = new List<string>();
                var bans = BanList.Load(banPath, warnings);
                var log = new ConnectionLog(logPath);

                using (var hostSession = NetSession.StartHost(
                    Port + 3, 2, "hostname", new SessionSettings(), 0,
                    new HostGuard { Bans = bans, Log = log }))
                {
                    using (var guestSession = NetSession.Join("127.0.0.1", Port + 3, "recorded_guest", 5000))
                    {
                        WaitForGuest(hostSession);

                        var host = new NetDriver(hostSession);
                        var guest = new NetDriver(guestSession);

                        host.DeterministicInput = true;
                        guest.DeterministicInput = true;

                        var hostPause = false;
                        var guestPause = false;
                        var watch = Stopwatch.StartNew();

                        while (watch.Elapsed < TimeSpan.FromSeconds(20) && host.CurrentTic < 40)
                        {
                            Step(host, ref hostPause);
                            Step(guest, ref guestPause);
                            Thread.Sleep(1);
                        }

                        // The guest goes away for good, and the host gives up on the slot
                        // with a one-second grace period rather than thirty.
                        hostSession.SetReconnectDialer(null, 1);
                        hostSession.DropPeerForTest(1);

                        watch = Stopwatch.StartNew();

                        while (watch.Elapsed < TimeSpan.FromSeconds(20) && hostSession.LeftCount == 0)
                        {
                            Step(host, ref hostPause);
                            Thread.Sleep(5);
                        }

                        Assert.AreEqual(1, hostSession.LeftCount, "the slot was given up on");
                    }
                }

                var lines = File.ReadAllLines(logPath);
                var text = string.Join("\n", lines);

                StringAssert.Contains(text, "join name=\"recorded_guest\"", text);
                StringAssert.Contains(text, "address=127.0.0.1", text);
                StringAssert.Contains(text, "leave name=\"recorded_guest\"", text);
                StringAssert.Contains(text, "did not come back", text);

                // The line has to be readable on its own: a log where "leave" does not
                // say which player or why is a log that answers nothing.
                foreach (var line in lines)
                {
                    StringAssert.Contains(line, " name=", line);
                    StringAssert.Contains(line, " address=", line);
                }
            }
            finally
            {
                Delete(banPath);
                Delete(logPath);
            }
        }

        [TestMethod]
        public void ARefusalIsLoggedWithItsReason()
        {
            var banPath = TempPath(".txt");
            var logPath = TempPath(".log");

            try
            {
                File.WriteAllLines(banPath, new[] { "name unwanted" });

                var warnings = new List<string>();
                var bans = BanList.Load(banPath, warnings);

                using (var host = NetSession.StartHost(
                    Port + 4, 2, "hostname", new SessionSettings(), 0,
                    new HostGuard { Bans = bans, Log = new ConnectionLog(logPath) }))
                {
                    try
                    {
                        using (var guest = NetSession.Join("127.0.0.1", Port + 4, "unwanted", 5000))
                        {
                            Assert.Fail("the banned guest joined");
                        }
                    }
                    catch (NetRefusalException)
                    {
                    }
                }

                var text = File.ReadAllText(logPath);

                StringAssert.Contains(text, "refuse name=\"unwanted\"", text);
                StringAssert.Contains(text, "reason=banned", text);

                // The rule that did it, not just the fact: otherwise the host is left
                // hunting through the file for which line is responsible.
                StringAssert.Contains(text, "name unwanted", text);
            }
            finally
            {
                Delete(banPath);
                Delete(logPath);
            }
        }

        [TestMethod]
        public void TheLogNeverContainsThePassword()
        {
            // A password in a log file is a password in every backup of that file. The
            // guess must not be echoed either: the same log is often what gets pasted
            // into a bug report.
            var logPath = TempPath(".log");
            const string secret = "correct-horse";
            const string guess = "guess-abc-123";

            try
            {
                using (var host = NetSession.StartHost(
                    Port + 5, 2, "hostname", new SessionSettings(), 0,
                    new HostGuard { Password = secret, Log = new ConnectionLog(logPath) }))
                {
                    try
                    {
                        using (var guest = NetSession.Join("127.0.0.1", Port + 5, "guesser", 5000, guess))
                        {
                            Assert.Fail("the wrong password was accepted");
                        }
                    }
                    catch (NetRefusalException)
                    {
                    }
                }

                var text = File.Exists(logPath) ? File.ReadAllText(logPath) : "";

                Assert.IsFalse(text.Contains(secret), "the log leaked the password:\n" + text);
                Assert.IsFalse(text.Contains(guess), "and echoed the guess:\n" + text);
                StringAssert.Contains(text, "wrong password", text);
            }
            finally
            {
                Delete(logPath);
            }
        }

        [TestMethod]
        public void ANicknameCannotForgeALogLine()
        {
            // Nicks are player-supplied text that ends up in a file somebody reads. An
            // unescaped newline forges an extra line, and an unescaped quote makes the
            // rest look like another field.
            var logPath = TempPath(".log");

            try
            {
                using (var log = new ConnectionLog(logPath))
                {
                    log.Join("evil\njoin name=\"victim\"", IPAddress.Parse("127.0.0.1"), 1);
                }

                var lines = File.ReadAllLines(logPath);

                Assert.AreEqual(1, lines.Length, "one event, one line");
                Assert.IsFalse(lines[0].Contains("\n"), "no newline inside the line");
                StringAssert.Contains(lines[0], "\\n", "the newline is visible as an escape");
            }
            finally
            {
                Delete(logPath);
            }
        }

        [TestMethod]
        public void TheLogRotatesRatherThanGrowingForever()
        {
            // A host that runs for weeks with an open game accumulates lines, and a log
            // that has grown to a hundred megabytes is one nobody opens.
            var logPath = TempPath(".log");
            var previous = logPath + ".1";

            try
            {
                var filler = new string('x', 1024);

                using (var log = new ConnectionLog(logPath))
                {
                    for (var i = 0; i < 1100; i++)
                    {
                        log.Join(filler, IPAddress.Parse("127.0.0.1"), 1);
                    }
                }

                Assert.IsTrue(new FileInfo(logPath).Length > 1024 * 1024, "the file grew past the limit");

                using (var log = new ConnectionLog(logPath))
                {
                    log.Join("after-rotation", IPAddress.Parse("127.0.0.1"), 1);
                }

                Assert.IsTrue(File.Exists(previous), "the oversized file was kept as .1");
                Assert.IsFalse(File.ReadAllText(logPath).Contains(filler), "the new file starts empty");
                StringAssert.Contains(File.ReadAllText(logPath), "after-rotation");
            }
            finally
            {
                Delete(logPath);
                Delete(previous);
            }
        }
    }
}
