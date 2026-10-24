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
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DPTDOOM.Shared;

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
                return Host(args, content, status, port, name);
            }

            if (mode == "join")
            {
                return Join(args, content, status, port, name);
            }

            throw new Exception("Unknown -net mode '" + mode + "'. Use 'host' or 'join'.");
        }

        private static NetSession Host(CommandLineArgs args, GameContent content, StatusHandler status, int port, string name)
        {
            var players = args.players.Present ? args.players.Value : 2;
            var settings = BuildSettings(args, content);

            // Bots fill the last slots, so a guest joining first lands on the
            // player right after the host rather than on a slot a bot drives.
            var bots = args.coopbots.Present
                ? MathEx.Clamp(args.coopbots.Value, 0, Player.MaxPlayerCount - 1)
                : 0;

            if (bots >= players)
            {
                throw new Exception("-coopbots " + bots + " leaves no slot for a guest with -players " +
                                    players + ". Raise -players.");
            }

            // Four or more players in co-op: let them pass through each other by
            // default. With two people blocking is the game; with four in a
            // corridor it is a jam, and the player at the back gets pushed out of
            // every lift. An explicit -noblock still wins for smaller sessions.
            if (players >= 4 && !args.noblock.Present)
            {
                settings.TeammatesBlock = false;
            }

            var relay = args.relay.Present;
            var delayOverride = args.netdelay.Present ? args.netdelay.Value : -1;

            // Validated here rather than at the first join: a password that was
            // mistyped into something unusable should stop the host now, while they
            // are looking at the screen, not produce a session nobody can enter.
            var password = ParsePassword(args);

            string masterHost = null;
            var masterPort = MasterProtocol.DefaultPort;

            if (args.master.Present)
            {
                if (!NetEndpoint.TryParse(args.master.Value, MasterProtocol.DefaultPort, out masterHost, out masterPort))
                {
                    throw new Exception("-master must be <host>[:port].");
                }
            }

            if (relay && masterHost == null)
            {
                // The relay credential is the listing lease, so there is nothing
                // to attach with until the session is registered.
                throw new Exception("-relay needs -master <host>[:port].");
            }

            var lines = new System.Collections.Generic.List<string>();
            lines.Add("PORT " + port);
            lines.Add("PLAYERS " + players);

            Presence presence = null;

            if (masterHost != null)
            {
                try
                {
                    presence = Presence.Register(
                        masterHost,
                        masterPort,
                        BuildPresenceInfo(settings, name, port),
                        6000);
                }
                catch (Exception e)
                {
                    if (relay)
                    {
                        // With relay requested the master is the only way in, so
                        // there is nothing useful to fall back to. Saying so now
                        // is better than hosting for five minutes and telling the
                        // player to wait for someone who can never arrive.
                        throw new Exception("Cannot list on the master " + masterHost + ":" + masterPort +
                                            ", and -relay needs it: " + e.Message);
                    }

                    // Listing was merely a convenience; the direct port still
                    // works, so a directory outage must not stop a LAN game.
                    Console.WriteLine("Net: could not list on the master (" + e.Message +
                                      "), hosting direct only.");
                }
            }

            if (presence != null)
            {
                Console.WriteLine("Net: listed on the master " + masterHost + ":" + masterPort +
                                  " as session " + presence.IdHex);

                lines.Add("SESSION " + presence.IdHex);
            }

            if (relay)
            {
                lines.Add("RELAY ON");
            }

            if (bots > 0)
            {
                lines.Add(bots + " BOT" + (bots == 1 ? "" : "S"));
            }

            if (delayOverride >= 0)
            {
                lines.Add("INPUT DELAY " + delayOverride + " TICS");
            }

            // Announced as a fact, never as a value: the status screen is photographed
            // for bug reports, and the console is redirected into log files by most of
            // the test scripts.
            if (!string.IsNullOrEmpty(password))
            {
                lines.Add("PASSWORD ON");
            }

            // Zero guests so far, and the slot count is what the loop below prints.
            lines.Add("WAITING FOR PLAYER 0/" + (players - bots - 1));

            status("HOSTING", lines.ToArray());

            var session = NetSession.StartHost(port, players, name, settings, delayOverride, password);

            if (presence != null)
            {
                session.AttachPresence(presence);
            }

            // Told before the wait, because the wait counts humans only: a bot
            // slot never connects, and waiting for it would hold the host at
            // "WAITING FOR PLAYER" for the full five minutes.
            session.AttachBots(bots);

            if (relay)
            {
                session.StartRelayHost(masterHost, masterPort, presence.Auth);
            }

            var deadline = DateTime.Now.AddSeconds(300);

            while (session.PlayerCount < session.HumansNeeded)
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

                // Counted as guests, not as players: PlayerCount includes the host,
                // so "1/1" was printed before anybody had joined and the bar was
                // already full. A progress bar that reads complete while the host is
                // still waiting is worse than no bar - it says the one thing that is
                // definitely not true.
                var guests = session.PlayerCount - 1;
                var guestSlots = session.HumansNeeded - 1;

                var waiting = new System.Collections.Generic.List<string>(lines);
                waiting[waiting.Count - 1] = "WAITING FOR PLAYER " + guests + "/" + guestSlots;

                status("HOSTING", waiting.ToArray());

                Thread.Sleep(100);
            }

            status("STARTING", new[]
            {
                session.PlayerCount + " PLAYERS" + (bots > 0 ? " + " + bots + " BOTS" : ""),
                "GET READY"
            });

            return session;
        }

        /// <summary>
        /// The password from the command line, or null, with every way of getting it
        /// wrong turned into a message instead of a silently open session.
        /// </summary>
        private static string ParsePassword(CommandLineArgs args)
        {
            if (args.passwordValueLookedLikeAnOption)
            {
                throw new Exception(
                    "-password needs a value, and one starting with '-' reads as another option. " +
                    "Use a password without a leading dash.");
            }

            var password = args.password.Present ? args.password.Value : null;

            string error;

            if (!ServerPassword.TryValidate(password, out error))
            {
                throw new Exception("-password: " + error + ".");
            }

            return password;
        }

        private static NetSession Join(CommandLineArgs args, GameContent content, StatusHandler status, int port, string name)
        {
            var password = ParsePassword(args);

            var directHost = (string)null;
            var directPort = port;

            if (args.address.Present)
            {
                if (!NetEndpoint.TryParse(args.address.Value, port, out directHost, out directPort))
                {
                    throw new Exception("-address must be <host>[:port].");
                }
            }

            byte[] relaySession = null;

            if (args.relaySession.Present)
            {
                relaySession = MasterProtocol.IdFromHex((args.relaySession.Value ?? "").Trim());

                if (relaySession == null)
                {
                    throw new Exception("-relay-session must be the 32 hex characters the host printed.");
                }
            }

            if (directHost == null && relaySession == null)
            {
                throw new Exception("-net join needs -address <host>[:port], or -relay-session <hex32> with -relay-master.");
            }

            var masterArg = args.relayMaster.Present ? args.relayMaster : args.master;
            string masterHost = null;
            var masterPort = MasterProtocol.DefaultPort;

            if (masterArg.Present)
            {
                if (!NetEndpoint.TryParse(masterArg.Value, MasterProtocol.DefaultPort, out masterHost, out masterPort))
                {
                    throw new Exception("-relay-master must be <host>[:port].");
                }
            }

            if (relaySession != null && masterHost == null)
            {
                throw new Exception("-relay-session needs -relay-master <host>[:port].");
            }

            NetSession session = null;

            if (directHost != null)
            {
                status("CONNECTING", new[] { directHost.ToUpper() + ":" + directPort });

                try
                {
                    session = NetSession.Join(directHost, directPort, name, 8000, password);
                }
                catch (Exception e)
                {
                    if (relaySession == null)
                    {
                        status("CONNECTION FAILED", new[] { DescribeFailure(directHost, directPort, e) });
                        Thread.Sleep(2500);
                        throw;
                    }

                    // Direct first, relay second. A LAN game or a forwarded port
                    // stays as fast as it was; the relay is the fallback, not the
                    // default, so nothing pays for it when it is not needed.
                    Console.WriteLine("Net: direct connection failed (" + e.Message + "), trying the relay.");
                    status("TRYING RELAY", new[]
                    {
                        masterHost.ToUpper() + ":" + masterPort,
                        "SESSION " + MasterProtocol.IdToHex(relaySession).ToUpper()
                    });
                }
            }

            if (session == null)
            {
                try
                {
                    var link = MasterClient.RelayJoin(masterHost, masterPort, relaySession, 12000);
                    session = NetSession.JoinRelay(link, name, password);
                }
                catch (Exception e)
                {
                    status("CONNECTION FAILED", new[] { DescribeFailure(masterHost, masterPort, e) });
                    Thread.Sleep(2500);
                    throw;
                }
            }

            var localChecksum = WadChecksum.Compute(content.Wad);
            if (session.Settings.WadChecksum != localChecksum)
            {
                session.Dispose();
                throw new Exception(
                    "The host is running a different WAD set. " +
                    "Both players must use the same IWAD and PWADs.");
            }

            // How to get back in after a drop. The direct address is dialled
            // again; a relay channel has to be asked for at the master, because
            // the guest never holds the host's address in that mode. Without a
            // dialer the session can only end on a disconnect, which is what it
            // did before reconnection existed.
            const int graceSeconds = 30;

            if (directHost != null)
            {
                var reconnectHost = directHost;
                var reconnectPort = directPort;

                session.SetReconnectDialer(
                    () =>
                    {
                        TcpClient unused = null;
                        return NetSession.DialDirect(reconnectHost, reconnectPort, 8000, out unused);
                    },
                    graceSeconds);
            }
            else
            {
                var reconnectMaster = masterHost;
                var reconnectMasterPort = masterPort;
                var reconnectId = relaySession;

                session.SetReconnectDialer(
                    () => MasterClient.RelayJoin(reconnectMaster, reconnectMasterPort, reconnectId, 12000).Stream,
                    graceSeconds);
            }

            status("CONNECTED", new[]
            {
                "PLAYER " + (session.LocalPlayer + 1) + " OF " + session.ExpectedPlayers,
                session.Relayed ? "VIA RELAY" : "DIRECT",
                "GET READY"
            });

            return session;
        }

        /// <summary>
        /// A short, ASCII-only reason for the status screen.
        ///
        /// The screen draws with Doom's own font, which has no Cyrillic and nothing
        /// below 0x20, so the exception's message came out as a smear of stray
        /// punctuation - the player saw "1 -- 127.0.0.1:5999" and learned nothing.
        /// The full message still goes to the console and the log, where a person
        /// debugging can read it; the screen gets what is readable and actionable.
        /// </summary>
        internal static string DescribeFailure(string host, int port, Exception error)
        {
            // The socket error can be wrapped: NetSession turns a connect timeout
            // into a plain Exception and leaves the SocketException underneath.
            System.Net.Sockets.SocketException socket = null;

            for (var e = error; e != null && socket == null; e = e.InnerException)
            {
                socket = e as System.Net.Sockets.SocketException;
            }

            if (socket != null)
            {
                switch (socket.SocketErrorCode)
                {
                    case System.Net.Sockets.SocketError.ConnectionRefused:
                        return host.ToUpper() + ":" + port + " REFUSED - IS THE HOST RUNNING?";

                    case System.Net.Sockets.SocketError.TimedOut:
                        return host.ToUpper() + ":" + port + " NO ANSWER - FIREWALL OR WRONG ADDRESS?";

                    case System.Net.Sockets.SocketError.HostNotFound:
                    case System.Net.Sockets.SocketError.NoData:
                        return host.ToUpper() + " NOT FOUND IN DNS";

                    case System.Net.Sockets.SocketError.NetworkUnreachable:
                    case System.Net.Sockets.SocketError.HostUnreachable:
                        return "NETWORK TO " + host.ToUpper() + " UNREACHABLE";
                }
            }

            // An answer from the host's own handshake. It must come before the
            // "refused" heuristic below, or "Host refused the connection: session is
            // full" is shown as "REFUSED - IS THE HOST RUNNING?" - a wrong diagnosis
            // pointing at the one thing that is definitely working.
            //
            // The reason travels with it, because the host knows exactly why and the
            // player cannot act on a guess: a full session, a stale token and an
            // abandoned slot are three different problems.
            var refusal = error as NetRefusalException;

            if (refusal != null)
            {
                var reason = refusal.Message;

                var separator = reason.IndexOf(": ", StringComparison.Ordinal);

                if (separator >= 0)
                {
                    reason = reason.Substring(separator + 2);
                }

                return host.ToUpperInvariant() + ":" + port + " REFUSED: " + reason.ToUpperInvariant();
            }

            // No socket error at all: the timeout we raise ourselves.
            if (error.Message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return host.ToUpper() + ":" + port + " NO ANSWER - FIREWALL OR WRONG ADDRESS?";
            }

            return host.ToUpper() + ":" + port + " FAILED - SEE THE LOG";
        }

        /// <summary>
        /// The public record of this session. Everything here is visible to
        /// anyone who asks the master, so it is limited to what a player needs to
        /// pick a game: the name, the map, the port and the rules.
        /// </summary>
        private static MasterSessionInfo BuildPresenceInfo(SessionSettings settings, string name, int port)
        {
            var info = new MasterSessionInfo();

            info.Name = Sanitize(name);
            info.MapText = settings.GameMode == GameMode.Commercial
                ? "MAP" + settings.Map.ToString("00")
                : "E" + settings.Episode + "M" + settings.Map;
            info.Port = port;
            info.Skill = (int)settings.Skill + 1;
            info.RuleBits = (settings.Deathmatch ? 1 : 0) |
                            (settings.FastMonsters ? 2 : 0) |
                            (settings.RespawnMonsters ? 4 : 0) |
                            (settings.NoMonsters ? 8 : 0);

            MasterProtocol.Put32(info.ResourceDigest, 0, settings.WadChecksum);

            var version = typeof(NetStartup).Assembly.GetName().Version.ToString();

            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                var digest = sha1.ComputeHash(Encoding.ASCII.GetBytes("DPT.DOOM " + version));
                Array.Copy(digest, info.BuildFingerprint, 20);
            }

            return info;
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "DPT.DOOM";
            }

            var chars = text.ToCharArray();

            for (var i = 0; i < chars.Length; i++)
            {
                // The directory protocol is printable ASCII only, and a machine
                // name is not guaranteed to be. Replacing is better than
                // refusing to start a LAN game over a hostname.
                if (chars[i] < 0x20 || chars[i] > 0x7E)
                {
                    chars[i] = '?';
                }
            }

            return new string(chars);
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

            // Co-op rules, with the defaults a co-op run wants.
            //
            // Four players in one corridor is the case that made this a rule:
            // with bodies blocking, the player at the back is shoved out of every
            // lift and nobody can pass a doorway. Two players are fine, so the
            // default flips on player count and an explicit switch still wins.
            settings.TeammatesBlock = !args.noblock.Present;
            settings.CoopDeathmatchWeapons = args.coopweapons.Present;
            settings.SharedKeys = !args.nosharedkeys.Present;

            return settings;
        }
    }
}
