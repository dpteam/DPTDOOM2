using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Boom's generalized stairs: direction, step size, speed, and whether the chain may cross a
    /// change of floor texture.
    ///
    /// This used to route to the vanilla stair builder, which can express none of it: the
    /// direction bit was dropped, so a map with **descending** stairs got ascending ones - the
    /// staircase appears, in the wrong place, and nothing in the log says why. The step table was
    /// wrong too: selector 0 means four units in Boom, and the decoder said eight.
    ///
    /// These read the movers straight after the line runs instead of letting the stairs travel.
    /// The decision - which sector, which height, which direction - is made when the line runs, so
    /// reading it there tests the decision without the map's geometry interfering.
    /// </summary>
    [TestClass]
    public class BoomStairsTest
    {
        private const int Tag = 20003;

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

            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);

            return game;
        }

        /// <summary>
        /// A sector that can start a staircase, the sector its first step leads to, and the line
        /// between them.
        ///
        /// The line must have the starting sector as its **front**, because that is how Boom walks
        /// the chain: it looks for a two-sided line whose front side is the current sector. A line
        /// the other way round is not a staircase to this code, and picking one would make the test
        /// pass or fail on which line happened to come first.
        /// </summary>
        private static bool FindStairStart(World world, out Sector start, out Sector next,
                                           out LineDef line)
        {
            foreach (var sector in world.Map.Sectors)
            {
                if (sector.SpecialData != null || IsOccupied(world, sector))
                {
                    continue;
                }

                foreach (var candidate in sector.Lines)
                {
                    if (candidate.FrontSector != sector || candidate.BackSector == null)
                    {
                        continue;
                    }

                    var back = candidate.BackSector;

                    if (back == sector || back.SpecialData != null)
                    {
                        continue;
                    }

                    start = sector;
                    next = back;
                    line = candidate;
                    return true;
                }
            }

            start = null;
            next = null;
            line = null;
            return false;
        }

        private static bool IsOccupied(World world, Sector sector)
        {
            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;

                if (mobj != null && mobj.Subsector != null && mobj.Subsector.Sector == sector)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A generalized stair line with the given bits.
        ///
        /// <paramref name="selector"/> is the two-bit field as the line carries it - 0, 1, 2 or 3 -
        /// not the step size in units. Passing the size was the first version of this helper and it
        /// silently wrote into the texture-ignore bit, which is how a test can be wrong and look
        /// reasonable.
        /// </summary>
        private static LineSpecial Stairs(bool up, int selector, bool ignoreTexture,
                                          BoomGeneralized.Speed speed)
        {
            var special = BoomGeneralized.GenStairsBase
                          | ((int)speed << 3)
                          | (selector << 6);

            if (up)
            {
                special |= 1 << 8;
            }

            if (ignoreTexture)
            {
                special |= 1 << 9;
            }

            return (LineSpecial)special;
        }

        [TestMethod]
        public void TheStepSizeComesFromTheLine()
        {
            // Selector to size, Boom's table.
            var table = new[] { 4, 8, 16, 24 };

            for (var selector = 0; selector < table.Length; selector++)
            {
                var step = table[selector];

                using (var content = GameContent.CreateDummy(WadPath.Doom2))
                {
                    var game = NewGame(content);
                    var world = game.World;

                    Sector start, next;
                    LineDef line;

                    Assert.IsTrue(FindStairStart(world, out start, out next, out line));
                    LinkFixture(start, next, line);

                    var expected = start.FloorHeight + Fixed.FromInt(step);

                    line.Special = Stairs(true, selector, true, BoomGeneralized.Speed.Normal);

                    Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                    var mover = start.SpecialData as FloorMove;

                    Assert.IsNotNull(mover, "selector " + selector + ": the first step started");
                    Assert.AreEqual(expected.Data, mover.FloorDestHeight.Data,
                        "selector " + selector + " means " + step + " units up");
                }
            }
        }

        [TestMethod]
        public void AStaircaseCanGoDown()
        {
            // The regression this exists for: the direction bit used to be dropped, so every
            // generalized staircase in every map went up.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                Sector start, next;
                LineDef line;

                Assert.IsTrue(FindStairStart(world, out start, out next, out line));
                LinkFixture(start, next, line);

                line.Special = Stairs(false, 1, true, BoomGeneralized.Speed.Normal);

                var destination = start.FloorHeight - Fixed.FromInt(8);

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                var mover = start.SpecialData as FloorMove;

                Assert.IsNotNull(mover);
                Assert.AreEqual(-1, mover.Direction, "the mover goes down");
                Assert.AreEqual(destination.Data, mover.FloorDestHeight.Data,
                    "and the destination is one step below");
            }
        }

        [TestMethod]
        public void TheChainStopsAtAChangeOfFloorTexture()
        {
            // In Boom a staircase ends where the floor texture changes: that is how the author
            // marks the last step. The bit says whether to ignore that.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                Sector start, next;
                LineDef line;

                Assert.IsTrue(FindStairStart(world, out start, out next, out line));
                LinkFixture(start, next, line);

                // The neighbour looks different, so the chain must stop there.
                next.FloorFlat = DifferentFlat(content, start.FloorFlat);

                line.Special = Stairs(true, 1, false, BoomGeneralized.Speed.Normal);

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                Assert.IsNotNull(start.SpecialData, "the first step still moves");
                Assert.IsNull(next.SpecialData, "and the chain stopped at the change");
            }
        }

        [TestMethod]
        public void TheChainCrossesAChangeOfFloorTextureWhenTheBitIsSet()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                Sector start, next;
                LineDef line;

                Assert.IsTrue(FindStairStart(world, out start, out next, out line));
                LinkFixture(start, next, line);

                next.FloorFlat = DifferentFlat(content, start.FloorFlat);

                line.Special = Stairs(true, 1, true, BoomGeneralized.Speed.Normal);

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                Assert.IsNotNull(start.SpecialData, "the first step moves");
                Assert.IsNotNull(next.SpecialData, "and the next one does too");

                var second = next.SpecialData as FloorMove;

                Assert.IsNotNull(second);
                Assert.AreEqual((start.FloorHeight + Fixed.FromInt(16)).Data,
                    second.FloorDestHeight.Data, "two steps up, not one");
            }
        }

        [TestMethod]
        public void TheSpeedComesFromTheLine()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                Sector start, next;
                LineDef line;

                Assert.IsTrue(FindStairStart(world, out start, out next, out line));
                LinkFixture(start, next, line);

                line.Special = Stairs(true, 1, true, BoomGeneralized.Speed.Turbo);

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                var mover = start.SpecialData as FloorMove;
                var expected = BoomGeneralized.SpeedOf(BoomGeneralized.Family.Stairs,
                    BoomGeneralized.Speed.Turbo);

                Assert.IsNotNull(mover);
                Assert.AreEqual(expected.Data, mover.Speed.Data, "turbo, not the vanilla stair speed");
            }
        }

        /// <summary>
        /// Makes the pair usable as a staircase: same tag, same floor texture, neighbour one step up.
        ///
        /// The texture has to match or the chain stops before it starts, and the height has to
        /// differ or the staircase is invisible - both would turn the test into a statement about
        /// MAP01's layout instead of about the format.
        /// </summary>
        private static void LinkFixture(Sector start, Sector next, LineDef line)
        {
            start.Tag = Tag;
            line.Tag = (short)Tag;

            next.FloorFlat = start.FloorFlat;
            next.FloorHeight = start.FloorHeight + Fixed.FromInt(64);
        }

        private static int DifferentFlat(GameContent content, int notThis)
        {
            for (var i = 0; i < content.Flats.Count; i++)
            {
                if (i != notThis)
                {
                    return i;
                }
            }

            Assert.Fail("this WAD has only one flat");
            return -1;
        }
    }
}
