using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// ANIMDEFS: which pictures animate, in which order, for how long.
    ///
    /// The engine's own ANIMATED lump can only describe a contiguous range at one speed, and the
    /// parser here reads the shape a mod actually needs: an explicit list, each frame with its own
    /// duration. That means the timing logic is new code and gets tested on its own, separate from
    /// whether the pictures resolve.
    /// </summary>
    [TestClass]
    public class AnimatedTextureTest
    {
        [TestInitialize]
        public void Before()
        {
            AnimatedTexture.Clear();
        }

        [TestCleanup]
        public void After()
        {
            AnimatedTexture.Clear();
        }

        [TestMethod]
        public void AFlatCycleIsRead()
        {
            var problems = new List<string>();

            AnimatedTexture.Add(
                "flat NUKAGE1\n" +
                "{\n" +
                "    pic NUKAGE1 tics 8\n" +
                "    pic NUKAGE2 tics 4\n" +
                "}", "test", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            Assert.AreEqual(1, AnimatedTexture.Count);

            var definition = AnimatedTexture.Definitions[0];

            Assert.IsFalse(definition.IsTexture, "a flat, not a texture");
            Assert.AreEqual(2, definition.Frames.Count);
            Assert.AreEqual("NUKAGE1", definition.Frames[0]);
            Assert.AreEqual(8, definition.Durations[0]);
            Assert.AreEqual("NUKAGE2", definition.Frames[1]);
            Assert.AreEqual(4, definition.Durations[1]);
        }

        [TestMethod]
        public void ATextureCycleIsRead()
        {
            var problems = new List<string>();

            AnimatedTexture.Add("texture MYWALL { pic MYWALL1 tics 2 pic MYWALL2 tics 2 }",
                "test", problems);

            Assert.AreEqual(1, AnimatedTexture.Count);
            Assert.IsTrue(AnimatedTexture.Definitions[0].IsTexture);
        }

        [TestMethod]
        public void TicsIsOptionalAndDefaultsToOne()
        {
            // ZDoom's default, and the common case in a hand-written file: a frame without a duration.
            var problems = new List<string>();

            AnimatedTexture.Add("flat F1 { pic F1 pic F2 tics 3 }", "test", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var definition = AnimatedTexture.Definitions[0];

            Assert.AreEqual(1, definition.Durations[0], "no 'tics' means one");
            Assert.AreEqual(3, definition.Durations[1]);
        }

        [TestMethod]
        public void CommentsInBothStylesAreSkipped()
        {
            var problems = new List<string>();

            AnimatedTexture.Add(
                "// a line comment\n" +
                "/* and a block\n" +
                "   comment */\n" +
                "flat F1\n" +
                "{\n" +
                "    pic F1 tics 2 // trailing\n" +
                "    pic F2 tics 2\n" +
                "}", "test", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            Assert.AreEqual(2, AnimatedTexture.Definitions[0].Frames.Count);
        }

        [TestMethod]
        public void ACycleOfOneFrameIsReported()
        {
            // It does not cycle. Adding it would make the renderer redraw the same picture for ever.
            var problems = new List<string>();

            AnimatedTexture.Add("flat F1 { pic F1 tics 8 }", "test", problems);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "fewer than two");
            Assert.AreEqual(0, AnimatedTexture.Count);
        }

        [TestMethod]
        public void AnUnsupportedKeywordIsNamedRatherThanIgnored()
        {
            // warp, cameratexture, switch are real ANIMDEFS keywords. Accepting them silently would
            // tell a modder their scrolling wall works when it does not.
            var problems = new List<string>();

            AnimatedTexture.Add(
                "warp MYWALL { pic MYWALL1 tics 2 pic MYWALL2 tics 2 }\n" +
                "flat F1 { pic F1 tics 2 pic F2 tics 2 }", "test", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "warp");
            StringAssert.Contains(text, "not an ANIMDEFS keyword");

            // And the cycle after it still applies: one unsupported entry costs itself.
            Assert.AreEqual(1, AnimatedTexture.Count);
        }

        [TestMethod]
        public void AnUnsupportedDirectiveInsideABlockIsNamed()
        {
            var problems = new List<string>();

            AnimatedTexture.Add(
                "flat F1\n" +
                "{\n" +
                "    pic F1 tics 2\n" +
                "    warp 2\n" +
                "    pic F2 tics 2\n" +
                "}", "test", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "warp");

            // The frames on both sides of it survive.
            Assert.AreEqual(2, AnimatedTexture.Definitions[0].Frames.Count);
        }

        [TestMethod]
        public void ABlockThatIsNotClosedIsReported()
        {
            var problems = new List<string>();

            AnimatedTexture.Add("flat F1 { pic F1 tics 2 pic F2 tics 2", "test", problems);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "not closed");
        }

        [TestMethod]
        public void ATicsValueThatIsNotANumberIsReported()
        {
            var problems = new List<string>();

            AnimatedTexture.Add("flat F1 { pic F1 tics soon pic F2 tics 2 }", "test", problems);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "whole number");
        }

        [TestMethod]
        public void APictureWithoutANameIsReported()
        {
            var problems = new List<string>();

            AnimatedTexture.Add("flat F1 { pic tics 2 pic F2 tics 2 }", "test", problems);

            Assert.IsTrue(problems.Count > 0);
        }

        // ------------------------------------------------------------ timing

        [TestMethod]
        public void AListCycleWalksItsOwnDurations()
        {
            // The reason the list shape exists: each frame has its own duration, so the cycle is not a
            // division but a walk. Frames 10 and 20, eight tics and four tics.
            var animation = new TextureAnimationInfo(false, new[] { 10, 20 }, new[] { 8, 4 });

            Assert.AreEqual(10, animation.PictureAt(0));
            Assert.AreEqual(10, animation.PictureAt(7));
            Assert.AreEqual(20, animation.PictureAt(8));
            Assert.AreEqual(20, animation.PictureAt(11));
            Assert.AreEqual(10, animation.PictureAt(12), "the cycle repeats");
            Assert.AreEqual(20, animation.PictureAt(20));
        }

        [TestMethod]
        public void AListCycleWithoutDurationsUsesTheFirstSpeed()
        {
            // ANIMDEFS always gives durations, but the constructor allows null and the fallback has to
            // be sane rather than a division by zero.
            var animation = new TextureAnimationInfo(false, new[] { 5, 6, 7 }, null);

            Assert.AreEqual(5, animation.PictureAt(0));
            Assert.AreEqual(6, animation.PictureAt(1));
            Assert.AreEqual(7, animation.PictureAt(2));
            Assert.AreEqual(5, animation.PictureAt(3));
        }

        [TestMethod]
        public void AContiguousRangeStillWorksTheWayItAlwaysDid()
        {
            // The engine's own shape must be untouched: this is the path every vanilla animation takes.
            var animation = new TextureAnimationInfo(false, 100, 100, 4, 8);

            Assert.IsNull(animation.Frames, "a range has no explicit list");

            Assert.AreEqual(100, animation.PictureAt(0));
            Assert.AreEqual(101, animation.PictureAt(8));
            Assert.AreEqual(103, animation.PictureAt(24));
            Assert.AreEqual(100, animation.PictureAt(32), "the range repeats");
        }

        [TestMethod]
        public void AnEmptyDurationsListDoesNotDivideByZero()
        {
            var animation = new TextureAnimationInfo(false, new[] { 1, 2 }, new int[0]);

            // Zero-length durations mean "no time at all", which the guard turns into the first frame
            // rather than an exception in the middle of a frame.
            Assert.AreEqual(1, animation.PictureAt(0));
            Assert.AreEqual(1, animation.PictureAt(99));
        }
    }
}
