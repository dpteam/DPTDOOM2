using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// What makes a headless host a server.
    ///
    /// A headless host already runs the game without a window and `-net host` already accepts
    /// players. The difference is entirely in what happens when nothing is happening, and every
    /// rule below came from a way the first attempt would have quit before anybody could connect:
    ///
    ///  - the host gives up after five minutes waiting for players, which a server with nobody on
    ///    it hits immediately;
    ///  - the loop treats "the world is not advancing" as a fault and stops;
    ///  - the ten-million-tic safety net stops a run after about three days.
    ///
    /// These are decisions, so they are here rather than buried in a loop with a sleep in it.
    /// </summary>
    [TestClass]
    public class DedicatedServerTest
    {
        [TestMethod]
        public void AnOrdinaryHeadlessRunTreatsAWaitAsAFault()
        {
            // The test run was asked for tics and is not getting them: that is a fault and it says so.
            Assert.IsTrue(DedicatedServer.WaitIsAFault(false, 5001, 5000));
            Assert.IsFalse(DedicatedServer.WaitIsAFault(false, 5000, 5000), "at the limit is not past it");
        }

        [TestMethod]
        public void AServerNeverTreatsAWaitAsAFault()
        {
            // "Nobody is connected yet" is the normal state of a server. Treating it as a fault is
            // the one thing that makes a dedicated server impossible.
            Assert.IsFalse(DedicatedServer.WaitIsAFault(true, 5001, 5000));
            Assert.IsFalse(DedicatedServer.WaitIsAFault(true, int.MaxValue, 5000));
        }

        [TestMethod]
        public void AServerHasNoTicLimitOfItsOwn()
        {
            Assert.IsFalse(DedicatedServer.TicLimitReached(true, 1000000, 0),
                "no limit given means no limit");
        }

        [TestMethod]
        public void AnExplicitTicLimitStillAppliesToAServer()
        {
            // How a server is tested: it must stop on request, not run until killed.
            Assert.IsFalse(DedicatedServer.TicLimitReached(true, 99, 100));
            Assert.IsTrue(DedicatedServer.TicLimitReached(true, 100, 100));
        }

        [TestMethod]
        public void AServerHasNoSafetyNet()
        {
            Assert.IsFalse(DedicatedServer.SafetyNetReached(true, 10000000));
            Assert.IsTrue(DedicatedServer.SafetyNetReached(false, 10000000),
                "a test run still needs one: a game that never ends would hang it");
        }

        [TestMethod]
        public void TheThreeVerbsAreUnderstood()
        {
            string argument;

            Assert.AreEqual(ServerCommand.Quit, DedicatedServer.Parse("quit", out argument));
            Assert.AreEqual(ServerCommand.Quit, DedicatedServer.Parse("  STOP  ", out argument));
            Assert.AreEqual(ServerCommand.Status, DedicatedServer.Parse("status", out argument));
            Assert.AreEqual(ServerCommand.Status, DedicatedServer.Parse("players", out argument));

            Assert.AreEqual(ServerCommand.Say, DedicatedServer.Parse("say hello there", out argument));
            Assert.AreEqual("hello there", argument, "the text after the verb, not the verb");
        }

        [TestMethod]
        public void AnUnknownVerbIsNotACommand()
        {
            string argument;

            Assert.AreEqual(ServerCommand.None, DedicatedServer.Parse("give all", out argument));
            Assert.AreEqual(ServerCommand.None, DedicatedServer.Parse("", out argument));
            Assert.AreEqual(ServerCommand.None, DedicatedServer.Parse("   ", out argument));
            Assert.AreEqual(ServerCommand.None, DedicatedServer.Parse("say", out argument),
                "'say' with nothing after it says nothing");
        }

        [TestMethod]
        public void ASaidLineIsCutToTheChatLimit()
        {
            // It travels the same path as a player's message, and a longer one would be cut off on
            // the wire without saying so - so it is cut here, where the operator can see it happen.
            string argument;

            var longText = new string('x', Chat.MaxLength + 50);

            Assert.AreEqual(ServerCommand.Say, DedicatedServer.Parse("say " + longText, out argument));
            Assert.AreEqual(Chat.MaxLength, argument.Length);
        }

        [TestMethod]
        public void TheStatusLineSaysWhatAnOperatorNeeds()
        {
            var text = DedicatedServer.Status(2, 3, 1, 1234, TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(8));

            StringAssert.Contains(text, "players 2/3", "how many, of how many");
            StringAssert.Contains(text, "tic 1234");
            StringAssert.Contains(text, "5m8s", "how long it has been up");
            StringAssert.Contains(text, "1", "and how many slots have gone quiet");
        }

        [TestMethod]
        public void AQuietSlotIsNotMentionedWhenThereIsNone()
        {
            // A line that always says "0 slots gone quiet" is a line nobody reads.
            var text = DedicatedServer.Status(1, 2, 0, 10, TimeSpan.FromSeconds(9));

            Assert.IsFalse(text.Contains("quiet"), text);
            StringAssert.Contains(text, "9s");
        }

        [TestMethod]
        public void DurationsReadTheWayAnOperatorReadsThem()
        {
            Assert.AreEqual("9s", DedicatedServer.Format(TimeSpan.FromSeconds(9)));
            Assert.AreEqual("5m8s", DedicatedServer.Format(new TimeSpan(0, 5, 8)));
            Assert.AreEqual("3h12m", DedicatedServer.Format(new TimeSpan(3, 12, 0)));
            Assert.AreEqual("4d2h", DedicatedServer.Format(new TimeSpan(4, 2, 0, 0)));
        }
    }
}
