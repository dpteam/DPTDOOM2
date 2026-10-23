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
using System.Threading;
using DPTDOOM.Net;

namespace DPTDOOM.Headless
{
    /// <summary>
    /// Runs the game without a window, sound or input. This is the mode used by
    /// automated tests, demo benchmarks, frame captures and network soak tests:
    ///   dptdoom -headless -iwad doom2.wad -warp 1 -tics 70 -shot frame.bmp
    ///   dptdoom -headless -iwad doom2.wad -net host -players 2 -tics 350
    /// </summary>
    internal static class HeadlessDoom
    {
        public static int Run(CommandLineArgs args)
        {
            var config = ConfigUtilities.GetConfig();
            config.video_fpsscale = 1;

            using (var content = new GameContent(args))
            {
                var video = new HeadlessVideo(config, content);

                NetSession session = null;
                NetDriver driver = null;

                if (args.net.Present)
                {
                    session = NetStartup.Create(args, content, (title, lines) =>
                    {
                        Console.WriteLine("Net: " + title + " (" + string.Join("; ", lines) + ")");
                    });
                    driver = new NetDriver(session);
                    driver.DeterministicInput = args.botinput.Present;
                }

                try
                {
                    var doom = new Doom(args, config, content, video, null, null, null, driver);

                    var tics = 0;
                    var limit = args.tics.Present ? args.tics.Value : 0;
                    var faulted = false;

                    while (true)
                    {
                        if (doom.Update() == UpdateResult.Completed)
                        {
                            break;
                        }

                        tics++;

                        var simulated = driver != null ? driver.CurrentTic : tics;

                        if (limit > 0 && simulated >= limit)
                        {
                            break;
                        }

                        // Safety net for a game that never ends without input.
                        if (tics >= 10000000)
                        {
                            break;
                        }

                        if (driver != null)
                        {
                            if (driver.Finished)
                            {
                                break;
                            }

                            if (driver.Faulted)
                            {
                                faulted = true;
                                break;
                            }

                            // The lockstep loop idles while waiting for peers.
                            Thread.Sleep(1);
                        }
                    }

                    if (args.shot.Present)
                    {
                        video.Render(doom, Fixed.One);
                        video.SaveBmp(args.shot.Value);
                        Console.WriteLine("Wrote frame: " + args.shot.Value);
                    }

                    Console.WriteLine("Tics: " + (driver != null ? driver.CurrentTic : tics));

                    if (doom.Game != null && doom.Game.World != null)
                    {
                        for (var i = 0; i < Player.MaxPlayerCount; i++)
                        {
                            var p = doom.Game.World.Options.Players[i];
                            if (p.InGame && p.Mobj != null)
                            {
                                Console.WriteLine("  player " + (i + 1) +
                                    " pos=" + p.Mobj.X.ToFloat().ToString("0") + "," + p.Mobj.Y.ToFloat().ToString("0") +
                                    " health=" + p.Health);
                            }
                        }
                    }

                    if (doom.Game != null && doom.Game.World != null)
                    {
                        Console.WriteLine(
                            "State: mobj=" + DoomDebug.GetMobjHash(doom.Game.World).ToString("x8") +
                            " sector=" + DoomDebug.GetSectorHash(doom.Game.World).ToString("x8"));
                    }

                    if (doom.QuitMessage != null)
                    {
                        Console.WriteLine(doom.QuitMessage);
                    }

                    if (driver != null)
                    {
                        session.Finish();
                    }

                    if (faulted)
                    {
                        Console.WriteLine("Network: " + driver.FaultReason);
                        return 2;
                    }
                }
                finally
                {
                    if (driver != null)
                    {
                        driver.Dispose();
                    }
                }
            }

            return 0;
        }
    }
}
