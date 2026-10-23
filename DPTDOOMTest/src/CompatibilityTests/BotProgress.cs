using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The co-op bot has to actually get somewhere. A bot that stands still is
    /// indistinguishable from a bot that is broken, so this measures progress:
    /// distance from the spawn point, and how many map lines it has reached.
    /// </summary>
    [TestClass]
    public class BotProgress
    {
        private static TicCmd[] NewCommands()
        {
            var cmds = new TicCmd[Player.MaxPlayerCount];
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                cmds[i] = new TicCmd();
            }

            return cmds;
        }

        [TestMethod]
        public void BotLeavesTheStartingRoom()
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

                var cmds = NewCommands();
                game.Update(cmds);

                var player = game.World.ConsolePlayer;
                var startX = player.Mobj.X.ToFloat();
                var startY = player.Mobj.Y.ToFloat();

                var bot = new DoomBot(game.World, player);

                const int Tics = 35 * 60; // one minute of game time
                var maxDistance = 0.0;

                for (var tic = 0; tic < Tics; tic++)
                {
                    bot.BuildTicCmd(cmds[0]);
                    game.Update(cmds);

                    if (player.Mobj == null)
                    {
                        break;
                    }

                    var dx = player.Mobj.X.ToFloat() - startX;
                    var dy = player.Mobj.Y.ToFloat() - startY;
                    var distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance > maxDistance)
                    {
                        maxDistance = distance;
                    }
                }

                Console.WriteLine("bot: max distance " + maxDistance.ToString("0") +
                                  " map units, coverage " + (bot.Coverage * 100).ToString("0.0") + "%");

                Assert.IsTrue(
                    maxDistance > 256,
                    "the bot moved " + maxDistance.ToString("0") + " map units; it must leave the start room");

                Assert.IsTrue(
                    bot.Coverage > 0.01,
                    "the bot visited " + (bot.Coverage * 100).ToString("0.0") + "% of the map; it must explore");
            }
        }

        [TestMethod]
        public void BotIsDeterministic()
        {
            // Same state in, same commands out. Anything else would break demo
            // recording and the network lockstep tests.
            var first = Run(200);
            var second = Run(200);

            Assert.AreEqual(first, second, "two identical runs produced different results");
        }

        private static string Run(int tics)
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var options = new GameOptions();
                options.GameVersion = content.Wad.GameVersion;
                options.GameMode = content.Wad.GameMode;
                options.MissionPack = content.Wad.MissionPack;
                options.Episode = 1;
                options.Map = 1;
                options.Players[0].InGame = true;

                var game = new DoomGame(content, options);
                game.DeferedInitNew();

                var cmds = NewCommands();
                game.Update(cmds);

                var bot = new DoomBot(game.World, game.World.ConsolePlayer);

                for (var tic = 0; tic < tics; tic++)
                {
                    bot.BuildTicCmd(cmds[0]);
                    game.Update(cmds);
                }

                return DoomDebug.GetMobjHash(game.World).ToString("x8") +
                       "/" + DoomDebug.GetSectorHash(game.World).ToString("x8");
            }
        }
    }
}
