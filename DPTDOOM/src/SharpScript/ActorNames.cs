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

namespace DPTDOOM.Scripting
{
    /// <summary>
    /// DECORATE actor names, by actor type.
    ///
    /// A mod names actors in its own vocabulary - "DoomImp", "BaronBall", "Rocket" - and DECORATE
    /// resolves those against the game's actor list. This engine's actor table holds a number per
    /// type and no name at all, so the names have to come from somewhere.
    ///
    /// **They are not written from memory.** A wrong name does not fail loudly: it silently
    /// addresses another actor, and a mod that spawns a rocket would spawn something else. So the
    /// list is taken from `refs/doom_ace-master/engine/decodoom.h`, whose `doom_actor_name[]` is
    /// indexed by actor type in vanilla Doom's own order - the same order this engine's
    /// <see cref="MobjType"/> uses. Every entry was checked index by index against that enum, and
    /// the first 137 line up exactly. The reserve after them belongs to mods; their friendly names
    /// are registered when DECORATE is read, so a spawned custom actor can be named too.
    ///
    /// The names are ZDoom's, which is what a DECORATE file is written against. A mod may also use
    /// the engine's shorter spellings ("Imp", "Pinky"), so <see cref="aliases"/> carries the ones
    /// worth having; these are the only entries here that were written by hand, and they are few on
    /// purpose.
    /// </summary>
    public static class ActorNames
    {
        /// <summary>How many actor types have a name. The rest of the table is the mod reserve.</summary>
        public const int NamedTypes = 137;

        /// <summary>
        /// DECORATE name for each actor type, indexed as <see cref="MobjType"/> is.
        ///
        /// Taken from `decodoom.h`, in that file's order, which is this engine's order.
        /// </summary>
        private static readonly string[] byType =
        {            "DoomPlayer", "ZombieMan", "ShotgunGuy", "Archvile", "ArchvileFire", "Revenant",
            "RevenantTracer", "RevenantTracerSmoke", "Fatso", "FatShot", "ChaingunGuy", "DoomImp",
            "Demon", "Spectre", "Cacodemon", "BaronOfHell", "BaronBall", "HellKnight",
            "LostSoul", "SpiderMastermind", "Arachnotron", "Cyberdemon", "PainElemental", "WolfensteinSS",
            "CommanderKeen", "BossBrain", "BossEye", "BossTarget", "SpawnShot", "SpawnFire",
            "ExplosiveBarrel", "DoomImpBall", "CacodemonBall", "Rocket", "PlasmaBall", "BFGBall",
            "ArachnotronPlasma", "BulletPuff", "Blood", "TeleportFog", "ItemFog", "TeleportDest",
            "BFGExtra", "GreenArmor", "BlueArmor", "HealthBonus", "ArmorBonus", "BlueCard",
            "RedCard", "YellowCard", "YellowSkull", "RedSkull", "BlueSkull", "Stimpack",
            "Medikit", "Soulsphere", "InvulnerabilitySphere", "Berserk", "BlurSphere", "RadSuit",
            "Allmap", "Infrared", "Megasphere", "Clip", "ClipBox", "RocketAmmo",
            "RocketBox", "Cell", "CellPack", "Shell", "ShellBox", "Backpack",
            "BFG9000", "Chaingun", "Chainsaw", "RocketLauncher", "PlasmaRifle", "Shotgun",
            "SuperShotgun", "TechLamp", "TechLamp2", "Column", "TallGreenColumn", "ShortGreenColumn",
            "TallRedColumn", "ShortRedColumn", "SkullColumn", "HeartColumn", "EvilEye", "FloatingSkull",
            "TorchTree", "BlueTorch", "GreenTorch", "RedTorch", "ShortBlueTorch", "ShortGreenTorch",
            "ShortRedTorch", "Stalagtite", "TechPillar", "Candlestick", "Candelabra", "BloodyTwitch",
            "Meat2", "Meat3", "Meat4", "Meat5", "NonsolidMeat2", "NonsolidMeat4",
            "NonsolidMeat3", "NonsolidMeat5", "NonsolidTwitch", "DeadCacodemon", "DeadMarine", "DeadZombieMan",
            "DeadDemon", "DeadLostSoul", "DeadDoomImp", "DeadShotgunGuy", "GibbedMarine", "GibbedMarineExtra",
            "HeadsOnAStick", "Gibs", "HeadOnAStick", "HeadCandles", "DeadStick", "LiveStick",
            "BigTree", "BurningBarrel", "HangNoGuts", "HangBNoBrain", "HangTLookingDown", "HangTSkull",
            "HangTLookingUp", "HangTNoBrain", "ColonGibs", "SmallBloodPool", "BrainStem",
        };

