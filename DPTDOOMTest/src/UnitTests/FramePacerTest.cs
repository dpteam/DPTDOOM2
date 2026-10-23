using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Video;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Tests for frame pacing and interpolation fractions.
    ///
    /// Two regressions are pinned here, and they are different bugs that looked
    /// similar from outside.
    ///
    /// <see cref="FractionNeverGoesBackwards"/> covers judder: an early version
    /// produced 1.0 followed by 0.5, so the picture stepped a full tic forward and
    /// half a tic back, every tic.
    ///
    /// <see cref="SlowFramesStillRunAtThirtyFiveTicsPerSecond"/> covers the game
    /// running slow: the simulation used to advance when
    /// <c>frame % fpsScale == 0</c>, tying the tic rate to the number of frames the
    /// machine managed. Drawing a larger window made the pistol fire slower while
    /// the frame counter still read 70, because the counter measures frames and the
    /// gun measures tics.
    /// </summary>
    [TestClass]
    public class FramePacerTest
    {
        [TestMethod]
        public void FractionNeverGoesBackwards()
        {
            foreach (var scale in new[] { 1, 2, 3, 4, 8 })
            {
                var pacer = new FramePacer(scale);
                var frameTime = 1.0 / pacer.TargetFps;
                var previous = 0.0;
                var interpolated = 0;

                for (var i = 0; i < 200; i++)
                {
                    var step = pacer.AdvanceBy(frameTime);
                    var fraction = step.Fraction.ToDouble();

                    if (step.Update)
                    {
                        // A new tic starts: the fraction wraps back down.
                        previous = fraction;
                        continue;
                    }

                    Assert.IsTrue(
                        fraction > previous,
                        "scale " + scale + ", frame " + step.Index +
                        ": fraction went from " + previous + " to " + fraction +
                        " (interpolation ran backwards)");

                    previous = fraction;
                    interpolated++;
                }

                Assert.IsTrue(
                    scale == 1 || interpolated > 0,
                    "scale " + scale + " never interpolated (scale 1 renders one frame per tic)");
            }
        }

        [TestMethod]
        public void ThirtyFiveTicsPerSecondAtTheTargetFrameRate()
        {
            foreach (var scale in new[] { 1, 2, 3, 4 })
            {
                var pacer = new FramePacer(scale);
                var frameTime = 1.0 / pacer.TargetFps;
                var tics = 0;

                // One simulated second.
                for (var i = 0; i < 35 * scale; i++)
                {
                    tics += pacer.AdvanceBy(frameTime).UpdateCount;
                }

                Assert.AreEqual(35, tics, 1,
                    "scale " + scale + " must advance 35 tics per second");
            }
        }

        [TestMethod]
        public void SlowFramesStillRunAtThirtyFiveTicsPerSecond()
        {
            // The reported bug: a bigger window draws fewer frames, and the game
            // used to slow down with it. Ten seconds at 20 frames per second must
            // still be ten seconds of game.
            foreach (var fps in new[] { 20.0, 30.0, 50.0, 120.0 })
            {
                var pacer = new FramePacer(2);
                var frameTime = 1.0 / fps;
                var frames = (int)(fps * 10);
                var tics = 0;

                for (var i = 0; i < frames; i++)
                {
                    tics += pacer.AdvanceBy(frameTime).UpdateCount;
                }

                Console.WriteLine("at " + fps + " fps: " + tics + " tics in " + frames + " frames");

                Assert.AreEqual(350, tics, 4,
                    "at " + fps + " fps the game must still run 35 tics per second, not " +
                    (tics / 10.0).ToString("0.0"));
            }
        }

        [TestMethod]
        public void ACatchUpFrameRunsSeveralTics()
        {
            // One frame taking three tics of time must run three tics, not one and
            // a half: the game cannot run at a fractional tic rate.
            var pacer = new FramePacer(2);

            // Consume the initial frame so the accumulator is at a known place.
            pacer.AdvanceBy(0);

            var step = pacer.AdvanceBy(3.0 / FramePacer.TicRate);

            Console.WriteLine("tics in one slow frame: " + step.UpdateCount);

            Assert.AreEqual(3, step.UpdateCount,
                "a frame three tics long must run three tics");
        }

        [TestMethod]
        public void AHugeStallDoesNotQueueUpAnEndlessBacklog()
        {
            // Dragging the window or a disk stall can block for a second. Queuing
            // a second of tics would make the next frame even slower, and the loop
            // would never recover, so the backlog is capped.
            var pacer = new FramePacer(2);

            var step = pacer.AdvanceBy(5.0);

            Console.WriteLine("tics after a 5 s stall: " + step.UpdateCount +
                              ", backlog " + pacer.BacklogSeconds.ToString("0.000") + " s");

            Assert.IsTrue(step.UpdateCount <= 4,
                "one frame must not run more than four tics (" + step.UpdateCount + ")");

            Assert.IsTrue(pacer.BacklogSeconds <= 1.0 / FramePacer.TicRate + 0.001,
                "the backlog must be dropped, not carried (" +
                pacer.BacklogSeconds.ToString("0.000") + " s)");

            // And the loop must recover: the next ordinary frame runs one tic.
            var next = pacer.AdvanceBy(1.0 / 70.0);
            Assert.AreEqual(1, next.UpdateCount, "the loop must recover after a stall");
        }

        [TestMethod]
        public void FractionStaysInRange()
        {
            foreach (var scale in new[] { 1, 2, 3, 7 })
            {
                var pacer = new FramePacer(scale);
                var frameTime = 1.0 / pacer.TargetFps;

                for (var i = 0; i < 100; i++)
                {
                    var fraction = pacer.AdvanceBy(frameTime).Fraction.ToDouble();

                    Assert.IsTrue(fraction > 0.0 && fraction <= 1.0,
                        "scale " + scale + ": fraction " + fraction + " is out of (0, 1]");
                }
            }
        }

        [TestMethod]
        public void TargetFpsMatchesDoomTicRate()
        {
            Assert.AreEqual(35.0, new FramePacer(1).TargetFps, 0.001);
            Assert.AreEqual(70.0, new FramePacer(2).TargetFps, 0.001);
            Assert.AreEqual(140.0, new FramePacer(4).TargetFps, 0.001);
        }

        [TestMethod]
        public void InvalidScaleIsClamped()
        {
            Assert.AreEqual(1, new FramePacer(0).FpsScale);
            Assert.AreEqual(1, new FramePacer(-5).FpsScale);
        }

        [TestMethod]
        public void NegativeOrNonsenseElapsedTimeIsIgnored()
        {
            var pacer = new FramePacer(2);

            pacer.AdvanceBy(0);

            var step = pacer.AdvanceBy(-1.0);
            Assert.AreEqual(0, step.UpdateCount, "a negative interval must not run tics");

            step = pacer.AdvanceBy(double.NaN);
            Assert.AreEqual(0, step.UpdateCount, "a NaN interval must not run tics");

            var fraction = step.Fraction.ToDouble();
            Assert.IsTrue(fraction > 0.0 && fraction <= 1.0,
                "the fraction must stay usable after a bad interval (" + fraction + ")");
        }
    }
}
