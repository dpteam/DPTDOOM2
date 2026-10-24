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

            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine(ApplicationInfo.Title);
            Console.ResetColor();

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
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(quitMessage);
                    Console.ResetColor();
                    WaitForKey();
                }

                return 0;
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e);
                Console.ResetColor();
                WaitForKey();
                return 1;
            }
        }

        private static void WaitForKey()
        {
            try
            {
                Console.Write("Press any key to exit.");
                Console.ReadKey();
            }
            catch
            {
            }
        }
    }
}
