//
// Copyright (C) 2026 DartPower Team
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using DPTDOOM.Audio;
using DPTDOOM.Net;
using DPTDOOM.UserInput;
using DPTDOOM.Video;

namespace DPTDOOM.Win32
{
    /// <summary>
    /// The playable Win32 front end: owns the window, the software renderer,
    /// the input translator, the optional network session and the game loop.
    /// </summary>
    internal sealed class Win32Doom : IDisposable
    {
        private readonly CommandLineArgs args;
        private readonly Config config;
        private readonly GameContent content;
        private readonly Win32Video video;
        private readonly ISound sound;
        private readonly IMusic music;
        private readonly Win32UserInput userInput;
        private readonly NetSession netSession;
        private readonly NetDriver netDriver;
        private readonly Doom doom;

        private FrameClock clock;

        private bool disposed;

        public Win32Doom(CommandLineArgs args)
        {
            this.args = args;

            config = ConfigUtilities.GetConfig();
            content = new GameContent(args);

            config.video_screenwidth = MathEx.Clamp(config.video_screenwidth, 320, 3200);
            config.video_screenheight = MathEx.Clamp(config.video_screenheight, 200, 2000);

            video = new Win32Video(config, content);

            if (!args.nosound.Present)
            {
                if (!args.nosfx.Present)
                {
                    sound = new Win32Sound(config, content);
                }

                if (!args.nomusic.Present)
                {
                    music = new Win32Music(config, content);
                }
            }

            if (args.net.Present)
            {
                netSession = ConnectWithStatus();
                netDriver = new NetDriver(netSession);
            }

            userInput = new Win32UserInput(config, video.Window);

            if (sound is Win32Sound win32Sound)
            {
                win32Sound.Start();
            }

            video.Window.KeyDown += key => doom.PostEvent(new DoomEvent(EventType.KeyDown, key));
            video.Window.KeyUp += key => doom.PostEvent(new DoomEvent(EventType.KeyUp, key));

            doom = new Doom(args, config, content, video, sound, music, userInput, netDriver);

            video.Window.Command += OnMenuCommand;
        }

        ////////////////////////////////////////////////////////////
        // Menu and tray commands
        ////////////////////////////////////////////////////////////

        private void OnMenuCommand(MenuCommand command)
        {
            try
            {
                Execute(command);
            }
            catch (Exception e)
            {
                Console.WriteLine("Menu command " + command + " failed: " + e.Message);
            }
        }

        private void Execute(MenuCommand command)
        {
            var window = video.Window;

            switch (command)
            {
                case MenuCommand.GameNew:
                    doom.NewGame(doom.Options.Skill, doom.Options.Episode, doom.Options.Map);
                    break;

                case MenuCommand.GameWarpNext:
                {
                    // Same restriction the engine uses: Doom II has 32 maps,
                    // Doom I has episodes of 9.
                    var limit = doom.Options.GameMode == GameMode.Commercial ? 32 : 9;
                    var next = doom.Options.Map + 1;
                    if (next > limit)
                    {
                        next = 1;
                    }

                    doom.NewGame(doom.Options.Skill, doom.Options.Episode, next);
                    break;
                }

                case MenuCommand.GameSave:
                    doom.PostEvent(new DoomEvent(EventType.KeyDown, DoomKey.F2));
                    break;

                case MenuCommand.GameLoad:
                    doom.PostEvent(new DoomEvent(EventType.KeyDown, DoomKey.F3));
                    break;

                case MenuCommand.GamePause:
                    doom.PostEvent(new DoomEvent(EventType.KeyDown, DoomKey.Pause));
                    break;

                case MenuCommand.GameQuit:
                case MenuCommand.TrayQuit:
                    doom.Quit();
                    break;

                case MenuCommand.ViewFullscreen:
                    window.SetFullscreen(!window.IsFullscreen);
                    break;

                case MenuCommand.ViewSize320:
                    window.SetClientSize(320, 200);
                    break;

                case MenuCommand.ViewSize640:
                    window.SetClientSize(640, 400);
                    break;

                case MenuCommand.ViewSize960:
                    window.SetClientSize(960, 600);
                    break;

                case MenuCommand.ViewSize1280:
                    window.SetClientSize(1280, 800);
                    break;

                case MenuCommand.ViewGammaUp:
                    video.GammaCorrectionLevel = (video.GammaCorrectionLevel + 1) % (video.MaxGammaCorrectionLevel + 1);
                    break;

                case MenuCommand.ViewGammaDown:
                    video.GammaCorrectionLevel =
                        (video.GammaCorrectionLevel + video.MaxGammaCorrectionLevel) % (video.MaxGammaCorrectionLevel + 1);
                    break;

                case MenuCommand.ViewMessages:
                    video.DisplayMessage = !video.DisplayMessage;
                    break;

                case MenuCommand.ViewFps:
                    window.ShowOverlay = !window.ShowOverlay;
                    break;

                case MenuCommand.AudioSfxUp:
                case MenuCommand.AudioSfxDown:
                    if (sound != null)
                    {
                        var step = command == MenuCommand.AudioSfxUp ? 1 : -1;
                        sound.Volume = MathEx.Clamp(sound.Volume + step, 0, sound.MaxVolume);
                    }

                    break;

                case MenuCommand.AudioMusicUp:
                case MenuCommand.AudioMusicDown:
                    if (music != null)
                    {
                        var step = command == MenuCommand.AudioMusicUp ? 1 : -1;
                        music.Volume = MathEx.Clamp(music.Volume + step, 0, music.MaxVolume);
                    }

                    break;

                case MenuCommand.AudioMuteSfx:
                    if (sound != null)
                    {
                        sound.Volume = sound.Volume > 0 ? 0 : sound.MaxVolume / 2;
                    }

                    break;

                case MenuCommand.AudioMuteMusic:
                    if (music != null)
                    {
                        music.Volume = music.Volume > 0 ? 0 : music.MaxVolume / 2;
                    }

                    break;

                case MenuCommand.DebugFpsLog:
                    ToggleFpsLog();
                    break;

                case MenuCommand.DebugSaveFrame:
                    SaveFrame();
                    break;

                case MenuCommand.DebugDumpState:
                    DumpState();
                    break;

                case MenuCommand.DebugSoundTest:
                    RenderSoundTest();
                    break;

                case MenuCommand.DebugGod:
                    ToggleCheat(CheatFlags.GodMode);
                    break;

                case MenuCommand.DebugNoClip:
                    ToggleCheat(CheatFlags.NoClip);
                    break;

                case MenuCommand.DebugGiveAll:
                    GiveAll();
                    break;

                case MenuCommand.HelpControls:
                    doom.PostEvent(new DoomEvent(EventType.KeyDown, DoomKey.F1));
                    break;

                case MenuCommand.HelpAbout:
                    ShowAbout();
                    break;

                case MenuCommand.TrayShow:
                    window.RestoreFromTray();
                    break;

                case MenuCommand.TrayHide:
                    window.MinimizeToTray = true;
                    window.HideToTray();
                    break;
            }

            UpdateMenuChecks();
        }

