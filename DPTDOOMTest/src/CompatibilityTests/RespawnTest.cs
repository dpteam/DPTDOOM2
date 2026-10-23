using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Death and respawn, solo versus co-op.
    ///
    /// This exists because of a real report: "I killed a bot and my level
    /// restarted". The cause was a test that asked the wrong question — it checked
    /// whether the game was networked, not how many people were playing. Local
    /// co-op bots are not a network game, so every death in a bot session reloaded
    /// the level from scratch.
    /// </summary>
    [TestClass]
    public class RespawnTest
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

        private static DoomGame NewGame(GameContent content, int playerCount)
        {
            var options = new GameOptions();
            options.GameVersion = content.Wad.GameVersion;
            options.GameMode = content.Wad.GameMode;
            options.MissionPack = content.Wad.MissionPack;
            options.Skill = GameSkill.Medium;
            options.Episode = 1;
            options.Map = 1;

            for (var i = 0; i < playerCount; i++)
            {
                options.Players[i].InGame = true;
            }

            var game = new DoomGame(content, options);
            game.DeferedInitNew();

            return game;
        }

        [TestMethod]
        public void KillingACoopBotDoesNotRestartTheLevel()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                // Two players: the human and one bot, exactly like -coopbots 1.
                var game = NewGame(content, 2);
                var cmds = NewCommands();

                game.Update(cmds);

                var bot = game.World.Options.Players[1];
                Assert.IsNotNull(bot.Mobj, "the bot must have a body");

                // Run a moment so the level is properly running.
                for (var tic = 0; tic < 35; tic++)
                {
                    game.Update(cmds);
                }

                var levelTimeBefore = game.World.LevelTime;

                // Kill the bot the way the player would: damage it until it dies.
                var botMobj = bot.Mobj;
                bot.Health = 1;
                game.World.ThingInteraction.DamageMobj(
                    botMobj, null, botMobj, 1000);

                Assert.IsTrue(bot.Health <= 0, "the bot must be dead");

                // The bot presses Use on the death screen to come back, which is
                // what makes it useful after a death. In a solo game that same
                // button reloads the level; in co-op it must only respawn.
                for (var tic = 0; tic < 35 * 5; tic++)
                {
                    cmds[1].Buttons = TicCmdButtons.Use;
                    game.Update(cmds);
                    cmds[1].Buttons = 0;
                }

                Console.WriteLine("level time before=" + levelTimeBefore +
                                  " after=" + game.World.LevelTime +
                                  " state=" + game.State +
                                  " bot health=" + bot.Health);

                Assert.AreEqual(GameState.Level, game.State,
                    "the level must still be running");

                Assert.IsTrue(game.World.LevelTime > levelTimeBefore,
                    "the level must have kept counting, not restarted from zero");

                Assert.IsTrue(bot.Health > 0,
                    "the bot must have respawned");

                Assert.IsNotNull(bot.Mobj, "the respawned bot must have a body");
            }
        }

        [TestMethod]
        public void SoloDeathStillRestartsTheLevel()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                // Vanilla behaviour, and it must not be lost: playing alone, dying
                // and pressing use reloads the level.
                var game = NewGame(content, 1);
                var cmds = NewCommands();

                game.Update(cmds);

                for (var tic = 0; tic < 35; tic++)
                {
                    game.Update(cmds);
                }

                var player = game.World.ConsolePlayer;
                var levelTimeBefore = game.World.LevelTime;

                player.Health = 1;
                game.World.ThingInteraction.DamageMobj(
                    player.Mobj, null, player.Mobj, 1000);

                Assert.IsTrue(player.Health <= 0, "the player must be dead");

                // Watch for the timer going backwards, which is what a level
                // reload looks like from outside. Comparing only the final value
                // would not prove anything: without a restart the timer would also
                // be larger than it was before.
                var sawRestart = false;
                var lastTime = game.World.LevelTime;

                for (var tic = 0; tic < 35 * 5; tic++)
                {
                    cmds[0].Buttons = TicCmdButtons.Use;
                    game.Update(cmds);
                    cmds[0].Buttons = 0;

                    if (game.World.LevelTime < lastTime)
                    {
                        sawRestart = true;
                    }

                    lastTime = game.World.LevelTime;
                }

                Console.WriteLine("level time before=" + levelTimeBefore +
                                  " after=" + game.World.LevelTime +
                                  " restarted=" + sawRestart +
                                  " health=" + player.Health);

                Assert.IsTrue(sawRestart,
                    "a solo death must restart the level, which resets the timer");

                Assert.IsTrue(player.Health > 0,
                    "the player must be alive after the restart");
            }
        }
    }
}
