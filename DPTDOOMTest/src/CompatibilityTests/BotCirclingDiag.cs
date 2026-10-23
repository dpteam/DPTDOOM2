using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Guards against the bot walking in circles.
    ///
    /// "The bots spin in circles" was a real report, and it had a measurable
    /// cause: the bot only walked when its heading was within about 34 degrees
    /// of the waypoint, so it spent most of the level pivoting on the spot.
    /// These numbers are what that looked like, and what it must not look like
    /// again.
    /// </summary>
    [TestClass]
    public class BotMovementTest
    {
        [TestMethod]
        public void BotDoesNotCircle()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
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
                for (var i = 0; i < cmds.Length; i++) cmds[i] = new TicCmd();
                game.Update(cmds);

                var nav = new NavGraph(game.World.Map);
                var bot = GameBot.Create(game.World, game.World.ConsolePlayer, nav, null, BotStyle.Rush, 0);

                // Entering the same 64-unit cell twice within two seconds means
                // the bot went round in a loop instead of getting somewhere.
                var recent = new Dictionary<long, int>();
                var lastCell = long.MinValue;

                var loops = 0;
                var idleTics = 0;
                var reverseTics = 0;
                var tics = 0;

                for (tics = 0; tics < 35 * 180; tics++)
                {
                    bot.BuildTicCmd(cmds[0]);
                    game.Update(cmds);

                    if (game.State != GameState.Level)
                    {
                        break;
                    }

                    var mobj = game.World.ConsolePlayer.Mobj;
                    var x = mobj.X.ToFloat();
                    var y = mobj.Y.ToFloat();

                    var cell = ((long)(int)(x / 64) << 32) ^ (long)(uint)(int)(y / 64);

                    int seenAt;
                    if (cell != lastCell &&
                        recent.TryGetValue(cell, out seenAt) && tics - seenAt < 70)
                    {
                        loops++;
                    }

                    lastCell = cell;
                    recent[cell] = tics;

                    if (cmds[0].ForwardMove == 0 && cmds[0].SideMove == 0)
                    {
                        idleTics++;
                    }

                    if (cmds[0].ForwardMove < 0)
                    {
                        reverseTics++;
                    }
                }

                var idleShare = tics == 0 ? 0 : (double)idleTics / tics;

                Console.WriteLine("tics=" + tics +
                                  " loops=" + loops +
                                  " idle=" + idleTics + " (" + (idleShare * 100).ToString("0") + "%)" +
                                  " reverse=" + reverseTics);

                Assert.IsTrue(game.State != GameState.Level,
                    "the level should have ended, so these numbers cover a whole run");

                Assert.IsTrue(loops < 60,
                    "the bot re-entered the same place " + loops + " times in one run; that is circling");

                Assert.IsTrue(idleShare < 0.40,
                    "the bot stood still for " + (idleShare * 100).ToString("0") +
                    "% of the run; it should walk while turning");

                Assert.AreEqual(0, reverseTics,
                    "the bot walked backwards " + reverseTics + " tics; it should turn and strafe");
            }
        }
    }
}
