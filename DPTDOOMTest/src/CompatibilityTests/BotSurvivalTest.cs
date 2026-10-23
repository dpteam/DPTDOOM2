using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The bot has to survive the level, not only reach the end of it.
    ///
    /// This is separate from "did it finish", because a bot can finish after dying
    /// several times - the level restarts, or the player respawns at the start, and
    /// the tic counter keeps going. On MAP01 at skill 3 the bots used to finish
    /// with zero deaths, and that property is what the navigation and combat code
    /// actually promise. A change that makes a bot die is a regression even if the
    /// level still ends.
    ///
    /// Skill 4 (Ultra-Violence) is measured and printed but not asserted. None of
    /// the styles reliably survives it: the bot treats every visible monster as
    /// something to walk towards and shoot, and on UV the trade is lost. Making the
    /// bots play UV is open work, and pretending otherwise with a test would hide
    /// it.
    /// </summary>
    [TestClass]
    public class BotSurvivalTest
    {
        private sealed class Result
        {
            public int Finished = -1;
            public int Deaths;
            public int Health;
            public int Sectors;
            public int TotalSectors;
        }

        private static Result Measure(GameSkill skill, BotStyle style)
        {
            var result = new Result();

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var options = new GameOptions();
                options.GameVersion = content.Wad.GameVersion;
                options.GameMode = content.Wad.GameMode;
                options.MissionPack = content.Wad.MissionPack;
                options.Skill = skill;
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

                var nav = new NavGraph(game.World.Map);
                var bot = GameBot.Create(game.World, game.World.ConsolePlayer, nav, null, style, 0);

                var wasDead = false;

                for (var tic = 0; tic < 35 * 300; tic++)
                {
                    bot.BuildTicCmd(cmds[0]);
                    game.Update(cmds);

                    var player = game.World.Options.Players[0];
                    var dead = player.Health <= 0 || player.Mobj == null;

                    if (dead && !wasDead)
                    {
                        result.Deaths++;
                    }

                    wasDead = dead;

                    if (game.State != GameState.Level)
                    {
                        result.Finished = tic;
                        break;
                    }
                }

                var final = game.World.Options.Players[0];

                result.Health = final.Health;
                result.Sectors = bot.VisitedSectors;
                result.TotalSectors = bot.TotalSectors;
            }

            return result;
        }

        private static void Report(string label, Result result)
        {
            Console.WriteLine(label + ": " +
                              (result.Finished >= 0 ? "finished at " + result.Finished : "did not finish") +
                              ", deaths " + result.Deaths +
                              ", health " + result.Health +
                              ", sectors " + result.Sectors + "/" + result.TotalSectors);
        }

        [TestMethod]
        public void NobodyDiesOnSkill3()
        {
            foreach (var style in new[]
            {
                BotStyle.Rush, BotStyle.Sweeper, BotStyle.Hunter, BotStyle.Cautious
            })
            {
                var result = Measure(GameSkill.Medium, style);
                Report("skill 3 " + style, result);

                Assert.AreEqual(0, result.Deaths,
                    style + " died on skill 3; MAP01 is survivable at this difficulty and " +
                    "a death means the bot walked into a fight it could not win");
            }
        }

        [TestMethod]
        public void UltraViolenceIsMeasured()
        {
            // Printed, not asserted - see the class comment. Keeping the
            // measurement here means the numbers stay visible in the test log
            // instead of living in a commit message nobody rereads.
            foreach (var style in new[]
            {
                BotStyle.Rush, BotStyle.Sweeper, BotStyle.Hunter, BotStyle.Cautious
            })
            {
                Report("skill 4 " + style, Measure(GameSkill.Hard, style));
            }
        }
    }
}
