using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// IDCLIP, the noclip cheat.
    ///
    /// It was broken in a way that is easy to miss: typing the cheat printed
    /// "noclip mode ON" and nothing else happened, because the flag it set was
    /// only ever read by the status bar. The movement code already knew how to
    /// pass through the level - CheckPosition returns early for a noclip thing -
    /// so the engine was fine and the switch was simply never flipped.
    ///
    /// These tests pin the behaviour where it matters: whether a move into solid
    /// rock is allowed.
    /// </summary>
    [TestClass]
    public class NoClipTest
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
        /// A point just behind a one-sided line, which is solid rock.
        ///
        /// "Behind" means the left of the vector Vertex1->Vertex2: in Doom the
        /// front side of a line is its right, so the left is where solid sector
        /// is not. Eight units past the line is close enough that the player's
        /// radius still overlaps the line, which is what makes the move blocked
        /// rather than merely suspicious.
        /// </summary>
        private static Fixed[] PointInsideRock(Map map)
        {
            foreach (var line in map.Lines)
            {
                if (line.BackSector != null)
                {
                    continue;
                }

                var x1 = line.Vertex1.X.ToDouble();
                var y1 = line.Vertex1.Y.ToDouble();
                var x2 = line.Vertex2.X.ToDouble();
                var y2 = line.Vertex2.Y.ToDouble();

                var dx = x2 - x1;
                var dy = y2 - y1;
                var length = Math.Sqrt(dx * dx + dy * dy);

                if (length < 1.0)
                {
                    continue;
                }

                var midX = (x1 + x2) / 2.0;
                var midY = (y1 + y2) / 2.0;

                // Left normal: the solid side.
                var nx = -dy / length;
                var ny = dx / length;

                return new[]
                {
                    Fixed.FromDouble(midX + nx * 8.0),
                    Fixed.FromDouble(midY + ny * 8.0)
                };
            }

            throw new Exception("MAP01 has no one-sided lines to test against");
        }

        [TestMethod]
        public void WallsBlockWithoutTheCheat()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var player = world.ConsolePlayer;

                Assert.IsNotNull(player.Mobj);

                player.Cheats &= ~CheatFlags.NoClip;

                var target = PointInsideRock(world.Map);

                Assert.IsFalse(
                    world.ThingMovement.CheckPosition(player.Mobj, target[0], target[1]),
                    "a position inside solid rock must be refused without noclip");
            }
        }

        [TestMethod]
        public void NoClipCheatLetsThePlayerThrough()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var player = world.ConsolePlayer;

                var target = PointInsideRock(world.Map);

                Assert.IsFalse(world.ThingMovement.CheckPosition(player.Mobj, target[0], target[1]),
                    "sanity: the point is solid before the cheat is on");

                player.Cheats |= CheatFlags.NoClip;

                Assert.IsTrue(
                    world.ThingMovement.CheckPosition(player.Mobj, target[0], target[1]),
                    "noclip must allow moving into solid rock");

                player.Cheats &= ~CheatFlags.NoClip;
            }
        }

        [TestMethod]
        public void TheCheatTogglesOffImmediately()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var player = world.ConsolePlayer;

                var target = PointInsideRock(world.Map);

                player.Cheats |= CheatFlags.NoClip;
                Assert.IsTrue(world.ThingMovement.CheckPosition(player.Mobj, target[0], target[1]));

                // Turning it off has to take effect at once, not after a respawn:
                // a debug toggle that needs a death to apply is worse than none.
                player.Cheats &= ~CheatFlags.NoClip;
                Assert.IsFalse(world.ThingMovement.CheckPosition(player.Mobj, target[0], target[1]),
                    "noclip must stop applying the moment it is switched off");
            }
        }

        [TestMethod]
        public void MonstersAreNotAffected()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var player = world.ConsolePlayer;

                // The cheat belongs to one player, not to the world. Reading it
                // from "any player" would also let every monster walk through
                // walls while one player noclips, which looks like a physics bug
                // rather than a cheat.
                player.Cheats |= CheatFlags.NoClip;

                var target = PointInsideRock(world.Map);

                var checkedMonster = false;

                foreach (var thinker in world.Thinkers)
                {
                    var mobj = thinker as Mobj;

                    if (mobj == null || mobj.Player != null)
                    {
                        continue;
                    }

                    checkedMonster = true;

                    Assert.IsFalse(
                        world.ThingMovement.CheckPosition(mobj, target[0], target[1]),
                        "a monster must stay blocked while the player noclips");
                }

                player.Cheats &= ~CheatFlags.NoClip;

                Assert.IsTrue(checkedMonster, "MAP01 has monsters to check");
            }
        }
    }
}
