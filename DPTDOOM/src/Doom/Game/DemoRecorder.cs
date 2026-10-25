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
    /// Writes a demo (.lmp) while the game runs.
    ///
    /// Two formats, chosen when the file is written:
    ///
    ///   109 - vanilla Doom 1.9: four bytes per player per tic, plays in any Doom.
    ///   110 - this port's own: five bytes, the extra one being the jump.
    ///
    ///   109/110, skill, episode, map, deathmatch, respawn, fast, nomonsters,
    ///   consoleplayer, playeringame[4],
    ///   then four (or five) bytes per player per tic,
    ///   then 0x80.
    ///
    /// The choice is made at the end rather than at the start, and that is deliberate: a
    /// recording in which nobody jumped is byte-for-byte a vanilla demo, so it still plays in
    /// other ports and still matches the recordings the compatibility tests pin their world
    /// hashes to. Writing version 110 unconditionally would have cost both for nothing.
    ///
    /// Only the high byte of the turn amount is stored. That is not a shortcut, it is the
    /// format: turn is written as a whole unit, and playback reconstructs it by shifting the
    /// byte back up. Recording the low byte would make the file unreadable for everything
    /// else.
    /// </summary>
    public sealed class DemoRecorder
    {
        public const byte Version = 109;

        /// <summary>The version this port writes when a jump was actually recorded.</summary>
        public const byte VersionWithJump = 110;

        public const byte Terminator = 0x80;

        private readonly List<byte> data = new List<byte>(1024 * 64);
        private readonly GameOptions options;
        private readonly bool[] inGame;
        private readonly int playerCount;

        private bool stopped;
        private bool usedJump;

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

                // Written for every tic so the records stay fixed-size; the version byte
                // decides at the end whether it is kept.
                data.Add(cmd.Jump);

                if (cmd.Jump != 0)
                {
                    usedJump = true;
                }
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

            var bytes = usedJump ? BuildWithJump() : BuildVanilla();

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, bytes);
        }

        /// <summary>
        /// The recording as a version 110 demo: five bytes per player per tic.
        /// </summary>
        private byte[] BuildWithJump()
        {
            var result = new byte[data.Count + 1];

            for (var i = 0; i < data.Count; i++)
            {
                result[i] = data[i];
            }

            result[0] = VersionWithJump;
            result[result.Length - 1] = Terminator;

            return result;
        }

        /// <summary>
        /// The recording as a vanilla version 109 demo: the jump byte is dropped.
        ///
        /// Only reachable when no jump was ever recorded, so the dropped byte is zero in
        /// every record - which means the two files describe the same simulation, and the
        /// compact one is the more useful of them.
        /// </summary>
        private byte[] BuildVanilla()
        {
            var header = 9 + Player.MaxPlayerCount;
            var result = new byte[header + TicCount * 4 * playerCount + 1];

            for (var i = 0; i < header; i++)
            {
                result[i] = data[i];
            }

            result[0] = Version;

            var source = header;
            var target = header;

            for (var tic = 0; tic < TicCount; tic++)
            {
                for (var player = 0; player < playerCount; player++)
                {
                    result[target++] = data[source++];
                    result[target++] = data[source++];
                    result[target++] = data[source++];
                    result[target++] = data[source++];

                    // The jump byte is skipped, not checked: BuildVanilla is only called when
                    // every one of them is zero.
                    source++;
                }
            }

            result[result.Length - 1] = Terminator;

            return result;
        }

        /// <summary>Tics recorded so far.</summary>
        public int TicCount
        {
            get
            {
                var header = 9 + Player.MaxPlayerCount;

                // Five bytes per player per tic, always, whatever the file will end up as:
                // the buffer is written before the format is chosen.
                return (data.Count - header) / (5 * playerCount);
            }
        }

        public bool Stopped
        {
            get { return stopped; }
        }
    }
}
