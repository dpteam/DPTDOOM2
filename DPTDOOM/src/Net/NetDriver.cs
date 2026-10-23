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
using DPTDOOM.UserInput;

namespace DPTDOOM.Net
{
    /// <summary>
    /// Lockstep driver. Every participant simulates the same tics with the
    /// same commands; the game only advances when a command from every player
    /// is available, so no state ever needs to be synchronised.
    /// </summary>
    public interface INetDriver : IDisposable
    {
        /// <summary>Applies the agreed session settings to the game options.</summary>
        void Configure(GameOptions options);

        /// <summary>
        /// Samples local input and tries to assemble one tic for every player.
        /// Returns false when a remote command has not arrived yet.
        /// </summary>
        bool TryAdvance(TicCmd[] cmds, IUserInput input, ref bool pause);

        /// <summary>Called after the game consumed the commands.</summary>
        void Commit();

        string Status { get; }
        bool Faulted { get; }
        string FaultReason { get; }
        bool Finished { get; }
        int LocalPlayer { get; }

        /// <summary>Total slots in the session, bots included.</summary>
        int ExpectedPlayers { get; }

        /// <summary>True on the machine that drives the bots.</summary>
        bool IsHost { get; }

        /// <summary>Hands the host's bots to the lockstep loop.</summary>
        void AttachBots(DoomBot[] bots);
    }

    public sealed class NetDriver : INetDriver
    {
        private readonly NetSession session;
        private readonly Dictionary<int, NetTic> localCommands = new Dictionary<int, NetTic>();

        private DoomBot[] bots;
        private bool botsSentForTic;

        private int tic;
        private int sendTic;

        public NetDriver(NetSession session)
        {
            this.session = session;
        }

        public void Configure(GameOptions options)
        {
            var settings = session.Settings;

            options.NetGame = true;
            options.Deathmatch = settings.Deathmatch ? 1 : 0;
            options.FastMonsters = settings.FastMonsters;
            options.RespawnMonsters = settings.RespawnMonsters;
            options.NoMonsters = settings.NoMonsters;

            // Rules that change the simulation. They come from the host through
            // the welcome, never from this machine's command line, or the two
            // ends would build different worlds.
            options.TeammatesBlock = settings.TeammatesBlock;
            options.CoopDeathmatchWeapons = settings.CoopDeathmatchWeapons;
            options.SharedKeys = settings.SharedKeys;

            options.Episode = settings.Episode;
            options.Map = settings.Map;
            options.Skill = settings.Skill;

            options.ConsolePlayer = session.LocalPlayer;

            var count = MathEx.Clamp(session.ExpectedPlayers, 2, Player.MaxPlayerCount);
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                options.Players[i].InGame = i < count;
            }
        }

        /// <summary>
        /// Bots that fill the last slots of the session.
        ///
        /// The array is indexed by player number, with nulls for the human slots.
        /// Only the host ever has one: a guest receives the bot commands over the
        /// wire like any other player's, which is what keeps the two ends from
        /// running two copies of the same bot and drifting apart.
        /// </summary>
        public void AttachBots(DoomBot[] value)
        {
            bots = value;
        }

        /// <summary>
        /// Generates a command for every bot and sends it as that player's tic.
        ///
        /// Runs once per simulated tic, not once per frame: TryAdvance is called
        /// repeatedly while waiting for a slow peer, and sampling the same bot
        /// again would put several frames into one tic.
        /// </summary>
        private void SendBotTics()
        {
            if (bots == null || botsSentForTic || !session.IsHost)
            {
                return;
            }

            botsSentForTic = true;

            for (var player = 0; player < bots.Length; player++)
            {
                if (bots[player] == null)
                {
                    continue;
                }

                var command = new TicCmd();
                bots[player].BuildTicCmd(command);

                var netTic = NetTic.From(command);
                netTic.Number = tic;

                session.SendTic(player, netTic);
            }
        }

