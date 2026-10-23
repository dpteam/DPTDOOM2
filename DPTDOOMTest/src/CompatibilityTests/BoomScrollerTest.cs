using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Boom texture scrollers.
    ///
    /// The stock maps carry almost none of these, so these tests take a level,
    /// give a line a scroller special, rebuild the scroller set and watch what
    /// moves. That is the only way to exercise the model without shipping a PWAD.
    ///
    /// What is being pinned is the PrBoom rule, not a number invented here: speed
    /// and direction come from the carrying line's own vector shifted right by
    /// SCROLL_SHIFT. A map's scrolling water is authored against that exact
    /// divisor, so getting it wrong scrolls everything at the wrong speed and
    /// nothing in the code would look broken.
    /// </summary>
    [TestClass]
    public class BoomScrollerTest
    {
        private const int ScrollShift = 5;
        private const double CarryFactor = 0.09375;

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

        private static TicCmd[] NewCommands()
        {
            var cmds = new TicCmd[Player.MaxPlayerCount];
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                cmds[i] = new TicCmd();
            }

            return cmds;
        }

        private static LineDef FindTwoSidedLine(Map map)
        {
            foreach (var line in map.Lines)
            {
                if (line.FrontSide != null && line.BackSide != null)
                {
                    return line;
                }
            }

            throw new Exception("MAP01 has no two-sided line to test with");
        }

        [TestMethod]
        public void SideScrollMovesTheTextureOneUnitPerTic()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                var line = FindTwoSidedLine(world.Map);
                line.Special = (LineSpecial)48;
                world.Specials.BuildScrollers();

                var before = line.FrontSide.TextureOffset;
                var cmds = NewCommands();

                for (var tic = 0; tic < 5; tic++)
                {
                    world.Update();
                }

                var moved = (line.FrontSide.TextureOffset - before).ToDouble();

                Assert.AreEqual(5.0, moved, 0.001,
                    "special 48 must scroll the front side by exactly one unit per tic");
            }
        }

        [TestMethod]
        public void LeftScrollIsTheMirrorOfRight()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                var line = FindTwoSidedLine(world.Map);
                line.Special = (LineSpecial)85;
                world.Specials.BuildScrollers();

                var before = line.FrontSide.TextureOffset;

                for (var tic = 0; tic < 5; tic++)
                {
                    world.Update();
                }

                var moved = (line.FrontSide.TextureOffset - before).ToDouble();

                Assert.AreEqual(-5.0, moved, 0.001,
                    "special 85 is the left-hand pair of 48 and must move the other way");
            }
        }

        [TestMethod]
        public void FloorScrollFollowsTheLineVector()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                var line = FindTwoSidedLine(world.Map);

                // Give the front sector a tag nobody else uses, so the scroller
                // targets exactly one sector and the assertion is unambiguous.
                var sector = line.FrontSector;
                var tag = 1999;
                sector.Tag = tag;
                line.Tag = (short)tag;
                line.Special = (LineSpecial)251;
                world.Specials.BuildScrollers();

                var expectedX = -line.Dx.ToDouble() / (1 << ScrollShift);
                var expectedY = line.Dy.ToDouble() / (1 << ScrollShift);

                var beforeX = sector.FloorFlatOffsetX;
                var beforeY = sector.FloorFlatOffsetY;

                for (var tic = 0; tic < 3; tic++)
                {
                    world.Update();
                }

                var movedX = (sector.FloorFlatOffsetX - beforeX).ToDouble();
                var movedY = (sector.FloorFlatOffsetY - beforeY).ToDouble();

                Assert.AreEqual(expectedX * 3, movedX, 0.01,
                    "a floor scroller's x speed is the line's dx shifted right by SCROLL_SHIFT, negated");
                Assert.AreEqual(expectedY * 3, movedY, 0.01,
                    "a floor scroller's y speed is the line's dy shifted right by SCROLL_SHIFT");
            }
        }

        [TestMethod]
        public void CeilingScrollTouchesTheCeilingNotTheFloor()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                var line = FindTwoSidedLine(world.Map);
                var sector = line.FrontSector;

                sector.Tag = 1998;
                line.Tag = (short)1998;
                line.Special = (LineSpecial)250;
                world.Specials.BuildScrollers();

                var floorBefore = sector.FloorFlatOffsetX;
                var ceilingBefore = sector.CeilingFlatOffsetX;
                var ceilingBeforeY = sector.CeilingFlatOffsetY;

                for (var tic = 0; tic < 3; tic++)
                {
                    world.Update();
                }

                Assert.AreEqual(0.0, (sector.FloorFlatOffsetX - floorBefore).ToDouble(), 0.001,
                    "special 250 scrolls the ceiling; the floor must not move");

                // Both axes are checked because a scroller's speed comes from the
                // carrying line's vector: a vertical line has dx of zero, so only y
                // moves, and asserting x alone would fail for the right behaviour.
                var ceilingMoved = Math.Abs((sector.CeilingFlatOffsetX - ceilingBefore).ToDouble()) +
                                   Math.Abs((sector.CeilingFlatOffsetY - ceilingBeforeY).ToDouble());

                Assert.AreNotEqual(0.0, ceilingMoved,
                    "special 250 must scroll the ceiling flat");
            }
        }

        [TestMethod]
        public void CarryPushesThingsStandingOnTheFloor()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                var line = FindTwoSidedLine(world.Map);

                // The scroller acts on sectors carrying the line's tag, so the tag
                // goes on the sector the player is standing in - otherwise the carry
                // would push a room the test never looks at, and pass or fail for
                // the wrong reason.
                var player = world.ConsolePlayer;
                var mobj = player.Mobj;

                Assert.IsNotNull(mobj);

                var sector = mobj.Subsector.Sector;

                sector.Tag = 1997;
                line.Tag = (short)1997;
                line.Special = (LineSpecial)252;
                world.Specials.BuildScrollers();

                var expectedX = (line.Dx.ToDouble() / (1 << ScrollShift)) * CarryFactor;
                var expectedY = (line.Dy.ToDouble() / (1 << ScrollShift)) * CarryFactor;

                mobj.MomX = Fixed.Zero;
                mobj.MomY = Fixed.Zero;

                world.Update();

                Assert.AreEqual(expectedX, mobj.MomX.ToDouble(), 0.01,
                    "a carrying floor must push things on it by dx scaled by CARRYFACTOR");
                Assert.AreEqual(expectedY, mobj.MomY.ToDouble(), 0.01,
                    "a carrying floor must push things on it by dy scaled by CARRYFACTOR");
            }
        }

        [TestMethod]
        public void ScrollingDoesNotChangeTheWorldHash()
        {
            // The offset is presentation state. If it ever became part of the
            // sector hash, every recorded reference hash would have to be
            // regenerated for a change that cannot affect gameplay - and worse,
            // a lockstep peer that skipped a scroll tic would be reported as
            // desynced when the worlds are identical.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                var hashBefore = DoomDebug.GetSectorHash(world);

                var line = FindTwoSidedLine(world.Map);
                var sector = line.FrontSector;
                sector.Tag = 1996;
                line.Tag = (short)1996;
                line.Special = (LineSpecial)250;
                world.Specials.BuildScrollers();

                for (var tic = 0; tic < 5; tic++)
                {
                    world.Update();
                }

                Assert.AreNotEqual(0.0, sector.CeilingFlatOffsetX.ToDouble() +
                                       sector.CeilingFlatOffsetY.ToDouble(),
                    "sanity: the ceiling did scroll");

                // Rebuild the scrollers away and the hash must be untouched by the
                // offsets that were applied.
                var hashAfter = DoomDebug.GetSectorHash(world);
                Assert.AreEqual(hashBefore, hashAfter,
                    "flat scrolling must not feed the sector hash");
            }
        }
    }
}
