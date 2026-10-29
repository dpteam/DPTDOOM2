using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;
using DPTDOOMTest.CompatibilityTests;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The frame reader against the shapes that actually appear in DOOM ACE's ademo1.wad.
    ///
    /// Each of these was a real failure found by running that mod, and each has the same shape of
    /// consequence: one line the reader did not understand used to poison everything after it, so a
    /// single marker or keyword turned into thousands of reports about tokens the modder never wrote
    /// as sprites. The tests therefore check both the meaning of the line and that the file after it
    /// still reads.
    /// </summary>
    [TestClass]
    public class DecorateFrameParsingTest
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

        private static DecorateActor ParseSingle(string text, out List<string> problems)
        {
            problems = new List<string>();

            var actors = DecorateConverter.Parse(text, problems);

            Assert.AreEqual(1, actors.Count, "one actor in, one actor out");
            return actors[0];
        }

        [TestMethod]
        public void TheKeepSpriteMarkerReusesThePreviousFrame()
        {
            // "----" is not a misspelt sprite: it means "keep the picture", and a flag-only frame
            // written this way must not lose the sprite the actor was showing.
            var actor = ParseSingle(
                "ACTOR Keeper 3001\n{\n  States\n  {\n  See:\n" +
                "    TROO A 4\n" +
                "    \"----\" B 4 A_ChangeFlag(\"NOPAIN\", 1)\n" +
                "  }\n}", out var problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            Assert.AreEqual("TROO", actor.Chains[0].Frames[1].Sprite);
        }

        [TestMethod]
        public void TheKeepSpriteMarkerWorksAcrossChains()
        {
            // A corpse chain continues the death animation, so "keep the previous sprite" looks at
            // the frame before it in the actor's state list, not only within its own label.
            var actor = ParseSingle(
                "ACTOR Keeper 3001\n{\n  States\n  {\n  Death:\n" +
                "    TROO A 4\n" +
                "    PLAY B 4\n" +
                "  MakeCorpse:\n" +
                "    \"----\" A 0 A_ChangeFlag(\"AMBUSH\", 1)\n" +
                "  }\n}", out var problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var corpse = actor.Chains[1];

            Assert.AreEqual("MakeCorpse", corpse.Label);
            Assert.AreEqual("PLAY", corpse.Frames[0].Sprite,
                "the corpse keeps the last sprite of the chain before it");
        }

        [TestMethod]
        public void KeepSpriteWithNothingBeforeItIsNamed()
        {
            var actor = ParseSingle(
                "ACTOR Keeper 3001\n{\n  States\n  {\n  See:\n" +
                "    \"----\" A 4\n" +
                "  }\n}", out var problems);

            Assert.AreEqual("TNT1", actor.Chains[0].Frames[0].Sprite,
                "an invisible frame is the honest equivalent of keeping nothing");
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "no previous frame");
        }

        [TestMethod]
        public void TheInvisibleSpriteIsAccepted()
        {
            // TNT1 is DECORATE's invisible frame and does not exist in vanilla Doom's sprite list.
            var actor = ParseSingle(
                "ACTOR Keeper 3001\n{\n  States\n  {\n  See:\n" +
                "    TNT1 A 4\n" +
                "  }\n}", out var problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            Assert.AreEqual("TNT1", actor.Chains[0].Frames[0].Sprite);
        }

        [TestMethod]
        public void OneUnreadableFrameDoesNotPoisonTheRestOfTheFile()
        {
            // The failure this guards against: leaving the argument list of the dropped frame in the
            // stream made '(' , ',' and ')' the "sprites" of every following line, so a single bad
            // frame produced thousands of reports.
            var actor = ParseSingle(
                "ACTOR Keeper 3001\n{\n  States\n  {\n  See:\n" +
                "    TROOO A 4 A_ChangeFlag(\"NOPAIN\", 1)\n" +
                "    TROO B 4\n" +
                "    TROO C 4\n" +
                "  }\n}", out var problems);

            Assert.AreEqual(1, problems.Count,
                "one bad line, one report: " + string.Join("; ", problems.ToArray()));

            var frames = actor.Chains[0].Frames;

            Assert.AreEqual(2, frames.Count, "the two readable frames are still converted");
            Assert.AreEqual("TROO", frames[0].Sprite);
            Assert.AreEqual("TROO", frames[1].Sprite);
        }

        [TestMethod]
        public void AFrameModifierIsConsumedAndNamedOnce()
        {
            // offset() and Fast sit between the tics and the action. Not reading them made the
            // state reader take "offset" for the next frame's sprite.
            var actor = ParseSingle(
                "ACTOR Keeper 3001\n{\n  States\n  {\n  See:\n" +
                "    TROO A 4 offset(1, 36) A_Look\n" +
                "    TROO B 4 Fast A_Look\n" +
                "    TROO C 4 Fast A_Look\n" +
                "  }\n}", out var problems);

            var frames = actor.Chains[0].Frames;

            Assert.AreEqual(3, frames.Count);
            Assert.AreEqual("A_Look", frames[0].Action, "the action after offset() is still read");
            Assert.AreEqual("A_Look", frames[1].Action);

            var text = string.Join("; ", problems.ToArray());

            Assert.AreEqual(2, problems.Count,
                "offset and Fast are named once each, not once per frame: " + text);
            StringAssert.Contains(text, "offset");
        }

        [TestMethod]
        public void AGotoOffsetCountsStates()
        {
            // "Goto Death + 2" means the state two entries after the label, which is the third
            // frame of that chain. Leaving "+ 2" behind made '+' look like a sprite name.
            var actor = ParseSingle(
                "ACTOR Keeper 3001\n{\n  States\n  {\n  See:\n" +
                "    TROO A 4\n" +
                "  goto Death + 2\n" +
                "  Death:\n" +
                "    TROO B 4\n" +
                "    TROO C 4\n" +
                "    TROO D 4\n" +
                "  }\n}", out var problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            Assert.AreEqual("Goto Death+2", actor.Chains[0].Next);
        }

        [TestMethod]
        public void AGotoOffsetPastTheEndIsReported()
        {
            var problems = new List<string>();

            DecorateConverter.Convert(
                "ACTOR Keeper 3001\n{\n  States\n  {\n  See:\n" +
                "    TROO A 4\n" +
                "  goto Death + 5\n" +
                "  Death:\n" +
                "    TROO B 4\n" +
                "  }\n}", DoomInfo.MobjInfos, problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "past the end");
        }
    }
}
