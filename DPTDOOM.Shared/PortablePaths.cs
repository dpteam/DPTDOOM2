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
using System.IO;

namespace DPTDOOM.Shared
{
    /// <summary>
    /// Shared helper for the "portable.txt next to the executable" convention
    /// used by the game, the launcher and the master server.
    /// </summary>
    public static class PortablePaths
    {
        public static string GetExeDirectory()
        {
            var path = System.Reflection.Assembly.GetEntryAssembly().Location;
            return Path.GetDirectoryName(path);
        }

        /// <summary>
        /// Returns the folder for user data. If a file named portable.txt sits
        /// next to the executable, everything lives in a "user" folder there;
        /// otherwise the executable directory itself is used.
        /// </summary>
        public static string GetDataDirectory()
        {
            var exeDirectory = GetExeDirectory();

            if (File.Exists(Path.Combine(exeDirectory, "portable.txt")))
            {
                var user = Path.Combine(exeDirectory, "user");
                if (!Directory.Exists(user))
                {
                    Directory.CreateDirectory(user);
                }

                return user;
            }

            return exeDirectory;
        }
    }
}
