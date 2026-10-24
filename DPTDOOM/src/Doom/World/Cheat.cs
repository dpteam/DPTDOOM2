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
using System.Linq;

namespace DPTDOOM
{
    public sealed class Cheat
    {
        private static CheatInfo[] list = new CheatInfo[]
        {
            new CheatInfo("idfa", (cheat, typed) => cheat.Execute(CheatAction.Idfa), false),
            new CheatInfo("idkfa", (cheat, typed) => cheat.Execute(CheatAction.Idkfa), false),
            new CheatInfo("iddqd", (cheat, typed) => cheat.Execute(CheatAction.God), false),
            new CheatInfo("idclip", (cheat, typed) => cheat.Execute(CheatAction.NoClip), false),
            new CheatInfo("idspispopd", (cheat, typed) => cheat.Execute(CheatAction.NoClip), false),
            new CheatInfo("iddt", (cheat, typed) => cheat.Execute(CheatAction.FullMap), true),
            new CheatInfo("idbehold", (cheat, typed) => cheat.Execute(CheatAction.PowerUpList), false),
            new CheatInfo("idbehold?", (cheat, typed) => cheat.Execute(CheatAction.PowerUp, typed.Last()), false),
            new CheatInfo("idchoppers", (cheat, typed) => cheat.Execute(CheatAction.Chainsaw), false),
            new CheatInfo("tntem", (cheat, typed) => cheat.Execute(CheatAction.KillMonsters), false),
            new CheatInfo("killem", (cheat, typed) => cheat.Execute(CheatAction.KillMonsters), false),
            new CheatInfo("fhhall", (cheat, typed) => cheat.Execute(CheatAction.KillMonsters), false),
            new CheatInfo("idclev??", (cheat, typed) => cheat.Execute(CheatAction.ChangeLevel, typed), true),
            new CheatInfo("idmus??", (cheat, typed) => cheat.Execute(CheatAction.ChangeMusic, typed), false)
        };

        private static readonly int maxCodeLength = list.Max(info => info.Code.Length);

        private World world;

        private char[] buffer;
        private int p;

        public Cheat(World world)
        {
            this.world = world;

            buffer = new char[maxCodeLength];
            p = 0;
        }

        /// <summary>
        /// Whether cheats may be used at all right now.
        ///
        /// Exposed because the console needs the same rule: a cheat typed at the
        /// prompt is the same cheat, and refusing it only in the keyboard path would
        /// leave a way in that desynchronises every other player.
        /// </summary>
        public bool Allowed
        {
            get { return !world.Options.NetGame && !world.Options.DemoPlayback; }
        }

        /// <summary>Why a cheat was refused, in one sentence.</summary>
        public string Refusal
        {
            get { return "Cheats are disabled in a network game and during demo playback."; }
        }

        /// <summary>
        /// Runs one cheat by name, so the console and the typed codes share a single
        /// implementation.
        ///
        /// A name rather than a method reference because the console parses what the
        /// player typed: mapping a string to an enum here means the console does not
        /// have to hold a table of delegates that could drift from this one.
        ///
        /// Returns false when the action is unknown.
        /// </summary>
        public bool Execute(CheatAction action)
        {
            return Execute(action, '\0');
        }