        /// <summary>
        /// Names a file may also use, mapped onto the entries above.
        ///
        /// Written by hand, and deliberately short: these are the spellings that appear in real
        /// mods beside the canonical one. Anything not here and not above is refused, and refusing
        /// is the point - a guess would address the wrong actor without saying so.
        /// </summary>
        private static readonly Dictionary<string, string> aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "imp", "DoomImp" },
            { "pinky", "Demon" },
            { "demon", "Demon" },
            { "spectre", "Spectre" },
            { "baron", "BaronOfHell" },
            { "knight", "HellKnight" },
            { "caco", "Cacodemon" },
            { "pain", "PainElemental" },
            { "arachno", "Arachnotron" },
            { "mancubus", "Fatso" },
            { "revenanttracer", "RevenantTracer" },
            { "impball", "DoomImpBall" },
            { "cacoball", "CacodemonBall" },
            { "baronball", "BaronBall" },
            { "fatshot", "FatShot" },
            { "troopshot", "DoomImpBall" },
            { "headshot", "CacodemonBall" },
            { "skeleton", "LostSoul" }
        };

        /// <summary>
        /// Names introduced by DECORATE in this load, including actors with no DoomEdNum.
        ///
        /// A custom actor cannot be looked up by its friendly name in the vanilla table, because
        /// that table has no such name. Keeping the name beside the newly allocated type is what
        /// lets A_SpawnItemEx refer to an effect or a projectile defined in the same mod.
        /// </summary>
        private static Dictionary<string, int> customTypes =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Forgets names from the previous game, before this game's DECORATE is read.</summary>
        public static void ClearCustomTypes()
        {
            customTypes.Clear();
        }

        /// <summary>Registers a custom actor name for the current game.</summary>
        public static void Register(string name, int type)
        {
            if (!string.IsNullOrEmpty(name) && type >= 0)
            {
                customTypes[name] = type;
            }
        }

        /// <summary>Snapshot used by tests, because this table is static just like MobjInfos.</summary>
        public static Dictionary<string, int> SaveCustomTypes()
        {
            return new Dictionary<string, int>(customTypes, StringComparer.OrdinalIgnoreCase);
        }

        public static void RestoreCustomTypes(Dictionary<string, int> saved)
        {
            customTypes = saved == null
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(saved, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The actor type a DECORATE name means, or -1.
        ///
        /// Matched without regard to case, because DECORATE names are written in whatever case the
        /// mod author liked, and the canonical spelling here is only canonical.
        /// </summary>
        public static int TypeOf(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            for (var i = 0; i < byType.Length; i++)
            {
                if (string.Equals(byType[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            string canonical;

            if (!aliases.TryGetValue(name, out canonical))
            {
                int custom;

                if (customTypes.TryGetValue(name, out custom))
                {
                    return custom;
                }

                return -1;
            }

            for (var i = 0; i < byType.Length; i++)
            {
                if (string.Equals(byType[i], canonical, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The name of a type, or null when no custom name is registered for it.</summary>
        public static string NameOf(int type)
        {
            if (type >= 0 && type < byType.Length)
            {
                return byType[type];
            }

            foreach (var pair in customTypes)
            {
                if (pair.Value == type)
                {
                    return pair.Key;
                }
            }

            return null;
        }

        /// <summary>Every name a file may use, for a report that has to list them.</summary>
        public static IList<string> All
        {
            get
            {
                var names = new List<string>(byType.Length + aliases.Count);

                foreach (var name in byType)
                {
                    names.Add(name);
                }

                foreach (var pair in aliases)
                {
                    names.Add(pair.Key);
                }

                foreach (var pair in customTypes)
                {
                    names.Add(pair.Key);
                }

                return names;
            }
        }
    }
}