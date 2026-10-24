using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The input buffer: how big, and when to change it.
    ///
    /// This is the arithmetic that decides how a lockstep session feels, and it is
    /// arithmetic on noisy numbers - which is exactly what a live test cannot check.
    /// A session on loopback has no jitter to measure, so the branches that matter
    /// are the ones no amount of playing would reach.
    /// </summary>
    [TestClass]
    public class DelayPolicyTest
    {
        /// <summary>A window in which a fifth of the tics were late: a real shortage.</summary>
        private const int Late = 60;

        /// <summary>Tics in a window, so the late ones are read as a share.</summary>
        private const int Many = 300;

        // ------------------------------------------------------------ jitter

        [TestMethod]
        public void ACleanLinkHasNoJitter()
        {
            var samples = new[] { 100, 100, 100, 100, 100 };

            Assert.AreEqual(0, DelayPolicy.JitterMs(samples));
            Assert.AreEqual(100, DelayPolicy.MedianMs(samples));
        }

        [TestMethod]
        public void OneUnluckyProbeIsNotJitter()
        {
            // The number this replaced was the spread, which is decided by the single
            // worst probe. One 40 ms hiccup on a clean link used to buy an extra tic
            // of input latency that was never needed again.
            var withHiccup = new[] { 100, 100, 100, 100, 100, 100, 140 };
            var spread = 40;

            Assert.AreEqual(0, DelayPolicy.JitterMs(withHiccup),
                "the hiccup is not what the link does most of the time");

            Assert.AreNotEqual(spread, DelayPolicy.JitterMs(withHiccup),
                "the spread of a handful of probes is the wrong statistic");
        }

        [TestMethod]
        public void AConsistentlyWobblyLinkReportsItsWobble()
        {
            var samples = new[] { 80, 120, 80, 120, 80, 120 };

            Assert.AreEqual(100, DelayPolicy.MedianMs(samples));
            Assert.AreEqual(20, DelayPolicy.JitterMs(samples));
        }

        [TestMethod]
        public void JitterGrowsWithTheSpread()
        {
            var calm = DelayPolicy.JitterMs(new[] { 100, 108, 100, 108, 100, 108 });
            var wild = DelayPolicy.JitterMs(new[] { 60, 140, 60, 140, 60, 140 });

            Assert.IsTrue(wild > calm, "a wilder link must report more jitter (" + calm + " vs " + wild + ")");
        }

        [TestMethod]
        public void NoSamplesMeansNoMeasurement()
        {
            Assert.AreEqual(-1, DelayPolicy.MedianMs(new int[0]));
            Assert.AreEqual(0, DelayPolicy.JitterMs(new int[0]));
        }

        // ------------------------------------------------------------ initial buffer

        [TestMethod]
        public void ALocalLinkNeedsNoBuffer()
        {
            Assert.AreEqual(0, DelayPolicy.Initial(0, 0));
            Assert.AreEqual(0, DelayPolicy.Initial(10, 0), "well under a tic of round trip");
        }

        [TestMethod]
        public void TheBufferCoversTheRoundTrip()
        {
            // 200 ms of round trip is 7 tics; the buffer plus the tic being simulated
            // has to cover the whole wait.
            var tics = DelayPolicy.Initial(200, 0);

            Assert.IsTrue(tics >= 7, "200 ms of round trip needs at least 7 tics, got " + tics);
            Assert.IsTrue(tics <= DelayPolicy.MaxTics, "and never past the cap");
        }

        [TestMethod]
        public void AnUnmeasuredLinkDoesNotAssumeALan()
        {
            // Assuming a LAN and then stalling once a second is worse than paying a
            // buffer sized for a real network.
            Assert.AreEqual(DelayPolicy.UnmeasuredTics, DelayPolicy.Initial(-1, 0));
            Assert.IsTrue(DelayPolicy.UnmeasuredTics > 0);
        }

        [TestMethod]
        public void JitterBuysAMargin()
        {
            var calm = DelayPolicy.Initial(200, 0);
            var wobbly = DelayPolicy.Initial(200, 40);

            Assert.IsTrue(wobbly > calm, "jitter has to buy room (" + calm + " vs " + wobbly + ")");
        }

        [TestMethod]
        public void TheMarginIsGradedAndBounded()
        {
            // One tic of extra cover for a little jitter, not a fixed hit for any
            // jitter at all - and never a slide towards the cap.
            var little = DelayPolicy.Initial(200, 20);
            var lots = DelayPolicy.Initial(200, 500);

            Assert.AreEqual(DelayPolicy.Initial(200, 0) + 1, little);
            Assert.AreEqual(DelayPolicy.Initial(200, 0) + 2, lots, "the margin stops at two tics");
        }

        [TestMethod]
        public void TheBufferNeverExceedsTheCap()
        {
            Assert.AreEqual(DelayPolicy.MaxTics, DelayPolicy.Initial(100000, 1000));
        }

        // ------------------------------------------------------------ adaptation

        [TestMethod]
        public void AFeltStallRaisesTheBuffer()
        {
            var next = DelayPolicy.Adjust(2, 200, 0, Late, Many, 0);

            Assert.IsTrue(next > 2, "a tic that was actually late means the buffer was too small");
        }

        [TestMethod]
        public void ARaiseJumpsStraightToWhatTheMeasurementAsksFor()
        {
            // The session is already stuttering. Easing towards the number that fixes
            // it means stuttering for several more two-second windows.
            var measured = DelayPolicy.Initial(300, 0);

            Assert.AreEqual(measured, DelayPolicy.Adjust(1, 300, 0, Late, Many, 0),
                "the measurement is the authority once a stall has proved it right");
        }

        [TestMethod]
        public void AStallWithNoBetterEstimateGrowsOneTic()
        {
            // The measurement says the current buffer is enough, but a tic was late
            // anyway. The estimate is wrong; correct it slowly rather than trusting it.
            var current = DelayPolicy.Initial(200, 0);

            Assert.AreEqual(current + 1, DelayPolicy.Adjust(current, 200, 0, Late, Many, 0));
        }

        [TestMethod]
        public void AStallNeverPushesPastTheCap()
        {
            Assert.AreEqual(DelayPolicy.MaxTics, DelayPolicy.Adjust(DelayPolicy.MaxTics, 200, 0, Late, Many, 0));
        }

        [TestMethod]
        public void AQuietWindowDoesNotShrinkTheBufferImmediately()
        {
            // The reward is one tic of latency; the risk is a stall the player feels.
            // Shrinking is the slow direction.
            var current = DelayPolicy.Initial(200, 0) + 2;

            for (var window = 0; window < DelayPolicy.QuietWindowsBeforeShrinking; window++)
            {
                Assert.AreEqual(current, DelayPolicy.Adjust(current, 200, 0, 0, Many, window),
                    "no shrink on quiet window " + window);
            }
        }

        [TestMethod]
        public void ALongQuietRunShrinksOneTic()
        {
            var current = DelayPolicy.Initial(200, 0) + 2;

            Assert.AreEqual(current - 1,
                DelayPolicy.Adjust(current, 200, 0, 0, Many, DelayPolicy.QuietWindowsBeforeShrinking));
        }

        [TestMethod]
        public void TheBufferNeverShrinksBelowWhatTheLinkNeeds()
        {
            // Otherwise it would cause the stall that raises it again, and the player
            // would feel the same hitch over and over.
            var needed = DelayPolicy.Initial(200, 0);

            Assert.AreEqual(needed,
                DelayPolicy.Adjust(needed, 200, 0, 0, Many, 1000),
                "already at the measurement, nothing to give back");
        }

        [TestMethod]
        public void ARecoveredLinkGivesTheLatencyBack()
        {
            // The buffer was raised when the link was bad; both the link and the
            // measurement have since improved.
            var raised = DelayPolicy.Initial(300, 0);

            Assert.AreEqual(raised - 1, DelayPolicy.Adjust(raised, 60, 0, 0, Many, 1000),
                "the slower measurement lets one tic go");
        }

        [TestMethod]
        public void ZeroStaysZeroWhileNothingStalls()
        {
            // A clean link is not a reason to invent a buffer, and a session at zero
            // must not drift upwards on its own.
            var current = 0;

            for (var window = 0; window < 100; window++)
            {
                current = DelayPolicy.Adjust(current, 5, 0, 0, Many, window);
            }

            Assert.AreEqual(0, current);
        }

        // ------------------------------------------------------------ what counts

        [TestMethod]
        public void OneLateTicInAWindowIsAHiccupNotAShortage()
        {
            // A single pause is the machine, not the link, and buying a permanent tic
            // of input latency for it is a bad trade that would also never be given
            // back. A tenth of the tics late is a link that cannot carry the session at
            // this buffer size.
            var current = 4;

            Assert.AreEqual(current, DelayPolicy.Adjust(current, 140, 18, 1, 300, 0),
                "one late tic in three hundred is a hiccup");
        }

        [TestMethod]
        public void AWidespreadShortageGrowsTheBuffer()
        {
            var current = 4;

            Assert.IsTrue(DelayPolicy.Adjust(current, 140, 18, 30, 300, 0) > current,
                "a tenth of the tics late is a buffer that is too small");
        }

        [TestMethod]
        public void TheShareIsWhatMattersNotTheCount()
        {
            // The window is two seconds of however fast the game is running: 70 tics in
            // a paced game, several hundred in an unthrottled test. A fixed count would
            // mean different things in each.
            var paced = DelayPolicy.StallsMatter(4, 70);
            var unthrottled = DelayPolicy.StallsMatter(4, 400);

            Assert.IsTrue(paced, "four of seventy matters");
            Assert.IsFalse(unthrottled, "four of four hundred does not");
        }

        [TestMethod]
        public void NoStallsIsNoShortage()
        {
            Assert.IsFalse(DelayPolicy.StallsMatter(0, 300));
        }

        [TestMethod]
        public void TheBufferStopsAtTheCeilingForThisLink()
        {
            // The ratchet. A jittery channel never reaches zero stalls, so a rule with
            // no stopping point walks the buffer to the cap and stays there - 285 ms of
            // input latency in exchange for late packets that no buffer removes.
            var ceiling = DelayPolicy.Ceiling(140, 18);

            Assert.IsTrue(ceiling < DelayPolicy.MaxTics,
                "a 140 ms link does not justify the hard cap");

            Assert.AreEqual(ceiling, DelayPolicy.Adjust(ceiling, 140, 18, 30, 300, 0),
                "already as deep as this link justifies");
        }

        [TestMethod]
        public void TheCeilingLeavesRoomForAnUnderestimatedLink()
        {
            // The probes run before the session's own traffic, so the measurement is
            // optimistic. Two tics is the room for that, not an open-ended allowance.
            Assert.AreEqual(DelayPolicy.Initial(140, 18) + DelayPolicy.SlackTics,
                DelayPolicy.Ceiling(140, 18));
        }

        [TestMethod]
        public void TheCeilingNeverExceedsTheHardCap()
        {
            Assert.AreEqual(DelayPolicy.MaxTics, DelayPolicy.Ceiling(100000, 1000));
        }

        [TestMethod]
        public void AnUnmeasuredLinkStillGrows()
        {
            // At the start of a session the probes may have failed, and stalls are then
            // the only evidence available. Refusing to act on them would leave a bad
            // buffer in place for the first seconds, which is when it is most visible.
            Assert.IsTrue(DelayPolicy.Adjust(1, -1, 0, Late, Many, 0) > 1);
        }

        // ------------------------------------------------------------ stall threshold

        [TestMethod]
        public void AWaitShorterThanATicIsNotAFeltStall()
        {
            // The frame pacer absorbs it: the tic still completes inside its own
            // budget. Counting it made a perfectly smooth loopback session read as half
            // stalled, and would have dragged the buffer up to the cap for nothing.
            Assert.IsFalse(DelayPolicy.IsFelt(0));
            Assert.IsFalse(DelayPolicy.IsFelt(DelayPolicy.TicMs - 1));
            Assert.IsTrue(DelayPolicy.IsFelt(DelayPolicy.TicMs));
            Assert.IsTrue(DelayPolicy.IsFelt(DelayPolicy.TicMs * 4));
        }
    }
}
