using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The Boom mover types that had to be added for the named line specials:
    /// lower-floor-to-nearest, the two ceiling targets, and the 212 toggle.
    ///
    /// These are checked through the real movers on the real map rather than by
    /// re-implementing the arithmetic in the test. A test that duplicates the
    /// formula only proves the formula was copied twice.
    /// </summary>
    [TestClass]
    public class BoomMoverTest
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
        /// A line whose tag points at the sector, so the sector action finds it.
        /// The action only uses the tag and the front sector, so an existing line
        /// from the map will do as long as its tag is set correctly.
        /// </summary>
        private static LineDef LineFor(World world, Sector sector)
        {
            // The sector action finds its targets by matching the line tag against
            // the sector tag, so both have to be set to the same value. Using the
            // sector number would only work on a map where tags happen to equal
            // indices, and MAP01 is not one.
            const int tag = 20000;

            sector.Tag = tag;

            foreach (var line in sector.Lines)
            {
                if (line.FrontSector == sector && line.FrontSide != null)
                {
                    line.Tag = (short)tag;
                    return line;
                }
            }

            return null;
        }

        [TestMethod]
        public void LowerFloorToNearestStopsAtTheNextStep()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sa = world.SectorAction;

                // Find a sector whose lowest neighbour is below it but which has
                // more than one neighbour height, so "next lower" and "lowest"
                // differ. Without such a sector the test proves nothing.
                Sector candidate = null;
                var lowest = Fixed.Zero;
                var next = Fixed.Zero;

                foreach (var sector in world.Map.Sectors)
                {
                    var low = Fixed.Zero;
                    var lowFound = false;

                    foreach (var line in sector.Lines)
                    {
                        var other = line.FrontSector == sector ? line.BackSector : line.FrontSector;

                        if (other == null || other == sector)
                        {
                            continue;
                        }

                        if (other.FloorHeight < sector.FloorHeight)
                        {
                            if (!lowFound || other.FloorHeight < low)
                            {
                                low = other.FloorHeight;
                                lowFound = true;
                            }
                        }
                    }

                    if (!lowFound)
                    {
                        continue;
                    }

                    // The next lower is strictly above the lowest when some
                    // neighbour sits between them.
                    var between = false;

                    foreach (var line in sector.Lines)
                    {
                        var other = line.FrontSector == sector ? line.BackSector : line.FrontSector;

                        if (other == null || other == sector)
                        {
                            continue;
                        }

                        if (other.FloorHeight > low && other.FloorHeight < sector.FloorHeight)
                        {
                            between = true;
                            break;
                        }
                    }

                    if (between && sector.SpecialData == null)
                    {
                        candidate = sector;
                        lowest = low;
                        break;
                    }
                }

                Assert.IsNotNull(candidate,
                    "MAP01 has no sector with three floor levels in a row, so this test cannot run");

                var sectorLine = LineFor(world, candidate);
                Assert.IsNotNull(sectorLine, "no line found for the chosen sector");

                var moved = sa.DoFloor(sectorLine, FloorMoveType.LowerFloorToNearest);
                Assert.IsTrue(moved, "the floor move was not created");

                var floor = candidate.SpecialData as FloorMove;
                Assert.IsNotNull(floor, "the sector has no floor thinker");

                Console.WriteLine("sector " + candidate.Number +
                                  " floor=" + candidate.FloorHeight.ToFloat() +
                                  " destination=" + floor.FloorDestHeight.ToFloat() +
                                  " lowest neighbour=" + lowest.ToFloat());

                Assert.IsTrue(floor.FloorDestHeight > lowest,
                    "lower-to-nearest must stop above the lowest neighbour, or it is just lower-to-lowest");

                Assert.IsTrue(floor.FloorDestHeight < candidate.FloorHeight,
                    "the destination must be below the current floor");
            }
        }

        [TestMethod]
        public void LowerToMaxFloorUsesTheHighestNeighbouringFloor()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sa = world.SectorAction;

                Sector candidate = null;
                var expected = Fixed.Zero;

                foreach (var sector in world.Map.Sectors)
                {
                    var high = Fixed.Zero;
                    var found = false;

                    foreach (var line in sector.Lines)
                    {
                        var other = line.FrontSector == sector ? line.BackSector : line.FrontSector;

                        if (other == null || other == sector)
                        {
                            continue;
                        }

                        if (other.FloorHeight > sector.FloorHeight)
                        {
                            if (!found || other.FloorHeight > high)
                            {
                                high = other.FloorHeight;
                                found = true;
                            }
                        }
                    }

                    // Only useful when the ceiling has room to come down to it.
                    if (found && high < sector.CeilingHeight && sector.SpecialData == null)
                    {
                        candidate = sector;
                        expected = high;
                        break;
                    }
                }

                Assert.IsNotNull(candidate, "MAP01 has no sector suitable for this test");

                var sectorLine = LineFor(world, candidate);
                Assert.IsNotNull(sectorLine);

                var moved = sa.DoCeiling(sectorLine, CeilingMoveType.LowerToMaxFloor);
                Assert.IsTrue(moved, "the ceiling move was not created");

                var ceiling = candidate.SpecialData as CeilingMove;
                Assert.IsNotNull(ceiling, "the sector has no ceiling thinker");

                Console.WriteLine("sector " + candidate.Number +
                                  " ceiling=" + candidate.CeilingHeight.ToFloat() +
                                  " target=" + ceiling.BottomHeight.ToFloat() +
                                  " highest neighbour floor=" + expected.ToFloat());

                Assert.AreEqual(expected.Data, ceiling.BottomHeight.Data,
                    "the ceiling must stop at the highest neighbouring floor");

                Assert.AreEqual(-1, ceiling.Direction, "the ceiling must be going down");
            }
        }

        [TestMethod]
        public void TogglePlatformFlipsBetweenFloorAndCeiling()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sa = world.SectorAction;

                // Any sector with head room will do.
                Sector candidate = null;

                foreach (var sector in world.Map.Sectors)
                {
                    if (sector.CeilingHeight - sector.FloorHeight > Fixed.FromInt(64) &&
                        sector.Lines.Length > 0 && sector.SpecialData == null)
                    {
                        candidate = sector;
                        break;
                    }
                }

                Assert.IsNotNull(candidate, "MAP01 has no sector with head room");

                var sectorLine = LineFor(world, candidate);
                Assert.IsNotNull(sectorLine);

                var floorHeight = candidate.FloorHeight;
                var ceilingHeight = candidate.CeilingHeight;

                var moved = sa.DoPlatform(sectorLine, PlatformType.ToggleUpDown, 0);
                Assert.IsTrue(moved, "the toggle platform was not created");

                var platform = candidate.SpecialData as Platform;
                Assert.IsNotNull(platform, "the sector has no platform thinker");

                // Boom's encoding: Low is the ceiling, High is the floor, and the
                // first trigger moves toward Low. It looks inverted and is not.
                Assert.AreEqual(ceilingHeight.Data, platform.Low.Data,
                    "the first end of the toggle is the ceiling");
                Assert.AreEqual(floorHeight.Data, platform.High.Data,
                    "the second end of the toggle is the original floor");
                Assert.AreEqual(PlatformState.Down, platform.Status,
                    "the first trigger sends the floor up, which Boom encodes as Down");
                Assert.IsTrue(platform.Crush,
                    "a toggle crushes anything in the way");

                Console.WriteLine("sector " + candidate.Number +
                                  " floor=" + floorHeight.ToFloat() +
                                  " ceiling=" + ceilingHeight.ToFloat());

                // Run it: the floor should reach the ceiling.
                for (var tic = 0; tic < 200; tic++)
                {
                    platform.Run();
                }

                Assert.AreEqual(ceilingHeight.Data, candidate.FloorHeight.Data,
                    "the floor must have reached the ceiling");

                Assert.AreEqual(PlatformState.InStasis, platform.Status,
                    "a toggle parks in stasis after moving, it does not wait and drift back");

                // Trigger it again: it must go back down, not stay put.
                sa.ActivateInStasis(candidate.Tag);
                Assert.AreEqual(PlatformState.Up, platform.Status,
                    "the second trigger must send it the other way");

                for (var tic = 0; tic < 200; tic++)
                {
                    platform.Run();
                }

                Assert.AreEqual(floorHeight.Data, candidate.FloorHeight.Data,
                    "the floor must be back at its original height");
            }
        }

        [TestMethod]
        public void NamedSpecialsDoNotClearRepeatableLines()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                // Pick a line and drive it through the cross dispatcher as a
                // player would. A repeatable special must survive; a one-shot must
                // not. This is the difference between 147 and 142, and getting it
                // wrong makes a repeatable lift work exactly once.
                var sector = world.Map.Sectors[0];
                var line = LineFor(world, sector);
                Assert.IsNotNull(line);

                line.Special = (LineSpecial)147;
                world.MapInteraction.CrossSpecialLine(line, 0, world.ConsolePlayer.Mobj);
                Assert.AreEqual(147, (int)line.Special,
                    "a repeatable walk-over line must keep its special");

                line.Special = (LineSpecial)142;
                world.MapInteraction.CrossSpecialLine(line, 0, world.ConsolePlayer.Mobj);
                Assert.AreEqual(0, (int)line.Special,
                    "a one-shot walk-over line must clear its special");
            }
        }
    }
}
