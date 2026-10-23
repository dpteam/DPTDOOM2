using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Video;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Tests for frame pacing and interpolation fractions.
    ///
    /// <see cref="FractionNeverGoesBackwards"/> is a regression test for real
    /// judder: the loop advanced the game and then derived the fraction from the
    /// next frame index, producing 1.0 followed by 0.5. The picture stepped a
    /// full tic forward and half a tic back, every tic.
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
                var previous = 0.0;
                var steps = 0;

                for (var i = 0; i < 200; i++)
                {
                    var step = pacer.Advance();
                    var frac = step.Fraction.ToDouble();

                    if (step.Update)
                    {
                        // A new tic starts: the fraction wraps back down.
                        previous = frac;
                        continue;
                    }

                    Assert.IsTrue(
                        frac > previous,
                        "scale " + scale + ", frame " + step.Index +
                        ": fraction went from " + previous + " to " + frac +
                        " (interpolation ran backwards)");

                    previous = frac;
                    steps++;
                }

                Assert.IsTrue(
                    scale == 1 || steps > 0,
                    "scale " + scale + " never interpolated (scale 1 renders one frame per tic)");
            }
        }

        [TestMethod]
        public void FirstFrameOfATicIsNotTheLast()
        {
            var pacer = new FramePacer(2);

            var first = pacer.Advance();
            Assert.IsTrue(first.Update, "frame 0 advances the game");
            Assert.AreEqual(0.5, first.Fraction.ToDouble(), 0.001,
                "the frame that advances the game must sit halfway, not at the end");

            var second = pacer.Advance();
            Assert.IsFalse(second.Update);
            Assert.AreEqual(1.0, second.Fraction.ToDouble(), 0.001,
                "the following frame completes the tic");
        }

        [TestMethod]
        public void UpdatesHappenAtTheSimulationRate()
        {
            foreach (var scale in new[] { 1, 2, 3, 4 })
            {
                var pacer = new FramePacer(scale);
                var updates = 0;

                for (var i = 0; i < 35 * scale; i++)
                {
                    if (pacer.Advance().Update)
                    {
                        updates++;
                    }
                }

                Assert.AreEqual(35, updates, "scale " + scale + " must advance 35 tics per second");
            }
        }

        [TestMethod]
        public void FractionStaysInRange()
        {
            foreach (var scale in new[] { 1, 2, 3, 7 })
            {
                var pacer = new FramePacer(scale);

                for (var i = 0; i < 100; i++)
                {
                    var frac = pacer.Advance().Fraction.ToDouble();

                    Assert.IsTrue(frac > 0.0 && frac <= 1.0,
                        "scale " + scale + ": fraction " + frac + " is out of (0, 1]");
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
        public void EveryTicIsVisitedExactlyOnce()
        {
            // At 4x the pattern must be update, -, -, - and repeat.
            var pacer = new FramePacer(4);
            var pattern = "";

            for (var i = 0; i < 8; i++)
            {
                pattern += pacer.Advance().Update ? "U" : "-";
            }

            Assert.AreEqual("U---U---", pattern);
        }
    }
}
