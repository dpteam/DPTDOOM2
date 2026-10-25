using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// SNDINFO: which lump a sound name plays.
    ///
    /// The engine finds a sound's lump by convention - "DS" plus the name - which works for the IWAD
    /// and stops working the moment a mod ships a recording called anything else. This is the file
    /// that breaks the convention, so what it says has to be exact: a wrong mapping plays the wrong
    /// sound, and a sound is the one thing a player notices immediately.
    /// </summary>
    [TestClass]
    public class SoundInfoTest
    {
        [TestInitialize]
        public void Before()
        {
            SoundInfo.Clear();
        }

        [TestCleanup]
        public void After()
        {
            SoundInfo.Clear();
        }

        [TestMethod]
        public void ANameMapsToALump()
        {
            var problems = new List<string>();

            SoundInfo.Add("pistol DSmygun", "test", problems);

            Assert.AreEqual(0, problems.Count);

            var lumps = SoundInfo.LumpsFor("pistol");

            Assert.IsNotNull(lumps);
            Assert.AreEqual(1, lumps.Count);
            Assert.AreEqual("DSmygun", lumps[0]);
        }

        [TestMethod]
        public void TheNameIsCaseInsensitive()
        {
            // The engine's Sfx names are upper case ("DOROPN"); a modder writes lower case.
            var problems = new List<string>();

            SoundInfo.Add("doropn DSmyopen", "test", problems);

            Assert.IsNotNull(SoundInfo.LumpsFor("DOROPN"));
            Assert.IsNotNull(SoundInfo.LumpsFor("doropn"));
        }

        [TestMethod]
        public void AnAliasPointsAtAnotherName()
        {
            var problems = new List<string>();

            SoundInfo.Add(
                "door/open DSmyopen\n" +
                "$alias doropn door/open", "test", problems);

            Assert.AreEqual(0, problems.Count);

            var lumps = SoundInfo.LumpsFor("doropn");

            Assert.IsNotNull(lumps, "the alias resolves");
            Assert.AreEqual("DSmyopen", lumps[0]);
        }

        [TestMethod]
        public void AnAliasChainResolves()
        {
            var problems = new List<string>();

            SoundInfo.Add(
                "real DSlump\n" +
                "$alias middle real\n" +
                "$alias top middle", "test", problems);

            Assert.AreEqual("DSlump", SoundInfo.LumpsFor("top")[0]);
        }

        [TestMethod]
        public void AnAliasCycleIsReportedRatherThanHanging()
        {
            // Two aliases pointing at each other are a cycle, not a sound. Without a depth limit the
            // lookup would spin forever inside a frame, which is a hang with a drawn window.
            var problems = new List<string>();

            SoundInfo.Add(
                "$alias a b\n" +
                "$alias b a", "test", problems);

            Assert.AreEqual(0, problems.Count, "the cycle is legal to write");

            Assert.IsNull(SoundInfo.LumpsFor("a"),
                "and resolves to nothing rather than to an infinite loop");
        }

        [TestMethod]
        public void ARandomSoundHasSeveralLumps()
        {
            var problems = new List<string>();

            SoundInfo.Add("$random dorcls { DSmyclose DSmyclose2 DSmyclose3 }", "test", problems);

            Assert.AreEqual(0, problems.Count);

            var lumps = SoundInfo.LumpsFor("dorcls");

            Assert.AreEqual(3, lumps.Count);
            Assert.AreEqual("DSmyclose", lumps[0]);
            Assert.AreEqual("DSmyclose3", lumps[2]);
        }

        [TestMethod]
        public void AnUnknownNameIsNullRatherThanEmpty()
        {
            // The caller has to be able to tell "no entry, use the DS convention" from "an entry with
            // no lumps". An empty list would make the second look like the first and the sound would
            // silently play the wrong lump.
            Assert.IsNull(SoundInfo.LumpsFor("nothing"));
            Assert.IsNull(SoundInfo.LumpsFor(""));
            Assert.IsNull(SoundInfo.LumpsFor(null));
        }

        [TestMethod]
        public void CommentsInBothStylesAreSkipped()
        {
            var problems = new List<string>();

            SoundInfo.Add(
                "// a line comment\n" +
                "/* and a block\n" +
                "   comment */\n" +
                "pistol DSlump // trailing\n", "test", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            Assert.AreEqual("DSlump", SoundInfo.LumpsFor("pistol")[0]);
        }

        [TestMethod]
        public void AnUnsupportedDirectiveIsNamedRatherThanIgnored()
        {
            // $volume and $attenuation are real SNDINFO directives. Accepting them silently would tell
            // a modder their sound is quieter when it is not.
            var problems = new List<string>();

            SoundInfo.Add(
                "$volume pistol 0.5\n" +
                "pistol DSlump", "test", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "$volume");
            StringAssert.Contains(text, "not a SNDINFO directive");

            // And the mapping after it still applies: one unsupported line costs itself.
            Assert.AreEqual("DSlump", SoundInfo.LumpsFor("pistol")[0]);
        }

        [TestMethod]
        public void ANameWithoutALumpIsReported()
        {
            var problems = new List<string>();

            SoundInfo.Add("pistol", "test", problems);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "no lump name");
        }

        [TestMethod]
        public void AnAliasWithoutTwoNamesIsReported()
        {
            var problems = new List<string>();

            SoundInfo.Add("$alias onlyone", "test", problems);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "two names");
        }

        [TestMethod]
        public void ARandomBlockThatIsNotClosedIsReported()
        {
            var problems = new List<string>();

            SoundInfo.Add("$random dorcls { DSone DStwo", "test", problems);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "not closed");
        }

        [TestMethod]
        public void AnEmptyRandomIsReported()
        {
            var problems = new List<string>();

            SoundInfo.Add("$random dorcls { }", "test", problems);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "no lumps");
        }

        [TestMethod]
        public void ALaterEntryReplacesAnEarlierOne()
        {
            // A PWAD's SNDINFO is loaded after the IWAD's, so the last word wins.
            var problems = new List<string>();

            SoundInfo.Add("pistol DSold", "iwad", problems);
            SoundInfo.Add("pistol DSnew", "pwad", problems);

            Assert.AreEqual(1, SoundInfo.Count);
            Assert.AreEqual("DSnew", SoundInfo.LumpsFor("pistol")[0]);
        }

        [TestMethod]
        public void AnEmptyFileIsNotAProblem()
        {
            var problems = new List<string>();

            SoundInfo.Add("", "test", problems);
            SoundInfo.Add(null, "test", problems);

            Assert.AreEqual(0, problems.Count);
            Assert.AreEqual(0, SoundInfo.Count);
        }
    }
}
