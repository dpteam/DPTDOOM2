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

        /// <summary>
        /// Frame timing for the in-game counter. One instance for the whole run: the
        /// renderer reads it every frame, and allocating one per frame would be a
        /// garbage-collection pause in the middle of the thing being measured.
        /// </summary>
        private readonly FrameStats stats = new FrameStats();
        private int slowCounter;

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

                case MenuCommand.ViewCrosshair:
                    // Cycled rather than toggled: four styles, and the player
                    // finds the one they like by pressing the same item again.
                    config.hud_crosshair = (config.hud_crosshair + 1) % 4;
                    break;

                case MenuCommand.ViewFreeLook:
                    config.view_freelook = !config.view_freelook;

                    if (!config.view_freelook)
                    {
                        // The game loop recentres on the next frame; saying so
                        // here would need the world, which is not always there.
                        Console.WriteLine("Free look off, view recentred.");
                    }
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

                case MenuCommand.DebugProfile:
                    ToggleRenderProfile();
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

                case MenuCommand.DebugWireframe:
                    DebugSettings.Wireframe = !DebugSettings.Wireframe;
                    break;

                case MenuCommand.DebugShowTriggers:
                DebugSettings.ShowTriggers = !DebugSettings.ShowTriggers;
                EnsureMapVisible();
                break;

            case MenuCommand.DebugShowBlockmap:
                DebugSettings.ShowBlockmap = !DebugSettings.ShowBlockmap;
                EnsureMapVisible();
                break;

            case MenuCommand.DebugMapOnly:
                    DebugSettings.MapOnly = !DebugSettings.MapOnly;
                    ToggleMapOnly();
                    break;

                case MenuCommand.DebugFreeze:
                    if (RequireSinglePlayer())
                    {
                        DebugSettings.FreezeMonsters = !DebugSettings.FreezeMonsters;
                    }

                    break;

                case MenuCommand.DebugSlowMotion:
                    if (RequireSinglePlayer())
                    {
                        // 1x -> 2x -> 4x -> 1x
                        DebugSettings.SlowMotion = DebugSettings.SlowMotion == 1 ? 2
                            : DebugSettings.SlowMotion == 2 ? 4 : 1;
                    }

                    break;

                case MenuCommand.DebugKillAll:
                    KillAllMonsters();
                    break;

                case MenuCommand.DebugRevealMap:
                    doom.Game.World.AutoMap.ToggleCheat();
                    break;

                case MenuCommand.DebugSpawnHealth:
                    SpawnNearPlayer(2012); // medikit
                    break;

                case MenuCommand.DebugSpawnArmor:
                    SpawnNearPlayer(2018); // green armor
                    break;

                case MenuCommand.DebugSpawnAmmo:
                    SpawnNearPlayer(2007); // clip
                    break;

                case MenuCommand.DebugWarpNext:
                    WarpRelative(1);
                    break;

                case MenuCommand.DebugWarpPrev:
                    WarpRelative(-1);
                    break;

                case MenuCommand.HelpControls:
                    doom.PostEvent(new DoomEvent(EventType.KeyDown, DoomKey.F1));
                    break;

                case MenuCommand.HelpAbout:
                    doom.ShowAbout();
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
        /// <summary>
        /// The window's own overlay text: state that the in-game counter does not
        /// show.
        ///
        /// The frame rate moved to the in-game profiler, which is drawn with Doom's
        /// font and lives in the top-right corner of the view. What is left here is
        /// the things that are not timing - which map, how many tics, what the bots
        /// are doing, what the session costs - because those are text a player reads
        /// once in a while rather than a number they watch.
        /// </summary>
        private string BuildOverlay(FrameStep step)
        {
            var game = doom.Game;
            var levelTime = game != null && game.World != null ? game.World.LevelTime : 0;
            var map = doom.Options.GameMode == GameMode.Commercial
                ? "MAP" + doom.Options.Map.ToString("00")
                : "E" + doom.Options.Episode + "M" + doom.Options.Map;

            var text = map + "   tic " + levelTime;

            var bots = doom.BotReport;

            if (bots != null)
            {
                text += "\n" + bots;
            }

            // Network state, because "it feels laggy" is unanswerable without a
            // number. Ping is the live round trip; delay is how far ahead input
            // is sent, which is what a lockstep session actually costs the player.
            if (netSession != null)
            {
                var ping = netSession.PingMs;
                text += "\n" + (netSession.IsHost ? "host" : "client") +
                        (netSession.Relayed ? " relay" : " direct") +
                        "   ping " + (ping > 0 ? ping + " ms" : "--") +
                        "   input delay " + netSession.Delay + " tics (" +
                        (netSession.Delay * 1000 / 35) + " ms)";
            }

            return text;
        }

        /// <summary>
        /// Turns the per-section frame profile on or off, and prints the report
        /// when it is turned off.
        ///
        /// Printing on the way out rather than continuously is deliberate: the
        /// numbers a player wants are the averages over the stretch they were
        /// curious about, not the last frame, which happens to be whichever way
        /// they were facing when they opened the menu.
        /// </summary>
        private void ToggleRenderProfile()
        {
            var profile = video.Profile;

            if (profile == null)
            {
                return;
            }

            profile.Enabled = !profile.Enabled;

            if (!profile.Enabled)
            {
                profile.WriteSummary(Console.Out);
            }
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

        /// <summary>
        /// Simulation-changing toggles are refused outside a plain single-player
        /// game: a demo or a network session that froze its monsters would be
        /// broken in a way that is hard to explain later.
        /// </summary>
        private bool RequireSinglePlayer()
        {
            if (DebugSettings.SimulationChangesAllowed(doom))
            {
                return true;
            }

            Console.WriteLine("Debug: this toggle only works in single player.");
            return false;
        }

        /// <summary>
        /// Debug overlays only draw on the automap, so switching one on shows the
        /// automap. Turning it on and seeing nothing would look like a broken
        /// command rather than a view the player is not looking at.
        /// </summary>
        private void EnsureMapVisible()
        {
            var world = doom.Game != null ? doom.Game.World : null;

            if (world == null)
            {
                return;
            }

            if (!world.AutoMap.Visible)
            {
                world.AutoMap.Open();
            }
        }

        private void ToggleMapOnly()
        {
            var world = doom.Game != null ? doom.Game.World : null;
            if (world == null)
            {
                return;
            }

            if (DebugSettings.MapOnly)
            {
                world.AutoMap.Open();
            }
            else
            {
                world.AutoMap.Close();
            }
        }

        private void KillAllMonsters()
        {
            var world = doom.Game != null ? doom.Game.World : null;
            if (world == null)
            {
                return;
            }

            // Collected first: damaging an object can spawn replacements or
            // remove it from the thinker list while it is being walked.
            var victims = new System.Collections.Generic.List<Mobj>();

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;
                if (mobj != null &&
                    (mobj.Flags & MobjFlags.CountKill) != 0 &&
                    mobj.Health > 0)
                {
                    victims.Add(mobj);
                }
            }

            foreach (var victim in victims)
            {
                world.ThingInteraction.DamageMobj(victim, null, null, 10000);
            }

            Console.WriteLine("Debug: killed " + victims.Count + " monsters.");
        }

        /// <summary>
        /// Spawns a thing by its map editor number, the way a level does, so the
        /// menu does not need to know the internal enum names.
        /// </summary>
        private void SpawnNearPlayer(int doomEdNum)
        {
            var player = ConsolePlayer();
            if (player == null || player.Mobj == null)
            {
                return;
            }

            var type = MobjType.Player;
            var found = false;

            for (var i = 0; i < DoomInfo.MobjInfos.Length; i++)
            {
                if (DoomInfo.MobjInfos[i].DoomEdNum == doomEdNum)
                {
                    type = (MobjType)i;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return;
            }

            var mobj = player.Mobj;
            doom.Game.World.ThingAllocation.SpawnMobj(mobj.X, mobj.Y, mobj.Z, type);
        }

        private void WarpRelative(int delta)
        {
            var limit = doom.Options.GameMode == GameMode.Commercial ? 32 : 9;
            var map = doom.Options.Map + delta;

            if (map > limit)
            {
                map = 1;
            }
            else if (map < 1)
            {
                map = limit;
            }

            doom.NewGame(doom.Options.Skill, doom.Options.Episode, map);
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
            menu.SetChecked(MenuCommand.ViewCrosshair, config.hud_crosshair != 0);
            menu.SetChecked(MenuCommand.ViewFreeLook, config.view_freelook);
            menu.SetChecked(MenuCommand.DebugFpsLog, clock != null && clock.IsLogging);
            menu.SetChecked(MenuCommand.DebugProfile, video.Profile != null && video.Profile.Enabled);

            var player = ConsolePlayer();
            menu.SetChecked(MenuCommand.DebugGod,
                player != null && (player.Cheats & CheatFlags.GodMode) != 0);
            menu.SetChecked(MenuCommand.DebugNoClip,
                player != null && (player.Cheats & CheatFlags.NoClip) != 0);
            menu.SetChecked(MenuCommand.AudioMuteSfx, sound != null && sound.Volume == 0);
            menu.SetChecked(MenuCommand.AudioMuteMusic, music != null && music.Volume == 0);

            menu.SetChecked(MenuCommand.DebugWireframe, DebugSettings.Wireframe);
            menu.SetChecked(MenuCommand.DebugShowTriggers, DebugSettings.ShowTriggers);
            menu.SetChecked(MenuCommand.DebugShowBlockmap, DebugSettings.ShowBlockmap);
            menu.SetChecked(MenuCommand.DebugMapOnly, DebugSettings.MapOnly);
            menu.SetChecked(MenuCommand.DebugFreeze, DebugSettings.FreezeMonsters);

            var slow = DebugSettings.SlowMotion > 1 ? "Замедление x" + DebugSettings.SlowMotion : "Замедление x1";
            menu.SetLabel(MenuCommand.DebugSlowMotion, slow);
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

            doom.Stats = stats;

            using (clock = new FrameClock(pacer.TargetFps))
            {
                if (args.fpslog.Present)
                {
                    clock.OpenLog(args.fpslog.Value);
                    video.Profiling = true;
                }

                if (args.profile.Present && video.Profile != null)
                {
                    video.Profile.Enabled = true;
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
                    var lastFrameTime = 0.0;
                    var started = false;
                    var finished = false;

                    while (!finished)
                    {
                        video.Window.PumpMessages();

                        if (video.Window.Closed)
                        {
                            break;
                        }

                        // Benchmark mode must not be throttled, or it measures the
                        // throttle instead of the machine. It also has to advance
                        // exactly one tic per iteration: -timedemo runs at
                        // fpsScale 1 and reports iterations per second, which only
                        // means anything if each iteration is one tic.
                        if (args.timedemo.Present)
                        {
                            var benchStep = pacer.Advance();
                            finished = doom.Update() == UpdateResult.Completed;
                            clock.EndFrame(benchStep, 0, 0, 0);
                            continue;
                        }

                        clock.WaitForNextFrame();

                        // The simulation is driven by elapsed time, not by how many
                        // frames were drawn. If drawing a bigger window is slower
                        // than the target frame rate, the frame count drops but the
                        // tic rate must not: a pistol fires on tics, and tying tics
                        // to frames made it fire slower at large window sizes while
                        // the frame counter still looked healthy.
                        var now = clock.NowSeconds;
                        var elapsed = started ? now - lastFrameTime : 1.0 / pacer.TargetFps;

                        lastFrameTime = now;
                        started = true;

                        var step = pacer.AdvanceBy(elapsed);

                        for (var tic = 0; tic < step.UpdateCount && !finished; tic++)
                        {
                            // Slow motion skips simulation tics, which is only
                            // meaningful in single player.
                            if (DebugSettings.SlowMotion <= 1 ||
                                (slowCounter++ % DebugSettings.SlowMotion) == 0)
                            {
                                finished = doom.Update() == UpdateResult.Completed;
                            }
                        }

                        if (finished)
                        {
                            break;
                        }

                        if (video.Window.ShowOverlay)
                        {
                            video.Window.OverlayText = BuildOverlay(step);
                        }

                        // The in-game counter reads this. Filled here, before the
                        // render, so the number belongs to the frame it is drawn on
                        // rather than to the one before it.
                        stats.Fps = clock.CurrentFps;
                        stats.TicsThisFrame = step.UpdateCount;
                        stats.RenderMs = video.LastRenderMs;
                        stats.PresentMs = video.LastPresentMs;
                        stats.Fraction = step.Fraction.ToDouble();
                        stats.TicsPerSecond = clock.TicsPerSecond;

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

                if (args.profile.Present && video.Profile != null)
                {
                    video.Profile.WriteSummary(Console.Out);
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
