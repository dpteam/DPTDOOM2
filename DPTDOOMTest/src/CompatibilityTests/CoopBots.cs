using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Local co-op bots: extra players in the same world, so one person can test
    /// a co-op session alone.
    /// </summary>
    [TestClass]
    public class CoopBots
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
        public void BotsJoinTheGameAndMove()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var options = new GameOptions();
                options.GameVersion = content.Wad.GameVersion;
                options.GameMode = content.Wad.GameMode;
                options.MissionPack = content.Wad.MissionPack;
                options.Episode = 1;
                options.Map = 1;

                // Player one is the human; three bots join.
                for (var i = 0; i < 4; i++)
                {
                    options.Players[i].InGame = true;
                }

                var game = new DoomGame(content, options);
                game.DeferedInitNew();

                var cmds = NewCommands();
                game.Update(cmds);

                var nav = new NavGraph(game.World.Map);
                var bots = new DoomBot[4];
                var startX = new float[4];
                var startY = new float[4];

                for (var i = 1; i < 4; i++)
                {
                    Assert.IsNotNull(game.World.Options.Players[i].Mobj,
                        "player " + (i + 1) + " has a body in the world");

                    bots[i] = new DoomBot(game.World, game.World.Options.Players[i], nav);
                    startX[i] = game.World.Options.Players[i].Mobj.X.ToFloat();
                    startY[i] = game.World.Options.Players[i].Mobj.Y.ToFloat();
                }

                for (var tic = 0; tic < 35 * 20; tic++)
                {
                    for (var i = 1; i < 4; i++)
                    {
                        bots[i].BuildTicCmd(cmds[i]);
                    }

                    game.Update(cmds);
                }

                for (var i = 1; i < 4; i++)
                {
                    var player = game.World.Options.Players[i];
                    Assert.IsNotNull(player.Mobj, "player " + (i + 1) + " is still in the world");

                    var dx = player.Mobj.X.ToFloat() - startX[i];
                    var dy = player.Mobj.Y.ToFloat() - startY[i];
                    var moved = Math.Sqrt(dx * dx + dy * dy);

                    Console.WriteLine("player " + (i + 1) + " moved " + moved.ToString("0") +
                                      " units, health " + player.Health);

                    Assert.IsTrue(moved > 64,
                        "player " + (i + 1) + " moved only " + moved.ToString("0") + " units");
                }
            }
        }

        [TestMethod]
        public void SharedGraphIsBuiltOnce()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var options = new GameOptions();
                options.GameVersion = content.Wad.GameVersion;
                options.GameMode = content.Wad.GameMode;
                options.MissionPack = content.Wad.MissionPack;
                options.Episode = 1;
                options.Map = 1;

                for (var i = 0; i < 4; i++)
                {
                    options.Players[i].InGame = true;
                }

                var game = new DoomGame(content, options);
                game.DeferedInitNew();

                var cmds = NewCommands();
                game.Update(cmds);

                var nav = new NavGraph(game.World.Map);

                var first = new DoomBot(game.World, game.World.Options.Players[1], nav);
                var second = new DoomBot(game.World, game.World.Options.Players[2], nav);

                Assert.AreSame(nav, first.Nav);
                Assert.AreSame(nav, second.Nav);
                Assert.IsTrue(nav.NodeCount > 0);
            }
        }
    }
}
