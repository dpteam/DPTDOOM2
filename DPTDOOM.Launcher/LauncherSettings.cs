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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DPTDOOM.Shared;

namespace DPTDOOM.Launcher
{
    /// <summary>
    /// The complete launcher form state. It is serialized with the same
    /// newline separated encoding the HTML page uses, so the page and the host
    /// never drift apart.
    /// </summary>
    internal sealed class LauncherState
    {
        public const char ListSeparator = '\u001f';

        public string Executable = "";
        public string Iwad = "";
        public int Map = 1;
        public int Skill = 3;
        public string Address = "127.0.0.1:5029";
        public int Port = 5029;
        public string Name = "";

        public bool NoMonsters;
        public bool Fast;
        public bool Respawn;
        public bool Deathmatch;

        /// <summary>Players pass through each other instead of blocking.</summary>
        public bool NoBlock;

        /// <summary>Spawn the duplicate weapons deathmatch maps scatter.</summary>
        public bool CoopWeapons;

        /// <summary>Each player has to find keys for themselves.</summary>
        public bool NoSharedKeys;

        public int Mode; // 0 = single, 1 = host, 2 = join

        public List<string> Pwads = new List<string>();

        public int Slot = 1;

        /// <summary>How many local co-op bots join when Mode is 3.</summary>
        public int CoopBots = 3;

        /// <summary>Master used for the relay. One address for listing and relay.</summary>
        public string Master = "";

        /// <summary>Public session id a guest types in, 32 hex characters.</summary>
        public string RelaySession = "";

        /// <summary>Host through the master's relay instead of a port forward.</summary>
        public bool Relay;

        /// <summary>
        /// Players in the session, host included. The engine is fixed at four:
        /// Player arrays, the demo format and the intermission all have four
        /// slots, so a larger number here would not be a bigger game.
        /// </summary>
        public int Players = 2;

        /// <summary>
        /// Slots filled by bots when hosting. They take the last slots, so guests
        /// join the ones before them; the host drives them and their commands
        /// travel over the wire like any player's.
        /// </summary>
        public int HostBots;

        /// <summary>
        /// The session password, or empty for an open game.
        ///
        /// Stored with the profile rather than asked every time: people host the same
        /// game for the same friends, and a password nobody can remember is a password
        /// that ends up in the chat, which is where it stops being one.
        /// </summary>
        public string Password = "";

        /// <summary>
        /// Where the host's ban list is, or empty for none.
        ///
        /// A path rather than a list in the settings: the file format is shared with the
        /// master, so an operator keeps one file beside the game instead of retyping
        /// rules into a dialog that cannot show comments or expiry.
        /// </summary>
        public string BanList = "";

        /// <summary>
        /// Where the host appends who joined, who left and who was refused, or empty.
        /// </summary>
        public string NetLog = "";

        public string Encode()
        {
            var flags = 0;
            if (NoMonsters) flags |= 1;
            if (Fast) flags |= 2;
            if (Respawn) flags |= 4;
            if (Deathmatch) flags |= 8;
            if (NoBlock) flags |= 16;
            if (CoopWeapons) flags |= 32;
            if (NoSharedKeys) flags |= 64;

            return string.Join("\n", new[]
            {
                Executable,
                Iwad,
                Map.ToString(CultureInfo.InvariantCulture),
                Skill.ToString(CultureInfo.InvariantCulture),
                Address,
                Port.ToString(CultureInfo.InvariantCulture),
                Name,
                flags.ToString(CultureInfo.InvariantCulture),
                Mode.ToString(CultureInfo.InvariantCulture),
                string.Join(ListSeparator.ToString(), Pwads.ToArray()),
                Slot.ToString(CultureInfo.InvariantCulture),
                CoopBots.ToString(CultureInfo.InvariantCulture),
                Master,
                RelaySession,
                Relay ? "1" : "0",
                Players.ToString(CultureInfo.InvariantCulture),
                HostBots.ToString(CultureInfo.InvariantCulture),
                Password ?? "",
                BanList ?? "",
                NetLog ?? ""
            });
        }

