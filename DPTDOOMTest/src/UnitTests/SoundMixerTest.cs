using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Audio;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Tests for the software mixer.
    ///
    /// The important one is <see cref="VoicesClampInsteadOfWrapping"/>: an
    /// earlier version accumulated voices into a short[], and adding two loud
    /// voices wrapped around instead of clipping. That does not sound like
    /// distortion, it sounds like harsh digital noise, which is exactly the
    /// bug that was reported.
    /// </summary>
    [TestClass]
    public class SoundMixerTest
    {
        private const int Rate = 44100;

        /// <summary>Largest positive value a DMX sample can produce.</summary>
        private const int Peak = 32511; // 127/128 * 32767

        /// <summary>
        /// Builds a DMX lump holding a constant value, so the expected output is
        /// trivially predictable.
        /// </summary>
        private static SoundSample Constant(byte value, int samples, int rate)
        {
            var lump = new byte[SoundSample.HeaderSize + samples];

            lump[0] = 3; // format
            lump[1] = 0;
            lump[2] = (byte)(rate & 0xFF);
            lump[3] = (byte)(rate >> 8);
            lump[4] = (byte)(samples & 0xFF);
            lump[5] = (byte)((samples >> 8) & 0xFF);
            lump[6] = (byte)((samples >> 16) & 0xFF);
            lump[7] = (byte)((samples >> 24) & 0xFF);

            for (var i = 0; i < samples; i++)
            {
                lump[SoundSample.HeaderSize + i] = value;
            }

            return SoundSample.Parse(lump);
        }

        [TestMethod]
        public void ParsesDmxHeader()
        {
            var sample = Constant(128, 1000, 11025);

            Assert.IsNotNull(sample);
            Assert.AreEqual(1000, sample.Count);
            Assert.AreEqual(11025, sample.SampleRate);
            Assert.AreEqual(0, sample.GetValue(0), "128 is the silence point");
        }

        [TestMethod]
        public void RejectsBrokenLumps()
        {
            Assert.IsNull(SoundSample.Parse(null));
            Assert.IsNull(SoundSample.Parse(new byte[0]));
            Assert.IsNull(SoundSample.Parse(new byte[4]), "too short for a header");

            // A header that claims a silly sample rate must not be trusted.
            var lump = new byte[64];
            lump[2] = 0xFF;
            lump[3] = 0xFF;
            Assert.IsNull(SoundSample.Parse(lump));
        }

        [TestMethod]
        public void TruncatedDataFallsBackToWhatIsThere()
        {
            var lump = new byte[SoundSample.HeaderSize + 10];

            // Claims 1000 samples, only 10 present.
            lump[4] = 0xE8;
            lump[5] = 0x03;
            lump[2] = 0x11;
            lump[3] = 0x2B;

            var sample = SoundSample.Parse(lump);

            Assert.IsNotNull(sample);
            Assert.AreEqual(10, sample.Count, "the mixer must not read past the lump");
        }

        [TestMethod]
        public void SingleVoiceReproducesTheSample()
        {
            var sample = Constant(255, 400, Rate);

            var mixer = new SoundMixer(1, Rate, 2);
            mixer.MasterVolume = 1F;
            mixer.Play(0, sample, 1F, 1F, 1F);

            var buffer = new short[64 * 2];
            mixer.Mix(buffer, 64);

            // Unsigned 8-bit maps 0..255 onto -1.0..+0.992, so 255 is the
            // positive peak of the format, not exactly 1.0.
            Assert.AreEqual(Peak, buffer[0]);
            Assert.AreEqual(Peak, buffer[1], "both channels get the same gain");
        }

        [TestMethod]
        public void VoicesClampInsteadOfWrapping()
        {
            // Two voices, each at half scale but with positive gain, so their
            // sum is 1.0 and the third voice pushes past full scale. With a
            // short accumulator this wrapped to a large negative value.
            var loud = Constant(255, 400, Rate);

            var mixer = new SoundMixer(3, Rate, 2);
            mixer.MasterVolume = 1F;

            mixer.Play(0, loud, 1F, 1F, 1F);
            mixer.Play(1, loud, 1F, 1F, 1F);
            mixer.Play(2, loud, 1F, 1F, 1F);

            var buffer = new short[32 * 2];
            mixer.Mix(buffer, 32);

            for (var i = 0; i < buffer.Length; i++)
            {
                Assert.IsTrue(
                    buffer[i] > 0,
                    "sample " + i + " is " + buffer[i] +
                    "; a positive signal must stay positive (overflow wrapped)");

                Assert.AreEqual(32767, buffer[i], "the sum must clamp at full scale");
            }
        }

        [TestMethod]
        public void NegativeSignalStaysNegativeWhenOverloaded()
        {
            var quiet = Constant(0, 400, Rate); // -1.0

            var mixer = new SoundMixer(2, Rate, 2);
            mixer.MasterVolume = 1F;

            mixer.Play(0, quiet, 1F, 1F, 1F);
            mixer.Play(1, quiet, 1F, 1F, 1F);

            var buffer = new short[32 * 2];
            mixer.Mix(buffer, 32);

            for (var i = 0; i < buffer.Length; i++)
            {
                Assert.AreEqual(-32767, buffer[i]);
            }
        }

        [TestMethod]
        public void ResamplesFromElevenKilohertz()
        {
            // A 11025 Hz sample played at 44100 Hz must take four output
            // frames per source sample, so a 100-sample lump lasts 400 frames.
            var sample = Constant(200, 100, 11025);

            var mixer = new SoundMixer(1, Rate, 2);
            mixer.Play(0, sample, 1F, 1F, 1F);

            var buffer = new short[16 * 2];
            var frames = 0;

            while (mixer.IsPlaying(0) && frames < 2000)
            {
                mixer.Mix(buffer, 16);
                frames += 16;
            }

            Assert.IsTrue(
                Math.Abs(frames - 400) <= 32,
                "expected about 400 output frames for a 11025 Hz lump, got " + frames);
        }

        [TestMethod]
        public void VolumeAndGainsAreApplied()
        {
            var sample = Constant(255, 400, Rate);

            var mixer = new SoundMixer(1, Rate, 2);
            mixer.MasterVolume = 0.5F;
            mixer.Play(0, sample, 1F, 0.5F, 1F);

            var buffer = new short[16 * 2];
            mixer.Mix(buffer, 16);

            Assert.AreEqual(Peak / 2, buffer[0], 2, "left: 1.0 * 0.5 * full scale");
            Assert.AreEqual(Peak / 4, buffer[1], 2, "right: 0.5 * 0.5 * full scale");
        }

        [TestMethod]
        public void PanningIsConstantPower()
        {
            var sample = Constant(255, 400, Rate);

            var mixer = new SoundMixer(1, Rate, 2);
            mixer.MasterVolume = 1F;

            // Hard left: 1.0 / 0.0.
            mixer.Play(0, sample, 1F, 0F, 1F);

            var buffer = new short[8 * 2];
            mixer.Mix(buffer, 8);

            Assert.AreEqual(Peak, buffer[0], 2);
            Assert.AreEqual(0, buffer[1], 2);
        }

        [TestMethod]
        public void StoppedVoiceIsSilent()
        {
            var sample = Constant(255, 400, Rate);

            var mixer = new SoundMixer(1, Rate, 2);
            mixer.Play(0, sample, 1F, 1F, 1F);
            mixer.Stop(0);

            var buffer = new short[8 * 2];
            mixer.Mix(buffer, 8);

            Assert.IsFalse(mixer.IsPlaying(0));
            Assert.AreEqual(0, buffer[0]);
        }

        [TestMethod]
        public void MixingIsDeterministic()
        {
            var sample = Constant(200, 400, 11025);

            var first = Render(sample);
            var second = Render(sample);

            Assert.AreEqual(first.Length, second.Length);

            for (var i = 0; i < first.Length; i++)
            {
                Assert.AreEqual(first[i], second[i], "frame " + i + " differs");
            }
        }

        private static short[] Render(SoundSample sample)
        {
            var mixer = new SoundMixer(1, Rate, 2);
            mixer.Play(0, sample, 0.8F, 0.6F, 1.02F);

            var buffer = new short[64 * 2];
            var output = new short[400 * 2];

            var frames = 0;
            while (frames < 400)
            {
                var block = Math.Min(64, 400 - frames);
                mixer.Mix(buffer, block);

                Array.Copy(buffer, 0, output, frames * 2, block * 2);
                frames += block;
            }

            return output;
        }
    }
}