        public bool TryAdvance(TicCmd[] cmds, IUserInput input, ref bool pause)
        {
            if (session.Faulted)
            {
                return false;
            }

            var delay = session.Delay;

            // Keep Delay commands in hand: the one being simulated now, plus
            // delay-1 more already sent to the peer.
            //
            // This is what makes a high-ping session playable. Without it the
            // tic cannot advance until the peer's command for that same tic
            // arrives, and the peer only produces it when it reaches that tic -
            // so the session runs at one tic per round trip. At 100 ms of ping
            // that is about 17 tics per second against the 35 the game wants:
            // the world visibly runs at half speed. Generating ahead decouples
            // the two, at the cost of `delay` tics of input latency.
            //
            // Generating only when the lead is short also bounds the damage of a
            // stall: while a peer is late the tic does not advance, so nothing
            // more is generated, and the backlog cannot grow into seconds of
            // input lag.
            if (sendTic < tic + delay + 1)
            {
                GenerateAndSend(input, ref pause);
            }

            if (sendTic < tic + delay + 1)
            {
                // Still warming up. The first simulation step waits a few tics,
                // which happens inside "GET READY" and is not visible.
                return false;
            }

            // Bots are local to the host, but their commands travel the same path
            // as a human's: stored for this tic and broadcast. The guest therefore
            // never runs bot logic and cannot disagree about what a bot did.
            SendBotTics();

            var count = session.ExpectedPlayers;

            for (var player = 0; player < count; player++)
            {
                if (player == session.LocalPlayer)
                {
                    continue;
                }

                if (!session.HasTic(player, tic))
                {
                    return false;
                }
            }

            NetTic local;
            if (!localCommands.TryGetValue(tic, out local))
            {
                return false;
            }

            local.CopyTo(cmds[session.LocalPlayer]);

            for (var player = 0; player < count; player++)
            {
                if (player == session.LocalPlayer)
                {
                    continue;
                }

                NetTic remote;
                if (!session.TryTakeTic(player, tic, out remote))
                {
                    // Cannot happen after the availability check, but staying
                    // on the safe side keeps the tic from being half applied.
                    return false;
                }

                remote.CopyTo(cmds[player]);
            }

            for (var player = count; player < Player.MaxPlayerCount; player++)
            {
                cmds[player].Clear();
            }

            return true;
        }

        /// <summary>
        /// Samples one local command and sends it immediately, stamped with the
        /// tic it belongs to. Sending at generation time, not at simulation
        /// time, is the whole point: the peer needs it Delay tics before it
        /// simulates that tic, not when we get there.
        /// </summary>
        private void GenerateAndSend(IUserInput input, ref bool pause)
        {
            NetTic command;

            if (DeterministicInput)
            {
                command = BuildDeterministicCommand(sendTic, session.LocalPlayer);
            }
            else
            {
                var local = new TicCmd();
                input.BuildTicCmd(local);

                if (pause)
                {
                    pause = false;
                    local.Buttons |= (byte)(TicCmdButtons.Special | TicCmdButtons.Pause);
                }

                command = NetTic.From(local);
            }

            command.Number = sendTic;

            lock (localCommands)
            {
                localCommands[sendTic] = command;
            }

            session.SendTic(session.LocalPlayer, command);
            sendTic++;
        }

        public void Commit()
        {
            lock (localCommands)
            {
                localCommands.Remove(tic);
            }

            botsSentForTic = false;
            tic++;
        }

        public void Dispose()
        {
            session.Dispose();
        }

        public string Status => session.IsHost ? "hosting" : "connected";

        /// <summary>Total slots in the session, bots included.</summary>
        public int ExpectedPlayers => session.ExpectedPlayers;

        /// <summary>True on the machine that drives the bots.</summary>
        public bool IsHost => session.IsHost;
        public bool Faulted => session.Faulted;
        public string FaultReason => session.FaultReason;
        public bool Finished => session.Finished;
        public int LocalPlayer => session.LocalPlayer;

        /// <summary>
        /// Replaces the local input with a deterministic sequence derived from
        /// (tic, player). Only used by automated network soak tests: both ends
        /// must compute exactly the same command for the same player and tic.
        /// </summary>
        public bool DeterministicInput { get; set; }

        public int CurrentTic => tic;

        private static NetTic BuildDeterministicCommand(int tic, int player)
        {
            var value = (uint)(tic * 1103515245 + player * 12345 + 7919);
            value = value * 1664525u + 1013904223u;

            var tic1 = new NetTic();

            // Walk forward most of the time, strafe and turn in slow cycles,
            // and press fire/use now and then. Deterministic and busy enough to
            // exercise monsters, doors and lifts.
            tic1.Forward = (byte)(sbyte)((tic / 140) % 5 == 4 ? 0 : 25);
            tic1.Side = (byte)(sbyte)(((tic / 53) % 3 - 1) * 12);
            tic1.AngleTurn = (short)(((tic / 17) % 9 - 4) * 320);
            tic1.Buttons = (byte)(((tic % 71) < 3 ? TicCmdButtons.Attack : 0) |
                                  ((tic % 97) < 2 ? TicCmdButtons.Use : 0));

            return tic1;
        }
    }
}
