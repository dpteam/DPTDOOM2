using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// What the bot can do today, measured.
    ///
    /// These are deliberately the behaviours that are known to work. Completing
    /// a level is not among them yet, and pretending otherwise with a test that
    /// always passes would hide the remaining work — see docs/BOTS.md.
    /// </summary>
    [TestClass]
    public class BotPathEfficiency
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

        [TestMethod]
        public void NavigationMeshCoversTheMap()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cmds = NewCommands();
                game.Update(cmds);

                var nav = new NavGraph(game.World.Map);

                Assert.IsTrue(nav.NodeCount > 100,
                    "the mesh has " + nav.NodeCount + " points; that is too few to navigate");

                Assert.IsTrue(nav.EdgeCount > nav.NodeCount,
                    "the mesh has " + nav.EdgeCount + " edges for " + nav.NodeCount + " points");

                // How many sectors have at least one point. Coverage of the map
                // is what lets the bot explore; full point-to-point connectivity
                // is a known remaining problem (docs/BOTS.md), so it is reported
                // rather than asserted.
                var covered = 0;
                var sectors = game.World.Map.Sectors.Length;

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
                                  " edges, " + covered + "/" + sectors + " sectors covered, " +
                                  reachable + "/" + nav.NodeCount + " points reachable from spawn");

                Assert.IsTrue(covered >= sectors * 0.9,
                    "the mesh covers only " + covered + " of " + sectors + " sectors");
            }
        }

        [TestMethod]
        public void BotExploresMostOfTheMap()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cmds = NewCommands();
                game.Update(cmds);

                var bot = new DoomBot(game.World, game.World.ConsolePlayer);

                for (var tic = 0; tic < 35 * 120; tic++)
                {
                    bot.BuildTicCmd(cmds[0]);
                    game.Update(cmds);

                    if (game.State != GameState.Level)
                    {
                        break;
                    }
                }

                var share = (double)bot.VisitedSectors / bot.TotalSectors;

                Console.WriteLine("visited " + bot.VisitedSectors + " of " + bot.TotalSectors +
                                  " sectors (" + (share * 100).ToString("0") + "%)");

                Assert.IsTrue(share > 0.5,
                    "the bot visited only " + (share * 100).ToString("0") +
                    "% of the map; it should get around");
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
                var bot = new DoomBot(game.World, player);

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
