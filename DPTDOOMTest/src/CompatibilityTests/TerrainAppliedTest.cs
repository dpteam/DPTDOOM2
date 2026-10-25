using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// TERRAIN reaches the world: the floor hurts, and the floor is slippery.
    ///
    /// The parser is tested on its own; what only a real load can show is the join, and the join
    /// is the whole point of the format. A terrain that parses and is bound to a flat this map
    /// does not use would leave every corridor exactly as it was.
    ///
    /// The flat is discovered rather than assumed: the test loads MAP01 once, asks which flat the
    /// player is standing on, and writes the TERRAIN for that flat. Hard-coding FLOOR4_8 would
    /// make the test a statement about MAP01's layout rather than about the format, and a map
    /// change would turn it into a test of nothing.
    /// </summary>
    [TestClass]
    public class TerrainAppliedTest
    {
        [TestInitialize]
        public void Before()
        {
            Terrain.Clear();
            LoadReport.Clear();
        }

        [TestCleanup]
        public void After()
        {
            Terrain.Clear();
        }

        /// <summary>The name of the flat the player starts on, with MAP01 loaded without a terrain.</summary>
        private static string PlayerFloorFlat()
        {
            using (var content = new GameContent(
                new CommandLineArgs(new[] { "-iwad", WadPath.Doom2, "-nodata" })))
            {
                var game = Build(content, out _);

                var sector = game.World.ConsolePlayer.Mobj.Subsector.Sector;

                return content.Flats[sector.FloorFlat].Name;
            }
        }

        [TestMethod]
        public void AHurtingTerrainTakesHealthFromThePlayer()
        {
            var flat = PlayerFloorFlat();

            var pwad = WritePwad(
                "terrain TestNukage\n" +
                "{\n" +
                "    damageamount 5\n" +
                "    damagetimemask 0\n" +
                "}\n" +
                "floor " + flat + " TestNukage\n");

            try
            {
                var args = new CommandLineArgs(
                    new[] { "-iwad", WadPath.Doom2, "-file", pwad, "-nodata" });

                using (var content = new GameContent(args))
                {
                    var game = Build(content, out var cmds);

                    var player = game.World.ConsolePlayer;

                    // Building the world already runs one tic, and mask zero means "every tic", so
                    // the starting health is measured rather than assumed to be 100.
                    var start = player.Health;

                    Assert.IsNotNull(Terrain.ForFlat(content.Flats.GetNumber(flat)),
                        "the flat is bound to the terrain");

                    // Mask zero is "every tic", so a handful of tics is enough to see it. Ten
                    // would still be survivable, which keeps the test about damage rather than
                    // about death.
                    for (var tic = 0; tic < 10; tic++)
                    {
                        game.Update(cmds);
                    }

                    Assert.AreEqual(start - 50, player.Health, "five damage times ten tics");
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        [TestMethod]
        public void WithoutATerrainTheFloorIsHarmless()
        {
            // The control. Without it, a test that finds damage proves nothing about the terrain:
            // the damage could have come from the map.
            using (var content = new GameContent(
                new CommandLineArgs(new[] { "-iwad", WadPath.Doom2, "-nodata" })))
            {
                var game = Build(content, out var cmds);

                var player = game.World.ConsolePlayer;

                for (var tic = 0; tic < 10; tic++)
                {
                    game.Update(cmds);
                }

                Assert.AreEqual(100, player.Health);
                Assert.IsFalse(Terrain.Any);
            }
        }

        [TestMethod]
        public void ASlipperyTerrainSlowsMovement()
        {
            // Friction is applied where movement is slowed, so it can only be measured by moving.
            // A factor of 0.25 should leave the player well short of where ordinary ground takes
            // them, and that is the assertion - not the exact distance, which depends on the
            // map's geometry.
            var flat = PlayerFloorFlat();

            var normal = WalkDistance(null);
            var slippery = WalkDistance(
                "terrain Slick { friction 0.25 }\nfloor " + flat + " Slick\n");

            Assert.IsTrue(normal > Fixed.Zero, "the player moved at all on ordinary ground");
            Assert.IsTrue(slippery < normal,
                "slippery ground moved the player " + slippery.ToFloat().ToString("0.0") +
                " against " + normal.ToFloat().ToString("0.0"));
        }

        [TestMethod]
        public void AProtectingTerrainSparesThePlayerWithIronFeet()
        {
            // `allowprotection` is the difference between a floor a suit protects against and one
            // it does not. Radiation suits exist in the engine already; this is what connects them
            // to a terrain instead of to a sector special.
            var flat = PlayerFloorFlat();

            var pwad = WritePwad(
                "terrain Shielded\n" +
                "{\n" +
                "    damageamount 5\n" +
                "    damagetimemask 0\n" +
                "    allowprotection\n" +
                "}\n" +
                "floor " + flat + " Shielded\n");

            try
            {
                var args = new CommandLineArgs(
                    new[] { "-iwad", WadPath.Doom2, "-file", pwad, "-nodata" });

                using (var content = new GameContent(args))
                {
                    var game = Build(content, out var cmds);

                    var player = game.World.ConsolePlayer;

                    // Measured, not assumed: the first tic already ran, and a hurting terrain may
                    // have taken health on it.
                    var start = player.Health;

                    // The suit, exactly as the engine gives it. `Powers` is a count of tics left,
                    // not a flag: setting it to one protects for one tic, which looks like the
                    // suit doing nothing.
                    player.Powers[(int)PowerType.IronFeet] = DoomInfo.PowerDuration.IronFeet;

                    for (var tic = 0; tic < 10; tic++)
                    {
                        game.Update(cmds);
                    }

                    Assert.AreEqual(start, player.Health, "the suit protects");
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        [TestMethod]
        public void ABindingToAFlatThisWadDoesNotHaveIsReported()
        {
            var pwad = WritePwad("terrain T { damageamount 5 }\nfloor NOSUCHFLAT T\n");

            try
            {
                var args = new CommandLineArgs(
                    new[] { "-iwad", WadPath.Doom2, "-file", pwad, "-nodata" });

                using (var content = new GameContent(args))
                {
                    Assert.IsTrue(LoadReport.HasProblems);
                    StringAssert.Contains(LoadReport.Describe(), "NOSUCHFLAT");
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        [TestMethod]
        public void ASplashThatIsNotDefinedIsReported()
        {
            var pwad = WritePwad(
                "terrain T { splash Missing }\n" +
                "floor NUKAGE1 T\n");

            try
            {
                var args = new CommandLineArgs(
                    new[] { "-iwad", WadPath.Doom2, "-file", pwad, "-nodata" });

                using (var content = new GameContent(args))
                {
                    Assert.IsTrue(LoadReport.HasProblems);
                    StringAssert.Contains(LoadReport.Describe(), "Missing");
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        /// <summary>
        /// Walks the player forward for a fixed number of tics and returns how far they got.
        ///
        /// The direction is the player's own facing, so the answer does not depend on the map's
        /// orientation, and the distance is measured in the map plane rather than per axis.
        /// </summary>
        private static Fixed WalkDistance(string terrainText)
        {
            string pwad = null;

            if (terrainText != null)
            {
                pwad = WritePwad(terrainText);
            }

            try
            {
                var args = pwad == null
                    ? new[] { "-iwad", WadPath.Doom2, "-nodata" }
                    : new[] { "-iwad", WadPath.Doom2, "-file", pwad, "-nodata" };

                using (var content = new GameContent(new CommandLineArgs(args)))
                {
                    var game = Build(content, out var cmds);

                    var player = game.World.ConsolePlayer;
                    var startX = player.Mobj.X;
                    var startY = player.Mobj.Y;

                    // Full forward input for thirty tics. The player's own command is the only
                    // thing driving movement, so nothing here touches the simulation by hand.
                    for (var tic = 0; tic < 30; tic++)
                    {
                        cmds[0].ForwardMove = 50;
                        game.Update(cmds);
                    }

                    return Geometry.AproxDistance(player.Mobj.X - startX, player.Mobj.Y - startY);
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        private static DoomGame Build(GameContent content, out TicCmd[] cmds)
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

            cmds = new TicCmd[Player.MaxPlayerCount];

            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);

            return game;
        }

        private static string WritePwad(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-terrain-" +
                                                      Guid.NewGuid().ToString("N") + ".wad");

            var data = System.Text.Encoding.ASCII.GetBytes(content);

            // Null-padded, not space-padded: that is the WAD format, and the reader stops at the
            // first zero.
            var name = new byte[8];
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("TERRAIN"), name, 7);

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

            return path;
        }

        private static void Delete(string path)
        {
            if (path == null)
            {
                return;
            }

            try { File.Delete(path); } catch { }
        }
    }
}
