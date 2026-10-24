using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The session password: the comparison, and what actually happens to a guest who
    /// gets it wrong.
    ///
    /// The comparison is checked here rather than through a session because the
    /// property that matters cannot be observed from outside: a comparison that stops
    /// at the first wrong character and one that does not produce the same yes or no,
    /// and only differ in how long they take. Since the password travels in the clear
    /// anyway, this is a hardening measure rather than the feature - so the test pins
    /// the behaviour that is checkable (the plain answers, the boundaries) and the
    /// constant-time part is a matter of the code shape, not of a measurement that
    /// would be flaky on a loaded machine.
    /// </summary>
    [TestClass]
    public class ServerPasswordTest
    {
        // ------------------------------------------------------------ the answer

        [TestMethod]
        public void AnOpenSessionAcceptsAnything()
        {
            // The default, and it has to stay cheap: a public game must not care about
            // any of this.
            Assert.IsTrue(ServerPassword.Matches(null, null));
            Assert.IsTrue(ServerPassword.Matches("", ""));
            Assert.IsTrue(ServerPassword.Matches("", "anything"));
        }

        [TestMethod]
        public void TheRightPasswordGetsIn()
        {
            Assert.IsTrue(ServerPassword.Matches("hunter2", "hunter2"));
        }

        [TestMethod]
        public void TheWrongPasswordDoesNot()
        {
            Assert.IsFalse(ServerPassword.Matches("hunter2", "hunter3"));
            Assert.IsFalse(ServerPassword.Matches("hunter2", "hunter"));
            Assert.IsFalse(ServerPassword.Matches("hunter2", "hunter22"));
            Assert.IsFalse(ServerPassword.Matches("hunter2", "Hunter2"), "and it is case sensitive");
        }

        [TestMethod]
        public void ASilentGuestIsNotAGuest()
        {
            // A build that predates the password sends nothing at all. It must not slip
            // into a protected session just because it had nothing to compare.
            Assert.IsFalse(ServerPassword.Matches("hunter2", null));
            Assert.IsFalse(ServerPassword.Matches("hunter2", ""));
        }

        [TestMethod]
        public void ACommonPrefixIsNotEnough()
        {
            // The shape of a timing attack: the attacker learns one character per probe.
            // Whatever the timing does, the answer must not become "yes" for a prefix.
            var target = "correct-horse-battery-staple";

            for (var length = 1; length < target.Length; length++)
            {
                Assert.IsFalse(ServerPassword.Matches(target, target.Substring(0, length)),
                    "a prefix of length " + length + " must not pass");
            }
        }

        [TestMethod]
        public void SpacesAndSymbolsCount()
        {
            Assert.IsTrue(ServerPassword.Matches("a b", "a b"));
            Assert.IsFalse(ServerPassword.Matches("a b", "ab"), "a space is a character");
            Assert.IsTrue(ServerPassword.Matches("пароль", "пароль"), "and so is the whole alphabet");
        }

        // ------------------------------------------------------------ validation

        [TestMethod]
        public void AnEmptyPasswordMeansNoPassword()
        {
            string error;

            Assert.IsTrue(ServerPassword.TryValidate(null, out error));
            Assert.IsTrue(ServerPassword.TryValidate("", out error));
            Assert.IsNull(error);
        }

        [TestMethod]
        public void AnOverlongPasswordIsRefusedRatherThanCut()
        {
            // Silently truncated at the wire limit means the host sets one password and
            // hands out another, and can never work out why nobody gets in.
            string error;
            var long_ = new string('x', ServerPassword.MaxLength + 1);

            Assert.IsFalse(ServerPassword.TryValidate(long_, out error));
            Assert.IsNotNull(error);
            StringAssert.Contains(error, ServerPassword.MaxLength.ToString());
        }

        [TestMethod]
        public void TheLongestAllowedPasswordIsAccepted()
        {
            string error;

            Assert.IsTrue(ServerPassword.TryValidate(new string('x', ServerPassword.MaxLength), out error));
        }

        [TestMethod]
        public void TheLimitIsInBytesAndSaysSo()
        {
            // A password within the character count but over the byte count would be
            // clipped on the wire, so the host would set one thing and hand out another.
            // Refused at startup, where the host can see it.
            string error;
            var cyrillic = new string('ж', ServerPassword.MaxLength);

            Assert.IsFalse(ServerPassword.TryValidate(cyrillic, out error));
            Assert.IsNotNull(error);
            StringAssert.Contains(error, "bytes", error);
        }

        [TestMethod]
        public void ACyrillicPasswordWellWithinTheLimitIsFine()
        {
            // The limit must not make non-Latin passwords impossible: half the characters
            // still fit, and a passphrase in Russian is the point.
            string error;

            Assert.IsTrue(ServerPassword.TryValidate("наши-в-субботу", out error), "a real passphrase fits");
        }

        [TestMethod]
        public void ApasswordOfOnlySpacesIsAQuotingMistake()
        {
            // Almost always an unquoted argument that got split. Accepting it produces a
            // session the host cannot get into and cannot explain.
            string error;

            Assert.IsFalse(ServerPassword.TryValidate("   ", out error));
            Assert.IsNotNull(error);
        }

        [TestMethod]
        public void ALeadingSpaceIsFine()
        {
            // Only an all-blank password is a mistake; " x" is a password somebody
            // meant, and trimming it silently would change what they typed.
            string error;

            Assert.IsTrue(ServerPassword.TryValidate(" x", out error));
        }

        // ------------------------------------------------------------ over the wire

        private const int Port = 5470;

        private static void WaitForGuest(NetSession host)
        {
            var watch = Stopwatch.StartNew();

            while (host.PlayerCount < 2 && watch.ElapsedMilliseconds < 5000)
            {
                Thread.Sleep(20);
            }
        }

        [TestMethod]
        public void AGuestWithoutThePasswordIsRefused()
        {
            using (var host = NetSession.StartHost(Port, 2, "hostname", new SessionSettings(), 0, "hunter2"))
            {
                try
                {
                    using (var guest = NetSession.Join("127.0.0.1", Port, "guestname", 5000))
                    {
                        Assert.Fail("the guest joined a protected session with no password");
                    }
                }
                catch (NetRefusalException refusal)
                {
                    // A refusal, not a failure: there is nothing to retry, and the
                    // reason is the only part a player can act on.
                    StringAssert.Contains(refusal.Message, "needs a password", refusal.Message);
                }

                Assert.AreEqual(1, host.PlayerCount, "nobody took the slot");
            }
        }

        [TestMethod]
        public void AGuessIsRefusedAndSaysSo()
        {
            using (var host = NetSession.StartHost(Port + 1, 2, "hostname", new SessionSettings(), 0, "hunter2"))
            {
                try
                {
                    using (var guest = NetSession.Join("127.0.0.1", Port + 1, "guestname", 5000, "letmein"))
                    {
                        Assert.Fail("the guest joined with the wrong password");
                    }
                }
                catch (NetRefusalException refusal)
                {
                    StringAssert.Contains(refusal.Message, "wrong password", refusal.Message);

                    // The refusal must not echo the real password back to the guesser.
                    Assert.IsFalse(refusal.Message.Contains("hunter2"),
                        "the refusal leaked the password: " + refusal.Message);
                }
            }
        }

        [TestMethod]
        public void TheRightPasswordPlaysTheGame()
        {
            using (var hostSession = NetSession.StartHost(Port + 2, 2, "hostname", new SessionSettings(), 0, "hunter2"))
            using (var guestSession = NetSession.Join("127.0.0.1", Port + 2, "guestname", 5000, "hunter2"))
            {
                WaitForGuest(hostSession);
                Assert.AreEqual(2, hostSession.PlayerCount, "the guest got in");

                var host = new NetDriver(hostSession);
                var guest = new NetDriver(guestSession);

                host.DeterministicInput = true;
                guest.DeterministicInput = true;

                var hostPause = false;
                var guestPause = false;
                var watch = Stopwatch.StartNew();

                while (watch.Elapsed < TimeSpan.FromSeconds(30) &&
                       (host.CurrentTic < 60 || guest.CurrentTic < 60))
                {
                    Step(host, ref hostPause);
                    Step(guest, ref guestPause);
                    Thread.Sleep(1);
                }

                // The password gates entry and nothing else: once through, it is an
                // ordinary session.
                Assert.IsTrue(host.CurrentTic >= 60, "the host played (tic " + host.CurrentTic + ")");
                Assert.IsTrue(guest.CurrentTic >= 60, "the guest played (tic " + guest.CurrentTic + ")");
            }
        }

        [TestMethod]
        public void ThePasswordIsInNoReportOrSetting()
        {
            // A password that shows up in a diagnostic, a log or the server listing
            // stops being a password. The settings object is the dangerous one: it is
            // serialised into the welcome and published to the master.
            const string secret = "correct-horse-battery";

            using (var host = NetSession.StartHost(Port + 3, 2, "hostname", new SessionSettings(), 0, secret))
            {
                Assert.IsFalse(host.Report().Contains(secret), "the session report leaked the password");

                var serialized = host.Settings.Serialize();
                var text = System.Text.Encoding.UTF8.GetString(serialized);

                Assert.IsFalse(text.Contains(secret), "the settings that travel and get published leaked it");
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
    }
}
