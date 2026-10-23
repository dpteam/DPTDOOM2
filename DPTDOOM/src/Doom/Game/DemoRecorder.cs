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
using System.IO;

namespace DPTDOOM
{
    /// <summary>
    /// Writes a vanilla-format demo (.lmp) while the game runs.
    ///
    /// The format is the one <see cref="Demo"/> reads, so a recording plays back
    /// with -playdemo and in any other Doom that accepts version 109 demos:
    ///
    ///   109, skill, episode, map, deathmatch, respawn, fast, nomonsters,
    ///   consoleplayer, playeringame[4],
    ///   then four bytes per player per tic,
    ///   then 0x80.
    ///
    /// Only the high byte of the turn amount is stored. That is not a shortcut,
    /// it is the format: turn is written as a whole unit, and playback
    /// reconstructs it by shifting the byte back up. Recording the low byte
    /// would make the file unreadable for everything else.
    /// </summary>
    public sealed class DemoRecorder
    {
        public const byte Version = 109;
        public const byte Terminator = 0x80;

        private readonly List<byte> data = new List<byte>(1024 * 64);
        private readonly GameOptions options;
        private readonly bool[] inGame;
        private readonly int playerCount;

        private bool stopped;

        public DemoRecorder(GameOptions options)
        {
            this.options = options;

            inGame = new bool[Player.MaxPlayerCount];
            playerCount = 0;

            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                inGame[i] = options.Players[i].InGame;
                if (inGame[i])
                {
                    playerCount++;
                }
            }

            if (playerCount == 0)
            {
                throw new Exception("Cannot record a demo with no players in the game.");
            }

            data.Add(Version);
            data.Add((byte)options.Skill);
            data.Add((byte)options.Episode);
            data.Add((byte)options.Map);
            data.Add((byte)options.Deathmatch);
            data.Add((byte)(options.RespawnMonsters ? 1 : 0));
            data.Add((byte)(options.FastMonsters ? 1 : 0));
            data.Add((byte)(options.NoMonsters ? 1 : 0));
            data.Add((byte)options.ConsolePlayer);

            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                data.Add((byte)(inGame[i] ? 1 : 0));
            }
        }

        /// <summary>
        /// Appends one tic of commands. Call once per simulated tic, with the
        /// commands the game actually consumed.
        /// </summary>
        public void WriteCmd(TicCmd[] cmds)
        {
            if (stopped)
            {
                return;
            }

            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                if (!inGame[i])
                {
                    continue;
                }

                var cmd = cmds[i];
                data.Add((byte)cmd.ForwardMove);
                data.Add((byte)cmd.SideMove);
                data.Add((byte)(cmd.AngleTurn >> 8));
                data.Add(cmd.Buttons);
            }
        }

        /// <summary>
        /// Finishes the demo and writes it. Safe to call more than once.
        /// </summary>
        public void Save(string path)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            data.Add(Terminator);

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, data.ToArray());
        }

        /// <summary>Tics recorded so far.</summary>
        public int TicCount
        {
            get
            {
                var header = 9 + Player.MaxPlayerCount;
                return (data.Count - header) / (4 * playerCount);
            }
        }

        public bool Stopped
        {
            get { return stopped; }
        }
    }
}
