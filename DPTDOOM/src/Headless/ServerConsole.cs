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
using System.Threading;

namespace DPTDOOM
{
    /// <summary>
    /// Reads the operator's typed commands from standard input, without blocking the game.
    ///
    /// A server has to be told things - to stop, mostly, and to say who is connected - and there is
    /// nothing else to tell it from: no window, no menu, no in-game console. Standard input is the
    /// one channel a service has on every platform this project targets, including a console window
    /// on Windows XP.
    ///
    /// The read happens on its own thread because <c>Console.ReadLine</c> blocks, and a blocking read
    /// in the game loop would stop the world every time nobody was typing. The queue is the only
    /// thing shared, and the loop drains it - so the game thread never waits on a human.
    ///
    /// A closed or unavailable stdin is not an error: a server started by a service manager, or with
    /// its input redirected from nothing, must run anyway. The thread simply ends.
    /// </summary>
    public sealed class ServerConsole
    {
        private readonly Queue<string> lines = new Queue<string>();
        private readonly Thread thread;

        private volatile bool running = true;

        public ServerConsole()
        {
            thread = new Thread(Read);
            thread.IsBackground = true;
            thread.Name = "DPTDOOM server console";

            try
            {
                thread.Start();
            }
            catch (Exception)
            {
                // No input at all. The server runs; it simply cannot be told anything from here.
                running = false;
            }
        }

        /// <summary>True while input is still being read, for a report.</summary>
        public bool HasInput
        {
            get { return running; }
        }

        /// <summary>
        /// Takes one line if the operator typed one.
        ///
        /// Never blocks: the game loop calls this every update, and a read that waited would stop
        /// the world for as long as nobody was typing.
        /// </summary>
        public bool TryTake(out string line)
        {
            lock (lines)
            {
                if (lines.Count == 0)
                {
                    line = null;
                    return false;
                }

                line = lines.Dequeue();
                return true;
            }
        }

        /// <summary>Stops the reader. Called on the way out so the thread does not outlive the game.</summary>
        public void Stop()
        {
            running = false;
        }

        /// <summary>
        /// Drains the pending lines and says whether any of them asked to stop.
        ///
        /// Needed **before** the game loop exists. The host waits for its players inside the
        /// network startup, before a world is built, and an operator who started a server by mistake
        /// has to be able to stop it during that wait - otherwise the only way out is to kill the
        /// process, and a server that cannot be stopped politely is a server that gets killed while
        /// holding a port and a log file.
        ///
        /// The other verbs have nothing to act on yet - there is no session to ask about and no
        /// players to talk to - so they are answered rather than queued: queuing them would replay
        /// a stale "status" minutes later, when it means something different.
        /// </summary>
        public bool PollForQuit()
        {
            var quit = false;
            string line;

            while (TryTake(out line))
            {
                string argument;

                switch (DedicatedServer.Parse(line, out argument))
                {
                    case ServerCommand.Quit:
                        quit = true;
                        break;

                    case ServerCommand.Status:
                    case ServerCommand.Say:
                        Console.WriteLine("Server: not started yet - waiting for players.");
                        break;
                }
            }

            return quit;
        }

        private void Read()
        {
            try
            {
                while (running)
                {
                    var line = Console.ReadLine();

                    if (line == null)
                    {
                        // End of input: nobody can talk to this server any more. That is not a
                        // reason to stop it - a service has no terminal and still has to run.
                        running = false;
                        return;
                    }

                    lock (lines)
                    {
                        lines.Enqueue(line);
                    }
                }
            }
            catch (Exception)
            {
                running = false;
            }
        }
    }
}
