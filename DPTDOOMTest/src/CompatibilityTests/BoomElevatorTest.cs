using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Boom's elevator and the texture/type change, checked through the real
    /// movers on the real map.
    ///
    /// The elevator is the one worth testing carefully: it drives two planes at
    /// once, and the order they move in decides whether the thing inside gets
    /// carried or crushed. A test that only checked the final heights would pass
    /// even if the order were wrong, so this one also watches the gap while it
    /// travels.
    ///
    /// LineDef.FrontSector is read-only, so the tests find an existing line whose
    /// front sector already is the one they need, rather than pointing a line
    /// somewhere else. That is closer to how a map actually works anyway: a switch
    /// lives in the wall of the sector you are standing in.
    /// </summary>
    [TestClass]
    public class BoomElevatorTest
    {
        private const int TestTag = 20001;

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
        /// A line that borders a sector and is usable as a trigger: it must have a
        /// front sector and a front side, because the actions read both.
        /// </summary>
        private static LineDef LineBordering(Sector sector)
        {
            foreach (var line in sector.Lines)
            {
                if (line.FrontSector != null && line.FrontSide != null)
                {
                    return line;
                }
            }

            return null;
        }

        [TestMethod]
        public void ElevatorKeepsTheGapWhileMoving()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sa = world.SectorAction;

                // A sector with head room and a neighbour at a higher floor, so
                // the elevator has somewhere to go.
                Sector candidate = null;

                foreach (var sector in world.Map.Sectors)
                {
                    if (sector.SpecialData != null)
                    {
                        continue;
                    }

                    if (sector.CeilingHeight - sector.FloorHeight < Fixed.FromInt(64))
                    {
                        continue;
                    }

                    foreach (var line in sector.Lines)
                    {
                        var other = line.FrontSector == sector ? line.BackSector : line.FrontSector;

                        if (other != null && other != sector &&
                            other.FloorHeight > sector.FloorHeight + Fixed.FromInt(16))
                        {
                            candidate = sector;
                            break;
                        }
                    }

                    if (candidate != null)
                    {
                        break;
                    }
                }

                Assert.IsNotNull(candidate, "MAP01 has no sector suitable for an elevator test");

                var trigger = LineBordering(candidate);
                Assert.IsNotNull(trigger, "the chosen sector has no usable line");

                candidate.Tag = TestTag;
                trigger.Tag = (short)TestTag;

                var originalGap = candidate.CeilingHeight - candidate.FloorHeight;
                var originalFloor = candidate.FloorHeight;

                var moved = sa.DoElevator(trigger, ElevatorType.Up);
                Assert.IsTrue(moved, "the elevator was not created");

                var elevator = candidate.SpecialData as Elevator;
                Assert.IsNotNull(elevator, "the sector has no elevator thinker");

                Assert.AreEqual(1, elevator.Direction, "up means direction 1");
                Assert.IsTrue(elevator.FloorDestHeight > originalFloor,
                    "the destination must be above the current floor");

                Console.WriteLine("sector " + candidate.Number +
                                  " floor=" + originalFloor.ToFloat() +
                                  " gap=" + originalGap.ToFloat() +
                                  " destination=" + elevator.FloorDestHeight.ToFloat());

                // Travel, checking the gap every tic. This is the part that
                // catches a wrong order: if the leading plane were the wrong one,
                // the gap would close and reopen on the way.
                for (var tic = 0; tic < 300; tic++)
                {
                    elevator.Run();

                    var gap = candidate.CeilingHeight - candidate.FloorHeight;

                    Assert.IsTrue(gap >= originalGap - Fixed.FromInt(1),
                        "the gap closed to " + gap.ToFloat() + " from " + originalGap.ToFloat() +
                        " at tic " + tic + "; the planes moved in the wrong order");
                }

                Assert.AreEqual(elevator.FloorDestHeight.Data, candidate.FloorHeight.Data,
                    "the floor must have reached its destination");

                Assert.AreEqual(originalGap.Data,
                    (candidate.CeilingHeight - candidate.FloorHeight).Data,
                    "the elevator must keep its height");

                Assert.IsNull(candidate.SpecialData,
                    "the elevator must release the sector when it arrives");
            }
        }

        [TestMethod]
        public void ElevatorToCurrentUsesTheSwitchSectorHeight()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sa = world.SectorAction;

                // A line whose front sector is higher than the sector behind it:
                // the switch is up on a ledge and calls the lift below.
                LineDef trigger = null;
                Sector lift = null;
                Sector platform = null;

                foreach (var line in world.Map.Lines)
                {
                    if (line.FrontSector == null || line.BackSector == null ||
                        line.FrontSector == line.BackSector || line.FrontSide == null)
                    {
                        continue;
                    }

                    var high = line.FrontSector;
                    var low = line.BackSector;

                    if (high.FloorHeight > low.FloorHeight + Fixed.FromInt(24) &&
                        low.CeilingHeight - low.FloorHeight > Fixed.FromInt(64) &&
                        low.SpecialData == null)
                    {
                        trigger = line;
                        lift = low;
                        platform = high;
                        break;
                    }
                }

                Assert.IsNotNull(trigger, "MAP01 has no sector pair suitable for this test");

                lift.Tag = TestTag;
                trigger.Tag = (short)TestTag;

                var moved = sa.DoElevator(trigger, ElevatorType.Current);
                Assert.IsTrue(moved, "the elevator was not created");

                var elevator = lift.SpecialData as Elevator;
                Assert.IsNotNull(elevator);

                Console.WriteLine("lift floor=" + lift.FloorHeight.ToFloat() +
                                  " switch floor=" + platform.FloorHeight.ToFloat() +
                                  " direction=" + elevator.Direction);

                Assert.AreEqual(platform.FloorHeight.Data, elevator.FloorDestHeight.Data,
                    "an elevator to Current must go to the switch sector's floor");

                Assert.AreEqual(1, elevator.Direction,
                    "the switch is above the lift, so it must go up");
            }
        }

        [TestMethod]
        public void ChangeCopiesTheLookFromTheLineSector()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sa = world.SectorAction;

                // A two-sided line with different flats on its two sides, so the
                // copy is visible.
                LineDef trigger = null;
                Sector target = null;
                Sector source = null;

                foreach (var line in world.Map.Lines)
                {
                    if (line.FrontSector == null || line.BackSector == null ||
                        line.FrontSector == line.BackSector || line.FrontSide == null)
                    {
                        continue;
                    }

                    if (line.FrontSector.FloorFlat != line.BackSector.FloorFlat)
                    {
                        trigger = line;
                        source = line.FrontSector;
                        target = line.BackSector;
                        break;
                    }
                }

                Assert.IsNotNull(trigger,
                    "MAP01 has no two-sided line with different flats, so this test cannot run");

                var wanted = source.FloorFlat;

                // Set the target to something else, so the assertion below can
                // only pass if the copy actually happened. Setting it to a value
                // that happens to equal the source's would make the test pass
                // without the code doing anything.
                target.FloorFlat = wanted == 0 ? 1 : 0;
                target.Tag = TestTag;
                trigger.Tag = (short)TestTag;

                var changed = sa.DoChange(trigger, false);
                Assert.IsTrue(changed, "the change did nothing");

                Console.WriteLine("target " + target.Number + " source " + source.Number +
                                  " flat=" + target.FloorFlat + " wanted=" + wanted);

                Assert.AreEqual(wanted, target.FloorFlat,
                    "the floor texture must be copied from the line's own sector");
            }
        }

        [TestMethod]
        public void NumericChangeFindsTheModelSectorByHeight()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sa = world.SectorAction;

                // The numeric model looks for a neighbour standing at the same
                // floor height as the target.
                Sector target = null;
                Sector model = null;
                LineDef trigger = null;

                foreach (var sector in world.Map.Sectors)
                {
                    foreach (var line in sector.Lines)
                    {
                        var other = line.FrontSector == sector ? line.BackSector : line.FrontSector;

                        if (other != null && other != sector &&
                            other.FloorHeight == sector.FloorHeight &&
                            line.FrontSector != null && line.FrontSide != null)
                        {
                            target = sector;
                            model = other;
                            trigger = line;
                            break;
                        }
                    }

                    if (target != null)
                    {
                        break;
                    }
                }

                Assert.IsNotNull(target,
                    "MAP01 has no two neighbouring sectors at the same height");

                const int wanted = 77;
                model.FloorFlat = wanted;
                target.FloorFlat = 3;
                target.Tag = TestTag;
                trigger.Tag = (short)TestTag;

                var changed = sa.DoChange(trigger, true);
                Assert.IsTrue(changed, "the change did nothing");

                Console.WriteLine("target " + target.Number + " model " + model.Number +
                                  " flat=" + target.FloorFlat);

                Assert.AreEqual(wanted, target.FloorFlat,
                    "the numeric change must copy the neighbouring sector at the same height");
            }
        }
    }
}
