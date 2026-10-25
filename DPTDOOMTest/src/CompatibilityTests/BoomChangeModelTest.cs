using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A generalized floor or ceiling copies the picture its change model says.
    ///
    /// One of these was a gap and the other a real defect: the numeric model was decoded and never
    /// used, so every change copied the line's front sector; and a generalized **ceiling** ignored
    /// its change field entirely, so it never swapped its flat at all. Both produce a level that
    /// looks wrong in a way nobody can trace back to the line that caused it.
    ///
    /// Most of these read the mover's own fields straight after the line runs, rather than letting
    /// the floor travel and looking at the sector afterwards. That is not a shortcut: the choice of
    /// picture is made when the line runs and applied when the move ends, so reading it early is
    /// what isolates the decision from the movement. The arrival itself is checked separately, on a
    /// ceiling, where the geometry of a real MAP01 does not get in the way.
    /// </summary>
    [TestClass]
    public class BoomChangeModelTest
    {
        private const int Tag = 20002;

        private static DoomGame NewGame(GameContent content, out TicCmd[] cmds)
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

        /// <summary>
        /// A sector with a two-sided neighbour, plus a line of its own that can be given a tag.
        ///
        /// The moving sector must be **empty and idle**: a floor that rises under a thing is
        /// reported as a crush and rolled back, and a sector that already owns a thinker cannot
        /// start a second move. The first version of this test picked sector 0 - the one the player
        /// stands in - and watched a floor that could not go anywhere.
        /// </summary>
        private static bool FindPair(World world, out Sector moving, out Sector neighbour,
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
                    if (candidate.BackSector == null || candidate.FrontSide == null)
                    {
                        continue;
                    }

                    var other = candidate.FrontSector == sector
                        ? candidate.BackSector
                        : candidate.FrontSector;

                    if (other == null || other == sector)
                    {
                        continue;
                    }

                    moving = sector;
                    neighbour = other;
                    line = candidate;
                    return true;
                }
            }

            moving = null;
            neighbour = null;
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

        /// <summary>The mover this sector now owns, or null.</summary>
        private static FloorMove FloorOf(Sector sector)
        {
            return sector.SpecialData as FloorMove;
        }

        private static CeilingMove CeilingOf(Sector sector)
        {
            return sector.SpecialData as CeilingMove;
        }

        [TestMethod]
        public void TheNumericModelTakesThePictureFromTheNeighbourAtTheTargetHeight()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                Sector moving, neighbour;
                LineDef line;

                Assert.IsTrue(FindPair(world, out moving, out neighbour, out line),
                    "MAP01 has an idle, empty sector sharing a two-sided line");

                var fromLine = FlatNumber(content, "FLOOR0_1");
                var fromModel = FlatNumber(content, "NUKAGE1");

                Assert.AreNotEqual(fromLine, fromModel, "the two flats must differ");

                moving.FloorFlat = fromLine;
                neighbour.FloorFlat = fromModel;

                // The floor rises to the highest neighbouring floor, which is where the neighbour
                // already is - no heights are touched, so nothing nearby can get stuck.
                neighbour.FloorHeight = moving.FloorHeight + Fixed.FromInt(64);

                moving.Tag = Tag;
                line.Tag = (short)Tag;

                line.Special = (LineSpecial)(
                    BoomGeneralized.GenFloorBase
                    | (0 << 7)              // Targ = highest neighbouring floor
                    | (1 << 6)              // Dirn = up
                    | (2 << 10)             // ChgT = change texture, leave type
                    | (1 << 5)              // ChgM = numeric model
                    | (1 << 3));            // Sped = normal

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                var floor = FloorOf(moving);

                Assert.IsNotNull(floor, "the floor is moving");
                Assert.AreEqual(fromModel, floor.Texture,
                    "the picture came from the neighbour at the target height, not the line");
                Assert.AreEqual(FloorMoveType.GeneralizedChange, floor.Type);
            }
        }

        [TestMethod]
        public void TheTriggerModelTakesThePictureFromTheLine()
        {
            // The control that makes the previous test mean something: with the model bit clear,
            // the same fixture must give the line's own picture. Without this, a code path that
            // always used the numeric model would pass the test above.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                Sector moving, neighbour;
                LineDef line;

                Assert.IsTrue(FindPair(world, out moving, out neighbour, out line));

                var fromLine = FlatNumber(content, "FLOOR0_1");
                var fromModel = FlatNumber(content, "NUKAGE1");

                moving.FloorFlat = fromLine;
                neighbour.FloorFlat = fromModel;
                neighbour.FloorHeight = moving.FloorHeight + Fixed.FromInt(64);

                moving.Tag = Tag;
                line.Tag = (short)Tag;

                line.Special = (LineSpecial)(
                    BoomGeneralized.GenFloorBase
                    | (0 << 7)
                    | (1 << 6)
                    | (2 << 10)
                    | (0 << 5)              // ChgM = trigger model
                    | (1 << 3));

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                var floor = FloorOf(moving);

                Assert.IsNotNull(floor);
                Assert.AreEqual(fromLine, floor.Texture, "the line's own picture");
                Assert.AreEqual(FloorMoveType.GeneralizedChange, floor.Type);
            }
        }

        [TestMethod]
        public void TheNumericModelChangesNothingWhenNoModelSectorIsThere()
        {
            // Boom's answer to a model that does not exist is to leave the picture alone rather
            // than to guess, and the floor still travels - which is why this cannot be a refusal.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                Sector moving, neighbour;
                LineDef line;

                Assert.IsTrue(FindPair(world, out moving, out neighbour, out line));

                moving.FloorFlat = FlatNumber(content, "FLOOR0_1");
                moving.Tag = Tag;
                line.Tag = (short)Tag;

                // A fixed 32-unit rise: no neighbour is at that height, so the search fails.
                // The neighbours are pushed far away so the test does not depend on where MAP01
                // happens to put them.
                MoveNeighboursAway(moving, neighbour);

                line.Special = (LineSpecial)(
                    BoomGeneralized.GenFloorBase
                    | (7 << 7)              // Targ = Fby32
                    | (1 << 6)
                    | (2 << 10)
                    | (1 << 5)
                    | (1 << 3));

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                var floor = FloorOf(moving);

                Assert.IsNotNull(floor, "the floor is still moving");
                Assert.AreEqual(FloorMoveType.Generalized, floor.Type,
                    "and it is a plain move, because there is nothing to copy");
            }
        }

        [TestMethod]
        public void AGeneralizedCeilingChangesItsPicture()
        {
            // This is the gap: the change field of a generalized ceiling was decoded and never
            // read, so the ceiling always arrived with the flat it left with.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                Sector moving, neighbour;
                LineDef line;

                Assert.IsTrue(FindPair(world, out moving, out neighbour, out line));

                var original = FlatNumber(content, "CEIL1_1");
                var fromModel = FlatNumber(content, "CEIL3_5");

                moving.CeilingFlat = original;
                neighbour.CeilingFlat = fromModel;
                neighbour.CeilingHeight = moving.CeilingHeight - Fixed.FromInt(64);

                moving.Tag = Tag;
                line.Tag = (short)Tag;

                line.Special = (LineSpecial)(
                    BoomGeneralized.GenCeilingBase
                    | (1 << 7)              // Targ = lowest neighbouring ceiling
                    | (0 << 6)              // Dirn = down
                    | (2 << 10)             // ChgT = change texture, leave type
                    | (1 << 5)              // ChgM = numeric model
                    | (1 << 3));

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                var ceiling = CeilingOf(moving);

                Assert.IsNotNull(ceiling, "the ceiling is moving");
                Assert.AreEqual(CeilingMoveType.GeneralizedChange, ceiling.Type,
                    "the change is actually wired up");
                Assert.AreEqual(fromModel, ceiling.Texture,
                    "and it took the neighbour's ceiling picture");
            }
        }

        [TestMethod]
        public void ACeilingWithoutAChangeIsAPlainMove()
        {
            // The other half: a generalized ceiling that says nothing about its picture must keep
            // the plain type, or every such ceiling would try to change to a flat it never named.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                Sector moving, neighbour;
                LineDef line;

                Assert.IsTrue(FindPair(world, out moving, out neighbour, out line));

                neighbour.CeilingHeight = moving.CeilingHeight - Fixed.FromInt(64);

                moving.Tag = Tag;
                line.Tag = (short)Tag;

                line.Special = (LineSpecial)(
                    BoomGeneralized.GenCeilingBase
                    | (1 << 7)
                    | (0 << 6)
                    | (0 << 10)             // ChgT = no change
                    | (0 << 5)
                    | (1 << 3));

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                var ceiling = CeilingOf(moving);

                Assert.IsNotNull(ceiling);
                Assert.AreEqual(CeilingMoveType.Generalized, ceiling.Type);
            }
        }

        [TestMethod]
        public void TheArrivalAppliesThePicture()
        {
            // The decision and the application are separate steps, and only this test connects
            // them: a ceiling given a change must actually be showing the new flat when it stops.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out var cmds);
                var world = game.World;

                Sector moving, neighbour;
                LineDef line;

                Assert.IsTrue(FindPair(world, out moving, out neighbour, out line));

                var original = FlatNumber(content, "CEIL1_1");
                var fromModel = FlatNumber(content, "CEIL3_5");

                moving.CeilingFlat = original;
                neighbour.CeilingFlat = fromModel;
                neighbour.CeilingHeight = moving.CeilingHeight - Fixed.FromInt(32);

                moving.Tag = Tag;
                line.Tag = (short)Tag;

                line.Special = (LineSpecial)(
                    BoomGeneralized.GenCeilingBase
                    | (1 << 7)
                    | (0 << 6)
                    | (2 << 10)
                    | (1 << 5)
                    | (1 << 3));

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, world.ConsolePlayer.Mobj));

                Assert.AreEqual(original, moving.CeilingFlat,
                    "the picture has not changed yet - only on arrival");

                for (var tic = 0; tic < 40; tic++)
                {
                    game.Update(cmds);
                }

                Assert.IsNull(moving.SpecialData, "the ceiling finished its move");
                Assert.AreEqual(fromModel, moving.CeilingFlat,
                    "and took the picture it was told to");
            }
        }

        /// <summary>
        /// Pushes the neighbours' floors far below, so a fixed-height move has no model to find.
        ///
        /// Only the floors are moved, and only downward: raising them into the ceiling would crush
        /// whatever stands there, which has nothing to do with what this test is about.
        /// </summary>
        private static void MoveNeighboursAway(Sector moving, Sector neighbour)
        {
            foreach (var candidate in moving.Lines)
            {
                if (candidate.BackSector == null)
                {
                    continue;
                }

                var other = candidate.FrontSector == moving
                    ? candidate.BackSector
                    : candidate.FrontSector;

                if (other == null || other == moving)
                {
                    continue;
                }

                other.FloorHeight = moving.FloorHeight - Fixed.FromInt(1024);
            }
        }

        private static int FlatNumber(GameContent content, string name)
        {
            var number = content.Flats.GetNumber(name);

            Assert.IsTrue(number >= 0, name + " is a flat in this WAD");
            return number;
        }
    }
}
