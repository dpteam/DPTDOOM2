using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Saving the moment shown by an attract demo and loading it as a normal
    /// single-player game.
    ///
    /// This is the behaviour the original DPT.DOOM had: during the demo you can
    /// press F2, the demo keeps playing, and later loading that slot gives you
    /// control of the level instead of replaying the demo.
    ///
    /// It works because a save is a plain level snapshot: the demo only supplies
    /// the tic commands that drove the world to that state, and those are not
    /// part of the save.
    /// </summary>
    [TestClass]
    public class DemoSnapshot
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

        private static int CountMobjs(World world)
        {
            var count = 0;
            foreach (var thinker in world.Thinkers)
            {
                if (thinker is Mobj)
                {
                    count++;
                }
            }

            return count;
        }

        [TestMethod]
        public void AttractDemoSnapshotLoadsAsSinglePlayer()
        {
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-demo-snapshot.dsg");

            try
            {
                using (var content = GameContent.CreateDummy(WadPath.Doom2))
                {
                    // 1. Play part of DEMO1, exactly as the attract loop does.
                    var demo = new Demo(content.Wad.ReadLump("DEMO1"));
                    var demoGame = new DoomGame(content, demo.Options);
                    demoGame.DeferedInitNew();

                    var cmds = NewCommands();
                    var tics = 0;

                    while (tics < 200 && demo.ReadCmd(cmds))
                    {
                        demoGame.Update(cmds);
                        tics++;
                    }

                    Assert.AreEqual(GameState.Level, demoGame.State, "the demo reached a level");
                    Assert.IsTrue(tics > 100, "the demo advanced far enough to be interesting");

                    var demoLevelTime = demoGame.World.LevelTime;
                    var demoMobjCount = CountMobjs(demoGame.World);
                    // Fixed deliberately does not support Equals (it throws), so
                    // the raw 16.16 values are compared instead.
                    var demoX = demoGame.World.ConsolePlayer.Mobj.X.Data;
                    var demoY = demoGame.World.ConsolePlayer.Mobj.Y.Data;
                    var demoHealth = demoGame.World.ConsolePlayer.Health;

                    // 2. Save that moment.
                    SaveAndLoad.Save(demoGame, "DEMO SNAPSHOT", path);
                    Assert.IsTrue(File.Exists(path), "the snapshot file was written");

                    // 3. Load it into a fresh single-player game.
                    var options = new GameOptions();
                    options.GameVersion = demo.Options.GameVersion;
                    options.GameMode = demo.Options.GameMode;
                    options.MissionPack = demo.Options.MissionPack;

                    var playerGame = new DoomGame(content, options);
                    SaveAndLoad.Load(playerGame, path);

                    Assert.AreEqual(GameState.Level, playerGame.State, "loading gives a playable level");
                    Assert.IsFalse(playerGame.Options.DemoPlayback, "the loaded game is not a demo");
                    Assert.IsTrue(playerGame.World.Options.Players[0].InGame, "player one is in the game");

                    // 4. The world must be the same moment.
                    Assert.AreEqual(demoLevelTime, playerGame.World.LevelTime, "level time survived");
                    // A vanilla save does not store the RNG, target and tracer
                    // pointers, so a full state hash is not expected to match.
                    // The set of objects and the player are what a snapshot
                    // must preserve.
                    Assert.AreEqual(demoMobjCount, CountMobjs(playerGame.World), "world objects survived");
                    Assert.AreEqual(demoX, playerGame.World.ConsolePlayer.Mobj.X.Data, "player X survived");
                    Assert.AreEqual(demoY, playerGame.World.ConsolePlayer.Mobj.Y.Data, "player Y survived");
                    Assert.AreEqual(demoHealth, playerGame.World.ConsolePlayer.Health, "player health survived");

                    // 5. And it must actually run with player input rather than
                    //    demo commands.
                    var idle = NewCommands();
                    playerGame.Update(idle);
                    Assert.AreEqual(GameState.Level, playerGame.State, "the loaded game keeps running");
                }
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [TestMethod]
        public void SavingDuringTheAttractDemoIsAccepted()
        {
            var slot = 5;
            var path = Path.Combine(ConfigUtilities.GetDataDirectory(), "doomsav" + slot + ".dsg");

            try
            {
                var args = new CommandLineArgs(new string[0]);
                var config = new Config();

                using (var content = GameContent.CreateDummy(WadPath.Doom2))
                {
                    // The application starts on the title screen and rolls into
                    // DEMO1 by itself; this is the same path a player sees.
                    var doom = new Doom(args, config, content, null, null, null, null);

                    var guard = 0;
                    while (doom.Opening.State != OpeningSequenceState.Demo && guard < 5000)
                    {
                        doom.Update();
                        guard++;
                    }

                    Assert.AreEqual(OpeningSequenceState.Demo, doom.Opening.State,
                        "the attract demo started");

                    // Let the demo reach a level: the first tics still process the
                    // deferred new-game action.
                    for (var i = 0; i < 5; i++)
                    {
                        doom.Update();
                    }

                    Assert.AreEqual(GameState.Level, doom.Opening.DemoGame.State);

                    Assert.IsTrue(doom.SaveGame(slot, "DEMO SNAPSHOT"),
                        "saving is accepted while an attract demo is playing");

                    // A save is deferred to the next simulation tic, so it can
                    // take a few frames when a wipe is running.
                    for (var i = 0; i < 200 && !File.Exists(path); i++)
                    {
                        doom.Update();
                    }

                    Assert.IsTrue(File.Exists(path), "the snapshot was written to slot " + slot);
                }
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [TestMethod]
        public void TitleScreenHasNothingToSave()
        {
            // The opening sequence starts on the title screen, where there is no
            // level yet. Saving there must be refused rather than crash.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var options = new GameOptions();
                options.GameMode = content.Wad.GameMode;
                options.MissionPack = content.Wad.MissionPack;

                var opening = new OpeningSequence(content, options);

                Assert.AreEqual(OpeningSequenceState.Title, opening.State);
                Assert.IsNull(opening.DemoGame, "no demo game exists on the title screen");
            }
        }
    }
}
