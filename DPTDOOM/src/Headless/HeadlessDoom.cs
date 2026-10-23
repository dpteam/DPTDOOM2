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
        /// <summary>
        /// Game tics simulated so far. Falls back to the update counter while the
        /// world does not exist yet (opening, menus, demo loading), so a run that
        /// never reaches a level still terminates.
        /// </summary>
        private static int GameTics(Doom doom, int updateCount)
        {
            var game = doom.Game;

            if (game != null && game.World != null)
            {
                return game.World.LevelTime;
            }

            return updateCount;
        }

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
                    var lastMap = 0;
                    var levelTics = 0;

                    while (true)
                    {
                        if (doom.Update() == UpdateResult.Completed)
                        {
                            break;
                        }

                        tics++;

                        // -tics means simulated game tics, not calls to Update.
                        //
                        // It used to count Update calls, and the level-start wipe
                        // consumes a variable number of them: WipeEffect seeds its
                        // column heights from DateTime.Now.Millisecond, so it takes
                        // anywhere from a few to forty updates to finish. The result
                        // was that "-tics 70" produced a different world every run —
                        // level time 28, 29, 32, 33, 35 across five launches of the
                        // same command — and any hash comparison was meaningless.
                        var simulated = driver != null
                            ? driver.CurrentTic
                            : GameTics(doom, tics);

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

                        // Report a level transition as it happens.
                        //
                        // Without this the only sign that a bot finished a map is
                        // a second "Load map" line, and the tic it took has to be
                        // guessed by bisecting -tics across several runs. The bot
                        // exists to finish levels, so the one number that measures
                        // it should be in the output.
                        //
                        // The elapsed count comes from World.LevelTime, which is
                        // the tic counter of the level itself. Counting loop
                        // iterations instead gave different answers for the same
                        // run depending on -tics, because the wipe and the
                        // intermission consume iterations that are not tics.
                        if (doom.Game != null && doom.Game.World != null)
                        {
                            var world = doom.Game.World;
                            var currentMap = doom.Options.Map;

                            if (currentMap != lastMap)
                            {
                                if (lastMap > 0)
                                {
                                    Console.WriteLine("Level MAP" + lastMap.ToString("00") +
                                                      " finished in " + levelTics + " tics (" +
                                                      (levelTics / 35) + "s)");
                                }

                                lastMap = currentMap;
                                levelTics = 0;
                            }

                            if (world.LevelTime > levelTics)
                            {
                                levelTics = world.LevelTime;
                            }
                        }
                    }

                    if (args.shot.Present)
                    {
                        video.Render(doom, Fixed.One);
                        video.SaveBmp(args.shot.Value);
                        Console.WriteLine("Wrote frame: " + args.shot.Value);
                    }

                    doom.FlushBotTrace();

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
                            " sector=" + DoomDebug.GetSectorHash(doom.Game.World).ToString("x8") +
                            " tic=" + doom.Game.World.LevelTime);
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
