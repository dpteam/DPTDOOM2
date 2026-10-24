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
using DPTDOOM.Headless;
using DPTDOOM.Win32;

namespace DPTDOOM
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                Console.Title = ApplicationInfo.Title;
            }
            catch
            {
            }

            // Checked before the banner, because the banner is what the flag turns
            // off and the flag is one of the arguments.
            foreach (var arg in args)
            {
                if (string.Equals(arg, "-nobanner", StringComparison.OrdinalIgnoreCase))
                {
                    ConsoleUi.Disable();
                }
            }

            ConsoleUi.Banner();

            try
            {
                var cmd = new CommandLineArgs(args);

                if (cmd.userdir.Present)
                {
                    ConfigUtilities.OverrideDataDirectory = cmd.userdir.Value;
                }

                if (cmd.version.Present)
                {
                    Console.WriteLine(ApplicationInfo.Title);
                    return 0;
                }

                DebugSettings.Wireframe = cmd.wireframe.Present;
                DebugSettings.FreezeMonsters = cmd.freeze.Present;
                DebugSettings.MapOnly = cmd.maponly.Present;
                DebugSettings.ShowTriggers = cmd.showtriggers.Present;
                DebugSettings.ShowBlockmap = cmd.showblockmap.Present;

                if (cmd.headless.Present)
                {
                    if (cmd.soundtest.Present)
                    {
                        using (var content = new GameContent(cmd))
                        {
                            var requested = cmd.sfx.Present
                                ? cmd.sfx.Value.Split(',')
                                : null;

                            return SoundTest.Run(content.Wad, cmd.soundtest.Value, requested);
                        }
                    }

                    return HeadlessDoom.Run(cmd);
                }

                string quitMessage;

                using (var app = new Win32Doom(cmd))
                {
                    app.Run();
                    quitMessage = app.QuitMessage;
                }

                if (quitMessage != null)
                {
                    ConsoleUi.Closing(quitMessage);
                    WaitForKey();
                }

                return 0;
            }
            catch (OperationCanceledException)
            {
                // The player closed the window while the game was still connecting. That is
                // a decision, not a failure: no error text, no key to press.
                ConsoleUi.Closing("closed");
                return 0;
            }
            catch (Exception e)
            {
                ConsoleUi.Closing("failed");

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e);
                Console.ResetColor();
                WaitForKey();
                return 1;
            }
        }

        private static void WaitForKey()
        {
            // The launcher starts the game and waits for the process to end. Holding the
            // process here means the launcher keeps reporting "game running" long after the
            // window is gone, and the only way out is to close the console by hand - which
            // then looks to the launcher like the game died. So when the launcher started us,
            // there is nobody to press a key for: exit.
            if (Environment.GetEnvironmentVariable("DPTDOOM_LAUNCHED") == "1")
            {
                return;
            }

            try
            {
                // Bounded, because an unattended process is worse than a lost message: a
                // game started by a script, by a desktop shortcut, or by a player who walked
                // away must still end.
                Console.Write("Press any key to exit, or wait " + PauseSeconds + " seconds. ");

                var deadline = Environment.TickCount + PauseSeconds * 1000;

                while (unchecked(Environment.TickCount - deadline) < 0)
                {
                    if (Console.KeyAvailable)
                    {
                        Console.ReadKey(true);
                        return;
                    }

                    System.Threading.Thread.Sleep(50);
                }

                Console.WriteLine();
            }
            catch
            {
                // No console, or the input is redirected: there is nobody to press a key.
            }
        }

        /// <summary>How long a closing message stays readable before the process ends.</summary>
        private const int PauseSeconds = 15;
    }
}
