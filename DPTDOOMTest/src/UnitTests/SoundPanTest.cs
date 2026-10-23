using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Audio;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Tests for stereo panning and distance attenuation.
    ///
    /// The left/right test is not hypothetical: the first version mapped a
    /// positive relative angle to the right channel, which mirrors the whole
    /// soundstage. A monster growling on your left came out of the right
    /// speaker.
    /// </summary>
    [TestClass]
    public class SoundPanTest
    {
        private const float Tolerance = 0.001F;

        [TestMethod]
        public void SourceAheadIsCentred()
        {
            float left, right;
            SoundPan.FromRelativeAngle(0F, out left, out right);

            Assert.AreEqual(0.7071F, left, Tolerance);
            Assert.AreEqual(0.7071F, right, Tolerance);
        }

        [TestMethod]
        public void PositiveAngleGoesToTheLeftChannel()
        {
            // Doom angles grow counterclockwise, so +90 degrees is the
            // listener's left.
            float left, right;
            SoundPan.FromRelativeAngle((float)(Math.PI / 2), out left, out right);

            Assert.AreEqual(1F, left, Tolerance, "positive angle must be the left channel");
            Assert.AreEqual(0F, right, Tolerance);
        }

        [TestMethod]
        public void NegativeAngleGoesToTheRightChannel()
        {
            float left, right;
            SoundPan.FromRelativeAngle((float)(-Math.PI / 2), out left, out right);

            Assert.AreEqual(0F, left, Tolerance);
            Assert.AreEqual(1F, right, Tolerance, "negative angle must be the right channel");
        }

        [TestMethod]
        public void BehindIsCentred()
        {
            float left, right;
            SoundPan.FromRelativeAngle((float)Math.PI, out left, out right);

            Assert.AreEqual(0.7071F, left, Tolerance);
            Assert.AreEqual(0.7071F, right, Tolerance);
        }

        [TestMethod]
        public void PanLawIsConstantPower()
        {
            for (var degrees = -180; degrees <= 180; degrees += 15)
            {
                float left, right;
                SoundPan.FromRelativeAngle((float)(degrees * Math.PI / 180), out left, out right);

                var power = left * left + right * right;
                Assert.AreEqual(1F, power, 0.002F, "power at " + degrees + " degrees");
            }
        }

        [TestMethod]
        public void GainsStayInRange()
        {
            for (var degrees = -360; degrees <= 360; degrees += 7)
            {
                float left, right;
                SoundPan.FromRelativeAngle((float)(degrees * Math.PI / 180), out left, out right);

                Assert.IsTrue(left >= 0F && left <= 1F, "left out of range at " + degrees);
                Assert.IsTrue(right >= 0F && right <= 1F, "right out of range at " + degrees);
            }
        }

        [TestMethod]
        public void NearSourcesAreNotAttenuated()
        {
            Assert.AreEqual(1F, SoundPan.DistanceDecay(0F, 160F, 1200F), Tolerance);
            Assert.AreEqual(1F, SoundPan.DistanceDecay(159F, 160F, 1200F), Tolerance);
        }

        [TestMethod]
        public void FarSourcesFadeOut()
        {
            Assert.AreEqual(0F, SoundPan.DistanceDecay(1200F, 160F, 1200F), Tolerance);
            Assert.AreEqual(0F, SoundPan.DistanceDecay(5000F, 160F, 1200F), Tolerance,
                "beyond the clip distance must not go negative");
        }

        [TestMethod]
        public void DecayIsMonotonic()
        {
            var previous = 1F;

            for (var distance = 160F; distance <= 1200F; distance += 40F)
            {
                var decay = SoundPan.DistanceDecay(distance, 160F, 1200F);

                Assert.IsTrue(decay <= previous + Tolerance, "decay increased at " + distance);
                Assert.IsTrue(decay >= 0F && decay <= 1F, "decay out of range at " + distance);

                previous = decay;
            }
        }
    }
}
