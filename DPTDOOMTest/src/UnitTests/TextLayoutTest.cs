using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Video;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Word wrapping for the message line.
    ///
    /// The line used to draw one string and advance until it ran out of characters, which at 320
    /// pixels is about forty of them - so a chat message, which may be ninety-six, was cut off at the
    /// right edge with nothing to say so. These tests pin the arithmetic, and one of them writes the
    /// screen to a BMP so the result can be looked at rather than only counted.
    /// </summary>
    [TestClass]
    public class TextLayoutTest
    {
        /// <summary>
        /// The Doom font's own widths, roughly: a capital is about six pixels and a space is four.
        ///
        /// A real measure would need the WAD, and this class deliberately knows nothing about fonts -
        /// so the tests supply the measurement the same way the renderer does.
        /// </summary>
        private static int WidthOf(char c)
        {
            return c == ' ' ? 4 : 6;
        }

        [TestMethod]
        public void AShortLineStaysOneLine()
        {
            var lines = TextLayout.Wrap("hello", 320, WidthOf);

            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("hello", lines[0]);
        }

        [TestMethod]
        public void ALongLineBreaksAtASpace()
        {
            // Ten words of five letters plus spaces: 6*5 + 4 = 34 pixels each, so three fit in 120.
            var text = "alpha bravo charlie delta echo foxtrot golf hotel india juliet";

            var lines = TextLayout.Wrap(text, 120, WidthOf);

            Assert.IsTrue(lines.Count > 1, "the text has to be split");

            foreach (var line in lines)
            {
                Assert.IsTrue(Measure(line) <= 120, "'" + line + "' fits in 120 pixels");
            }

            // No word was lost or broken, which is what breaking at a space is for.
            Assert.AreEqual(text.Replace(" ", ""), string.Join("", new System.Collections.Generic.List<string>(lines).ToArray()).Replace(" ", ""));
        }

        [TestMethod]
        public void AnExplicitNewlineIsAlwaysABreak()
        {
            // Several of the engine's own strings contain one, and drawing them as a single line
            // silently swallowed the break: a newline has no glyph, so the drawing loop skipped it
            // without moving the cursor.
            var lines = TextLayout.Wrap("first\nsecond", 320, WidthOf);

            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("first", lines[0]);
            Assert.AreEqual("second", lines[1]);
        }

        [TestMethod]
        public void AnEmptyParagraphIsABlankLine()
        {
            var lines = TextLayout.Wrap("a\n\nb", 320, WidthOf);

            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual("", lines[1], "a double newline is a blank line, not a merged one");
        }

        [TestMethod]
        public void AWordLongerThanTheLineIsBrokenRatherThanDropped()
        {
            // A chat message can be a URL or a run of digits with no spaces in it, and refusing the
            // whole word would lose the message.
            var lines = TextLayout.Wrap("aaaaaaaaaaaaaaaaaaaaaaaaaaaa", 60, WidthOf);

            Assert.IsTrue(lines.Count > 1, "the run has to be split");

            foreach (var line in lines)
            {
                Assert.IsTrue(Measure(line) <= 60, "'" + line + "' fits");
            }

            Assert.AreEqual(28, string.Join("", new System.Collections.Generic.List<string>(lines).ToArray()).Length, "no character was lost");
        }

        [TestMethod]
        public void TrailingSpacesAreNotKept()
        {
            // A break at a space consumes it, so the next line does not start with one.
            var lines = TextLayout.Wrap("alpha bravo charlie", 60, WidthOf);

            foreach (var line in lines)
            {
                Assert.AreEqual(line.TrimEnd(), line, "'" + line + "' has no trailing space");
            }
        }

        [TestMethod]
        public void NothingFitsSoNothingIsTruncated()
        {
            // A width of zero cannot be satisfied. The honest answer is the explicit lines as they
            // were written, not an empty message.
            var lines = TextLayout.Wrap("first\nsecond", 0, WidthOf);

            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("first", lines[0]);
        }

        [TestMethod]
        public void OnlyTheLinesThatFitAreDrawn()
        {
            Assert.AreEqual(3, TextLayout.LinesThatFit(0, 8, 24), "three lines of eight in 24 pixels");
            Assert.AreEqual(0, TextLayout.LinesThatFit(30, 8, 24), "no room left");
            Assert.AreEqual(0, TextLayout.LinesThatFit(0, 0, 100), "a zero line height draws nothing");
        }

        /// <summary>
        /// The message line, drawn into a real screen and written out as a picture.
        ///
        /// This is the one check a count cannot make: whether a long message is readable on the
        /// screen rather than running off the right edge. The file is left behind on purpose - the
        /// assertion proves the text was split, and the picture proves it looks right.
        /// </summary>
        [TestMethod]
        public void ALongMessageWrapsOnARealScreen()
        {
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("No IWAD to take the font from");
                return;
            }

            DrawScreen screen;
            byte[] palette;

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                screen = new DrawScreen(content.Wad, 320, 200);

                // The first PLAYPAL entry, which is the palette the game starts in. Read here
                // because a screen holds palette indices and a picture needs colours.
                palette = new byte[768];
                Array.Copy(content.Wad.ReadLump("PLAYPAL"), palette, palette.Length);
            }

            const string message =
                "This is a long chat message that a player typed, and on a screen of three hundred " +
                "and twenty pixels it cannot possibly fit on one line";

            var drawn = screen.DrawTextWrapped(message, 0, 7, 1, 200);

            Assert.IsTrue(drawn > 1, "the message was split over several lines");

            // Every drawn line is inside the screen. The right edge is what the old code broke.
            var lines = TextLayout.Wrap(message, 320, c => screen.CharWidth(c, 1));

            foreach (var line in lines)
            {
                var width = 0;

                foreach (var ch in line)
                {
                    width += screen.CharWidth(ch, 1);
                }

                Assert.IsTrue(width <= 320, "'" + line + "' is " + width + " pixels wide");
            }

            Dump(screen, palette, "message-wrap.bmp");
        }

        /// <summary>
        /// A message long enough to reach the bottom stops above the status bar.
        ///
        /// The bar is drawn before the message, so text that reached it would be painted over the
        /// health and ammo - which reads as a rendering fault rather than as a long message. The
        /// limit is passed in by the renderer as the top of the bar, and this checks that nothing
        /// is drawn below it.
        /// </summary>
        [TestMethod]
        public void AMessageTooLongForTheRoomStopsAboveTheStatusBar()
        {
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("No IWAD to take the font from");
                return;
            }

            DrawScreen screen;

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                screen = new DrawScreen(content.Wad, 320, 200);
            }

            var barTop = 200 - StatusBarRenderer.Height;

            var message = string.Empty;

            for (var i = 0; i < 40; i++)
            {
                message += "word ";
            }

            screen.DrawTextWrapped(message, 0, 7, 1, barTop);

            // The buffer starts at zero and glyph pixels are not zero, so anything below the bar top
            // means the message was drawn over the status bar.
            //
            // The frame is stored by columns - `height * x + y` - not by rows. Reading it as rows
            // gives a picture of stripes, which is a trap this project has fallen into before.
            for (var y = barTop; y < 200; y++)
            {
                for (var x = 0; x < 320; x++)
                {
                    Assert.AreEqual(0, screen.Data[200 * x + y],
                        "a pixel below the status bar top at " + x + "," + y);
                }
            }
        }

        private static int Measure(string line)
        {
            var width = 0;

            foreach (var ch in line)
            {
                width += WidthOf(ch);
            }

            return width;
        }

        /// <summary>Writes the frame out, bottom-up and 24 bits per pixel as a BMP is.</summary>
        private static void Dump(DrawScreen screen, byte[] palette, string name)
        {
            var path = Path.Combine(Path.GetTempPath(), name);

            var width = screen.Width;
            var height = screen.Height;
            var stride = (width * 3 + 3) & ~3;
            var size = 54 + stride * height;

            var bytes = new byte[size];

            bytes[0] = (byte)'B';
            bytes[1] = (byte)'M';
            BitConverter.GetBytes(size).CopyTo(bytes, 2);
            BitConverter.GetBytes(54).CopyTo(bytes, 10);
            BitConverter.GetBytes(40).CopyTo(bytes, 14);
            BitConverter.GetBytes(width).CopyTo(bytes, 18);
            BitConverter.GetBytes(height).CopyTo(bytes, 22);
            BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
            BitConverter.GetBytes((short)24).CopyTo(bytes, 28);

            for (var y = 0; y < height; y++)
            {
                // Bottom row first, which is what a positive height in the header means. The frame
                // itself is stored by columns - `height * x + y` - which is a trap this project has
                // fallen into before: reading it as rows gives a picture of nothing but stripes.
                for (var x = 0; x < width; x++)
                {
                    var target = 54 + y * stride + x * 3;
                    var index = screen.Data[height * x + (height - 1 - y)] * 3;

                    bytes[target] = palette[index];
                    bytes[target + 1] = palette[index + 1];
                    bytes[target + 2] = palette[index + 2];
                }
            }

            File.WriteAllBytes(path, bytes);
        }
    }
}
