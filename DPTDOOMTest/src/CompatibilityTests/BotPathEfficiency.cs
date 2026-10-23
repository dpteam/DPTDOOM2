using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// What the bots can do on a real map, measured.
    ///
    /// These are the behaviours the port is expected to keep working. They run
    /// headless on the real MAP01, so a regression in navigation, in the exit
    /// switch handling or in the learning shows up as a failure rather than as a
    /// bot that mysteriously wanders off.
    /// </summary>
    [TestClass]
    public class BotCompletesLevelTest
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

            return game;
        }

        /// <summary>
        /// Runs one bot until the level ends or the tic budget runs out.
        /// Returns the tic the level ended on, or -1.
        /// </summary>
        private static int Play(
            GameContent content,
            BotStyle style,
            BotBrain brain,
            int maxTics,
            out DoomBot runner,
            out DoomGame finishedGame)
        {
            var game = NewGame(content);
            var cmds = NewCommands();

            game.Update(cmds);

            var nav = new NavGraph(game.World.Map);
            runner = new DoomBot(game.World, game.World.ConsolePlayer, nav, brain, style);

            for (var tic = 0; tic < maxTics; tic++)
            {
                runner.BuildTicCmd(cmds[0]);
                game.Update(cmds);

                if (game.State != GameState.Level)
                {
                    finishedGame = game;
                    return tic;
                }
            }

            finishedGame = game;
            return -1;
        }

        [TestMethod]
        public void BotCompletesMap01()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                DoomBot runner;
                DoomGame game;

                var tic = Play(content, BotStyle.Rush, null, 35 * 180, out runner, out game);

                Console.WriteLine("Rush finished at tic " + tic +
                                  " (" + BotBrain.FormatTime(tic) + "), health " +
                                  game.World.ConsolePlayer.Health);

                Assert.IsTrue(tic > 0, "the bot did not finish MAP01 within three minutes");
                Assert.IsTrue(runner.Finished,
                    "the level ended without the bot pressing the exit itself");
            }
        }

        [TestMethod]
        public void EveryStyleCanFinish()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var styles = new[]
                {
                    BotStyle.Rush,
                    BotStyle.Sweeper,
                    BotStyle.Hunter,
                    BotStyle.Cautious
                };

                foreach (var style in styles)
                {
                    DoomBot runner;
                    DoomGame game;

                    var tic = Play(content, style, null, 35 * 240, out runner, out game);

                    Console.WriteLine(style + ": " + (tic > 0 ? BotBrain.FormatTime(tic) : "did not finish") +
                                      ", sectors " + runner.VisitedSectors + "/" + runner.TotalSectors +
                                      ", health " + game.World.ConsolePlayer.Health);

                    Assert.IsTrue(tic > 0, style + " did not finish MAP01");
                    Assert.IsTrue(runner.Finished, style + " did not press the exit itself");
                }
            }
        }

        [TestMethod]
        public void SecondRunReusesTheLearnedRoute()
        {
            var folder = Path.Combine(
                Path.GetTempPath(),
                "dptdoom-botrun-" + Guid.NewGuid().ToString("N"));

            try
            {
                using (var content = GameContent.CreateDummy(WadPath.Doom2))
                {
                    var brain = new BotBrain(folder, "MAP01-learning");

                    DoomBot first;
                    DoomGame game;

                    var firstTic = Play(content, BotStyle.Rush, brain, 35 * 180, out first, out game);
                    Assert.IsTrue(firstTic > 0, "the first run did not finish");

                    brain.RecordRun(
                        first.Style, firstTic, first.Finished,
                        first.RouteToExit, first.RouteToExitLength);

                    Assert.IsTrue(brain.BestRoute.Length > 1,
                        "a successful run should leave a route behind");

                    // Second run: the brain is reloaded from disk, as it would be
                    // on the next launch.
                    var reloaded = new BotBrain(folder, "MAP01-learning");

                    Assert.AreEqual(firstTic, reloaded.BestTics);

                    DoomBot second;
                    DoomGame game2;

                    var secondTic = Play(content, BotStyle.Rush, reloaded, 35 * 180, out second, out game2);

                    Console.WriteLine("first " + BotBrain.FormatTime(firstTic) +
                                      " second " + BotBrain.FormatTime(secondTic) +
                                      " route " + reloaded.BestRoute.Length + " nodes");

                    Assert.IsTrue(secondTic > 0, "the second run did not finish");
                    Assert.IsTrue(second.Finished, "the second run did not press the exit itself");

                    // Replaying the remembered route must not be slower than
                    // finding it from scratch.
                    Assert.IsTrue(secondTic <= firstTic,
                        "the second run took longer than the first (" + secondTic + " > " + firstTic + ")");
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, true);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        [TestMethod]
        public void NavigationMeshCoversTheMap()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cmds = NewCommands();
                game.Update(cmds);

                var nav = new NavGraph(game.World.Map);
                var sectors = game.World.Map.Sectors.Length;

                var covered = 0;
                for (var s = 0; s < sectors; s++)
                {
                    if (nav.FirstNodeOfSector(s) >= 0)
                    {
                        covered++;
                    }
                }

                var start = nav.NodeAt(
                    game.World.ConsolePlayer.Mobj.X,
                    game.World.ConsolePlayer.Mobj.Y);

                var reachable = nav.ReachableCount(start);

                Console.WriteLine("mesh: " + nav.NodeCount + " points, " + nav.EdgeCount +
                                  " edges, " + covered + "/" + sectors + " sectors, " +
                                  reachable + " reachable from spawn");

                Assert.IsTrue(nav.NodeCount > 100,
                    "the mesh has " + nav.NodeCount + " points; that is too few to navigate");

                Assert.IsTrue(nav.EdgeCount > nav.NodeCount,
                    "the mesh has " + nav.EdgeCount + " edges for " + nav.NodeCount + " points");

                Assert.IsTrue(covered >= sectors * 0.9,
                    "the mesh covers only " + covered + " of " + sectors + " sectors");
            }
        }

        [TestMethod]
        public void WalkedDistanceStaysCloseToProgress()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cmds = NewCommands();

                game.Update(cmds);

                var player = game.World.ConsolePlayer;
                var bot = new DoomBot(game.World, player, null, null, BotStyle.Rush);

                var startX = player.Mobj.X.ToFloat();
                var startY = player.Mobj.Y.ToFloat();

                var lastX = startX;
                var lastY = startY;
                var walked = 0.0;

                for (var tic = 0; tic < 35 * 30; tic++)
                {
                    bot.BuildTicCmd(cmds[0]);
                    game.Update(cmds);

                    var x = player.Mobj.X.ToFloat();
                    var y = player.Mobj.Y.ToFloat();

                    var dx = x - lastX;
                    var dy = y - lastY;
                    walked += Math.Sqrt(dx * dx + dy * dy);

                    lastX = x;
                    lastY = y;
                }

                var netX = lastX - startX;
                var netY = lastY - startY;
                var progress = Math.Sqrt(netX * netX + netY * netY);

                // Straight-line progress ignores corners, so a ratio above 1 is
                // expected. A bot walking in circles scores very high; this
                // limit is loose on purpose and only catches gross thrashing.
                var ratio = progress > 1 ? walked / progress : double.MaxValue;

                Console.WriteLine("walked=" + walked.ToString("0") +
                                  " progress=" + progress.ToString("0") +
                                  " ratio=" + (progress > 1 ? ratio.ToString("0.0") : "n/a"));

                Assert.IsTrue(progress > 128,
                    "the bot barely moved (" + progress.ToString("0") + " units)");

                Assert.IsTrue(ratio < 15.0,
                    "the bot walked " + ratio.ToString("0.0") +
                    " times further than it got; that is the circling bug");
            }
        }
    }
}
