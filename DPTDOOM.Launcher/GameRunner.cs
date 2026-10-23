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
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DPTDOOM.Launcher
{
    /// <summary>
    /// Turns launcher state into a command line and starts the game. No shell
    /// is involved: the executable is started directly.
    /// </summary>
    internal static class GameRunner
    {
        public static string FindGameExe(LauncherState state)
        {
            if (!string.IsNullOrEmpty(state.Executable) && File.Exists(state.Executable))
            {
                return state.Executable;
            }

            var directory = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(directory, "dptdoom.exe"),
                Path.Combine(directory, "DPTDOOM.exe"),
                Path.Combine(directory, "..", "DPTDOOM", "bin", "Release", "net40", "dptdoom.exe"),
                Path.Combine(directory, "..", "..", "..", "DPTDOOM", "bin", "Release", "net40", "dptdoom.exe"),
                Path.Combine(directory, "..", "..", "..", "..", "DPTDOOM", "bin", "Release", "net40", "dptdoom.exe")
            };

            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }

            return null;
        }

        public static string BuildArguments(LauncherState state)
        {
            var builder = new StringBuilder();

            Append(builder, "-iwad", state.Iwad);

            foreach (var pwad in state.Pwads)
            {
                Append(builder, "-file", pwad);
            }

            Append(builder, "-skill", state.Skill.ToString());

            if (IsDoomOne(state.Iwad))
            {
                Append(builder, "-warp", "1 " + state.Map);
            }
            else
            {
                Append(builder, "-warp", state.Map.ToString());
            }

            if (state.Deathmatch)
            {
                builder.Append("-deathmatch ");
            }

            if (state.NoBlock)
            {
                builder.Append("-noblock ");
            }

            if (state.CoopWeapons)
            {
                builder.Append("-coopweapons ");
            }

            if (state.NoSharedKeys)
            {
                builder.Append("-nosharedkeys ");
            }

            if (state.Fast)
            {
                builder.Append("-fast ");
            }

            if (state.Respawn)
            {
                builder.Append("-respawn ");
            }

            if (state.NoMonsters)
            {
                builder.Append("-nomonsters ");
            }

            Append(builder, "-userdir", GetProfileDirectory(state.Slot));

            if (!string.IsNullOrEmpty(state.Name))
            {
                Append(builder, "-name", state.Name);
            }

            if (state.Mode == 1)
            {
                builder.Append("-net host ");
                Append(builder, "-players", state.Players.ToString());
                Append(builder, "-port", state.Port.ToString());

                if (state.Relay && !string.IsNullOrEmpty(state.Master))
                {
                    // The host registers itself, so the field does not need a
                    // port forward: both ends connect out to the master.
                    Append(builder, "-master", state.Master);
                    builder.Append("-relay ");
                }
            }
            else if (state.Mode == 2)
            {
                builder.Append("-net join ");

                var relaySession = (state.RelaySession ?? "").Trim();

                if (!string.IsNullOrEmpty(state.Address) || relaySession.Length == 0)
                {
                    Append(builder, "-address", JoinAddress(state));
                }

                if (relaySession.Length > 0 && !string.IsNullOrEmpty(state.Master))
                {
                    // Direct first, relay second: the game itself falls back, so
                    // a LAN game stays direct and only an unreachable address
                    // costs the extra round trip.
                    Append(builder, "-relay-master", state.Master);
                    Append(builder, "-relay-session", relaySession);
                }
            }
            else if (state.Mode == 3)
            {
                // Local co-op: the human plus bots in the same world.
                var bots = state.CoopBots;
                if (bots < 1) bots = 1;
                else if (bots > 3) bots = 3;

                Append(builder, "-coopbots", bots.ToString());
            }

            return builder.ToString().TrimEnd();
        }

        private static string JoinAddress(LauncherState state)
        {
            var address = (state.Address ?? "").Trim();
            if (address.Length == 0)
            {
                address = "127.0.0.1";
            }

            if (address.IndexOf(':') < 0)
            {
                address = address + ":" + state.Port;
            }

            return address;
        }

        public static string GetProfileDirectory(int slot)
        {
            var directory = Path.Combine(
                LauncherSettings.GetDataDirectory(),
                "game",
                "profile" + slot);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return directory;
        }

        public static string GetSessionDirectory()
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var directory = Path.Combine(LauncherSettings.GetDataDirectory(), "sessions", stamp);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return directory;
        }

        public static Process Start(LauncherState state, string logPath)
        {
            var exe = FindGameExe(state);
            if (exe == null)
            {
                throw new Exception("dptdoom.exe не найден рядом с лаунчером.");
            }

            if (string.IsNullOrEmpty(state.Iwad) || !File.Exists(state.Iwad))
            {
                throw new Exception("Сначала выбери IWAD.");
            }

            foreach (var pwad in state.Pwads)
            {
                if (!File.Exists(pwad))
                {
                    throw new Exception("PWAD не найден: " + pwad);
                }
            }

            var startInfo = new ProcessStartInfo();
            startInfo.FileName = exe;
            startInfo.Arguments = BuildArguments(state);
            startInfo.WorkingDirectory = Path.GetDirectoryName(exe);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = false;

            if (!string.IsNullOrEmpty(logPath))
            {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
            }

            var process = Process.Start(startInfo);

            if (!string.IsNullOrEmpty(logPath))
            {
                var writer = new StreamWriter(logPath, false, Encoding.UTF8);
                writer.AutoFlush = true;

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        try { writer.WriteLine(e.Data); } catch { }
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        try { writer.WriteLine(e.Data); } catch { }
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.Exited += (sender, e) =>
                {
                    try { writer.Dispose(); } catch { }
                };

                process.EnableRaisingEvents = true;
            }

            return process;
        }

        public static bool IsDoomOne(string iwad)
        {
            if (string.IsNullOrEmpty(iwad))
            {
                return false;
            }

            var name = Path.GetFileName(iwad).ToUpperInvariant();
            return name == "DOOM.WAD" || name == "DOOM1.WAD" ||
                   name == "FREEDOOM1.WAD" || name == "FREEDOOM.WAD";
        }

        public static List<string> FindIwads(IEnumerable<string> searchDirectories)
        {
            var names = new[]
            {
                "DOOM2.WAD", "PLUTONIA.WAD", "TNT.WAD", "DOOM.WAD",
                "DOOM1.WAD", "FREEDOOM2.WAD", "FREEDOOM1.WAD"
            };

            var found = new List<string>();

            foreach (var directory in searchDirectories)
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var name in names)
                {
                    var path = Path.Combine(directory, name);
                    if (File.Exists(path) && !found.Contains(path))
                    {
                        found.Add(path);
                    }
                }
            }

            return found;
        }

        private static void Append(StringBuilder builder, string name, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            builder.Append(name);
            builder.Append(' ');
            builder.Append(Quote(value));
            builder.Append(' ');
        }

        private static string Quote(string value)
        {
            if (value.IndexOf(' ') < 0 && value.IndexOf('"') < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
