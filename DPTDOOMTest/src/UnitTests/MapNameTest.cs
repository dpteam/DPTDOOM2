using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Level names: "MAP32", "E1M1", or a bare number.
    ///
    /// Names because that is how every mod, walkthrough and idgames text file refers to
    /// a level, and a PWAD that replaces MAP32 is the standard way to ship a secret map.
    /// A bare number still works and means the current game's own numbering.
    ///
    /// These call the real parser rather than a copy of it. A test that reimplements
    /// the rules it is checking passes while the code is broken.
    /// </summary>
    [TestClass]
    public class MapNameTest
    {
        private static int episode;
        private static int map;

        private static bool Parse(string text, GameMode mode, int currentEpisode = 1)
        {
            return LevelName.TryParse(text, mode, currentEpisode, out episode, out map);
        }

        [TestMethod]
        public void MapNamesUseTheConventionalForm()
        {
            Assert.IsTrue(Parse("MAP32", GameMode.Commercial), "MAP32 is accepted");
            Assert.AreEqual(32, map, "MAP32 is the 32nd map, not the 3rd of something");
            Assert.AreEqual(1, episode);
        }

        [TestMethod]
        public void MapNamesAreCaseInsensitive()
        {
            Assert.IsTrue(Parse("map07", GameMode.Commercial));
            Assert.AreEqual(7, map);
        }

        [TestMethod]
        public void EpisodeNamesAreAccepted()
        {
            Assert.IsTrue(Parse("E1M1", GameMode.Retail));
            Assert.AreEqual(1, episode);
            Assert.AreEqual(1, map);

            Assert.IsTrue(Parse("E4M9", GameMode.Retail));
            Assert.AreEqual(4, episode);
            Assert.AreEqual(9, map);
        }

        [TestMethod]
        public void ABareNumberMeansThisGamesNumbering()
        {
            Assert.IsTrue(Parse("32", GameMode.Commercial));
            Assert.AreEqual(32, map, "Doom II: 32 is MAP32");

            // Doom 1 has nine maps per episode, so 32 is not a map at all. It has to be
            // refused rather than clamped into E1M9: silently starting the wrong level
            // is worse than an error message.
            Assert.IsFalse(Parse("32", GameMode.Retail), "Doom 1 has no map 32");

            Assert.IsTrue(Parse("8", GameMode.Retail));
            Assert.AreEqual(8, map);
            Assert.AreEqual(1, episode, "a bare number keeps the current episode");
        }

        [TestMethod]
        public void TheCurrentEpisodeIsKept()
        {
            Assert.IsTrue(Parse("3", GameMode.Retail, 2));
            Assert.AreEqual(2, episode);
            Assert.AreEqual(3, map);
        }

        [TestMethod]
        public void NonsenseIsRefused()
        {
            Assert.IsFalse(Parse("MAP0", GameMode.Commercial), "MAP0 does not exist");
            Assert.IsFalse(Parse("MAP33", GameMode.Commercial), "there are 32 maps");
            Assert.IsFalse(Parse("E5M1", GameMode.Retail), "there are four episodes");
            Assert.IsFalse(Parse("E1M10", GameMode.Retail), "nine maps per episode");
            Assert.IsFalse(Parse("hello", GameMode.Commercial));
            Assert.IsFalse(Parse("", GameMode.Commercial));
            Assert.IsFalse(Parse(null, GameMode.Commercial));
        }

        [TestMethod]
        public void FormattingMatchesParsing()
        {
            // A round trip, so the name the console reports for the current level is
            // one the console would accept back.
            Assert.AreEqual("MAP32", LevelName.Format(GameMode.Commercial, 1, 32));
            Assert.AreEqual("E1M1", LevelName.Format(GameMode.Retail, 1, 1));

            int e;
            int m;

            Assert.IsTrue(LevelName.TryParse(LevelName.Format(GameMode.Commercial, 1, 32),
                                             GameMode.Commercial, 1, out e, out m));
            Assert.AreEqual(32, m);

            Assert.IsTrue(LevelName.TryParse(LevelName.Format(GameMode.Retail, 3, 7),
                                             GameMode.Retail, 1, out e, out m));
            Assert.AreEqual(3, e);
            Assert.AreEqual(7, m);
        }
    }
}
