using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// ANIMDEFS reaches the renderer's translation table.
    ///
    /// The parser is tested on its own; what can only be checked here is the join: that a cycle written
    /// in the file ends up in the map's animation list, that its frames are the pictures the file named,
    /// and that the flat actually changes over time. A cycle that parses correctly and is never
    /// consulted would leave the level exactly as it was, and nothing in the parser tests would notice.
    ///
    /// The observable is <see cref="Specials.FlatTranslation"/> - the table the renderer indexes when it
    /// draws a floor - so this checks what is drawn, not what was parsed.
    /// </summary>
    [TestClass]
    public class AnimatedTextureAppliedTest
    {
        [TestInitialize]
        public void Before()
        {
            AnimatedTexture.Clear();
            LoadReport.Clear();
        }

        [TestCleanup]
        public void After()
        {
            AnimatedTexture.Clear();
        }

        [TestMethod]
        public void ACycleChangesTheFlatOverTime()
        {
            // NUKAGE1 to NUKAGE3: three frames, two tics each, so the cycle is six tics long. The
            // engine's own ANIMATED lump already animates these - the point here is that ANIMDEFS
            // produced a cycle of its own with its own timing, not that the flats animate at all.
            var problems = new List<string>();

            AnimatedTexture.Add(
                "flat NUKAGE1\n" +
                "{\n" +
                "    pic NUKAGE1 tics 2\n" +
                "    pic NUKAGE2 tics 2\n" +
                "    pic NUKAGE3 tics 2\n" +
                "}", "test", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            using (var content = RealContent())
            {
                var game = BuildMap01(content);
                var world = game.World;

                var cycle = FindCycle(world, false);

                Assert.IsNotNull(cycle, "the cycle reached the map");
                Assert.AreEqual(3, cycle.Frames.Length, "with the three pictures the file named");

                var flat = content.Flats.GetNumber("NUKAGE1");
                Assert.IsTrue(flat >= 0, "NUKAGE1 is a flat in this WAD");

                var seen = new List<int>();
                var cmds = new TicCmd[Player.MaxPlayerCount];

                for (var i = 0; i < cmds.Length; i++)
                {
                    cmds[i] = new TicCmd();
                }

                // Driven through the game rather than through Specials.Update: the animation is keyed on
                // world.LevelTime, which only the world advances. Calling the specials directly left the
                // clock at zero and the frame never changed - which is what the first version of this
                // test reported, and it was the test that was wrong, not the animation.
                for (var tic = 0; tic < 12; tic++)
                {
                    game.Update(cmds);

                    var shown = world.Specials.FlatTranslation[flat];

                    if (seen.Count == 0 || seen[seen.Count - 1] != shown)
                    {
                        seen.Add(shown);
                    }
                }

                Assert.IsTrue(seen.Count >= 3,
                    "the floor changed picture over time, saw " + seen.Count + " distinct frames");
            }
        }

        [TestMethod]
        public void WithoutAnEntryNothingNewAnimates()
        {
            // The guard against a false positive: the count is whatever the engine's own table gives.
            using (var content = RealContent())
            {
                var world = BuildMap01(content).World;

                foreach (var animation in world.Map.Animation.Animations)
                {
                    Assert.IsNull(animation.Frames,
                        "every cycle is a contiguous range, because nothing was described");
                }
            }
        }

        [TestMethod]
        public void AnEntryForAnotherPictureIsNotUsed()
        {
            var problems = new List<string>();

            AnimatedTexture.Add("flat FWATER1 { pic FWATER1 tics 2 pic FWATER2 tics 2 }",
                "test", problems);

            using (var content = RealContent())
            {
                var world = BuildMap01(content).World;

                var cycle = FindCycle(world, false);

                Assert.IsNotNull(cycle, "the cycle is there");

                var nukage = content.Flats.GetNumber("NUKAGE1");
                Assert.IsTrue(nukage >= 0);

                // NUKAGE keeps the engine's own animation; the new cycle is a different list.
                Assert.IsFalse(Array.IndexOf(cycle.Frames, nukage) >= 0,
                    "the new cycle is not about NUKAGE");
            }
        }

        [TestMethod]
        public void AMissingFrameIsReportedAndTheCycleStillWorks()
        {
            // A cycle that lost one picture still animates: refusing the whole thing over one absent
            // frame would be worse than the gap, and the report names which frame it was.
            var problems = new List<string>();

            AnimatedTexture.Add(
                "flat NUKAGE1\n" +
                "{\n" +
                "    pic NUKAGE1 tics 2\n" +
                "    pic NOSUCHFLAT tics 2\n" +
                "    pic NUKAGE3 tics 2\n" +
                "}", "test", problems);

            Assert.AreEqual(0, problems.Count, "the file itself is fine");

            using (var content = RealContent())
            {
                var world = BuildMap01(content).World;

                var cycle = FindCycle(world, false);

                Assert.IsNotNull(cycle, "the cycle survived");
                Assert.AreEqual(2, cycle.Frames.Length, "with the two frames that exist");

                Assert.IsTrue(LoadReport.HasProblems, "and the absent one was reported");
                StringAssert.Contains(LoadReport.Describe(), "NOSUCHFLAT");
            }
        }

        [TestMethod]
        public void TheAnimdefsLumpIsReadFromTheWad()
        {
            var pwad = Path.Combine(Path.GetTempPath(), "dptdoom-animdefs-" +
                                                      Guid.NewGuid().ToString("N") + ".wad");

            try
            {
                WritePwad(pwad, "ANIMDEFS",
                    "flat NUKAGE1\n{\n    pic NUKAGE1 tics 2\n    pic NUKAGE2 tics 2\n}\n");

                var args = new CommandLineArgs(
                    new[] { "-iwad", WadPath.Doom2, "-file", pwad, "-nodata" });

                using (var content = new GameContent(args))
                {
                    var world = BuildMap01(content).World;

                    var cycle = FindCycle(world, false);

                    Assert.IsNotNull(cycle, "the lump was found without a flag");
                    Assert.AreEqual(2, cycle.Frames.Length);
                }
            }
            finally
            {
                try { File.Delete(pwad); } catch { }
            }
        }

        /// <summary>The first cycle that came from ANIMDEFS, or null.</summary>
        private static TextureAnimationInfo FindCycle(World world, bool isTexture)
        {
            foreach (var animation in world.Map.Animation.Animations)
            {
                if (animation.Frames != null && animation.IsTexture == isTexture)
                {
                    return animation;
                }
            }

            return null;
        }

        private static GameContent RealContent()
        {
            return new GameContent(new CommandLineArgs(new[] { "-iwad", WadPath.Doom2, "-nodata" }));
        }

        private static void WritePwad(string path, string lumpName, string content)
        {
            var data = System.Text.Encoding.ASCII.GetBytes(content);

            // Null-padded, not space-padded: that is the WAD format, and the reader stops at the first
            // zero.
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

        private static DoomGame BuildMap01(GameContent content)
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

            return game;
        }
    }
}
