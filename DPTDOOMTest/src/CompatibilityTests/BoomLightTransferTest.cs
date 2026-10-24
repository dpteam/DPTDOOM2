using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Boom's light transfer lines: 213 for floors, 261 for ceilings.
    ///
    /// The feature exists so a lava floor can pulse with a light above it. That only
    /// works if the *index* of the control sector is stored and its light is read at
    /// render time - baking the value during map load would look right for one frame
    /// and then stay frozen while the control sector flickered.
    ///
    /// So the tests check the live read, not just the wiring.
    /// </summary>
    [TestClass]
    public class BoomLightTransferTest
    {
        private static DoomGame NewGame(GameContent content)
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
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);

            return game;
        }

        /// <summary>
        /// Takes a two-sided line, makes its front sector the control, and tags a
        /// different sector to receive the light.
        /// </summary>
        private static void Wire(World world, int special, out Sector control, out Sector target)
        {
            control = null;
            target = null;

            foreach (var line in world.Map.Lines)
            {
                if (line.FrontSide == null || line.FrontSide.Sector == null ||
                    line.BackSide == null || line.BackSide.Sector == null)
                {
                    continue;
                }

                control = line.FrontSide.Sector;
                target = line.BackSide.Sector;

                line.Tag = (short)1993;
                line.Special = (LineSpecial)special;
                target.Tag = 1993;

                break;
            }

            Assert.IsNotNull(control, "MAP01 has a two-sided line to build the transfer on");
            Assert.AreNotSame(control, target, "the control and the target are different sectors");

            world.Specials.BuildLightTransfers();
        }

        [TestMethod]
        public void FloorBorrowsTheControlSectorsLight()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sectors = world.Map.Sectors;

                Sector control;
                Sector target;
                Wire(world, 213, out control, out target);

                control.LightLevel = 224;
                target.LightLevel = 32;

                Assert.AreEqual(224, target.FloorLightLevel(sectors),
                    "the floor is lit as if it were the control sector");

                // The ceiling is untouched: 213 is the floor special.
                Assert.AreEqual(32, target.CeilingLightLevel(sectors),
                    "special 213 must not touch the ceiling");
            }
        }

        [TestMethod]
        public void CeilingBorrowsTheControlSectorsLight()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sectors = world.Map.Sectors;

                Sector control;
                Sector target;
                Wire(world, 261, out control, out target);

                control.LightLevel = 208;
                target.LightLevel = 48;

                Assert.AreEqual(208, target.CeilingLightLevel(sectors),
                    "the ceiling is lit as if it were the control sector");

                Assert.AreEqual(48, target.FloorLightLevel(sectors),
                    "special 261 must not touch the floor");
            }
        }

        [TestMethod]
        public void TheLightIsReadLiveNotCopied()
        {
            // This is the whole point of the special: a flickering control sector has
            // to animate everything that borrows from it. A value copied at load time
            // would pass every other test here and still be wrong.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sectors = world.Map.Sectors;

                Sector control;
                Sector target;
                Wire(world, 213, out control, out target);

                control.LightLevel = 255;
                Assert.AreEqual(255, target.FloorLightLevel(sectors));

                control.LightLevel = 96;
                Assert.AreEqual(96, target.FloorLightLevel(sectors),
                    "changing the control sector must change the borrowed light");
            }
        }

        [TestMethod]
        public void SectorsWithoutATransferKeepTheirOwnLight()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sectors = world.Map.Sectors;

                // Before any transfer line is wired, nothing borrows anything. This
                // is what keeps vanilla maps byte-identical: every sector reads its
                // own light, exactly as before.
                foreach (var sector in sectors)
                {
                    Assert.AreEqual(-1, sector.FloorLightSector);
                    Assert.AreEqual(-1, sector.CeilingLightSector);
                    Assert.AreEqual(sector.LightLevel, sector.FloorLightLevel(sectors));
                    Assert.AreEqual(sector.LightLevel, sector.CeilingLightLevel(sectors));
                }
            }
        }
    }
}
