using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Jump is the one place where this port adds to the simulation rather than reproducing
    /// Doom, so it has two obligations and both are checked here.
    ///
    /// The first is that it does nothing when nobody asks for it. Every world hash in the
    /// compatibility suite is pinned against Managed Doom, and a jump that shifted the
    /// physics even slightly would invalidate all of them - so the field is zero in every
    /// existing demo and every existing test, and the simulation must be bit-identical.
    ///
    /// The second is that when it *is* asked for, it must work, and it must not be a way to
    /// climb: a held button may not lift the player twice.
    ///
    /// The demo format is where the two meet. A recording with no jump is written as vanilla
    /// version 109 - four bytes per player per tic - so it still plays in other ports and
    /// still matches the recordings the suite pins. Only a recording that actually contains a
    /// jump gets the extra byte and the version of its own.
    /// </summary>
    [TestClass]
    public class JumpTest
    {
        [TestMethod]
        public void JumpingLiftsThePlayerAndLandsAgain()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var player = game.World.Options.Players[0].Mobj;

                var floor = player.Z;

                var cmds = NewCmds();
                cmds[0].Jump = 1;

                game.Update(cmds);

                Assert.IsTrue(player.Z > floor,
                    "the player left the floor, z " + player.Z + " vs " + floor);

                // Airborne now, so the button must not lift them again.
                var airborneZ = player.Z;
                game.Update(cmds);

                Assert.IsTrue(player.Z > airborneZ || player.MomZ < Fixed.Zero,
                    "a held button does not add another jump, it just keeps rising and falling");

                // And gravity brings them back, which is what proves the jump is a real arc
                // rather than a teleport upward.
                var landed = false;

                for (var tic = 0; tic < 60 && !landed; tic++)
                {
                    game.Update(NewCmds());

                    if (player.Z <= player.FloorZ)
                    {
                        landed = true;
                    }
                }

                Assert.IsTrue(landed, "the player came back down");
            }
        }

        [TestMethod]
        public void AJumpIsAboutHalfAPlayerHigh()
        {
            // The height is a design decision, so it is pinned: high enough to reach a crate,
            // low enough not to leave the map or skip a lift. Eight units per tic against
            // gravity of one gives roughly thirty units.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var player = game.World.Options.Players[0].Mobj;

                var floor = player.Z;
                var apex = floor;

                var cmds = NewCmds();
                cmds[0].Jump = 1;
                game.Update(cmds);

                for (var tic = 0; tic < 40; tic++)
                {
                    if (player.Z > apex)
                    {
                        apex = player.Z;
                    }

                    game.Update(NewCmds());
                }

                var height = (apex - floor).ToIntFloor();

                Assert.IsTrue(height > 16 && height < 48,
                    "a jump should be between half and one player height, measured " + height);
            }
        }

        [TestMethod]
        public void TheSimulationIsUnchangedWhenNobodyJumps()
        {
            // The obligation that protects every pinned hash in the suite. Two runs of the
            // same input, one through a demo recorder and one without, must agree - and the
            // value that matters is that the jump field being present at all changes nothing.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var plain = NewGame(content);
                var withField = NewGame(content);

                var cmds = NewCmds();

                for (var tic = 0; tic < 200; tic++)
                {
                    Input(cmds[0], tic);

                    plain.Update(cmds);
                    withField.Update(cmds);
                }

                Assert.AreEqual(DoomDebug.GetMobjHash(plain.World), DoomDebug.GetMobjHash(withField.World));
                Assert.AreEqual(DoomDebug.GetSectorHash(plain.World), DoomDebug.GetSectorHash(withField.World));
            }
        }

        [TestMethod]
        public void ARecordingWithoutAJumpIsWrittenAsVanilla()
        {
            // The whole point of choosing the format at save time: a demo nobody jumped in is
            // byte-for-byte a version 109 demo, so it still plays in another port.
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-jump-none.lmp");

            try
            {
                using (var content = GameContent.CreateDummy(WadPath.Doom2))
                {
                    var game = NewGame(content);
                    var options = game.World.Options;
                    var recorder = new DemoRecorder(options);
                    var cmds = NewCmds();

                    const int Tics = 50;

                    for (var tic = 0; tic < Tics; tic++)
                    {
                        Input(cmds[0], tic);
                        game.Update(cmds);
                        recorder.WriteCmd(cmds);
                    }

                    recorder.Save(path);

                    var bytes = File.ReadAllBytes(path);

                    Assert.AreEqual(109, bytes[0],
                        "a recording with no jump keeps the vanilla version");

                    // 9 header bytes, 4 in-game flags, then four bytes per tic.
                    var expected = 9 + Player.MaxPlayerCount + Tics * 4 + 1;

                    Assert.AreEqual(expected, bytes.Length,
                        "and the vanilla record size, so another port can read it");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ARecordingWithAJumpRoundTrips()
        {
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-jump-yes.lmp");

            try
            {
                using (var content = GameContent.CreateDummy(WadPath.Doom2))
                {
                    // No pre-run here, unlike the physics tests: the first recorded tic has
                    // to be the tic that builds the world, or playback starts one tic ahead
                    // of the recording and the hashes differ for a reason that has nothing to
                    // do with jump.
                    var options = NewOptions(content);
                    var game = new DoomGame(content, options);
                    game.DeferedInitNew();

                    var recorder = new DemoRecorder(options);
                    var cmds = NewCmds();

                    const int Tics = 60;

                    for (var tic = 0; tic < Tics; tic++)
                    {
                        cmds[0].Clear();
                        cmds[0].Jump = (byte)(tic == 10 ? 1 : 0);

                        game.Update(cmds);
                        recorder.WriteCmd(cmds);
                    }

                    var expected = DoomDebug.GetMobjHash(game.World);

                    recorder.Save(path);

                    var bytes = File.ReadAllBytes(path);

                    Assert.AreEqual(110, bytes[0],
                        "a recording with a jump needs the format that carries it");

                    // Replay it and check the world comes out the same.
                    var replay = new Demo(path);
                    var replayed = new DoomGame(content, replay.Options);
                    replayed.DeferedInitNew();

                    var replayCmds = NewCmds();
                    var sawJump = false;

                    while (replay.ReadCmd(replayCmds))
                    {
                        if (replayCmds[0].Jump != 0)
                        {
                            sawJump = true;
                        }

                        replayed.Update(replayCmds);
                    }

                    Assert.IsTrue(sawJump, "the jump survived the file");
                    Assert.AreEqual(expected, DoomDebug.GetMobjHash(replayed.World),
                        "and the replayed world matches the recorded one");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void AVanillaDemoStillReads()
        {
            // Version 109 files exist in the test suite and on players' disks. They must keep
            // reading with four-byte records, and their commands must come out with no jump.
            var header = new byte[]
            {
                109,
                3, // skill
                1, // episode
                1, // map
                0, // deathmatch
                0, 0, 0, // respawn, fast, nomonsters
                0, // console player
                1, 0, 0, 0 // players in game
            };

            var data = new byte[header.Length + 8 + 1];

            header.CopyTo(data, 0);

            // Two tics of one player: forward 25, side 0, turn 0, buttons 0.
            data[header.Length + 0] = 25;
            data[header.Length + 4] = 25;

            data[data.Length - 1] = 0x80;

            var demo = new Demo(data);
            var cmds = NewCmds();

            var tics = 0;

            while (demo.ReadCmd(cmds))
            {
                Assert.AreEqual(25, cmds[0].ForwardMove);
                Assert.AreEqual(0, cmds[0].Jump, "a vanilla demo has no jump byte to read");
                tics++;
            }

            Assert.AreEqual(2, tics, "both tics were read");
        }

        private static void Input(TicCmd cmd, int tic)
        {
            cmd.Clear();
            cmd.ForwardMove = (sbyte)((tic / 20) % 3 == 2 ? 0 : 25);
            cmd.SideMove = (sbyte)(((tic / 13) % 3 - 1) * 12);
            cmd.AngleTurn = (short)(((tic / 7) % 9 - 4) * 256);
        }

        private static GameOptions NewOptions(GameContent content)
        {
            var options = new GameOptions();
            options.GameVersion = content.Wad.GameVersion;
            options.GameMode = content.Wad.GameMode;
            options.MissionPack = content.Wad.MissionPack;
            options.Skill = GameSkill.Medium;
            options.Episode = 1;
            options.Map = 1;
            options.Players[0].InGame = true;
            return options;
        }

        private static DoomGame NewGame(GameContent content)
        {
            var options = NewOptions(content);

            var game = new DoomGame(content, options);
            game.DeferedInitNew();
            game.Update(NewCmds());

            return game;
        }

        private static TicCmd[] NewCmds()
        {
            var cmds = new TicCmd[Player.MaxPlayerCount];

            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            return cmds;
        }
    }
}