        /// <summary>
        /// Text for the debug overlay. Kept short: it is drawn over the game
        /// and every extra line hides part of the view.
        /// </summary>
        private string BuildOverlay(FrameStep step)
        {
            var game = doom.Game;
            var levelTime = game != null && game.World != null ? game.World.LevelTime : 0;
            var map = doom.Options.GameMode == GameMode.Commercial
                ? "MAP" + doom.Options.Map.ToString("00")
                : "E" + doom.Options.Episode + "M" + doom.Options.Map;

            var text = "FPS " + clock.CurrentFps.ToString("0.0") +
                       "   " + map + "   tic " + levelTime;

            if (args.fpslog.Present || clock.IsLogging)
            {
                text += "\nrender " + video.LastRenderMs.ToString("0.0") +
                        " ms   present " + video.LastPresentMs.ToString("0.0") + " ms";
            }

            return text;
        }

        private void ToggleFpsLog()
        {
            if (clock == null)
            {
                return;
            }

            if (clock.IsLogging)
            {
                clock.CloseLog();
                Console.WriteLine("Frame log closed.");
                return;
            }

            // Debug artifacts go to their own folder so they never mix with
            // the binaries next to the executable.
            var path = Path.Combine(
                ConfigUtilities.GetDataDirectory(),
                "debug",
                "fps-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".tsv");

            clock.OpenLog(path);
            Console.WriteLine("Frame log: " + path);
        }

        private void SaveFrame()
        {
            var directory = Path.Combine(ConfigUtilities.GetDataDirectory(), "screenshots");
            var path = Path.Combine(directory, "frame-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bmp");

            video.SaveFrame(path);
            Console.WriteLine("Saved frame: " + path);
        }

        private void DumpState()
        {
            var game = doom.Game;
            if (game == null || game.World == null)
            {
                Console.WriteLine("No level loaded; nothing to dump.");
                return;
            }

            var text =
                "level=" + game.Options.Episode + "/" + game.Options.Map +
                " time=" + game.World.LevelTime +
                " mobj=" + DoomDebug.GetMobjHash(game.World).ToString("x8") +
                " sector=" + DoomDebug.GetSectorHash(game.World).ToString("x8");

            Console.WriteLine("State: " + text);
        }

        private void RenderSoundTest()
        {
            var path = Path.Combine(
                ConfigUtilities.GetDataDirectory(),
                "debug",
                "sfx-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".wav");

            Headless.SoundTest.Run(content.Wad, path, null);
        }

        private void ToggleCheat(CheatFlags flag)
        {
            var player = ConsolePlayer();
            if (player == null)
            {
                return;
            }

            if ((player.Cheats & flag) != 0)
            {
                player.Cheats &= ~flag;
            }
            else
            {
                player.Cheats |= flag;
            }
        }

        private void GiveAll()
        {
            var player = ConsolePlayer();
            if (player == null)
            {
                return;
            }

            for (var i = 0; i < player.WeaponOwned.Length; i++)
            {
                player.WeaponOwned[i] = true;
            }

            for (var i = 0; i < player.Ammo.Length; i++)
            {
                player.Ammo[i] = player.MaxAmmo[i];
            }
        }

        private Player ConsolePlayer()
        {
            var game = doom.Game;
            if (game == null || game.World == null)
            {
                return null;
            }

            return game.World.ConsolePlayer;
        }

        private void ShowAbout()
        {
            NativeMethods.MessageBox(
                video.Window.Handle,
                ApplicationInfo.Title + "\n\n" +
                "DOOM source port for the DartPower Team.\n" +
                "Based on Managed Doom by Nobuaki Tanaka.\n\n" +
                "Engine: GNU GPL v2 or later.\n" +
                "DOOM belongs to id Software; game data is not included.",
                "О программе",
                NativeMethods.MB_OK | NativeMethods.MB_ICONINFORMATION);
        }

        private void UpdateMenuChecks()
        {
            var window = video.Window;
            var menu = window.Menu;
            if (menu == null)
            {
                return;
            }

            menu.SetChecked(MenuCommand.ViewFullscreen, window.IsFullscreen);
            menu.SetChecked(MenuCommand.ViewMessages, video.DisplayMessage);
            menu.SetChecked(MenuCommand.ViewFps, window.ShowOverlay);
            menu.SetChecked(MenuCommand.DebugFpsLog, clock != null && clock.IsLogging);

            var player = ConsolePlayer();
            menu.SetChecked(MenuCommand.DebugGod,
                player != null && (player.Cheats & CheatFlags.GodMode) != 0);
            menu.SetChecked(MenuCommand.DebugNoClip,
                player != null && (player.Cheats & CheatFlags.NoClip) != 0);
            menu.SetChecked(MenuCommand.AudioMuteSfx, sound != null && sound.Volume == 0);
            menu.SetChecked(MenuCommand.AudioMuteMusic, music != null && music.Volume == 0);
        }

        /// <summary>
        /// Runs the network handshake while keeping the window alive and
        /// showing progress with the game's own font.
        /// </summary>
        private NetSession ConnectWithStatus()
        {
            NetSession session = null;
            Exception error = null;

            var thread = new Thread(() =>
            {
                try
                {
                    session = NetStartup.Create(args, content, ShowStatus);
                }
                catch (Exception e)
                {
                    error = e;
                }
            });

            thread.IsBackground = true;
            thread.Start();

            while (thread.IsAlive)
            {
                video.Window.PumpMessages();
                Thread.Sleep(50);
            }

            if (error != null)
            {
                throw error;
            }

            return session;
        }

        private void ShowStatus(string title, string[] lines)
        {
            try
            {
                video.Window.PumpMessages();
                video.ShowStatus(title, lines);
                video.Window.SetTitle(ApplicationInfo.Title + " - " + title);
            }
            catch
            {
            }
        }

        public void Run()
        {
            var fpsScale = args.timedemo.Present ? 1 : MathEx.Clamp(config.video_fpsscale, 1, 100);

            var pacer = new FramePacer(fpsScale);

            using (clock = new FrameClock(pacer.TargetFps))
            {
                if (args.fpslog.Present)
                {
                    clock.OpenLog(args.fpslog.Value);
                    video.Profiling = true;
                }

                if (video.Window.Menu != null)
                {
                    video.Window.Menu.SetRadio(
                        MenuCommand.ViewSize320, MenuCommand.ViewSize1280, MenuCommand.ViewSize640);
                }

                UpdateMenuChecks();

                NativeMethods.timeBeginPeriod(1);

                try
                {
                    while (true)
                    {
                        video.Window.PumpMessages();

                        if (video.Window.Closed)
                        {
                            break;
                        }

                        clock.WaitForNextFrame();

                        var step = pacer.Advance();

                        if (step.Update)
                        {
                            if (doom.Update() == UpdateResult.Completed)
                            {
                                break;
                            }
                        }

                        if (args.timedemo.Present)
                        {
                            // Benchmark mode: run as fast as possible, no drawing.
                            clock.EndFrame(step, 0, 0, clock.LastWaitMs);
                            continue;
                        }

                        if (video.Window.ShowOverlay)
                        {
                            video.Window.OverlayText = BuildOverlay(step);
                        }

                        video.Render(doom, step.Fraction);
                        clock.EndFrame(step, video.LastRenderMs, video.LastPresentMs, clock.LastWaitMs);
                    }
                }
                finally
                {
                    NativeMethods.timeEndPeriod(1);
                }

                if (args.fpslog.Present)
                {
                    clock.WriteSummary(Console.Out);
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            config.Save(ConfigUtilities.GetConfigPath());

            if (userInput != null)
            {
                userInput.ReleaseMouse();
            }

            if (netDriver != null)
            {
                netDriver.Dispose();
            }

            if (music is IDisposable disposableMusic)
            {
                disposableMusic.Dispose();
            }

            if (sound is IDisposable disposableSound)
            {
                disposableSound.Dispose();
            }

            video.Dispose();
            content.Dispose();
        }

        public string QuitMessage => doom.QuitMessage;
    }
}