        public bool Execute(CheatAction action, char argument)
        {
            switch (action)
            {
                case CheatAction.God:
                    GodMode();
                    return true;

                case CheatAction.NoClip:
                    NoClip();
                    return true;

                case CheatAction.Weapons:
                    GiveWeapons();
                    world.ConsolePlayer.SendMessage(DoomInfo.Strings.STSTR_FAADDED);
                    return true;

                case CheatAction.Idfa:
                    FullAmmo();
                    return true;

                case CheatAction.Idkfa:
                    FullAmmoAndKeys();
                    return true;

                case CheatAction.Keys:
                    GiveKeys();
                    world.ConsolePlayer.SendMessage(DoomInfo.Strings.STSTR_KFAADDED);
                    return true;

                case CheatAction.FullMap:
                    FullMap();
                    return true;

                case CheatAction.PowerUpList:
                    ShowPowerUpList();
                    return true;

                case CheatAction.PowerUp:
                    DoPowerUp(argument);
                    return true;

                case CheatAction.Chainsaw:
                    GiveChainsaw();
                    return true;

                case CheatAction.KillMonsters:
                    KillMonsters();
                    return true;

                case CheatAction.ChangeLevel:
                    ChangeLevel(argument.ToString());
                    return true;

                case CheatAction.ChangeMusic:
                    ChangeMusic(argument.ToString());
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Runs one cheat by its console name. Used by the console command, which
        /// takes words rather than single characters.
        /// </summary>
        public bool Execute(CheatAction action, string argument)
        {
            if (action == CheatAction.ChangeLevel)
            {
                ChangeLevel(argument);
                return true;
            }

            if (action == CheatAction.ChangeMusic)
            {
                ChangeMusic(argument);
                return true;
            }

            return Execute(action);
        }

        public bool DoEvent(DoomEvent e)
        {
            // Cheats change the world, and in a lockstep network game the other
            // end never learns about the change: the session desyncs. During demo
            // playback it corrupts what is being checked. Neither is fixable by
            // sending the cheat over the wire - it is a debugging tool, not a
            // game feature - so it is refused instead of quietly breaking things.
            if (!Allowed)
            {
                if (e.Type == EventType.KeyDown)
                {
                    Console.WriteLine(Refusal);
                }

                return true;
            }

            if (e.Type == EventType.KeyDown)
            {
                buffer[p] = e.Key.GetChar();

                p = (p + 1) % buffer.Length;

                CheckBuffer();
            }

            return true;
        }

        private void CheckBuffer()
        {
            for (var i = 0; i < list.Length; i++)
            {
                var code = list[i].Code;
                var q = p;
                int j;
                for (j = 0; j < code.Length; j++)
                {
                    q--;
                    if (q == -1)
                    {
                        q = buffer.Length - 1;
                    }
                    var ch = code[code.Length - j - 1];
                    if (buffer[q] != ch && ch != '?')
                    {
                        break;
                    }
                }

                if (j == code.Length)
                {
                    var typed = new char[code.Length];
                    var k = code.Length;
                    q = p;
                    for (j = 0; j < code.Length; j++)
                    {
                        k--;
                        q--;
                        if (q == -1)
                        {
                            q = buffer.Length - 1;
                        }
                        typed[k] = buffer[q];
                    }

                    if (world.Options.Skill != GameSkill.Nightmare || list[i].AvailableOnNightmare)
                    {
                        list[i].Action(this, new string(typed));
                    }
                }
            }
        }

        private void GiveWeapons()
        {
            var player = world.ConsolePlayer;
            if (world.Options.GameMode == GameMode.Commercial)
            {
                for (var i = 0; i < (int)WeaponType.Count; i++)
                {
                    player.WeaponOwned[i] = true;
                }
            }
            else
            {
                for (var i = 0; i <= (int)WeaponType.Missile; i++)
                {
                    player.WeaponOwned[i] = true;
                }
                player.WeaponOwned[(int)WeaponType.Chainsaw] = true;
                if (world.Options.GameMode != GameMode.Shareware)
                {
                    player.WeaponOwned[(int)WeaponType.Plasma] = true;
                    player.WeaponOwned[(int)WeaponType.Bfg] = true;
                }
            }

            player.Backpack = true;
            for (var i = 0; i < (int)AmmoType.Count; i++)
            {
                player.MaxAmmo[i] = 2 * DoomInfo.AmmoInfos.Max[i];
                player.Ammo[i] = 2 * DoomInfo.AmmoInfos.Max[i];
            }
        }

        private void FullAmmo()
        {
            GiveWeapons();
            var player = world.ConsolePlayer;
            player.ArmorType = DoomInfo.DeHackEdConst.IdfaArmorClass;
            player.ArmorPoints = DoomInfo.DeHackEdConst.IdfaArmor;
            player.SendMessage(DoomInfo.Strings.STSTR_FAADDED);
        }

        private void FullAmmoAndKeys()
        {
            GiveWeapons();
            GiveKeys();
            var player = world.ConsolePlayer;
            player.ArmorType = DoomInfo.DeHackEdConst.IdkfaArmorClass;
            player.ArmorPoints = DoomInfo.DeHackEdConst.IdkfaArmor;
            player.SendMessage(DoomInfo.Strings.STSTR_KFAADDED);
        }

        private void GodMode()
        {
            var player = world.ConsolePlayer;
            if ((player.Cheats & CheatFlags.GodMode) != 0)
            {
                player.Cheats &= ~CheatFlags.GodMode;
                player.SendMessage(DoomInfo.Strings.STSTR_DQDOFF);
            }
            else
            {
                player.Cheats |= CheatFlags.GodMode;
                player.Health = Math.Max(DoomInfo.DeHackEdConst.GodModeHealth, player.Health);
                player.Mobj.Health = player.Health;
                player.SendMessage(DoomInfo.Strings.STSTR_DQDON);
            }
        }

        private void NoClip()
        {
            var player = world.ConsolePlayer;
            if ((player.Cheats & CheatFlags.NoClip) != 0)
            {
                player.Cheats &= ~CheatFlags.NoClip;
                player.SendMessage(DoomInfo.Strings.STSTR_NCOFF);
            }
            else
            {
                player.Cheats |= CheatFlags.NoClip;
                player.SendMessage(DoomInfo.Strings.STSTR_NCON);
            }
        }

        private void FullMap()
        {
            world.AutoMap.ToggleCheat();
        }

        private void ShowPowerUpList()
        {
            var player = world.ConsolePlayer;
            player.SendMessage(DoomInfo.Strings.STSTR_BEHOLD);
        }

        private void DoPowerUp(char typed)
        {
            switch (typed)
            {
                case 'v':
                    ToggleInvulnerability();
                    break;
                case 's':
                    ToggleStrength();
                    break;
                case 'i':
                    ToggleInvisibility();
                    break;
                case 'r':
                    ToggleIronFeet();
                    break;
                case 'a':
                    ToggleAllMap();
                    break;
                case 'l':
                    ToggleInfrared();
                    break;
            }
        }

        /// <summary>
        /// All six keycards and skulls.
        ///
        /// Split out of <see cref="FullAmmoAndKeys"/> so the console can give keys
        /// without handing over a full arsenal - "give keys" is what a player wants
        /// when they are stuck at a door, not "idkfa".
        /// </summary>
        private void GiveKeys()
        {
            var player = world.ConsolePlayer;

            for (var i = 0; i < (int)CardType.Count; i++)
            {
                player.Cards[i] = true;
            }
        }

        private void ToggleInvulnerability()
        {
            var player = world.ConsolePlayer;
            if (player.Powers[(int)PowerType.Invulnerability] > 0)
            {
                player.Powers[(int)PowerType.Invulnerability] = 0;
            }
            else
            {
                player.Powers[(int)PowerType.Invulnerability] = DoomInfo.PowerDuration.Invulnerability;
            }
            player.SendMessage(DoomInfo.Strings.STSTR_BEHOLDX);
        }

        private void ToggleStrength()
        {
            var player = world.ConsolePlayer;
            if (player.Powers[(int)PowerType.Strength] != 0)
            {
                player.Powers[(int)PowerType.Strength] = 0;
            }
            else
            {
                player.Powers[(int)PowerType.Strength] = 1;
            }
            player.SendMessage(DoomInfo.Strings.STSTR_BEHOLDX);
        }

        private void ToggleInvisibility()
        {
            var player = world.ConsolePlayer;
            if (player.Powers[(int)PowerType.Invisibility] > 0)
            {
                player.Powers[(int)PowerType.Invisibility] = 0;
                player.Mobj.Flags &= ~MobjFlags.Shadow;
            }
            else
            {
                player.Powers[(int)PowerType.Invisibility] = DoomInfo.PowerDuration.Invisibility;
                player.Mobj.Flags |= MobjFlags.Shadow;
            }
            player.SendMessage(DoomInfo.Strings.STSTR_BEHOLDX);
        }

        private void ToggleIronFeet()
        {
            var player = world.ConsolePlayer;
            if (player.Powers[(int)PowerType.IronFeet] > 0)
            {
                player.Powers[(int)PowerType.IronFeet] = 0;
            }
            else
            {
                player.Powers[(int)PowerType.IronFeet] = DoomInfo.PowerDuration.IronFeet;
            }
            player.SendMessage(DoomInfo.Strings.STSTR_BEHOLDX);
        }

        private void ToggleAllMap()
        {
            var player = world.ConsolePlayer;
            if (player.Powers[(int)PowerType.AllMap] != 0)
            {
                player.Powers[(int)PowerType.AllMap] = 0;
            }
            else
            {
                player.Powers[(int)PowerType.AllMap] = 1;
            }
            player.SendMessage(DoomInfo.Strings.STSTR_BEHOLDX);
        }

        private void ToggleInfrared()
        {
            var player = world.ConsolePlayer;
            if (player.Powers[(int)PowerType.Infrared] > 0)
            {
                player.Powers[(int)PowerType.Infrared] = 0;
            }
            else
            {
                player.Powers[(int)PowerType.Infrared] = DoomInfo.PowerDuration.Infrared;
            }
            player.SendMessage(DoomInfo.Strings.STSTR_BEHOLDX);
        }

        private void GiveChainsaw()
        {
            var player = world.ConsolePlayer;
            player.WeaponOwned[(int)WeaponType.Chainsaw] = true;
            player.SendMessage(DoomInfo.Strings.STSTR_CHOPPERS);
        }

        private void KillMonsters()
        {
            var player = world.ConsolePlayer;
            var count = 0;
            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;
                if (mobj != null &&
                    mobj.Player == null &&
                    ((mobj.Flags & MobjFlags.CountKill) != 0 || mobj.Type == MobjType.Skull) &&
                    mobj.Health > 0)
                {
                    world.ThingInteraction.DamageMobj(mobj, null, player.Mobj, 10000);
                    count++;
                }
            }
            player.SendMessage(count + " monsters killed");
        }

        private void ChangeLevel(string typed)
        {
            if (world.Options.GameMode == GameMode.Commercial)
            {
                int map;
                if (!int.TryParse(typed.Substring(typed.Length - 2, 2), out map))
                {
                    return;
                }
                var skill = world.Options.Skill;
                world.Game.DeferedInitNew(skill, 1, map);
            }
            else
            {
                int episode;
                if (!int.TryParse(typed.Substring(typed.Length - 2, 1), out episode))
                {
                    return;
                }
                int map;
                if (!int.TryParse(typed.Substring(typed.Length - 1, 1), out map))
                {
                    return;
                }
                var skill = world.Options.Skill;
                world.Game.DeferedInitNew(skill, episode, map);
            }
        }

        private void ChangeMusic(string typed)
        {
            var options = new GameOptions();
            options.GameMode = world.Options.GameMode;
            if (world.Options.GameMode == GameMode.Commercial)
            {
                int map;
                if (!int.TryParse(typed.Substring(typed.Length - 2, 2), out map))
                {
                    return;
                }
                options.Map = map;
            }
            else
            {
                int episode;
                if (!int.TryParse(typed.Substring(typed.Length - 2, 1), out episode))
                {
                    return;
                }
                int map;
                if (!int.TryParse(typed.Substring(typed.Length - 1, 1), out map))
                {
                    return;
                }
                options.Episode = episode;
                options.Map = map;
            }
            world.Options.Music.StartMusic(Map.GetMapBgm(options), true);
            world.ConsolePlayer.SendMessage(DoomInfo.Strings.STSTR_MUS);
        }



        private class CheatInfo
        {
            public readonly string Code;
            public readonly Action<Cheat, string> Action;
            public readonly bool AvailableOnNightmare;

            public CheatInfo(string code, Action<Cheat, string> action, bool availableOnNightmare)
            {
                Code = code;
                Action = action;
                AvailableOnNightmare = availableOnNightmare;
            }
        }
    }
}
