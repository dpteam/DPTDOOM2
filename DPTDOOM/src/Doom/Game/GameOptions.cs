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
using DPTDOOM.Video;
using DPTDOOM.Audio;
using DPTDOOM.UserInput;

namespace DPTDOOM
{
    public sealed class GameOptions
    {
        private GameVersion gameVersion;
        private GameMode gameMode;
        private MissionPack missionPack;

        private Player[] players;
        private int consolePlayer;

        private int episode;
        private int map;
        private GameSkill skill;

        private bool demoPlayback;
        private bool netGame;

        private int deathmatch;
        private bool fastMonsters;
        private bool respawnMonsters;
        private bool noMonsters;

        private bool teammatesBlock;
        private bool coopDeathmatchWeapons;
        private bool sharedKeys;

        private IntermissionInfo intermissionInfo;

        private DoomRandom random;

        private IVideo video;
        private ISound sound;
        private IMusic music;
        private IUserInput userInput;

        public GameOptions()
        {
            gameVersion = GameVersion.Version109;
            gameMode = GameMode.Commercial;
            missionPack = MissionPack.Doom2;

            players = new Player[Player.MaxPlayerCount];
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                players[i] = new Player(i);
            }
            players[0].InGame = true;
            consolePlayer = 0;

            episode = 1;
            map = 1;
            skill = GameSkill.Medium;

            demoPlayback = false;
            netGame = false;

            deathmatch = 0;
            fastMonsters = false;
            respawnMonsters = false;
            noMonsters = false;

            // Co-op defaults: players do block each other, which is how Doom has
            // always behaved, but keys are shared because a level that needs
            // three keys otherwise stalls the moment one player holds a door and
            // another cannot reach the switch.
            teammatesBlock = true;
            coopDeathmatchWeapons = false;
            sharedKeys = true;

            intermissionInfo = new IntermissionInfo();

            random = new DoomRandom();

            video = NullVideo.GetInstance();
            sound = NullSound.GetInstance();
            music = NullMusic.GetInstance();
            userInput = NullUserInput.GetInstance();
        }

        public GameOptions(CommandLineArgs args, GameContent content) : this()
        {
            if (args.solonet.Present)
            {
                netGame = true;
            }

            gameVersion = content.Wad.GameVersion;
            gameMode = content.Wad.GameMode;
            missionPack = content.Wad.MissionPack;
        }

        public GameVersion GameVersion
        {
            get => gameVersion;
            set => gameVersion = value;
        }

        public GameMode GameMode
        {
            get => gameMode;
            set => gameMode = value;
        }

        public MissionPack MissionPack
        {
            get => missionPack;
            set => missionPack = value;
        }

        public Player[] Players
        {
            get => players;
        }

        public int ConsolePlayer
        {
            get => consolePlayer;
            set => consolePlayer = value;
        }

        public int Episode
        {
            get => episode;
            set => episode = value;
        }

        public int Map
        {
            get => map;
            set => map = value;
        }

        public GameSkill Skill
        {
            get => skill;
            set => skill = value;
        }

        public bool DemoPlayback
        {
            get => demoPlayback;
            set => demoPlayback = value;
        }

        public bool NetGame
        {
            get => netGame;
            set => netGame = value;
        }

        public int Deathmatch
        {
            get => deathmatch;
            set => deathmatch = value;
        }

        public bool FastMonsters
        {
            get => fastMonsters;
            set => fastMonsters = value;
        }

        public bool RespawnMonsters
        {
            get => respawnMonsters;
            set => respawnMonsters = value;
        }

        public bool NoMonsters
        {
            get => noMonsters;
            set => noMonsters = value;
        }

        /// <summary>
        /// Whether players collide with each other.
        ///
        /// Doom has always let players block each other, which is fine for two
        /// players and becomes a traffic jam in a corridor with four. Turning it
        /// off makes players pass through each other instead of fighting for the
        /// same doorway. It is only meaningful in co-op: in deathmatch bodies
        /// blocking the way is part of the game.
        /// </summary>
        public bool TeammatesBlock
        {
            get => teammatesBlock;
            set => teammatesBlock = value;
        }

        /// <summary>
        /// Whether weapon and ammo spots that a map placed for deathmatch spawn in
        /// co-op. Off by default: those extra copies clutter a cooperative run,
        /// and the level is balanced without them. Single player still spawns
        /// everything, which is what "как в сингле" means here.
        /// </summary>
        public bool CoopDeathmatchWeapons
        {
            get => coopDeathmatchWeapons;
            set => coopDeathmatchWeapons = value;
        }

        /// <summary>
        /// Whether picking up a key gives it to the whole team.
        ///
        /// On by default. Without it a co-op run deadlocks easily: one player
        /// grabs the blue key, walks through the door, and the second player has
        /// to find it again while the first holds a lift. The key is not
        /// consumed, so both players walk through their own door.
        /// </summary>
        public bool SharedKeys
        {
            get => sharedKeys;
            set => sharedKeys = value;
        }

        public IntermissionInfo IntermissionInfo
        {
            get => intermissionInfo;
        }

        public DoomRandom Random
        {
            get => random;
        }

        public IVideo Video
        {
            get => video;
            set => video = value;
        }

        public ISound Sound
        {
            get => sound;
            set => sound = value;
        }

        public IMusic Music
        {
            get => music;
            set => music = value;
        }

        public IUserInput UserInput
        {
            get => userInput;
            set => userInput = value;
        }
    }
}
