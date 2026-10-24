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
        /// <summary>
        /// Prints whatever the in-game console has gained since the last call.
        ///
        /// There is no console panel in a headless run, so "-consolecmd" would
        /// otherwise run and leave no trace at all - which is what makes a console
        /// command impossible to test from a script. Output goes to stdout, where the
        /// other diagnostics already are.
        /// </summary>
        private static void PrintConsoleOutput(Doom doom, ref int printed)
        {
            var console = doom.ConsoleOverlay;

            if (console == null || console.LineCount <= printed)
            {
                return;
            }

            for (var i = printed; i < console.LineCount; i++)
            {
                var line = console.Lines[i];

                var prefix = line.Kind == ConsoleLineKind.Error ? "! "
                           : line.Kind == ConsoleLineKind.Echo ? "> "
                           : line.Kind == ConsoleLineKind.Hint ? "- "
                           : "  ";

                Console.WriteLine(prefix + line.Text);
            }

            printed = console.LineCount;
        }

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

        if (args.profile.Present && video.Profile != null)
        {
            video.Profile.Enabled = true;
        }

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
                    var stateAt = args.stateat.Present ? args.stateat.Value : 0;
                    var stateAtPrinted = false;
                    var faulted = false;
                    var lastMap = 0;
                    var levelTics = 0;
                    var printedConsoleLines = 0;

                    // Progress watch. A paused game never advances its level time, so
                    // a "-tics 200" run would spin until the safety net at ten million
                    // updates - which it did, in 1.4 seconds, and then reported
                    // "Tics: 10000000" as if that meant something. Any genuine stall
                    // deserves the same treatment: stop and say so.
                    var lastSimulated = -1;
                    var stalledUpdates = 0;

                    while (true)
                    {
                        if (doom.Update() == UpdateResult.Completed)
                        {
                            break;
                        }

                        // Console output is printed to stdout as well as into the
                        // overlay. There is no overlay here, so without this a
                        // "-consolecmd" would run and leave no trace - which is exactly
                        // what makes a console command impossible to test.
                        PrintConsoleOutput(doom, ref printedConsoleLines);

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

                        // Checked before the limit, so the reported count is the tic
                        // the run actually reached rather than the one before it.
                        if (simulated == lastSimulated)
                        {
                            // A network session holding the game for a peer is not a
                            // stall: the tic cannot advance until the peer's command
                            // arrives, and that is the design rather than a fault. The
                            // grace period is thirty seconds, which is several thousand
                            // updates, so counting them stopped a legitimate run with
                            // "simulation is not advancing (paused?)" - a wrong answer
                            // that looked like a stall.
                            if (driver == null || !driver.Reconnecting)
                            {
                                stalledUpdates++;
                            }
                        }
                        else
                        {
                            stalledUpdates = 0;
                        }

                        lastSimulated = simulated;

                        // A hash at a named tic, so two processes that stop at
                        // different moments can still be compared.
                        //
                        // This exists because of a real failure: the host reaches its
                        // -tics limit and quits while the guest is still a tic or two
                        // behind, so the guest stops early with "the other player left
                        // the game" and the final State lines describe different tics.
                        // The comparison then reports a divergence that is not one. The
                        // deeper the input buffer, the wider that gap - which is to say
                        // the failure got more likely exactly when the buffer learned to
                        // grow.
                        if (stateAt > 0 && simulated >= stateAt && !stateAtPrinted &&
                            doom.Game != null && doom.Game.World != null)
                        {
                            stateAtPrinted = true;

                            Console.WriteLine(
                                "State@" + stateAt + ": mobj=" +
                                DoomDebug.GetMobjHash(doom.Game.World).ToString("x8") +
                                " sector=" +
                                DoomDebug.GetSectorHash(doom.Game.World).ToString("x8"));
                        }

                        if (limit > 0 && simulated >= limit)
                        {
                            break;
                        }

                        // A paused game stops advancing the level time, which is the
                        // point of a pause - but a headless run asked for tics, and if
                        // it cannot get them it should say so instead of spinning.
                        if (stalledUpdates > 5000)
                        {
                            Console.WriteLine("Simulation is not advancing (paused?); stopping after " +
                                              simulated + " tics.");
                            break;
                        }

                        // Safety net for a game that never ends without input.
                        if (tics >= 10000000)
                        {
                            break;
                        }

                        // A profile needs frames to measure, and headless renders
                        // none unless asked: drawing every frame would slow every
                        // determinism run for a number nobody asked for. Opt-in, so
                        // the numbers come from a real draw of a real world.
                        if (args.profile.Present && doom.Game != null && doom.Game.World != null)
                        {
                            video.Render(doom, Fixed.One);
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

                    if (args.profile.Present && video.Profile != null)
                    {
                        video.Profile.WriteSummary(Console.Out);
                    }

                    doom.FlushBotTrace();

                    // The number of tics that were actually simulated, not the number of loop
                    // iterations. They differ when the run stops early - a paused game,
                    // or the safety net - and reporting the iterations made a stalled
                    // run look like it had simulated five thousand tics.
                    Console.WriteLine("Tics: " + (driver != null ? driver.CurrentTic : Math.Max(0, lastSimulated)));

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
