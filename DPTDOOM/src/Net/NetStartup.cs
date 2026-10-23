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

namespace DPTDOOM.Net
{
    /// <summary>
    /// Builds a <see cref="NetSession"/> from the command line and waits until
    /// the session is complete. Kept apart from the window code so the headless
    /// build can host and join sessions for automated tests.
    /// </summary>
    public static class NetStartup
    {
        public delegate void StatusHandler(string title, string[] lines);

        public static NetSession Create(CommandLineArgs args, GameContent content, StatusHandler status)
        {
            var port = args.port.Present ? args.port.Value : NetProtocol.DefaultPort;
            var name = args.name.Present ? args.name.Value : Environment.MachineName;
            var mode = args.net.Value.ToLower();

            if (mode == "host")
            {
                var players = args.players.Present ? args.players.Value : 2;
                var settings = BuildSettings(args, content);

                status("HOSTING", new[]
                {
                    "PORT " + port,
                    "PLAYERS " + players,
                    "WAITING FOR PLAYER " + 1 + "/" + (players - 1)
                });

                var session = NetSession.StartHost(port, players, name, settings);

                var deadline = DateTime.Now.AddSeconds(300);
                while (session.PlayerCount < session.ExpectedPlayers)
                {
                    if (session.Faulted)
                    {
                        session.Dispose();
                        throw new Exception("Network: " + session.FaultReason);
                    }

                    if (DateTime.Now > deadline)
                    {
                        session.Dispose();
                        throw new Exception("Timed out waiting for players.");
                    }

                    status("HOSTING", new[]
                    {
                        "PORT " + port,
                        "PLAYERS " + session.PlayerCount + "/" + session.ExpectedPlayers,
                        "WAITING FOR PLAYER " + session.PlayerCount + "/" + (session.ExpectedPlayers - 1)
                    });

                    Thread.Sleep(100);
                }

                status("STARTING", new[] { session.PlayerCount + " PLAYERS", "GET READY" });

                return session;
            }

            if (mode == "join")
            {
                if (!args.address.Present)
                {
                    throw new Exception("-net join requires -address <host>[:port].");
                }

                var address = args.address.Value;
                var colon = address.LastIndexOf(':');
                if (colon > 0)
                {
                    int parsedPort;
                    if (int.TryParse(address.Substring(colon + 1), out parsedPort))
                    {
                        port = parsedPort;
                        address = address.Substring(0, colon);
                    }
                }

                status("CONNECTING", new[] { address.ToUpper() + ":" + port });

                NetSession session;
                try
                {
                    session = NetSession.Join(address, port, name, 10000);
                }
                catch (Exception e)
                {
                    status("CONNECTION FAILED", new[] { e.Message.ToUpper() });
                    Thread.Sleep(1500);
                    throw;
                }

                var localChecksum = WadChecksum.Compute(content.Wad);
                if (session.Settings.WadChecksum != localChecksum)
                {
                    session.Dispose();
                    throw new Exception(
                        "The host is running a different WAD set. " +
                        "Both players must use the same IWAD and PWADs.");
                }

                status("CONNECTED", new[]
                {
                    "PLAYER " + (session.LocalPlayer + 1) + " OF " + session.ExpectedPlayers,
                    "GET READY"
                });

                return session;
            }

            throw new Exception("Unknown -net mode '" + mode + "'. Use 'host' or 'join'.");
        }

        public static SessionSettings BuildSettings(CommandLineArgs args, GameContent content)
        {
            var settings = new SessionSettings();

            settings.GameMode = content.Wad.GameMode;
            settings.MissionPack = content.Wad.MissionPack;
            settings.WadChecksum = WadChecksum.Compute(content.Wad);

            if (args.warp.Present)
            {
                settings.Episode = args.warp.Value.Item1;
                settings.Map = args.warp.Value.Item2;
            }
            else if (args.episode.Present)
            {
                settings.Episode = args.episode.Value;
                settings.Map = 1;
            }

            if (args.skill.Present)
            {
                settings.Skill = (GameSkill)MathEx.Clamp(args.skill.Value - 1, 0, 4);
            }

            settings.Deathmatch = args.deathmatch.Present || args.altdeath.Present;
            settings.FastMonsters = args.fast.Present;
            settings.RespawnMonsters = args.respawn.Present;
            settings.NoMonsters = args.nomonsters.Present;

            return settings;
        }
    }
}
