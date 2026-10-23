using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Demo recording (-record) must produce a file that plays back to exactly
    /// the same world state. That is the only meaningful test of a demo: a file
    /// that parses but desynchronises is worse than no feature at all.
    /// </summary>
    [TestClass]
    public class DemoRecording
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

        /// <summary>
        /// A deterministic input pattern. It is not a bot, it only has to be
        /// varied enough to move the player, shoot and open doors.
        ///
        /// Turn amounts are multiples of 256 on purpose. The demo format stores
        /// only the high byte of the turn, so a recording is exact only for
        /// commands that are whole units already. Vanilla has the same property,
        /// which is why a recorded demo reproduces playback runs, not
        /// necessarily the live session that produced it.
        /// </summary>
        private static void Script(TicCmd cmd, int tic)
        {
            cmd.Clear();
            cmd.ForwardMove = (sbyte)((tic / 20) % 3 == 2 ? 0 : 25);
            cmd.SideMove = (sbyte)(((tic / 13) % 3 - 1) * 12);
            cmd.AngleTurn = (short)(((tic / 7) % 9 - 4) * 256);

            if (tic % 37 < 3)
            {
                cmd.Buttons |= TicCmdButtons.Attack;
            }

            if (tic % 53 < 2)
            {
                cmd.Buttons |= TicCmdButtons.Use;
            }
        }

        [TestMethod]
        public void RecordedDemoReplaysToTheSameState()
        {
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-record-test.lmp");

            try
            {
                using (var content = GameContent.CreateDummy(WadPath.Doom2))
                {
                    // 1. Play a short game and record it.
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

                    var recorder = new DemoRecorder(options);
                    var cmds = NewCommands();

                    const int Tics = 400;

                    for (var tic = 0; tic < Tics; tic++)
                    {
                        Script(cmds[0], tic);
                        game.Update(cmds);
                        recorder.WriteCmd(cmds);
                    }

                    var expectedMobj = DoomDebug.GetMobjHash(game.World);
                    var expectedSector = DoomDebug.GetSectorHash(game.World);
                    var expectedX = game.World.ConsolePlayer.Mobj.X.Data;
                    var expectedY = game.World.ConsolePlayer.Mobj.Y.Data;
                    var expectedHealth = game.World.ConsolePlayer.Health;

                    recorder.Save(path);

                    Assert.AreEqual(Tics, recorder.TicCount, "every tic was recorded");
                    Assert.IsTrue(File.Exists(path), "the demo file exists");

                    // 2. Play the recording back through the normal demo reader.
                    var demo = new Demo(path);
                    Assert.AreEqual(options.Skill, demo.Options.Skill);
                    Assert.AreEqual(options.Map, demo.Options.Map);
                    Assert.IsTrue(demo.Options.DemoPlayback, "the header marks it as a demo");

                    var replay = new DoomGame(content, demo.Options);
                    replay.DeferedInitNew();

                    var replayCmds = NewCommands();
                    var played = 0;

                    while (demo.ReadCmd(replayCmds))
                    {
                        replay.Update(replayCmds);
                        played++;
                    }

                    Assert.AreEqual(Tics, played, "the demo contains exactly the recorded tics");

                    // 3. The world must be identical.
                    Assert.AreEqual(expectedMobj, DoomDebug.GetMobjHash(replay.World), "objects match");
                    Assert.AreEqual(expectedSector, DoomDebug.GetSectorHash(replay.World), "sectors match");
                    Assert.AreEqual(expectedX, replay.World.ConsolePlayer.Mobj.X.Data, "player X matches");
                    Assert.AreEqual(expectedY, replay.World.ConsolePlayer.Mobj.Y.Data, "player Y matches");
                    Assert.AreEqual(expectedHealth, replay.World.ConsolePlayer.Health, "health matches");
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
        public void HeaderDescribesTheSession()
        {
            var options = new GameOptions();
            options.Skill = GameSkill.Nightmare;
            options.Episode = 2;
            options.Map = 7;
            options.FastMonsters = true;
            options.NoMonsters = true;
            options.Players[0].InGame = true;

            var recorder = new DemoRecorder(options);
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-header-test.lmp");

            try
            {
                recorder.Save(path);

                var bytes = File.ReadAllBytes(path);

                Assert.AreEqual(DemoRecorder.Version, bytes[0], "version byte");
                Assert.AreEqual((byte)GameSkill.Nightmare, bytes[1], "skill");
                Assert.AreEqual((byte)2, bytes[2], "episode");
                Assert.AreEqual((byte)7, bytes[3], "map");
                Assert.AreEqual((byte)1, bytes[6], "fast monsters");
                Assert.AreEqual((byte)1, bytes[7], "no monsters");
                Assert.AreEqual((byte)1, bytes[9], "player one in game");
                Assert.AreEqual(DemoRecorder.Terminator, bytes[bytes.Length - 1], "terminator");

                // The reader must accept it too, which is what matters.
                var demo = new Demo(path);
                Assert.AreEqual(GameSkill.Nightmare, demo.Options.Skill);
                Assert.AreEqual(7, demo.Options.Map);
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
        public void TurnIsStoredAsWholeUnits()
        {
            // The format keeps only the high byte of the turn amount. Recording
            // must truncate the same way the reader reconstructs it, otherwise
            // playback would turn by a different amount than the recording.
            var options = new GameOptions();
            options.Players[0].InGame = true;

            var recorder = new DemoRecorder(options);
            var cmds = NewCommands();

            cmds[0].AngleTurn = 0x1234;
            recorder.WriteCmd(cmds);

            var path = Path.Combine(Path.GetTempPath(), "dptdoom-turn-test.lmp");

            try
            {
                recorder.Save(path);

                var demo = new Demo(path);
                var replay = NewCommands();

                Assert.IsTrue(demo.ReadCmd(replay));
                Assert.AreEqual((short)(0x12 << 8), replay[0].AngleTurn,
                    "only the high byte survives, and playback shifts it back");
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
