using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// LOCKDEFS: what a numbered lock needs, says and plays.
    ///
    /// The grammar is small but it has one thing the other formats do not: a message is a
    /// **sentence**, so the tokenizer has to keep a quoted string whole. A reader that split on
    /// spaces would leave the mod with the first word of its own message and no error.
    ///
    /// The other thing pinned here is what happens to the directives this port cannot honour.
    /// `remotemessage` and `mapcolor` are real LOCKDEFS keywords with no meaning in this engine -
    /// one line to the player, one automap colour - and both are named in the report rather than
    /// accepted and ignored.
    /// </summary>
    [TestClass]
    public class LockDefsTest
    {
        [TestInitialize]
        public void Before()
        {
            LockDefs.Clear();
        }

        [TestCleanup]
        public void After()
        {
            LockDefs.Clear();
        }

        private static List<string> Parse(string text)
        {
            var problems = new List<string>();
            LockDefs.Add(text, "test", problems);
            return problems;
        }

        private static string All(List<string> problems)
        {
            return string.Join("; ", problems.ToArray());
        }

        [TestMethod]
        public void ALockIsRead()
        {
            var problems = Parse(
                "lock 1\n" +
                "{\n" +
                "    message \"You need the red card\"\n" +
                "    redcard\n" +
                "}");

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(1, LockDefs.Count);

            var definition = LockDefs.Find(1);

            Assert.IsNotNull(definition);
            Assert.AreEqual("You need the red card", definition.Message);
            Assert.AreEqual(1, definition.Groups.Count);
            Assert.AreEqual(CardType.RedCard, definition.Groups[0].Keys[0]);
            Assert.AreEqual(Sfx.OOF, definition.Sound, "the default refusal sound");
        }

        [TestMethod]
        public void AMessageKeepsItsSpaces()
        {
            // The reason this format needs its own tokenizer.
            var problems = Parse("lock 2 { message \"Find a blue key, then come back\" bluecard }");

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual("Find a blue key, then come back", LockDefs.Find(2).Message);
        }

        [TestMethod]
        public void AnUnquotedMessageIsStillOneValue()
        {
            var problems = Parse("lock 2 { message Nope bluecard }");

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual("Nope", LockDefs.Find(2).Message);
        }

        [TestMethod]
        public void EqualsIsAcceptedBetweenAKeywordAndItsValue()
        {
            // Both spellings appear in real files, and a reader that only took one would fail on
            // half of them for no reason.
            var problems = Parse("lock 3 { message = \"Nope\" lockedsound = oof bluecard }");

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual("Nope", LockDefs.Find(3).Message);
            Assert.AreEqual(Sfx.OOF, LockDefs.Find(3).Sound);
        }

        [TestMethod]
        public void EveryKeyNameIsUnderstood()
        {
            var problems = Parse(
                "lock 4\n" +
                "{\n" +
                "    bluecard\n" +
                "    yellowcard\n" +
                "    redcard\n" +
                "    blueskull\n" +
                "    yellowskull\n" +
                "    redskull\n" +
                "}");

            Assert.AreEqual(0, problems.Count, All(problems));

            // A bare key is its own group: every one of them is needed.
            Assert.AreEqual(6, LockDefs.Find(4).Groups.Count);
        }

        [TestMethod]
        public void AnyMakesOneGroupOfSeveralKeys()
        {
            // "a blue card or a blue skull" - the shape Boom cannot write.
            var problems = Parse("lock 5 { any { bluecard blueskull } }");

            Assert.AreEqual(0, problems.Count, All(problems));

            var definition = LockDefs.Find(5);

            Assert.AreEqual(1, definition.Groups.Count);
            Assert.AreEqual(2, definition.Groups[0].Keys.Count);
        }

        [TestMethod]
        public void AnyColourIsShorthandForTheCardAndTheSkull()
        {
            var problems = Parse("lock 6 { anyblue }");

            Assert.AreEqual(0, problems.Count, All(problems));

            var group = LockDefs.Find(6).Groups[0];

            Assert.AreEqual(2, group.Keys.Count);
            Assert.IsTrue(group.Keys.Contains(CardType.BlueCard));
            Assert.IsTrue(group.Keys.Contains(CardType.BlueSkull));
        }

        [TestMethod]
        public void AKeyThisPortDoesNotKnowIsNamed()
        {
            var problems = Parse("lock 7 { bluekey }");

            StringAssert.Contains(All(problems), "bluekey");
            StringAssert.Contains(All(problems), "not a key");
        }

        [TestMethod]
        public void ALockWithNoKeysIsReported()
        {
            // Allowed, because removing a lock is a thing a mod may want - but said out loud,
            // because a lock that quietly needs nothing is the failure the whole class exists
            // to prevent.
            var problems = Parse("lock 8 { message \"open\" }");

            Assert.AreEqual(1, problems.Count, All(problems));
            StringAssert.Contains(All(problems), "opens for anyone");
            Assert.IsNotNull(LockDefs.Find(8), "and the definition is kept");
        }

        [TestMethod]
        public void ARemoteMessageIsNamedRatherThanSwallowed()
        {
            // ZDoom shows the other players a different line. This engine shows one line to the
            // player who tried the door and tells nobody else, so there is no second line.
            var problems = Parse(
                "lock 1\n" +
                "{\n" +
                "    message \"Nope\"\n" +
                "    remotemessage \"Player needs a key\"\n" +
                "    bluecard\n" +
                "}");

            StringAssert.Contains(All(problems), "remotemessage");
            Assert.AreEqual("Nope", LockDefs.Find(1).Message, "the local line still applies");
        }

        [TestMethod]
        public void AMapColourIsNamedRatherThanSwallowed()
        {
            var problems = Parse("lock 1 { mapcolor 255 0 0 bluecard }");

            // One problem, not three: `mapcolor` takes three numbers, and skipping only one
            // leaves "0" and "0" to be read as key names.
            Assert.AreEqual(1, problems.Count, All(problems));
            StringAssert.Contains(All(problems), "mapcolor");

            var definition = LockDefs.Find(1);

            Assert.AreEqual(1, definition.Groups.Count, "and the key is still read");
            Assert.AreEqual(CardType.BlueCard, definition.Groups[0].Keys[0]);
        }

        [TestMethod]
        public void ALockedSoundIsReadByName()
        {
            var problems = Parse("lock 1 { lockedsound oof bluecard }");

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(Sfx.OOF, LockDefs.Find(1).Sound);
        }

        [TestMethod]
        public void ASoundThisPortDoesNotHaveIsNamed()
        {
            var problems = Parse("lock 1 { lockedsound nosuchsound bluecard }");

            StringAssert.Contains(All(problems), "nosuchsound");
            Assert.AreEqual(Sfx.OOF, LockDefs.Find(1).Sound, "and the default is kept");
        }

        [TestMethod]
        public void ASoundNameIsNotReadAsANumber()
        {
            // Enum.TryParse alone would take "5" and hand back the fifth sound, so a typo would
            // play the wrong noise instead of being reported.
            var problems = Parse("lock 1 { lockedsound 5 bluecard }");

            StringAssert.Contains(All(problems), "not a sound");
        }

        [TestMethod]
        public void ClearlocksDropsTheBuiltIns()
        {
            var problems = Parse("clearlocks\nlock 2 { bluecard }");

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.IsTrue(LockDefs.BuiltInsRemoved);
            Assert.AreEqual(1, LockDefs.Count, "only the one after it");
            Assert.IsNull(LockDefs.Find(1));
        }

        [TestMethod]
        public void ClearIsTheOtherSpellingOfClearLocks()
        {
            var problems = Parse("clear");

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.IsTrue(LockDefs.BuiltInsRemoved);
        }

        [TestMethod]
        public void AnUnknownKeywordIsNamed()
        {
            var problems = Parse("locks 1 { bluecard }");

            StringAssert.Contains(All(problems), "locks");
            StringAssert.Contains(All(problems), "not a LOCKDEFS keyword");
        }

        [TestMethod]
        public void ALockWithoutANumberIsReported()
        {
            var problems = Parse("lock { bluecard }");

            Assert.AreEqual(1, problems.Count, All(problems));
            StringAssert.Contains(All(problems), "no number");
        }

        [TestMethod]
        public void ALockNumberOutsideTheRangeIsReported()
        {
            var problems = Parse("lock 300 { bluecard }");

            Assert.AreEqual(1, problems.Count, All(problems));
            StringAssert.Contains(All(problems), "outside the range");
            Assert.AreEqual(0, LockDefs.Count);
        }

        [TestMethod]
        public void AnUnclosedBlockIsReportedAndTheLockIsKept()
        {
            // The lock is what the author wrote; only the file's structure is broken.
            var problems = Parse("lock 1 { bluecard message \"Nope\"");

            StringAssert.Contains(All(problems), "not closed");
            Assert.IsNotNull(LockDefs.Find(1));
        }

        [TestMethod]
        public void CommentsInBothStylesAreSkipped()
        {
            var problems = Parse(
                "// a line comment\n" +
                "/* and a block\n" +
                "   comment */\n" +
                "lock 1\n" +
                "{\n" +
                "    bluecard // trailing\n" +
                "}");

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(1, LockDefs.Find(1).Groups.Count);
        }

        [TestMethod]
        public void OneBadLockDoesNotCostTheNextOne()
        {
            var problems = Parse(
                "lock 1 { nosuchkey }\n" +
                "lock 2 { bluecard }");

            Assert.IsTrue(problems.Count > 0);
            Assert.IsNotNull(LockDefs.Find(2), "the lock after the bad one is read");
        }

        [TestMethod]
        public void ReadingTwiceReplacesRatherThanDoubles()
        {
            // Two files may define the same number; the last one wins, and the table is keyed by
            // number so a second pass cannot leave two definitions for one lock.
            Parse("lock 1 { bluecard }");
            Parse("lock 1 { redcard }");

            Assert.AreEqual(1, LockDefs.Count);
            Assert.AreEqual(CardType.RedCard, LockDefs.Find(1).Groups[0].Keys[0]);
        }
    }
}