        public static LauncherState Decode(string payload)
        {
            var state = new LauncherState();

            if (string.IsNullOrEmpty(payload))
            {
                return state;
            }

            var fields = payload.Split('\n');

            state.Executable = Field(fields, 0);
            state.Iwad = Field(fields, 1);
            state.Map = ParseInt(Field(fields, 2), 1, 32, 1);
            state.Skill = ParseInt(Field(fields, 3), 1, 5, 3);
            state.Address = Field(fields, 4);
            state.Port = ParseInt(Field(fields, 5), 1024, 65535, 5029);
            state.Name = Field(fields, 6);

            var flags = ParseInt(Field(fields, 7), 0, 127, 0);
            state.NoMonsters = (flags & 1) != 0;
            state.Fast = (flags & 2) != 0;
            state.Respawn = (flags & 4) != 0;
            state.Deathmatch = (flags & 8) != 0;
            state.NoBlock = (flags & 16) != 0;
            state.CoopWeapons = (flags & 32) != 0;
            state.NoSharedKeys = (flags & 64) != 0;

            state.Mode = ParseInt(Field(fields, 8), 0, 3, 0);

            var list = Field(fields, 9);
            if (list.Length > 0)
            {
                foreach (var item in list.Split(ListSeparator))
                {
                    if (item.Trim().Length > 0)
                    {
                        state.Pwads.Add(item);
                    }
                }
            }

            state.Slot = ParseInt(Field(fields, 10), 1, 3, 1);
            state.CoopBots = ParseInt(Field(fields, 11), 1, 3, 3);
            state.Master = Field(fields, 12).Trim();
            state.RelaySession = Field(fields, 13).Trim();
            state.Relay = Field(fields, 14).Trim() == "1";
            state.Players = ParseInt(Field(fields, 15), 2, 4, 2);
            state.HostBots = ParseInt(Field(fields, 16), 0, 3, 0);
            state.Password = Field(fields, 17);
            state.BanList = Field(fields, 18);
            state.NetLog = Field(fields, 19);

            return state;
        }

        private static string Field(string[] fields, int index)
        {
            return index < fields.Length ? fields[index] : "";
        }

        private static int ParseInt(string text, int min, int max, int fallback)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return fallback;
            }

            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    /// <summary>
    /// Settings file handling. Everything lives next to the executable when
    /// portable.txt is present, otherwise in %LOCALAPPDATA%\DPTDOOM.
    /// </summary>
    internal sealed class LauncherSettings
    {
        public string Theme = "dark";
        public string Master = "play.dartpower.xyz:25135";
        public LauncherState State = new LauncherState();

        /// <summary>
        /// A settings file written before the relay existed carries no master in
        /// its state, and an empty field would silently disable the relay. Seed
        /// it once from the saved master so an upgrade keeps working without the
        /// user retyping an address that never changed.
        /// </summary>
        private void SeedRelayDefaults()
        {
            if (string.IsNullOrEmpty(State.Master))
            {
                State.Master = Master;
            }
        }

        public static string GetDataDirectory()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;

            if (File.Exists(Path.Combine(exeDirectory, "portable.txt")))
            {
                var user = Path.Combine(exeDirectory, "user");
                if (!Directory.Exists(user))
                {
                    Directory.CreateDirectory(user);
                }

                return user;
            }

            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            if (string.IsNullOrEmpty(root))
            {
                root = exeDirectory;
            }

            var directory = Path.Combine(root, "DPTDOOM");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return directory;
        }

        public static bool IsPortable
        {
            get { return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portable.txt")); }
        }

        private static string GetPath()
        {
            return Path.Combine(GetDataDirectory(), "launcher.ini");
        }

        public static LauncherSettings Load()
        {
            var settings = new LauncherSettings();
            var path = GetPath();

            if (!File.Exists(path))
            {
                settings.SeedRelayDefaults();
                return settings;
            }

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();

                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                {
                    continue;
                }

                var split = line.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, split).Trim().ToLowerInvariant();
                var value = line.Substring(split + 1).Trim();

                switch (key)
                {
                    case "theme":
                        settings.Theme = value == "light" ? "light" : "dark";
                        break;

                    case "master":
                        settings.Master = value;
                        break;

                    case "state":
                        settings.State = LauncherState.Decode(Unescape(value));
                        break;
                }
            }

            settings.SeedRelayDefaults();
            return settings;
        }

        public void Save()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# " + Branding.LauncherName + " settings");
            builder.AppendLine("theme=" + Theme);
            builder.AppendLine("master=" + Master);
            builder.AppendLine("state=" + Escape(State.Encode()));

            try
            {
                File.WriteAllText(GetPath(), builder.ToString());
            }
            catch
            {
            }
        }

        private static string GetProfilePath(int slot)
        {
            return Path.Combine(GetDataDirectory(), "profile" + slot + ".ini");
        }

        public void SaveProfile(int slot)
        {
            try
            {
                File.WriteAllText(GetProfilePath(slot), "state=" + Escape(State.Encode()) + Environment.NewLine);
            }
            catch
            {
            }
        }

        public bool LoadProfile(int slot)
        {
            var path = GetProfilePath(slot);
            if (!File.Exists(path))
            {
                return false;
            }

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                var split = line.IndexOf('=');

                if (split > 0 && line.Substring(0, split).Trim().ToLowerInvariant() == "state")
                {
                    var state = LauncherState.Decode(Unescape(line.Substring(split + 1).Trim()));
                    state.Slot = slot;
                    State = state;
                    return true;
                }
            }

            return false;
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string Unescape(string value)
        {
            var builder = new StringBuilder(value.Length);
            var escaped = false;

            foreach (var ch in value)
            {
                if (escaped)
                {
                    builder.Append(ch == 'n' ? '\n' : ch);
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }
    }
}
