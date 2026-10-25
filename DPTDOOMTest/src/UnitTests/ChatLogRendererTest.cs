using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Video;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The chat log on a real screen.
    ///
    /// The data half is tested separately; what needs a screen is the layout: that lines wrap, that
    /// they do not reach the status bar, and that when the room runs out the **newest** message is
    /// the one that stays - a player is waiting for that one, not for the oldest.
    ///
    /// One test writes the frame out as a picture, because "the text fits" and "the text is
    /// readable" are different claims and only the second one is what a player sees.
    /// </summary>
    [TestClass]
    public class ChatLogRendererTest
    {
        private static DrawScreen NewScreen()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                return new DrawScreen(content.Wad, 320, 200);
            }
        }

        private static int BarTop
        {
            get { return 200 - StatusBarRenderer.Height; }
        }

        [TestMethod]
        public void NoLinesDrawNothing()
        {
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("No IWAD for the font");
                return;
            }

            var screen = NewScreen();

            Assert.AreEqual(0, ChatLogRenderer.Render(screen, new List<ChatEntry>(), 1, BarTop));
            Assert.AreEqual(0, ChatLogRenderer.Render(screen, null, 1, BarTop));
        }

        [TestMethod]
        public void EachLineIsOneLineWhenItIsShort()
        {
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("No IWAD for the font");
                return;
            }

            var screen = NewScreen();
            var entries = new List<ChatEntry>();

            entries.Add(new ChatEntry { Name = "a", Text = "hello" });
            entries.Add(new ChatEntry { Name = "b", Text = "there" });

            Assert.AreEqual(2, ChatLogRenderer.Render(screen, entries, 1, BarTop));
        }

        [TestMethod]
        public void ALongLineWrapsAndTakesMoreRoom()
        {
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("No IWAD for the font");
                return;
            }

            var screen = NewScreen();
            var entries = new List<ChatEntry>();

            entries.Add(new ChatEntry
            {
                Name = "talker",
                Text = "this message is long enough that it cannot fit on a single line of the " +
                       "screen so it has to wrap"
            });

            Assert.IsTrue(ChatLogRenderer.Render(screen, entries, 1, BarTop) > 1,
                "the message wrapped onto several lines");
        }

        [TestMethod]
        public void NothingIsDrawnOverTheStatusBar()
        {
            // The bar is drawn first, so text reaching it would be painted over the health and ammo.
            // The frame is stored by columns - height * x + y - and reading it as rows gives stripes.
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("No IWAD for the font");
                return;
            }

            var screen = NewScreen();
            var entries = new List<ChatEntry>();

            for (var i = 0; i < ChatLog.MaxLines; i++)
            {
                entries.Add(new ChatEntry { Name = "talker", Text = new string('x', 90) });
            }

            ChatLogRenderer.Render(screen, entries, 1, BarTop);

            for (var y = BarTop; y < 200; y++)
            {
                for (var x = 0; x < 320; x++)
                {
                    Assert.AreEqual(0, screen.Data[200 * x + y],
                        "a pixel below the status bar top at " + x + "," + y);
                }
            }
        }

        [TestMethod]
        public void TheNewestMessageSurvivesWhenTheRoomRunsOut()
        {
            // Three messages cannot all fit, so the oldest go and the newest stays - a player is
            // waiting for that one. Checked by giving the log room for exactly one line and
            // comparing the result with the newest message drawn on its own: if the oldest had
            // survived instead, the two frames would differ.
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("No IWAD for the font");
                return;
            }

            const string oldest = "the oldest message, which nobody is waiting for any more";
            const string newest = "newest";

            var entries = new List<ChatEntry>();

            entries.Add(new ChatEntry { Name = "a", Text = oldest });
            entries.Add(new ChatEntry { Name = "b", Text = oldest });
            entries.Add(new ChatEntry { Name = "c", Text = newest });

            var only = new List<ChatEntry>();
            only.Add(new ChatEntry { Name = "c", Text = newest });

            // Room for one line: the top margin plus a single eight-pixel row.
            var bottom = ChatLogRenderer.TopMargin + 8;

            var all = NewScreen();
            var expected = NewScreen();

            var drawn = ChatLogRenderer.Render(all, entries, 1, bottom);

            Assert.AreEqual(1, drawn, "exactly one line of room, so exactly one is drawn");

            ChatLogRenderer.Render(expected, only, 1, bottom);

            for (var i = 0; i < all.Data.Length; i++)
            {
                Assert.AreEqual(expected.Data[i], all.Data[i],
                    "the frame is the newest message, not the oldest");
            }
        }

        /// <summary>
        /// The log as a player sees it: three speakers, one long message, written out as a picture.
        /// </summary>
        [TestMethod]
        public void TheLogIsReadableOnARealScreen()
        {
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("No IWAD for the font");
                return;
            }

            DrawScreen screen;
            byte[] palette;

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                screen = new DrawScreen(content.Wad, 320, 200);

                palette = new byte[768];
                Array.Copy(content.Wad.ReadLump("PLAYPAL"), palette, palette.Length);
            }

            var entries = new List<ChatEntry>();

            entries.Add(new ChatEntry { Name = "alpha", Text = "wait for me at the red door" });
            entries.Add(new ChatEntry
            {
                Name = "bravo",
                Text = "the one upstairs? there are two of them and the lift is broken"
            });
            entries.Add(new ChatEntry { Name = "alpha", Text = "the one with the nukage" });

            Assert.AreEqual(4, ChatLogRenderer.Render(screen, entries, 1, BarTop),
                "three lines, one of which wrapped");

            Dump(screen, palette, "chat-log.bmp");
        }

        /// <summary>Writes the frame out, bottom-up, 24 bits per pixel, columns read as columns.</summary>
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
