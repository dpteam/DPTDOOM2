using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Headless;
using DPTDOOM.Video;
using DPTDOOMTest.CompatibilityTests;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The frame-sequence render mode: PNG writing, layers and colour grades.
    ///
    /// The point of the mode is footage, so the tests check the things a take depends on: a file an
    /// encoder can read, a layer that really is a cut-out rather than a rectangle of black, and a
    /// grade that changes the picture without changing the world.
    /// </summary>
    [TestClass]
    public class RenderSequenceTest
    {
        private ActorTableSnapshot snapshot;

        [TestInitialize]
        public void Before()
        {
            snapshot = ActorTableSnapshot.Take();
        }

        [TestCleanup]
        public void After()
        {
            snapshot.Restore();
        }

        private static string TempFile()
        {
            return Path.Combine(Path.GetTempPath(), "dptdoom-png-" +
                Guid.NewGuid().ToString("N") + ".png");
        }

        private static byte[] Frame(int width, int height, byte red, byte green, byte blue)
        {
            var pixels = new byte[width * height * 4];

            for (var i = 0; i < width * height; i++)
            {
                pixels[i * 4] = red;
                pixels[i * 4 + 1] = green;
                pixels[i * 4 + 2] = blue;
                pixels[i * 4 + 3] = 255;
            }

            return pixels;
        }

        [TestMethod]
        public void PngRoundTripsThroughTheEnginesOwnReader()
        {
            // The writer and the reader are separate code; agreeing on a file the engine can read
            // back is the cheapest proof that the writer is not producing something only it
            // understands.
            var path = TempFile();

            try
            {
                PngWriter.Save(path, Frame(4, 3, 10, 20, 30), 4, 3);

                var image = PngImage.Decode(File.ReadAllBytes(path));

                Assert.AreEqual(4, image.Width);
                Assert.AreEqual(3, image.Height);
                Assert.AreEqual(10, image.Rgba[0]);
                Assert.AreEqual(20, image.Rgba[1]);
                Assert.AreEqual(30, image.Rgba[2]);
                Assert.AreEqual(255, image.Rgba[3], "an RGB file decodes as opaque");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void RgbaPngKeepsTheAlphaChannel()
        {
            var path = TempFile();

            try
            {
                var pixels = Frame(2, 2, 1, 2, 3);
                pixels[3] = 0;      // first pixel transparent
                pixels[7] = 128;    // second half transparent

                PngWriter.SaveRgba(path, pixels, 2, 2);

                var image = PngImage.Decode(File.ReadAllBytes(path));

                Assert.AreEqual(0, image.Rgba[3], "a transparent pixel stays transparent");
                Assert.AreEqual(128, image.Rgba[7], "a partial alpha stays partial");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void AGradeChangesThePaletteAndNotTheWorld()
        {
            // The whole reason a grade is a palette transform: the simulation never sees a colour,
            // so a graded take and a plain take are the same run.
            var source = new uint[256];

            for (var i = 0; i < source.Length; i++)
            {
                // Deliberately unequal channels: a grey would survive desaturation unchanged, and
                // a test built on greys would pass whatever the grade did.
                source[i] = (uint)i | ((uint)(255 - i) << 8) | ((uint)(i / 2) << 16) | (0xFFu << 24);
            }

            var graded = new uint[256];

            var noir = new PaletteGrade();
            noir.Saturation = 0;

            noir.Apply(source, graded);

            var red = (byte)(graded[200] & 0xFF);
            var green = (byte)((graded[200] >> 8) & 0xFF);
            var blue = (byte)((graded[200] >> 16) & 0xFF);

            Assert.IsTrue(Math.Abs(red - green) <= 1 && Math.Abs(green - blue) <= 1,
                "a grey has equal channels: " + red + "," + green + "," + blue);
            Assert.AreNotEqual(source[200], graded[200], "and it is not the same colour");
        }

        [TestMethod]
        public void AnIdentityGradeIsRecognisedSoItIsNotApplied()
        {
            var grade = new PaletteGrade();

            Assert.IsTrue(grade.IsIdentity, "a fresh grade changes nothing");

            grade.Saturation = 0.5;

            Assert.IsFalse(grade.IsIdentity);
        }

        [TestMethod]
        public void ATintMultipliesTheChannels()
        {
            var source = new uint[256];
            source[10] = 100u | (100u << 8) | (100u << 16) | (0xFFu << 24);

            var graded = new uint[256];

            var warm = new PaletteGrade();
            warm.TintRed = 2.0;
            warm.TintBlue = 0.5;

            warm.Apply(source, graded);

            Assert.AreEqual(200, (int)(graded[10] & 0xFF), "red is doubled and clamped to 255 max");
            Assert.AreEqual(100, (int)((graded[10] >> 8) & 0xFF));
            Assert.AreEqual(50, (int)((graded[10] >> 16) & 0xFF));
        }

        [TestMethod]
        public void PosteriseQuantisesTheChannels()
        {
            var source = new uint[256];
            source[5] = 130u | (130u << 8) | (130u << 16) | (0xFFu << 24);

            var graded = new uint[256];

            var poster = new PaletteGrade();
            poster.Posterise = 4;

            poster.Apply(source, graded);

            var red = (byte)(graded[5] & 0xFF);

            Assert.AreEqual(0, red % 64, "four levels means multiples of 64: " + red);
        }
    }
}
