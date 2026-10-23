using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The PNG decoder and the palette conversion that lets a PK3 provide flats.
    ///
    /// The test builds its own PNG files (see <see cref="PngBuilder"/>) rather
    /// than shipping binaries: the decoder's whole job is to handle the format
    /// correctly, so the test has to control the format — bit depth, colour type,
    /// and especially the five scanline filters, which are where a decoder
    /// usually gets it wrong.
    /// </summary>
    [TestClass]
    public class PngTest
    {
        [TestMethod]
        public void DecodesRgbAtEveryFilter()
        {
            // A 4x4 image whose colours vary, encoded five times with a different
            // filter each time. All five must decode to the same pixels.
            const int size = 4;
            var raw = new byte[size * size * 3];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var offset = (y * size + x) * 3;
                    raw[offset] = (byte)(x * 60);
                    raw[offset + 1] = (byte)(y * 60);
                    raw[offset + 2] = (byte)(x * y * 10);
                }
            }

            for (var filter = 0; filter <= 4; filter++)
            {
                var png = PngBuilder.Build(size, size, 2, 8, raw, filter, null);
                var image = PngImage.Decode(png);

                Assert.AreEqual(size, image.Width);
                Assert.AreEqual(size, image.Height);

                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var source = (y * size + x) * 3;
                        var target = (y * size + x) * 4;

                        Assert.AreEqual(raw[source], image.Rgba[target],
                            "red at " + x + "," + y + " with filter " + filter);
                        Assert.AreEqual(raw[source + 1], image.Rgba[target + 1],
                            "green at " + x + "," + y + " with filter " + filter);
                        Assert.AreEqual(raw[source + 2], image.Rgba[target + 2],
                            "blue at " + x + "," + y + " with filter " + filter);
                        Assert.AreEqual(255, image.Rgba[target + 3]);
                    }
                }
            }
        }

        [TestMethod]
        public void DecodesPaletteImage()
        {
            var palette = new byte[]
            {
                255, 0, 0,    // 0 red
                0, 255, 0,    // 1 green
                0, 0, 255     // 2 blue
            };

            var raw = new byte[] { 0, 1, 2, 1 };

            var png = PngBuilder.Build(4, 1, 3, 8, raw, 0, palette);
            var image = PngImage.Decode(png);

            Assert.AreEqual(255, image.Rgba[0]);
            Assert.AreEqual(0, image.Rgba[1]);

            Assert.AreEqual(0, image.Rgba[4]);
            Assert.AreEqual(255, image.Rgba[5]);

            Assert.AreEqual(0, image.Rgba[8]);
            Assert.AreEqual(0, image.Rgba[9]);
            Assert.AreEqual(255, image.Rgba[10]);
        }

        [TestMethod]
        public void RejectsSomethingThatIsNotAPng()
        {
            var failed = false;

            try
            {
                PngImage.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            }
            catch (Exception)
            {
                failed = true;
            }

            Assert.IsTrue(failed, "junk must be rejected, not decoded into nonsense");

            Assert.IsFalse(PngImage.LooksLikePng(new byte[] { 0, 1, 2 }));
        }

        [TestMethod]
        public void ConvertsToAFlatInTheRightPaletteIndices()
        {
            // A palette where entry 0 is black, 1 is white.
            var playpal = new byte[256 * 3];
            playpal[3] = 255; playpal[4] = 255; playpal[5] = 255;

            // A 64x64 image: left half black, right half white. The flat must come
            // out as indices 0 and 1, 4096 bytes in total.
            var raw = new byte[64 * 64 * 3];

            for (var y = 0; y < 64; y++)
            {
                for (var x = 32; x < 64; x++)
                {
                    var offset = (y * 64 + x) * 3;
                    raw[offset] = 255;
                    raw[offset + 1] = 255;
                    raw[offset + 2] = 255;
                }
            }

            var png = PngBuilder.Build(64, 64, 2, 8, raw, 0, null);
            var image = PngImage.Decode(png);
            var flat = Pk3Graphics.ToFlat(image, playpal);

            Assert.AreEqual(64 * 64, flat.Length, "a flat is exactly 4096 bytes");

            Assert.AreEqual(0, flat[0], "top-left is black");
            Assert.AreEqual(1, flat[63], "top-right is white");
            Assert.AreEqual(0, flat[64 * 63], "bottom-left is black");
            Assert.AreEqual(1, flat[64 * 64 - 1], "bottom-right is white");
        }

        [TestMethod]
        public void ScalesAnImageThatIsNot64x64()
        {
            var playpal = new byte[256 * 3];
            playpal[3] = 255; playpal[4] = 255; playpal[5] = 255;

            // 32x32 all white: must be scaled up to 64x64 and stay white.
            var png = PngBuilder.SolidRgb(32, 32, 255, 255, 255);
            var flat = Pk3Graphics.ToFlat(PngImage.Decode(png), playpal);

            Assert.AreEqual(64 * 64, flat.Length);

            foreach (var index in flat)
            {
                Assert.AreEqual(1, index, "every pixel must still be white");
            }
        }

        [TestMethod]
        public void RecognisesFlatPaths()
        {
            Assert.IsTrue(Pk3Graphics.IsFlatPath("flats/FLOOR0_1.png"));
            Assert.IsTrue(Pk3Graphics.IsFlatPath("FLOOR0_1.png"));
            Assert.IsFalse(Pk3Graphics.IsFlatPath("textures/WALL.png"));
            Assert.IsFalse(Pk3Graphics.IsFlatPath("flats/FLOOR0_1.txt"));

            Assert.AreEqual("FLOOR0_1", Pk3Graphics.LumpNameOf("flats/floor0_1.png"));
            Assert.IsNull(Pk3Graphics.LumpNameOf("flats/a-very-long-flat-name.png"));
        }
    }
}
