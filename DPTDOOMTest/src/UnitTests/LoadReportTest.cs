using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The load report, and the two substitutions that make it worth having.
    ///
    /// Broken content is normal: a PWAD whose animation cycle points at a missing
    /// flat, a monster drawn with three of eight rotations, a thing type from a source
    /// port with more things than Doom has. Throwing on the first one gives the player
    /// an exception instead of a level with one still texture; carrying on silently
    /// means nobody ever learns why.
    ///
    /// The regression guard is the last test here: none of this may fire on the IWAD.
    /// If vanilla content starts reporting problems, the loader is wrong, not the WAD.
    /// </summary>
    [TestClass]
    public class LoadReportTest
    {
        [TestCleanup]
        public void CleanUp()
        {
            LoadReport.Clear();
        }

        [TestMethod]
        public void AnEmptyReportSaysNothing()
        {
            // A clean load must not end with "problems: none". A heading that is almost
            // always empty trains people to ignore it.
            LoadReport.Clear();

            Assert.IsFalse(LoadReport.HasProblems);
            Assert.AreEqual("", LoadReport.Describe());
        }

        [TestMethod]
        public void ProblemsAreGroupedByKind()
        {
            LoadReport.Clear();

            LoadReport.Add("bad animation cycle", "A to B", "those textures do not animate");
            LoadReport.Add("bad animation cycle", "C to D", "those textures do not animate");
            LoadReport.Add("unknown thing type", "type 9999 at 0,0", "the object is not placed");

            Assert.AreEqual(3, LoadReport.Count);

            var text = LoadReport.Describe();

            StringAssert.Contains(text, "bad animation cycle: 2", text);
            StringAssert.Contains(text, "unknown thing type: 1", text);
        }

        [TestMethod]
        public void EveryProblemSaysWhatHappenedInstead()
        {
            // "Something was skipped" without "and this is what you see" is a message a
            // player can do nothing with.
            LoadReport.Clear();
            LoadReport.Add("unknown thing type", "type 9999", "the object is not placed");

            StringAssert.Contains(LoadReport.Describe(), "the object is not placed");
        }

        [TestMethod]
        public void TheListingIsLimitedButTheCountIsExact()
        {
            // A mod with eighty broken flats would otherwise bury the report it is part
            // of.
            LoadReport.Clear();

            for (var i = 0; i < 20; i++)
            {
                LoadReport.Add("bad animation cycle", "cycle " + i, "no animation");
            }

            var text = LoadReport.Describe();

            StringAssert.Contains(text, "bad animation cycle: 20", "the count is exact");
            StringAssert.Contains(text, "and 16 more", "but the listing stops early: " + text);
        }

        [TestMethod]
        public void ClearEmptiesIt()
        {
            LoadReport.Add("bad animation cycle", "A to B", "no animation");
            Assert.IsTrue(LoadReport.HasProblems);

            LoadReport.Clear();
            Assert.IsFalse(LoadReport.HasProblems);
        }

        // ------------------------------------------------------------ sprites

        [TestMethod]
        public void AnIncompleteSpriteIsFilledFromTheFrontView()
        {
            // A PWAD that adds a monster often draws the front view and forgets the
            // rest. Vanilla draws nothing for a missing rotation, so the monster
            // vanishes when it turns; throwing refuses to start the game at all. The
            // front view from every angle is the least wrong of the three.
            var info = new SpriteLookup.SpriteInfo();

            var front = new Patch("TESTA1", 1, 1, 0, 0, new Column[0][]);
            info.Patches[0] = front;

            var missing = info.MissingRotations();

            Assert.AreEqual(7, missing.Count, "seven rotations are missing");
            Assert.IsFalse(missing.Contains(0), "the front view is not one of them");

            Assert.IsTrue(info.FillMissingRotations(), "the gaps can be filled");

            Assert.AreEqual(0, info.MissingRotations().Count, "and none are left");

            for (var i = 0; i < 8; i++)
            {
                Assert.AreSame(front, info.Patches[i], "rotation " + i + " shows the front view");
            }

            // All eight are the same picture, so the sprite has no rotation - which is
            // what makes the renderer draw it from every angle instead of picking one.
            Assert.IsFalse(info.HasRotation(), "the filled sprite is not rotational");
        }

        [TestMethod]
        public void AFrameWithNothingAtAllCannotBeFilled()
        {
            // There is nothing to copy from. The caller drops the frame rather than
            // indexing an empty array at render time.
            var info = new SpriteLookup.SpriteInfo();

            Assert.AreEqual(8, info.MissingRotations().Count);
            Assert.IsFalse(info.FillMissingRotations(),
                "a frame with no patches at all has nothing to fall back on");
        }

        [TestMethod]
        public void ACompleteSpriteIsLeftAlone()
        {
            var info = new SpriteLookup.SpriteInfo();

            for (var i = 0; i < 8; i++)
            {
                info.Patches[i] = new Patch("TESTA" + (i + 1), 1, 1, 0, 0, new Column[0][]);
            }

            Assert.AreEqual(0, info.MissingRotations().Count);
            Assert.IsTrue(info.HasRotation(), "eight different pictures is a rotation");
        }

        // ------------------------------------------------------------ vanilla

        [TestMethod]
        public void TheIwadLoadsWithoutAWord()
        {
            // The guard that matters most. If vanilla content starts reporting
            // problems, the loader is wrong and the report has become noise.
            LoadReport.Clear();

            // -nodata: this test is about the IWAD loading clean, not about what the game writes
            // to the data directory. Without it the run would unpack the actor folder into the
            // machine's real data directory, and a stale folder there would then fail this test
            // for a reason that has nothing to do with the IWAD.
            using (var content = new GameContent(
                new CommandLineArgs(new[] { "-iwad", WadPath.Doom2, "-nodata" })))
            {
                Assert.IsFalse(LoadReport.HasProblems,
                    "the IWAD loads clean, but reported:\n" + LoadReport.Describe());
            }
        }

        // ------------------------------------------------------------ helpers

    }
}
