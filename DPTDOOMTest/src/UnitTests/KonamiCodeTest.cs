using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The Konami code, which is the only way into ROFL mode.
    ///
    /// The interesting part is not that the sequence works - it is what counts as "not it".
    /// An easter egg that fires during ordinary menu use is worse than one nobody finds: the
    /// player gets a mode they did not ask for and cannot explain. Every rule here exists
    /// because a simpler matcher fails one of these cases.
    /// </summary>
    [TestClass]
    public class KonamiCodeTest
    {
        private static readonly DoomKey[] Code =
        {
            DoomKey.Up, DoomKey.Up,
            DoomKey.Down, DoomKey.Down,
            DoomKey.Left, DoomKey.Right, DoomKey.Left, DoomKey.Right,
            DoomKey.B, DoomKey.A
        };

        private static bool Type(KonamiCode konami, DoomKey[] keys)
        {
            var completed = false;

            foreach (var key in keys)
            {
                if (konami.Feed(key))
                {
                    completed = true;
                }

                konami.Update();
            }

            return completed;
        }

        [TestMethod]
        public void TheCodeCompletes()
        {
            var konami = new KonamiCode();

            Assert.IsTrue(Type(konami, Code), "up up down down left right left right b a");
        }

        [TestMethod]
        public void TheCodeCompletesTwiceInARow()
        {
            // The same sequence typed again is the only way to turn the mode off, so it has to
            // start from the beginning rather than needing something else first.
            var konami = new KonamiCode();

            Assert.IsTrue(Type(konami, Code), "first time");
            Assert.IsTrue(Type(konami, Code), "and again, which is how it is turned off");
        }

        [TestMethod]
        public void APlainMenuWalkDoesNotComplete()
        {
            // The failure mode that matters. "up up down down" is four rows of scrolling, and
            // the code has repeated pairs, so a lenient matcher that keeps a partial match
            // would complete on the fifth key of ordinary navigation.
            var konami = new KonamiCode();

            var walk = new[]
            {
                DoomKey.Up, DoomKey.Up, DoomKey.Down, DoomKey.Down, DoomKey.Up,
                DoomKey.Up, DoomKey.Down, DoomKey.Down, DoomKey.Up, DoomKey.Down
            };

            Assert.IsFalse(Type(konami, walk), "scrolling a menu is not the code");
        }

        [TestMethod]
        public void APauseBreaksTheCode()
        {
            // Reading a menu and then carrying on must not finish a half-typed code.
            var konami = new KonamiCode();

            for (var i = 0; i < 5; i++)
            {
                konami.Feed(Code[i]);
                konami.Update();
            }

            for (var tic = 0; tic < 3 * 35; tic++)
            {
                konami.Update();
            }

            Assert.AreEqual(0, konami.Progress, "a three-second pause starts over");

            // And the rest of the sequence alone does not complete it.
            var tail = new[] { Code[5], Code[6], Code[7], Code[8], Code[9] };

            Assert.IsFalse(Type(konami, tail));
        }

        [TestMethod]
        public void AnyOtherKeyStartsOver()
        {
            // Strict, and deliberately so. Ignoring keys that are not in the code would let
            // "up, up, down, down, left, right, left, right, B, x, A" complete, and the extra
            // rule buys nothing: nobody presses a letter by accident between two arrows.
            var konami = new KonamiCode();

            konami.Feed(DoomKey.Up);
            konami.Feed(DoomKey.Up);

            Assert.AreEqual(2, konami.Progress);

            konami.Feed(DoomKey.F1);

            Assert.AreEqual(0, konami.Progress, "a key outside the code is still a mistake");
        }

        [TestMethod]
        public void AnOvershootStartsOverRatherThanRecovering()
        {
            // "up, up, up, down..." is a mistake. One rule instead of two, and the same rule
            // every code of this kind has always had.
            var konami = new KonamiCode();

            konami.Feed(DoomKey.Up);
            konami.Feed(DoomKey.Up);

            Assert.AreEqual(2, konami.Progress);

            konami.Feed(DoomKey.Up);

            Assert.AreEqual(0, konami.Progress);
        }

        [TestMethod]
        public void AKeyThatCouldHaveBeenTheNextOneRestarts()
        {
            // Up, up, down, down, left, ENTER, right: pressing enter in a menu is not part of
            // any secret, and it is exactly the key a player hits when a row looks interesting.
            var konami = new KonamiCode();

            konami.Feed(DoomKey.Up);
            konami.Feed(DoomKey.Up);
            konami.Feed(DoomKey.Down);
            konami.Feed(DoomKey.Down);
            konami.Feed(DoomKey.Left);

            Assert.AreEqual(5, konami.Progress);

            konami.Feed(DoomKey.Down);

            Assert.AreEqual(0, konami.Progress, "a wrong key is a mistake, not a detour");
        }

        [TestMethod]
        public void AMistakeInTheMiddleStartsOver()
        {
            // The recovery case that was tried and dropped: treating "up, up, up, down" as an
            // attempt that began at the second up means keeping a partial-match table for a
            // ten-key sequence, to save the player four keypresses.
            var konami = new KonamiCode();

            var keys = new[]
            {
                DoomKey.Up, DoomKey.Up, DoomKey.Up,
                DoomKey.Down, DoomKey.Down,
                DoomKey.Left, DoomKey.Right, DoomKey.Left, DoomKey.Right,
                DoomKey.B, DoomKey.A
            };

            Assert.IsFalse(Type(konami, keys), "an overshoot is a mistake, not a detour");
        }
    }
}
