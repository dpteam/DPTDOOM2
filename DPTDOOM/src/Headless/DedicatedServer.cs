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

namespace DPTDOOM
{
    /// <summary>What the operator asked the server to do with one line of input.</summary>
    public enum ServerCommand
    {
        /// <summary>A line that means nothing here.</summary>
        None,

        /// <summary>Print who is connected and how the session is doing.</summary>
        Status,

        /// <summary>Stop the server.</summary>
        Quit,

        /// <summary>Say something to everybody.</summary>
        Say
    }

    /// <summary>
    /// The rules that make a headless host into a server.
    ///
    /// A headless host already runs the game without a window, and `-net host` already accepts
    /// players. What it is **not** is a server, and the difference is entirely in what it does when
    /// nothing is happening:
    ///
    ///  - it treats "the world is not advancing" as a fault and stops after a few thousand updates,
    ///    with a message about a paused game. A server waiting for its first player does exactly
    ///    that, so it quits before anybody can connect - which is the whole failure this class
    ///    exists to prevent;
    ///  - it stops at ten million tics, a safety net that is right for a test and wrong for a
    ///    server that is meant to run for weeks;
    ///  - it has no way to be told anything. A server needs to be told to stop, and wants to be
    ///    asked who is on it.
    ///
    /// The rules are here rather than in the loop so they can be tested: a run that waits, a run
    /// that stops, and the arithmetic of when a wait becomes a fault are all decisions, and a
    /// decision buried in a loop with a `Thread.Sleep` cannot be checked.
    /// </summary>
    public static class DedicatedServer
    {
        /// <summary>
        /// Whether a long wait is a fault.
        ///
        /// For an ordinary headless run it is: the run was asked for tics and is not getting them.
        /// For a server it never is: waiting for a player is the normal state of a server with
        /// nobody on it, and the only thing a "fault" would achieve is to quit before the first
        /// player arrives.
        /// </summary>
        public static bool WaitIsAFault(bool dedicated, int stalledUpdates, int limit)
        {
            return !dedicated && stalledUpdates > limit;
        }

        /// <summary>
        /// Whether the run has reached its tic limit.
        ///
        /// A server has no limit unless one was asked for: its job is to keep running. An explicit
        /// <c>-tics</c> still applies, because that is how a server is tested.
        /// </summary>
        public static bool TicLimitReached(bool dedicated, int simulated, int limit)
        {
            if (limit <= 0)
            {
                return false;
            }

            return simulated >= limit;
        }

        /// <summary>
        /// The safety net against a game that never ends without input.
        ///
        /// Ten million for a test run; none for a server. A server that stopped after ten million
        /// tics would stop silently after about three days, and the person running it would have no
        /// reason to connect the two facts.
        /// </summary>
        public static bool SafetyNetReached(bool dedicated, int updates)
        {
            return !dedicated && updates >= 10000000;
        }

        /// <summary>
        /// One line of the operator's input as a command.
        ///
        /// Deliberately small. A server console that can run arbitrary console commands is a way to
        /// change the simulation from outside the game, and every such command would have to be
        /// justified against lockstep - the same reason the in-game console refuses <c>map</c> in a
        /// network game. Three verbs that cannot touch the world, and nothing else.
        /// </summary>
        public static ServerCommand Parse(string line, out string argument)
        {
            argument = null;

            if (string.IsNullOrEmpty(line))
            {
                return ServerCommand.None;
            }

            var text = line.Trim();

            if (text.Length == 0)
            {
                return ServerCommand.None;
            }

            var space = text.IndexOf(' ');
            var verb = space < 0 ? text : text.Substring(0, space);

            if (space >= 0)
            {
                argument = text.Substring(space + 1).Trim();
            }

            if (string.Equals(verb, "quit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(verb, "exit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(verb, "stop", StringComparison.OrdinalIgnoreCase))
            {
                return ServerCommand.Quit;
            }

            if (string.Equals(verb, "status", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(verb, "list", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(verb, "players", StringComparison.OrdinalIgnoreCase))
            {
                return ServerCommand.Status;
            }

            if (string.Equals(verb, "say", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(argument))
                {
                    argument = null;
                    return ServerCommand.None;
                }

                // The same limit the chat uses, because this line travels the same path and a
                // longer one would be cut off on the wire without saying so.
                if (argument.Length > Chat.MaxLength)
                {
                    argument = argument.Substring(0, Chat.MaxLength);
                }

                return ServerCommand.Say;
            }

            argument = null;
            return ServerCommand.None;
        }

        /// <summary>
        /// What a server says about itself.
        ///
        /// Every number here is one an operator has to have: how long it has been up, how many tics
        /// the world has run, who is connected and which slots have gone quiet. Without them the only
        /// way to tell a working server from a hung one is to try joining it.
        /// </summary>
        public static string Status(int players, int slots, int left, int tic, TimeSpan up)
        {
            return "up " + Format(up) +
                   ", tic " + tic +
                   ", players " + players + "/" + slots +
                   (left > 0 ? ", slots gone quiet " + left : "");
        }

        /// <summary>A duration as an operator reads it: 4d2h, 3h12m, 5m8s, 9s.</summary>
        public static string Format(TimeSpan value)
        {
            if (value.TotalDays >= 1)
            {
                return (int)value.TotalDays + "d" + value.Hours + "h";
            }

            if (value.TotalHours >= 1)
            {
                return (int)value.TotalHours + "h" + value.Minutes + "m";
            }

            if (value.TotalMinutes >= 1)
            {
                return (int)value.TotalMinutes + "m" + value.Seconds + "s";
            }

            return (int)value.TotalSeconds + "s";
        }
    }
}
