using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// MAPINFO reaches the level it describes.
    ///
    /// The parser is tested on its own in MapInfoTest; what can only be checked here is the join
    /// with the map: that the title the automap prints, the sky the renderer draws and the track
    /// the music backend is asked for all come from the file. A parser that reads correctly and is
    /// never consulted is the failure this file exists to rule out.
    ///
    /// Real content, not <c>CreateDummy</c>: the dummy texture lookup answers "DUMMY" for every
    /// name, so a test that asks it whether the sky changed would pass for the wrong reason.
    /// </summary>
    [TestClass]
    public class MapInfoAppliedTest
    {
        [TestInitialize]
        public void Before()
        {
            MapInfo.Clear();
            LoadReport.Clear();
        }

        [TestCleanup]
        public void After()
        {
            MapInfo.Clear();
        }

        [TestMethod]
        public void TheTitleSkyAndMusicReachTheLevel()
        {
            var problems = new List<string>();

            MapInfo.Add(
                "map MAP01 \"Test Chamber\"\n" +
                "{\n" +
                "    sky1 = \"SKY3\"\n" +
                "    music = \"D_E1M2\"\n" +
                "}", "test", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            using (var content = RealContent())
            {
                var world = BuildMap01(content);

                Assert.AreEqual("Test Chamber", world.Map.Title.ToString(),
                    "the automap and the intermission both read this");
                Assert.AreEqual("SKY3", world.Map.SkyTexture.Name, "the renderer draws this");
                Assert.AreEqual("D_E1M2", world.Map.MapInfoMusic,
                    "and the music backend is asked for this lump");
            }
        }

        [TestMethod]
        public void WithoutAnEntryTheEngineValuesAreUsed()
        {
            // The guard against a false positive: nothing about the defaults may change.
            using (var content = RealContent())
            {
                var world = BuildMap01(content);

                Assert.AreNotEqual("Test Chamber", world.Map.Title.ToString());
                Assert.AreEqual("SKY1", world.Map.SkyTexture.Name,
                    "MAP01 keeps the sky the engine chose for it");
                Assert.IsNull(world.Map.MapInfoMusic,
                    "no entry means no music override, not an empty one");
            }
        }

        [TestMethod]
        public void AnEntryForAnotherMapChangesNothing()
        {
            var problems = new List<string>();

            MapInfo.Add("map MAP07 \"Dead Simple\" { sky1 = \"SKY4\" }", "test", problems);

            using (var content = RealContent())
            {
                var world = BuildMap01(content);

                Assert.AreNotEqual("Dead Simple", world.Map.Title.ToString());
                Assert.AreEqual("SKY1", world.Map.SkyTexture.Name);
                Assert.IsNull(world.Map.MapInfoMusic);
            }
        }

        [TestMethod]
        public void AnUnknownSkyIsReportedAndTheEngineSkyStays()
        {
            // A sky name that is not a texture: the level must still load, with the sky it had, and
            // the report must say which name was wrong. The alternative is either a crash on a
            // missing texture or a level silently drawing the wrong sky.
            var problems = new List<string>();

            MapInfo.Add("map MAP01 { sky1 = \"NOSUCHSKY\" }", "test", problems);

            using (var content = RealContent())
            {
                var world = BuildMap01(content);

                Assert.AreEqual("SKY1", world.Map.SkyTexture.Name,
                    "the level kept the sky it would have had anyway");

                Assert.IsTrue(LoadReport.HasProblems, "and the wrong name was reported");
                StringAssert.Contains(LoadReport.Describe(), "NOSUCHSKY");
            }
        }

        [TestMethod]
        public void TheMapInfoLumpIsReadFromTheWad()
        {
            // The lump is the way a mod ships this without telling the player to pass a flag, which
            // is the whole reason it exists. Built here rather than committed, so the test is
            // self-contained.
            var pwad = Path.Combine(Path.GetTempPath(), "dptdoom-mapinfo-" +
                                                      Guid.NewGuid().ToString("N") + ".wad");

            try
            {
                WritePwad(pwad, "MAPINFO",
                    "map MAP01 \"From The Lump\"\n{\n    music = \"D_E1M2\"\n}\n");

                var args = new CommandLineArgs(
                    new[] { "-iwad", WadPath.Doom2, "-file", pwad, "-nodata" });

                using (var content = new GameContent(args))
                {
                    var world = BuildMap01(content);

                    Assert.AreEqual("From The Lump", world.Map.Title.ToString(),
                        "the lump was found without a flag");
                    Assert.AreEqual("D_E1M2", world.Map.MapInfoMusic);
                }
            }
            finally
            {
                try { File.Delete(pwad); } catch { }
            }
        }

        /// <summary>Real content, with the actor folder left alone.</summary>
        private static GameContent RealContent()
        {
            return new GameContent(new CommandLineArgs(new[] { "-iwad", WadPath.Doom2, "-nodata" }));
        }

        private static void WritePwad(string path, string lumpName, string content)
        {
            var data = System.Text.Encoding.ASCII.GetBytes(content);
            // Null-padded, not space-padded: that is the WAD format, and the reader stops at the
            // first zero. A space-padded name keeps its trailing space and never matches a lookup -
            // which is also what vanilla Doom does, so the strictness is faithful rather than
            // inconvenient.
            var name = new byte[8];
            var bytes = System.Text.Encoding.ASCII.GetBytes(lumpName.ToUpperInvariant());
            Array.Copy(bytes, name, Math.Min(8, bytes.Length));

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("PWAD"));
                writer.Write(1);
                writer.Write(12 + data.Length);

                writer.Write(data);
                writer.Write(12);
                writer.Write(data.Length);
                writer.Write(name);
            }
        }

        private static World BuildMap01(GameContent content)
        {
            var options = new GameOptions();
            options.GameVersion = content.Wad.GameVersion;
            options.GameMode = content.Wad.GameMode;
            options.MissionPack = content.Wad.MissionPack;
            options.Skill = GameSkill.Medium;
            options.Episode = 1;
            options.Map = 1;
            options.Players[0].InGame = true;

            var game = new DoomGame(content, options);
            game.DeferedInitNew();

            var cmds = new TicCmd[Player.MaxPlayerCount];

            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);

            return game.World;
        }
    }
}
