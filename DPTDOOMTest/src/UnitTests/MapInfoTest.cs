using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// MAPINFO is read as a small subset, and the subset is checked from both sides: what it
    /// understands, and what it refuses to pretend to understand.
    ///
    /// The refusals matter as much as the readings. A parser that silently accepts `cluster` or
    /// `defaultmap` produces a mod that looks supported and is not, and the author finds out by
    /// noticing that nothing happened - which is the worst way to find out.
    /// </summary>
    [TestClass]
    public class MapInfoTest
    {
        [TestInitialize]
        public void Before()
        {
            MapInfo.Clear();
        }

        [TestCleanup]
        public void After()
        {
            MapInfo.Clear();
        }

        [TestMethod]
        public void ATitleIsRead()
        {
            var problems = new List<string>();

            MapInfo.Add("map MAP01 \"The Hangar\"", "test", problems);

            Assert.AreEqual(0, problems.Count);

            var entry = MapInfo.For("MAP01");

            Assert.IsNotNull(entry);
            Assert.AreEqual("MAP01", entry.Map);
            Assert.AreEqual("The Hangar", entry.Title);
        }

        [TestMethod]
        public void ASkyAndMusicAreReadFromABlock()
        {
            var problems = new List<string>();

            MapInfo.Add(
                "map MAP02 \"Underhalls\"\n" +
                "{\n" +
                "    sky1 = \"SKY2\"\n" +
                "    music = \"D_E1M2\"\n" +
                "}", "test", problems);

            Assert.AreEqual(0, problems.Count);

            var entry = MapInfo.For("MAP02");

            Assert.AreEqual("Underhalls", entry.Title);
            Assert.AreEqual("SKY2", entry.SkyTexture);
            Assert.AreEqual("D_E1M2", entry.MusicLump);
        }

        [TestMethod]
        public void TheEqualsSignIsOptional()
        {
            // Both spellings appear in real files: ZDoom accepts `sky1 SKY2` as well as
            // `sky1 = SKY2`, and a mod author should not have to learn which this port wants.
            var problems = new List<string>();

            MapInfo.Add("map MAP01 { sky1 SKY3\n music D_RUNNIN }", "test", problems);

            var entry = MapInfo.For("MAP01");

            Assert.AreEqual("SKY3", entry.SkyTexture);
            Assert.AreEqual("D_RUNNIN", entry.MusicLump);
        }

        [TestMethod]
        public void AnUnquotedValueIsAccepted()
        {
            var problems = new List<string>();

            MapInfo.Add("map MAP01 { sky1 = SKY4 }", "test", problems);

            Assert.AreEqual("SKY4", MapInfo.For("MAP01").SkyTexture);
        }

        [TestMethod]
        public void AnEntryThatOnlySetsTheSkyKeepsTheName()
        {
            // The case the ordering inside the map constructor depends on: a mod that wants a
            // different sky must not blank the level's name.
            var problems = new List<string>();

            MapInfo.Add("map MAP01 { sky1 = \"SKY2\" }", "test", problems);

            var entry = MapInfo.For("MAP01");

            Assert.IsNull(entry.Title, "no title was written, so none is claimed");
            Assert.AreEqual("SKY2", entry.SkyTexture);
        }

        [TestMethod]
        public void CommentsInBothStylesAreSkipped()
        {
            var problems = new List<string>();

            MapInfo.Add(
                "// a line comment\n" +
                "/* and a block\n" +
                "   comment */\n" +
                "map MAP01 \"Name\" // trailing\n" +
                "{\n" +
                "    sky1 = \"SKY2\"\n" +
                "}", "test", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var entry = MapInfo.For("MAP01");

            Assert.AreEqual("Name", entry.Title);
            Assert.AreEqual("SKY2", entry.SkyTexture);
        }

        [TestMethod]
        public void SeveralMapsAreReadInOneFile()
        {
            var problems = new List<string>();

            MapInfo.Add(
                "map MAP01 \"One\"\n" +
                "map MAP02 \"Two\"\n" +
                "map MAP03 \"Three\" { sky1 = \"SKY3\" }", "test", problems);

            Assert.AreEqual(3, MapInfo.Count);
            Assert.AreEqual("One", MapInfo.For("MAP01").Title);
            Assert.AreEqual("Two", MapInfo.For("MAP02").Title);
            Assert.AreEqual("SKY3", MapInfo.For("MAP03").SkyTexture);
        }

        [TestMethod]
        public void TheMapNameIsCaseInsensitive()
        {
            // A level is looked up by its lump name, and a mod that writes "map map01" means the
            // same level as one that writes "MAP01".
            var problems = new List<string>();

            MapInfo.Add("map map01 \"Lower\"", "test", problems);

            Assert.IsNotNull(MapInfo.For("MAP01"));
            Assert.IsNotNull(MapInfo.For("map01"));
        }

        [TestMethod]
        public void AnUnsupportedKeywordIsNamedRatherThanIgnored()
        {
            // The whole reason this test exists: silence is what makes an author believe a
            // setting took effect.
            var problems = new List<string>();

            MapInfo.Add(
                "cluster 1\n" +
                "{\n" +
                "    flat = \"FLOOR1\"\n" +
                "}\n" +
                "map MAP01 \"Still Read\"", "test", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "cluster");
            StringAssert.Contains(text, "not a MAPINFO keyword");

            // And the entry after it is still read: one unsupported feature costs itself.
            Assert.AreEqual("Still Read", MapInfo.For("MAP01").Title);
        }

        [TestMethod]
        public void AnUnsupportedPropertyInsideABlockIsNamed()
        {
            var problems = new List<string>();

            MapInfo.Add(
                "map MAP01\n" +
                "{\n" +
                "    sky1 = \"SKY2\"\n" +
                "    cluster = 3\n" +
                "    fogdensity = 128\n" +
                "}", "test", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "cluster");
            StringAssert.Contains(text, "fogdensity");

            // The supported property in the same block still applies.
            Assert.AreEqual("SKY2", MapInfo.For("MAP01").SkyTexture);
        }

        [TestMethod]
        public void AnEntryWithoutAMapNameIsReported()
        {
            var problems = new List<string>();

            MapInfo.Add("map", "test", problems);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "no map name");
        }

        [TestMethod]
        public void APropertyWithoutAValueIsReported()
        {
            // An empty sky name would make the level fail to find a texture, and the message would
            // be about the symptom rather than the cause.
            var problems = new List<string>();

            MapInfo.Add("map MAP01 { sky1 = }", "test", problems);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "no value");
        }

        [TestMethod]
        public void JunkIsRejectedRatherThanGuessed()
        {
            var problems = new List<string>();

            MapInfo.Add("", "test", problems);
            MapInfo.Add(null, "test", problems);

            Assert.AreEqual(0, problems.Count, "an empty file is not a problem");
            Assert.AreEqual(0, MapInfo.Count);
        }

        [TestMethod]
        public void ALaterEntryReplacesAnEarlierOne()
        {
            // A PWAD's MAPINFO is loaded after the IWAD's, so the last word wins - which is what
            // a mod overriding one map expects.
            var problems = new List<string>();

            MapInfo.Add("map MAP01 \"Old\"", "iwad", problems);
            MapInfo.Add("map MAP01 \"New\"", "pwad", problems);

            Assert.AreEqual(1, MapInfo.Count);
            Assert.AreEqual("New", MapInfo.For("MAP01").Title);
        }
    }
}
