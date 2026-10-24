using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Video;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Vertical look and the crosshair.
    ///
    /// Both are presentation and neither may touch the simulation: pitch is not in
    /// a TicCmd, so it cannot desynchronise a lockstep session, and a crosshair is
    /// drawn after everything else. These tests pin that, because the obvious wrong
    /// implementation - storing the tilt on the player, or nudging the view height -
    /// would look identical on screen and break demos.
    ///
    /// The rest is geometry: the horizon has to move, the weapon must not, and the
    /// clamp has to hold.
    /// </summary>
    [TestClass]
    public class ViewPitchTest
    {
        private static DoomGame NewGame(GameContent content, Config config)
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
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);

            return game;
        }

        /// <summary>
        /// Renders one frame at the given tilt and returns the frame hash.
        /// </summary>
        private static int RenderFrame(Config config, GameContent content, DoomGame game,
                                       int pitch, int crosshair)
        {
            var renderer = new Renderer(config, content);
            renderer.Profile.Enabled = false;

            var destination = new byte[renderer.Width * renderer.Height * 4];

            // The renderer takes the pitch through the same door the game loop uses,
            // so this exercises the real path and not a test-only shortcut.
            renderer.RenderGameForTest(game, Fixed.FromInt(pitch), crosshair, destination);

            var hash = 17;
            foreach (var b in destination)
            {
                hash = hash * 31 + b;
            }

            return hash;
        }

        [TestMethod]
        public void PitchIsNotPartOfTheWorld()
        {
            // The world hash must be identical at every tilt. If the tilt ever
            // reaches the player or the view height, a demo recorded with it would
            // not replay - and in a network game the two ends would disagree.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var config = new Config();
                var game = NewGame(content, config);

                var before = DoomDebug.GetMobjHash(game.World);

                RenderFrame(config, content, game, 45, 0);
                RenderFrame(config, content, game, -45, 3);

                var after = DoomDebug.GetMobjHash(game.World);

                Assert.AreEqual(before, after, "rendering with a tilt does not change the world");
            }
        }

        [TestMethod]
        public void TiltingMovesTheHorizon()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var config = new Config();
                var game = NewGame(content, config);

                var level = RenderFrame(config, content, game, 0, 0);
                var up = RenderFrame(config, content, game, 40, 0);
                var down = RenderFrame(config, content, game, -40, 0);

                Assert.AreNotEqual(level, up, "looking up changes the frame");
                Assert.AreNotEqual(level, down, "looking down changes the frame");
                Assert.AreNotEqual(up, down, "up and down are not the same picture");
            }
        }

        /// <summary>
        /// Whether two frames agree at one pixel.
        ///
        /// The destination buffer is one uint per pixel, so a byte-level comparison
        /// at index y*width+x reads the middle of somebody else's pixel - which is
        /// how an earlier version of this test managed to "prove" that the weapon
        /// moved.
        /// </summary>
        private static bool SamePixel(byte[] a, byte[] b, int x, int y)
        {
            var offset = (y * 640 + x) * 4;

            return a[offset + 0] == b[offset + 0] &&
                   a[offset + 1] == b[offset + 1] &&
                   a[offset + 2] == b[offset + 2] &&
                   a[offset + 3] == b[offset + 3];
        }

        [TestMethod]
        public void TheWeaponDoesNotFollowTheTilt()
        {
            // The gun is held by the player, so it stays put while the view moves.
            // Getting this wrong is not obvious: the sprite keeps its position but
            // shows the wrong part of its own texture, which reads as a glitch
            // rather than as a misplaced gun.
            //
            // Comparing a tilt against the level view does not work, because the
            // weapon is a masked patch and the world shows through its transparent
            // parts - those pixels move with the world and differ legitimately.
            // Comparing two *opposite* tilts does work: everything in the world
            // moves by a large amount, so any pixel that is identical in both can
            // only belong to the weapon.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var config = new Config();
                var game = NewGame(content, config);

                var up = RenderFrameBytes(config, content, game, 45);
                var down = RenderFrameBytes(config, content, game, -45);

                var viewHeight = 336;

                var same = 0;

                for (var y = 0; y < viewHeight; y++)
                {
                    for (var x = 0; x < 640; x++)
                    {
                        if (!SamePixel(up, down, x, y))
                        {
                            continue;
                        }

                        same++;
                    }
                }

                Assert.IsTrue(same > 3000,
                    "the weapon covers the same pixels at opposite tilts (" + same + " identical)");
            }
        }

        [TestMethod]
        public void TheCrosshairSitsInTheMiddle()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var config = new Config();
                var game = NewGame(content, config);

                var off = RenderFrameBytes(config, content, game, 0, 0);
                var cross = RenderFrameBytes(config, content, game, 0, 1);
                var dot = RenderFrameBytes(config, content, game, 0, 3);

                // The centre of the 3D view, not of the window: the status bar takes
                // the bottom 32 virtual pixels, so the view is 336 tall at 640x400.
                const int cx = 320;
                const int cy = 168;

                // A crosshair is drawn in a small box around the centre. Counting the
                // changed pixels there is sturdier than picking one: the exact centre
                // is deliberately empty in the cross style, so a single-pixel check
                // would be testing the gap rather than the mark.
                var changed = 0;

                for (var y = cy - 20; y <= cy + 20; y++)
                {
                    for (var x = cx - 20; x <= cx + 20; x++)
                    {
                        if (!SamePixel(off, cross, x, y))
                        {
                            changed++;
                        }
                    }
                }

                Assert.IsTrue(changed > 20, "the cross is drawn around the centre (" + changed + " pixels)");

                Assert.IsTrue(SamePixel(off, cross, cx, cy), "the cross leaves the centre open");

                Assert.IsFalse(SamePixel(off, dot, cx, cy), "the dotted style fills the centre");

                // The corners must be untouched: a crosshair that changed the whole
                // frame would mean it was drawn before the world.
                Assert.IsTrue(SamePixel(off, cross, 10, 10), "the corner is untouched");
                Assert.IsTrue(SamePixel(off, cross, 630, 330), "the far corner is untouched");
            }
        }

        [TestMethod]
        public void EveryCrosshairStyleIsDifferent()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var config = new Config();
                var game = NewGame(content, config);

                var seen = new System.Collections.Generic.List<int>();

                for (var style = 0; style <= 3; style++)
                {
                    var hash = RenderFrame(config, content, game, 0, style);

                    Assert.IsFalse(seen.Contains(hash),
                        "crosshair style " + style + " is not the same picture as an earlier one");

                    seen.Add(hash);
                }
            }
        }

        private static byte[] RenderFrameBytes(Config config, GameContent content, DoomGame game, int pitch)
        {
            return RenderFrameBytes(config, content, game, pitch, 0);
        }

        private static byte[] RenderFrameBytes(Config config, GameContent content, DoomGame game,
                                               int pitch, int crosshair)
        {
            var renderer = new Renderer(config, content);
            var destination = new byte[renderer.Width * renderer.Height * 4];

            renderer.RenderGameForTest(game, Fixed.FromInt(pitch), crosshair, destination);

            return destination;
        }
    }
}
