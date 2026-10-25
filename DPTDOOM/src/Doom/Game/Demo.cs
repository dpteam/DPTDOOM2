//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
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

namespace DPTDOOM
{
    public sealed class Demo
    {
        private int p;
        private byte[] data;

        private GameOptions options;

        private int playerCount;

        /// <summary>Whether this demo carries a jump byte per player per tic.</summary>
        private readonly bool jumpByte;

        public Demo(byte[] data)
        {
            p = 0;

            var version = data[p++];

            if (version != 109 && version != 110)
            {
                throw new Exception("Demo is from a different game version!");
            }

            // Version 109 is vanilla Doom 1.9: four bytes per player per tic. Version 110 is
            // this port's own, and adds one byte - the jump. Reading both is what lets an old
            // demo still play, which matters because the compatibility tests pin world hashes
            // to recordings made before jump existed.
            jumpByte = version >= 110;

            this.data = data;

            options = new GameOptions();
            options.Skill = (GameSkill)data[p++];
            options.Episode = data[p++];
            options.Map = data[p++];
            options.Deathmatch = data[p++];
            options.RespawnMonsters = data[p++] != 0;
            options.FastMonsters = data[p++] != 0;
            options.NoMonsters = data[p++] != 0;
            options.ConsolePlayer = data[p++];

            options.Players[0].InGame = data[p++] != 0;
            options.Players[1].InGame = data[p++] != 0;
            options.Players[2].InGame = data[p++] != 0;
            options.Players[3].InGame = data[p++] != 0;

            options.DemoPlayback = true;

            playerCount = 0;
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                if (options.Players[i].InGame)
                {
                    playerCount++;
                }
            }
            if (playerCount >= 2)
            {
                options.NetGame = true;
            }
        }

        public Demo(string fileName) : this(File.ReadAllBytes(fileName))
        {
        }

        public bool ReadCmd(TicCmd[] cmds)
        {
            if (p == data.Length)
            {
                return false;
            }

            if (data[p] == 0x80)
            {
                return false;
            }

            var recordSize = jumpByte ? 5 : 4;

            if (p + recordSize * playerCount > data.Length)
            {
                return false;
            }

            var players = options.Players;
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                if (players[i].InGame)
                {
                    var cmd = cmds[i];
                    cmd.ForwardMove = (sbyte)data[p++];
                    cmd.SideMove = (sbyte)data[p++];
                    cmd.AngleTurn = (short)(data[p++] << 8);
                    cmd.Buttons = data[p++];

                    if (jumpByte)
                    {
                        cmd.Jump = data[p++];
                    }
                }
            }

            return true;
        }

        public GameOptions Options => options;
    }
}
