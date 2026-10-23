using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Boom's silent teleporters.
    ///
    /// The point of a silent teleporter is that nothing happens: no flash, no stop,
    /// no change of speed. So the tests check the things that would betray it — the
    /// momentum vector, the heading relative to the entry line, and the height above
    /// the floor — rather than only checking that the position changed.
    /// </summary>
    [TestClass]
    public class SilentTeleportTest
    {
        private const int TestTag = 20002;

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

        [TestMethod]
        public void SilentLineTeleportKeepsSpeedAndHeading()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sa = world.SectorAction;
                var player = world.ConsolePlayer;

                // Two different two-sided lines, tagged alike: entry and exit.
                LineDef entry = null;
                LineDef exit = null;

                foreach (var line in world.Map.Lines)
                {
                    if (line.BackSector == null || line.FrontSide == null ||
                        line.Dx == Fixed.Zero && line.Dy == Fixed.Zero)
                    {
                        continue;
                    }

                    if (entry == null)
                    {
                        entry = line;
                        continue;
                    }

                    if (line != entry &&
                        (Math.Abs(line.Dx.Data) > 0 || Math.Abs(line.Dy.Data) > 0))
                    {
                        exit = line;
                        break;
                    }
                }

                Assert.IsNotNull(entry, "no entry line found");
                Assert.IsNotNull(exit, "no exit line found");

                entry.Tag = (short)TestTag;
                exit.Tag = (short)TestTag;

                var mobj = player.Mobj;

                // Put the player on the entry line and give them momentum, so the
                // test can tell whether the momentum survived.
                mobj.X = (entry.Vertex1.X + entry.Vertex2.X) / 2;
                mobj.Y = (entry.Vertex1.Y + entry.Vertex2.Y) / 2;
                mobj.MomX = Fixed.FromInt(8);
                mobj.MomY = Fixed.FromInt(4);
                mobj.Angle = Angle.Ang45;

                var speedBefore = Math.Sqrt(
                    mobj.MomX.ToFloat() * mobj.MomX.ToFloat() +
                    mobj.MomY.ToFloat() * mobj.MomY.ToFloat());

                var startX = mobj.X.ToFloat();
                var startY = mobj.Y.ToFloat();

                var moved = sa.SilentLineTeleport(entry, 0, mobj, false);
                Assert.IsTrue(moved, "the silent line teleport did not happen");

                var speedAfter = Math.Sqrt(
                    mobj.MomX.ToFloat() * mobj.MomX.ToFloat() +
                    mobj.MomY.ToFloat() * mobj.MomY.ToFloat());

                var endX = mobj.X.ToFloat();
                var endY = mobj.Y.ToFloat();

                var distance = Math.Sqrt(
                    (endX - startX) * (endX - startX) + (endY - startY) * (endY - startY));

                Console.WriteLine("moved " + distance.ToString("0") + " units" +
                                  " speed " + speedBefore.ToString("0.0") +
                                  " -> " + speedAfter.ToString("0.0") +
                                  " angle " + mobj.Angle.ToDegree().ToString("0"));

                Assert.IsTrue(distance > 64,
                    "the player must actually have been moved (" + distance.ToString("0") + " units)");

                // The whole point: speed is preserved. A vanilla teleporter would
                // arrive at rest; a silent one carries the momentum across.
                Assert.IsTrue(Math.Abs(speedAfter - speedBefore) < 0.5,
                    "a silent teleporter must keep the speed (" + speedBefore.ToString("0.0") +
                    " -> " + speedAfter.ToString("0.0") + ")");
            }
        }

        [TestMethod]
        public void SilentTeleportKeepsHeightAboveTheFloor()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sa = world.SectorAction;
                var player = world.ConsolePlayer;
                var mobj = player.Mobj;

                // A teleport destination thing, placed by hand in a sector with a
                // matching tag. That is exactly how a map marks a silent exit.
                var destSector = world.Map.Sectors[1];
                destSector.Tag = TestTag;

                var destination = world.ThingAllocation.SpawnMobj(
                    destSector.SoundOrigin.X,
                    destSector.SoundOrigin.Y,
                    destSector.FloorHeight,
                    MobjType.Teleportman);

                destination.Angle = Angle.Ang0;

                // The entry line, tagged to match.
                LineDef entry = null;

                foreach (var line in world.Map.Lines)
                {
                    if (line.BackSector != null && line.FrontSide != null)
                    {
                        entry = line;
                        break;
                    }
                }

                Assert.IsNotNull(entry, "no entry line found");
                entry.Tag = (short)TestTag;

                // Put the player 40 units above the floor, mid-air: a silent
                // teleporter must keep that height, not drop them to the ground.
                mobj.X = entry.Vertex1.X;
                mobj.Y = entry.Vertex1.Y;
                mobj.Z = mobj.FloorZ + Fixed.FromInt(40);

                var heightBefore = (mobj.Z - mobj.FloorZ).ToFloat();

                var moved = sa.SilentTeleport(entry, 0, mobj);
                Assert.IsTrue(moved, "the silent teleport did not happen");

                var heightAfter = (mobj.Z - mobj.FloorZ).ToFloat();

                Console.WriteLine("height above floor " + heightBefore.ToString("0") +
                                  " -> " + heightAfter.ToString("0"));

                Assert.IsTrue(Math.Abs(heightAfter - heightBefore) < 2.0,
                    "a silent teleporter must keep the height above the floor (" +
                    heightBefore.ToString("0") + " -> " + heightAfter.ToString("0") + ")");
            }
        }

        [TestMethod]
        public void SilentTeleportRefusesMissilesAndTheBackSide()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var sa = world.SectorAction;
                var player = world.ConsolePlayer;
                var mobj = player.Mobj;

                LineDef entry = null;

                foreach (var line in world.Map.Lines)
                {
                    if (line.BackSector != null && line.FrontSide != null)
                    {
                        entry = line;
                        break;
                    }
                }

                Assert.IsNotNull(entry);

                entry.Tag = (short)TestTag;

                // Crossing from the back side must do nothing: otherwise a
                // teleporter cannot be walked out of.
                Assert.IsFalse(sa.SilentTeleport(entry, 1, mobj),
                    "a silent teleport must not fire from the back side");

                Assert.IsFalse(sa.SilentLineTeleport(entry, 1, mobj, false),
                    "a silent line teleport must not fire from the back side");

                // A rocket must pass straight through.
                var rocket = world.ThingAllocation.SpawnMobj(
                    entry.Vertex1.X, entry.Vertex1.Y, Fixed.FromInt(41), MobjType.Rocket);

                Assert.IsFalse(sa.SilentTeleport(entry, 0, rocket),
                    "a missile must not be teleported");
            }
        }
    }
}
