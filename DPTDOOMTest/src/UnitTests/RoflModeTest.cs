using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Video;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// ROFL mode: when it turns itself on, and that it changes nothing about the world.
    ///
    /// The second half is the load-bearing one. Mirroring the *view* rather than the level was
    /// chosen precisely so that the simulation is untouched - the world hash is identical, a
    /// demo replays exactly, and lockstep never has to know the mode exists. If that ever stops
    /// being true, this is where it shows up.
    /// </summary>
    [TestClass]
    public class RoflModeTest
    {
        /// <summary>
        /// A screen to flip. The real one takes a WAD because it loads the font, and this test
        /// has no use for the font - but the constructor is the only way in, and building a
        /// fake would mean testing a fake.
        /// </summary>
        private static DrawScreen NewScreen()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                return new DrawScreen(content.Wad, 320, 200);
            }
        }
        [TestMethod]
        public void AprilFirstTurnsItOnWhenNobodyHasChosen()
        {
            var config = new Config();

            RoflMode.ApplyDefault(config, new DateTime(2026, 4, 1, 9, 30, 0));

            Assert.IsTrue(config.rofl_mode, "April 1 is the joke");
            Assert.IsFalse(config.rofl_set, "and it is still not the player's choice");
        }

        [TestMethod]
        public void EveryOtherDayLeavesItOff()
        {
            var config = new Config();

            RoflMode.ApplyDefault(config, new DateTime(2026, 3, 31, 23, 59, 0));

            Assert.IsFalse(config.rofl_mode);

            RoflMode.ApplyDefault(config, new DateTime(2026, 4, 2, 0, 1, 0));

            Assert.IsFalse(config.rofl_mode, "the joke lasts a day, not a month");
        }

        [TestMethod]
        public void AnExplicitChoiceSurvivesAprilFirst()
        {
            // The rule that stops the mode being a prank on the owner of the machine: somebody
            // who turned it off last April must not find it on again this April.
            var config = new Config();

            RoflMode.Toggle(config);
            Assert.IsTrue(config.rofl_mode);
            Assert.IsTrue(config.rofl_set);

            RoflMode.Toggle(config);
            Assert.IsFalse(config.rofl_mode, "off, because they said so");

            RoflMode.ApplyDefault(config, new DateTime(2027, 4, 1, 12, 0, 0));

            Assert.IsFalse(config.rofl_mode, "April 1 does not override a choice");
        }

        [TestMethod]
        public void TheChoiceIsRememberedInTheConfigFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-rofl.cfg");

            try
            {
                var saved = new Config();
                RoflMode.Toggle(saved);
                saved.Save(path);

                var loaded = new Config(path);

                Assert.IsTrue(loaded.rofl_mode, "the mode came back");
                Assert.IsTrue(loaded.rofl_set, "and so did the fact that it was chosen");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void AFreshConfigStaysSilentAboutIt()
        {
            // A config file that mentions a secret mode is a hint, and this one is supposed to
            // have no hints.
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-rofl-silent.cfg");

            try
            {
                new Config().Save(path);

                var text = File.ReadAllText(path);

                Assert.IsFalse(text.Contains("rofl"), "an untouched config says nothing about it");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void TheMessageSaysWhatHappened()
        {
            StringAssert.Contains(RoflMode.Message(true), "ENABLED");
            StringAssert.Contains(RoflMode.Message(false), "DISABLED");
        }

        [TestMethod]
        public void MirroringAFrameIsItsOwnInverse()
        {
            // The one invariant a flip has to satisfy, checked on a buffer rather than on the
            // screen: mirroring twice is doing nothing. This catches an off-by-one in the
            // column arithmetic but not a wrong axis, which is why the two tests below exist.
            var screen = NewScreen();

            var original = new byte[320 * 200];
            var random = new Random(12345);

            for (var i = 0; i < original.Length; i++)
            {
                original[i] = (byte)random.Next(256);
            }

            Array.Copy(original, screen.Data, original.Length);

            screen.MirrorRect(0, 0, 320, 200);
            screen.MirrorRect(0, 0, 320, 200);

            for (var i = 0; i < original.Length; i++)
            {
                if (original[i] != screen.Data[i])
                {
                    Assert.Fail("pixel " + i + " changed: " + original[i] + " became " +
                                screen.Data[i]);
                }
            }
        }

        [TestMethod]
        public void MirroringMovesPixelsAcrossColumnsNotAcrossRows()
        {
            // The axis test. The buffer is column-major, so a row-major flip produces something
            // that looks almost like a mirror - and did, for a while. Marking one pixel and
            // checking where it lands is the only way to tell the two apart.
            var screen = NewScreen();

            var column = 5;
            var row = 7;

            screen.Data[column * 200 + row] = 42;

            screen.MirrorRect(0, 0, 320, 200);

            Assert.AreEqual(42, screen.Data[(319 - column) * 200 + row],
                "the pixel moved to the mirrored column, in the same row");

            Assert.AreEqual(0, screen.Data[column * 200 + row],
                "and the original place is now whatever was opposite it");
        }

        [TestMethod]
        public void MirroringTurnsTheFirstColumnIntoTheLast()
        {
            // The buffer is column-major: index x * height + y. Written as rows, this test
            // passed against a broken implementation, which is how the flip came out looking
            // scrambled instead of mirrored.
            var screen = NewScreen();

            screen.Data[0] = 7;                       // column 0, row 0
            screen.Data[319 * 200] = 9;               // column 319, row 0

            screen.MirrorRect(0, 0, 320, 200);

            Assert.AreEqual(9, screen.Data[0], "the last column is now the first");
            Assert.AreEqual(7, screen.Data[319 * 200], "and the first is the last");
        }

        [TestMethod]
        public void MirroringOnlyTouchesTheRectangleItIsGiven()
        {
            // The view window is not the whole screen: the status bar sits under it and must
            // stay the right way round.
            var screen = NewScreen();

            screen.Data[0] = 1;                     // column 0, row 0 - inside
            screen.Data[319 * 200] = 2;             // column 319, row 0 - inside
            screen.Data[0 * 200 + 190] = 3;         // column 0, row 190 - below the window
            screen.Data[319 * 200 + 190] = 4;       // column 319, row 190 - below the window

            screen.MirrorRect(0, 0, 320, 168);

            Assert.AreEqual(2, screen.Data[0]);
            Assert.AreEqual(1, screen.Data[319 * 200]);
            Assert.AreEqual(3, screen.Data[0 * 200 + 190], "the row below the window is untouched");
            Assert.AreEqual(4, screen.Data[319 * 200 + 190]);
        }
    }
}
