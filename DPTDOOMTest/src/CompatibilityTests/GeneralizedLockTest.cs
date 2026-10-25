using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Which key a Boom generalized locked door needs.
    ///
    /// The rules are Boom's, from `P_CanUnlockGenDoor` in
    /// `refs/prboom-plus/prboom2/src/p_spec.c`, and two of them are the kind of thing a
    /// reimplementation gets wrong without noticing:
    ///
    ///  - the `LockedNKeys` bit is **not** a count. It means "a skull opens a card lock",
    ///    so with it set, "all keys" is one key of each colour rather than all six;
    ///  - the refusal line is not one line. Boom says "a red card" when the type matters
    ///    and "a red key" when it does not, and a player who is told the wrong one looks
    ///    for the wrong item.
    ///
    /// These are pure checks over a six-element card array, so they pin the rules without
    /// a map: a door that opens without its key is otherwise indistinguishable from a map
    /// built without a lock.
    /// </summary>
    [TestClass]
    public class GeneralizedLockTest
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

        private static bool[] NoKeys()
        {
            return new bool[(int)CardType.Count];
        }

        private static bool[] Keys(params CardType[] kinds)
        {
            var cards = NoKeys();

            foreach (var kind in kinds)
            {
                cards[(int)kind] = true;
            }

            return cards;
        }

        private static string Message(int key, bool skullsCountAsCards, bool[] cards)
        {
            var verdict = GeneralizedLock.Check(key, skullsCountAsCards, cards);

            if (verdict.Open)
            {
                Assert.IsNull(verdict.Message, "an open door has nothing to say");
                return null;
            }

            Assert.IsNotNull(verdict.Message, "a refusal always says why");
            return verdict.Message;
        }

        [TestMethod]
        public void AnyKeyOpensWithAnySingleKey()
        {
            Assert.IsFalse(GeneralizedLock.Check(GeneralizedLock.AnyKey, false, NoKeys()).Open);

            foreach (CardType kind in Enum.GetValues(typeof(CardType)))
            {
                if (kind == CardType.Count)
                {
                    continue;
                }

                Assert.IsTrue(GeneralizedLock.Check(GeneralizedLock.AnyKey, false, Keys(kind)).Open,
                    kind + " should open an any-key lock");
            }

            Assert.AreEqual((string)DoomInfo.Strings.PD_ANY,
                Message(GeneralizedLock.AnyKey, false, NoKeys()));
        }

        [TestMethod]
        public void ACardLockWantsTheCardWhenTheTypeMatters()
        {
            // Without the skull bit, a red skull is not a red card.
            Assert.IsTrue(GeneralizedLock.Check(GeneralizedLock.RedCard, false,
                Keys(CardType.RedCard)).Open);

            Assert.IsFalse(GeneralizedLock.Check(GeneralizedLock.RedCard, false,
                Keys(CardType.RedSkull)).Open, "a skull is not the card");

            Assert.AreEqual((string)DoomInfo.Strings.PD_REDC,
                Message(GeneralizedLock.RedCard, false, Keys(CardType.RedSkull)));
        }

        [TestMethod]
        public void ACardLockAcceptsASkullWhenTheTypeDoesNotMatter()
        {
            // This is what the LockedNKeys bit means: skulls count as cards.
            Assert.IsTrue(GeneralizedLock.Check(GeneralizedLock.RedCard, true,
                Keys(CardType.RedSkull)).Open);

            // And the refusal no longer names the card, because either would do.
            Assert.AreEqual((string)DoomInfo.Strings.PD_REDK,
                Message(GeneralizedLock.RedCard, true, NoKeys()));
        }

        [TestMethod]
        public void ASkullLockIsTheMirrorImage()
        {
            Assert.IsTrue(GeneralizedLock.Check(GeneralizedLock.RedSkull, false,
                Keys(CardType.RedSkull)).Open);

            Assert.IsFalse(GeneralizedLock.Check(GeneralizedLock.RedSkull, false,
                Keys(CardType.RedCard)).Open, "a card is not the skull");

            Assert.AreEqual((string)DoomInfo.Strings.PD_REDS,
                Message(GeneralizedLock.RedSkull, false, Keys(CardType.RedCard)));

            Assert.AreEqual((string)DoomInfo.Strings.PD_REDK,
                Message(GeneralizedLock.RedSkull, true, NoKeys()));
        }

        [TestMethod]
        public void EachColourHasItsOwnLine()
        {
            // Blue, yellow and red are separate locks; a table that shared one line would
            // send the player looking for the wrong colour.
            Assert.AreEqual((string)DoomInfo.Strings.PD_BLUEC,
                Message(GeneralizedLock.BlueCard, false, NoKeys()));
            Assert.AreEqual((string)DoomInfo.Strings.PD_YELLOWC,
                Message(GeneralizedLock.YellowCard, false, NoKeys()));
            Assert.AreEqual((string)DoomInfo.Strings.PD_BLUES,
                Message(GeneralizedLock.BlueSkull, false, NoKeys()));
            Assert.AreEqual((string)DoomInfo.Strings.PD_YELLOWS,
                Message(GeneralizedLock.YellowSkull, false, NoKeys()));
        }

        [TestMethod]
        public void AllKeysWantsSixWhenTheTypeMatters()
        {
            var allSix = Keys(CardType.BlueCard, CardType.YellowCard, CardType.RedCard,
                              CardType.BlueSkull, CardType.YellowSkull, CardType.RedSkull);

            Assert.IsTrue(GeneralizedLock.Check(GeneralizedLock.AllKeys, false, allSix).Open);

            // Three cards and no skulls is not enough.
            var threeCards = Keys(CardType.BlueCard, CardType.YellowCard, CardType.RedCard);

            Assert.IsFalse(GeneralizedLock.Check(GeneralizedLock.AllKeys, false, threeCards).Open);

            Assert.AreEqual((string)DoomInfo.Strings.PD_ALL6,
                Message(GeneralizedLock.AllKeys, false, threeCards));
        }

        [TestMethod]
        public void AllKeysWantsOneOfEachColourWhenSkullsCountAsCards()
        {
            // The same bit flips this lock from "six" to "three": with skulls counting as
            // cards, one red, one blue and one yellow is the whole set.
            var oneEach = Keys(CardType.BlueCard, CardType.YellowSkull, CardType.RedCard);

            Assert.IsTrue(GeneralizedLock.Check(GeneralizedLock.AllKeys, true, oneEach).Open);

            var twoColours = Keys(CardType.BlueCard, CardType.RedCard);

            Assert.IsFalse(GeneralizedLock.Check(GeneralizedLock.AllKeys, true, twoColours).Open);

            Assert.AreEqual((string)DoomInfo.Strings.PD_ALL3,
                Message(GeneralizedLock.AllKeys, true, twoColours));
        }

        [TestMethod]
        public void AKeyNumberThisPortDoesNotKnowRefuses()
        {
            // Boom defines 0 to 7. Anything else is a number no line should carry, and
            // opening the door would be a lock that does not lock.
            var allSix = Keys(CardType.BlueCard, CardType.YellowCard, CardType.RedCard,
                              CardType.BlueSkull, CardType.YellowSkull, CardType.RedSkull);

            var verdict = GeneralizedLock.Check(8, true, allSix);

            Assert.IsFalse(verdict.Open);
            Assert.IsNotNull(verdict.Message);
        }

        [TestMethod]
        public void TheKeyNumbersAreBooms()
        {
            // Pinned against keykind_e in p_spec.h: AnyKey, RCard, BCard, YCard, RSkull,
            // BSkull, YSkull, AllKeys. A map's linedef carries these numbers.
            Assert.AreEqual(0, GeneralizedLock.AnyKey);
            Assert.AreEqual(1, GeneralizedLock.RedCard);
            Assert.AreEqual(2, GeneralizedLock.BlueCard);
            Assert.AreEqual(3, GeneralizedLock.YellowCard);
            Assert.AreEqual(4, GeneralizedLock.RedSkull);
            Assert.AreEqual(5, GeneralizedLock.BlueSkull);
            Assert.AreEqual(6, GeneralizedLock.YellowSkull);
            Assert.AreEqual(7, GeneralizedLock.AllKeys);
        }

        [TestMethod]
        public void AFileDefinitionWinsOverTheBuiltIn()
        {
            // The reason LOCKDEFS exists: Boom's eight shapes cannot say "a blue card or a
            // red skull", and a mod that wants that must be able to say it.
            var problems = new System.Collections.Generic.List<string>();

            LockDefs.Add(
                "lock 1\n" +
                "{\n" +
                "    any { bluecard redskull }\n" +
                "    message \"Find a blue card or a red skull\"\n" +
                "}", "test", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var verdict = GeneralizedLock.Check(GeneralizedLock.RedCard, false,
                Keys(CardType.RedSkull));

            Assert.IsTrue(verdict.Open, "the file said a red skull would do");

            Assert.IsTrue(GeneralizedLock.Check(GeneralizedLock.RedCard, false,
                Keys(CardType.BlueCard)).Open, "and so would a blue card");

            Assert.IsFalse(GeneralizedLock.Check(GeneralizedLock.RedCard, false,
                Keys(CardType.RedCard)).Open,
                "but a red card is neither, so Boom's own answer is overridden");

            // The message comes from the file, not from Boom.
            var refused = GeneralizedLock.Check(GeneralizedLock.RedCard, false, NoKeys());

            Assert.IsFalse(refused.Open);
            Assert.AreEqual("Find a blue card or a red skull", refused.Message);
        }

        [TestMethod]
        public void ADefinitionWithoutAMessageUsesTheGenericLine()
        {
            var problems = new System.Collections.Generic.List<string>();

            LockDefs.Add("lock 2 { anyblue }", "test", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var verdict = GeneralizedLock.Check(GeneralizedLock.BlueCard, false, NoKeys());

            Assert.IsFalse(verdict.Open);
            Assert.AreEqual((string)DoomInfo.Strings.PD_LOCKED, verdict.Message);
        }

        [TestMethod]
        public void ClearlocksLeavesAnUndefinedLockOpen()
        {
            // The choice, stated as a test: refusing would make the door impassable for
            // ever, and a door that does not lock is the lesser harm. The loader says so
            // once instead of leaving the player to guess.
            var problems = new System.Collections.Generic.List<string>();

            LockDefs.Add("clearlocks\nlock 3 { bluecard }", "test", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            Assert.IsTrue(GeneralizedLock.Check(GeneralizedLock.RedCard, false, NoKeys()).Open,
                "lock 1 has no definition any more");

            Assert.IsFalse(GeneralizedLock.Check(GeneralizedLock.YellowCard, false, NoKeys()).Open,
                "but lock 3 does");
        }
    }
}
