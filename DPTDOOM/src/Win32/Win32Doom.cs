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
using System.Threading;
using DPTDOOM.Audio;
using DPTDOOM.Net;
using DPTDOOM.UserInput;

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
            var targetFps = 35.0 * fpsScale;
            var frameTime = 1.0 / targetFps;

            var stopwatch = Stopwatch.StartNew();
            var nextFrame = 0.0;
            var frameCount = 0;

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

                    if (frameCount % fpsScale == 0)
                    {
                        if (doom.Update() == UpdateResult.Completed)
                        {
                            break;
                        }
                    }

                    frameCount++;

                    if (args.timedemo.Present)
                    {
                        // Benchmark mode: run as fast as possible, no drawing.
                        continue;
                    }

                    var frameFrac = Fixed.FromInt(frameCount % fpsScale + 1) / fpsScale;
                    video.Render(doom, frameFrac);

                    nextFrame += frameTime;
                    var wait = nextFrame - stopwatch.Elapsed.TotalSeconds;
                    if (wait > 0)
                    {
                        Thread.Sleep((int)(wait * 1000));
                    }
                    else
                    {
                        // We are behind; do not accumulate a sleep debt.
                        nextFrame = stopwatch.Elapsed.TotalSeconds;
                    }
                }
            }
            finally
            {
                NativeMethods.timeEndPeriod(1);
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
