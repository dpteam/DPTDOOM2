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

        private static DoomString Message(int key, bool skullsCountAsCards, bool[] cards)
        {
            DoomString message;
            var open = GeneralizedLock.CanOpen(key, skullsCountAsCards, cards, out message);

            if (open)
            {
                Assert.IsNull(message, "an open door has nothing to say");
                return null;
            }

            Assert.IsNotNull(message, "a refusal always says why");
            return message;
        }

        [TestMethod]
        public void AnyKeyOpensWithAnySingleKey()
        {
            Assert.IsFalse(GeneralizedLock.CanOpen(GeneralizedLock.AnyKey, false, NoKeys(),
                out _));

            foreach (CardType kind in Enum.GetValues(typeof(CardType)))
            {
                if (kind == CardType.Count)
                {
                    continue;
                }

                Assert.IsTrue(GeneralizedLock.CanOpen(GeneralizedLock.AnyKey, false,
                    Keys(kind), out _), kind + " should open an any-key lock");
            }

            Assert.AreEqual(DoomInfo.Strings.PD_ANY,
                Message(GeneralizedLock.AnyKey, false, NoKeys()));
        }

        [TestMethod]
        public void ACardLockWantsTheCardWhenTheTypeMatters()
        {
            // Without the skull bit, a red skull is not a red card.
            Assert.IsTrue(GeneralizedLock.CanOpen(GeneralizedLock.RedCard, false,
                Keys(CardType.RedCard), out _));

            Assert.IsFalse(GeneralizedLock.CanOpen(GeneralizedLock.RedCard, false,
                Keys(CardType.RedSkull), out _), "a skull is not the card");

            Assert.AreEqual(DoomInfo.Strings.PD_REDC,
                Message(GeneralizedLock.RedCard, false, Keys(CardType.RedSkull)));
        }

        [TestMethod]
        public void ACardLockAcceptsASkullWhenTheTypeDoesNotMatter()
        {
            // This is what the LockedNKeys bit means: skulls count as cards.
            Assert.IsTrue(GeneralizedLock.CanOpen(GeneralizedLock.RedCard, true,
                Keys(CardType.RedSkull), out _));

            // And the refusal no longer names the card, because either would do.
            Assert.AreEqual(DoomInfo.Strings.PD_REDK,
                Message(GeneralizedLock.RedCard, true, NoKeys()));
        }

        [TestMethod]
        public void ASkullLockIsTheMirrorImage()
        {
            Assert.IsTrue(GeneralizedLock.CanOpen(GeneralizedLock.RedSkull, false,
                Keys(CardType.RedSkull), out _));

            Assert.IsFalse(GeneralizedLock.CanOpen(GeneralizedLock.RedSkull, false,
                Keys(CardType.RedCard), out _), "a card is not the skull");

            Assert.AreEqual(DoomInfo.Strings.PD_REDS,
                Message(GeneralizedLock.RedSkull, false, Keys(CardType.RedCard)));

            Assert.AreEqual(DoomInfo.Strings.PD_REDK,
                Message(GeneralizedLock.RedSkull, true, NoKeys()));
        }

        [TestMethod]
        public void EachColourHasItsOwnLine()
        {
            // Blue, yellow and red are separate locks; a table that shared one line would
            // send the player looking for the wrong colour.
            Assert.AreEqual(DoomInfo.Strings.PD_BLUEC,
                Message(GeneralizedLock.BlueCard, false, NoKeys()));
            Assert.AreEqual(DoomInfo.Strings.PD_YELLOWC,
                Message(GeneralizedLock.YellowCard, false, NoKeys()));
            Assert.AreEqual(DoomInfo.Strings.PD_BLUES,
                Message(GeneralizedLock.BlueSkull, false, NoKeys()));
            Assert.AreEqual(DoomInfo.Strings.PD_YELLOWS,
                Message(GeneralizedLock.YellowSkull, false, NoKeys()));
        }

        [TestMethod]
        public void AllKeysWantsSixWhenTheTypeMatters()
        {
            var allSix = Keys(CardType.BlueCard, CardType.YellowCard, CardType.RedCard,
                              CardType.BlueSkull, CardType.YellowSkull, CardType.RedSkull);

            Assert.IsTrue(GeneralizedLock.CanOpen(GeneralizedLock.AllKeys, false, allSix, out _));

            // Three cards and no skulls is not enough.
            var threeCards = Keys(CardType.BlueCard, CardType.YellowCard, CardType.RedCard);

            Assert.IsFalse(GeneralizedLock.CanOpen(GeneralizedLock.AllKeys, false, threeCards,
                out _));

            Assert.AreEqual(DoomInfo.Strings.PD_ALL6,
                Message(GeneralizedLock.AllKeys, false, threeCards));
        }

        [TestMethod]
        public void AllKeysWantsOneOfEachColourWhenSkullsCountAsCards()
        {
            // The same bit flips this lock from "six" to "three": with skulls counting as
            // cards, one red, one blue and one yellow is the whole set.
            var oneEach = Keys(CardType.BlueCard, CardType.YellowSkull, CardType.RedCard);

            Assert.IsTrue(GeneralizedLock.CanOpen(GeneralizedLock.AllKeys, true, oneEach, out _));

            var twoColours = Keys(CardType.BlueCard, CardType.RedCard);

            Assert.IsFalse(GeneralizedLock.CanOpen(GeneralizedLock.AllKeys, true, twoColours,
                out _));

            Assert.AreEqual(DoomInfo.Strings.PD_ALL3,
                Message(GeneralizedLock.AllKeys, true, twoColours));
        }

        [TestMethod]
        public void AKeyNumberThisPortDoesNotKnowRefuses()
        {
            // Boom defines 0 to 7. Anything else is a number no line should carry, and
            // opening the door would be a lock that does not lock.
            var allSix = Keys(CardType.BlueCard, CardType.YellowCard, CardType.RedCard,
                              CardType.BlueSkull, CardType.YellowSkull, CardType.RedSkull);

            Assert.IsFalse(GeneralizedLock.CanOpen(8, true, allSix, out var message));
            Assert.IsNotNull(message);
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
    }
}
