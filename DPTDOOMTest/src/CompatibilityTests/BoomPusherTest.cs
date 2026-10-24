using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Boom pushers (wind and current) and the sector-special flag bits they live in.
    ///
    /// The flag bits matter on their own: a Boom map that wants nukage plus wind sets
    /// 0x207, and until this was fixed the damage disappeared because the effect
    /// switch compared the whole 16-bit value against 7. That is a silent
    /// regression on any Boom map, and it is checked here separately from the
    /// pushers.
    /// </summary>
    [TestClass]
    public class BoomPusherTest
    {
        /// <summary>PrBoom's PUSH_FACTOR: momentum per tic is the speed over 128.</summary>
        private const int PushFactor = 128;

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
        /// Wires one wind or current onto the sector the player is standing in, so
        /// the assertion is about the player and nothing else. Returns the line so a
        /// test can compute the force the map asked for.
        /// </summary>
        private static LineDef AttachPusher(World world, int lineSpecial, bool withFlag, out Mobj mobj)
        {
            // A line with a real horizontal component: a vertical line would make
            // every x assertion pass for the wrong reason.
            LineDef chosen = null;

            foreach (var line in world.Map.Lines)
            {
                if (line.FrontSide != null && line.BackSide != null &&
                    Math.Abs(line.Dx.ToDouble()) > 1.0)
                {
                    chosen = line;
                    break;
                }
            }

            Assert.IsNotNull(chosen, "MAP01 has a two-sided line with a horizontal direction");

            mobj = world.ConsolePlayer.Mobj;
            Assert.IsNotNull(mobj, "the player has a body");

            var sector = mobj.Subsector.Sector;

            sector.Tag = 1994;
            chosen.Tag = (short)1994;
            chosen.Special = (LineSpecial)lineSpecial;

            sector.Special = withFlag
                ? (SectorSpecial)(7 | SectorSpecials.PushMask)
                : (SectorSpecial)7;

            world.Specials.BuildPushers();

            return chosen;
        }

        [TestMethod]
        public void FlagBitsDoNotHideTheVanillaEffect()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                // Nukage (7) plus Boom's pusher flag, exactly as a Boom map writes it.
                var sector = world.ConsolePlayer.Mobj.Subsector.Sector;
                sector.Special = (SectorSpecial)(7 | SectorSpecials.PushMask);

                Assert.AreEqual(7, SectorSpecials.EffectOf(sector),
                    "the vanilla effect must survive the flag bits");

                Assert.IsTrue(SectorSpecials.IsPusher(sector),
                    "the pusher flag must be visible on its own");

                // And a plain vanilla sector must be unaffected by the masking.
                sector.Special = (SectorSpecial)7;

                Assert.AreEqual(7, SectorSpecials.EffectOf(sector));
                Assert.IsFalse(SectorSpecials.IsPusher(sector));
            }
        }

        [TestMethod]
        public void WindPushesAStandingPlayerAtHalfForce()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                Mobj mobj;
                var line = AttachPusher(world, 224, true, out mobj);

                // On the ground: half force.
                mobj.MomX = Fixed.Zero;
                mobj.MomY = Fixed.Zero;

                world.Update();

                var fullForce = line.Dx.ToDouble() / PushFactor;
                var halfForce = fullForce * 0.5;
                var got = mobj.MomX.ToDouble();

                // Friction is applied in the same tic, so the push is compared as a
                // direction plus a bound: it must have happened (non-zero), it must
                // be in the line's direction, and it must not exceed half force.
                // Without the non-zero part this test passed while doing nothing.
                Assert.IsTrue(Math.Abs(got) > 0.0001,
                    "a standing player must actually be pushed, got " + got);

                Assert.IsTrue(Math.Sign(got) == Math.Sign(fullForce),
                    "the push follows the line's direction, got " + got +
                    " for a line of " + fullForce);

                Assert.IsTrue(Math.Abs(got) <= Math.Abs(halfForce) + 0.0001,
                    "a standing player gets at most half force: " + Math.Abs(got) +
                    " vs " + Math.Abs(halfForce));
            }
        }

        [TestMethod]
        public void PusherWithoutTheFlagDoesNothing()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                // The line is there, the tag matches, but the sector was never given
                // Boom's 0x200 flag - which is what a map that forgot it looks like.
                Mobj mobj;
                AttachPusher(world, 224, false, out mobj);

                mobj.MomX = Fixed.Zero;
                mobj.MomY = Fixed.Zero;

                world.Update();

                Assert.AreEqual(0.0, mobj.MomX.ToDouble(), 0.01,
                    "no flag on the sector means no wind, as in PrBoom");
                Assert.AreEqual(0.0, mobj.MomY.ToDouble(), 0.01);
            }
        }

        [TestMethod]
        public void CurrentDoesNotPushAPlayerInTheAir()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;

                Mobj mobj;
                AttachPusher(world, 225, true, out mobj);

                // Lift the body clear of the floor: a current only moves what is on
                // the ground, which is the whole difference between it and a wind.
                mobj.Z = mobj.FloorZ + Fixed.FromInt(32);
                mobj.MomX = Fixed.Zero;
                mobj.MomY = Fixed.Zero;

                world.Update();

                Assert.AreEqual(0.0, mobj.MomX.ToDouble(), 0.01,
                    "a current must not move a player who is not on the floor");
            }
        }
    }
}
