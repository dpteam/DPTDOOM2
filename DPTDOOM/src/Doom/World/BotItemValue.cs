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

namespace DPTDOOM
{
    /// <summary>
    /// What a thing lying on the floor is worth to a bot, and how badly it wants
    /// it right now.
    ///
    /// The bot used to ignore pickups completely. It would walk past a medikit at
    /// twenty health, run out of shells, and die to the first demon it could not
    /// shoot. A player never does that, and the fix is not clever: notice the
    /// thing, decide whether the detour is worth it, go get it.
    ///
    /// Two decisions matter here, and both are deliberately simple:
    ///
    ///   - **What is it?** Read from the sprite name. This is how every Doom
    ///     source port identifies items, it survives DeHackEd and PWADs, and it
    ///     needs no table of thing numbers. A thing with no recognised sprite is
    ///     worth nothing, which is the safe default: the bot never chases
    ///     decorations.
    ///
    ///   - **Is it worth the walk?** Need times value divided by distance. A
    ///     medikit at full health is worth nothing; the same medikit at 30 health
    ///     is worth crossing the room for. Temperament's greed scales the whole
    ///     thing, so a greedy bot fetches a clip it does not need and a focused
    ///     one walks past a rocket launcher.
    /// </summary>
    public static class BotItemValue
    {
        /// <summary>What kind of thing this is, for scoring.</summary>
        public enum Kind
        {
            Nothing = 0,
            Health,
            Armor,
            Ammo,
            Key,
            Weapon,
            PowerUp
        }

        /// <summary>
        /// Classifies a thing by its sprite name.
        ///
        /// Names are the three-to-four letter sprite prefixes from the IWAD, which
        /// is the same vocabulary a PWAD author uses. Anything unrecognised is
        /// <see cref="Kind.Nothing"/>.
        /// </summary>
        public static Kind Classify(Mobj thing)
        {
            if (thing == null || thing.State == null)
            {
                return Kind.Nothing;
            }

            var index = (int)thing.State.Sprite;

            if (index < 0 || index >= DoomInfo.SpriteNames.Length)
            {
                return Kind.Nothing;
            }

            var name = DoomInfo.SpriteNames[index].ToString();

            if (name == null || name.Length < 3)
            {
                return Kind.Nothing;
            }

            switch (name)
            {
                // Keys first: they are the one thing a bot must never walk past,
                // because without them whole parts of the map are sealed.
                case "BKEY": case "BSKU":
                case "RKEY": case "RSKU":
                case "YKEY": case "YSKU":
                    return Kind.Key;

                case "BON1": case "BON2":
                case "STIM": case "MEDI":
                case "SOUL": case "MEGA":
                    return Kind.Health;

                case "ARM1": case "ARM2":
                    return Kind.Armor;

                case "CLIP": case "AMMO":
                case "SHEL":
                case "ROCK":
                case "CELL": case "CELP":
                case "BPAK":
                    return Kind.Ammo;

                case "SHOT": case "MGUN": case "LAUN":
                case "PLAS": case "BFUG": case "CSAW":
                    return Kind.Weapon;

                case "PINV": case "PSTR": case "SUIT":
                    return Kind.PowerUp;
            }

            return Kind.Nothing;
        }

        /// <summary>
        /// How much the bot wants this thing right now, from 0 to about 3.
        ///
        /// Need is what makes this useful rather than a magnet for every clip on
        /// the floor: the same medikit scores 0 at 100 health and 3 at 20.
        /// </summary>
        public static float Desire(Kind kind, Player player)
        {
            if (player == null)
            {
                return 0F;
            }

            switch (kind)
            {
                case Kind.Key:
                    // Always. A key is the difference between finishing the level
                    // and wandering it.
                    return 3F;

                case Kind.Health:
                    if (player.Health >= 95)
                    {
                        return 0F;
                    }

                    // Steep: at 20 health this is the most important thing on the
                    // map, at 80 it is barely worth a step sideways.
                    return (100 - player.Health) / 40F;

                case Kind.Armor:
                    if (player.ArmorPoints >= 180)
                    {
                        return 0F;
                    }

                    return player.ArmorPoints < 50 ? 1.2F : 0.5F;

                case Kind.Ammo:
                    // Only when something is running low, and never for a weapon
                    // the bot does not have.
                    return AmmoDesire(player);

                case Kind.Weapon:
                    // Worth a detour only if it is a real upgrade. The bot cannot
                    // tell which weapon a pickup is without the thing number, so
                    // this is deliberately modest and lets greed decide.
                    return 0.6F;

                case Kind.PowerUp:
                    return 0.8F;
            }

            return 0F;
        }

        public static float AmmoDesire(Player player)
        {
            var bullets = player.Ammo[(int)AmmoType.Clip];
            var shells = player.Ammo[(int)AmmoType.Shell];
            var rockets = player.Ammo[(int)AmmoType.Missile];
            var cells = player.Ammo[(int)AmmoType.Cell];

            var worst = 0F;

            if (player.WeaponOwned[(int)WeaponType.Shotgun] ||
                player.WeaponOwned[(int)WeaponType.SuperShotgun] ||
                player.WeaponOwned[(int)WeaponType.Chaingun])
            {
                worst = Math.Max(worst, Shortage(shells, 20));
            }

            if (player.WeaponOwned[(int)WeaponType.Chaingun])
            {
                worst = Math.Max(worst, Shortage(bullets, 40));
            }

            if (player.WeaponOwned[(int)WeaponType.Missile])
            {
                worst = Math.Max(worst, Shortage(rockets, 5));
            }

            if (player.WeaponOwned[(int)WeaponType.Plasma] ||
                player.WeaponOwned[(int)WeaponType.Bfg])
            {
                worst = Math.Max(worst, Shortage(cells, 40));
            }

            return worst;
        }

        /// <summary>How badly a pool of ammo is missed, 0 to 1.2.</summary>
        private static float Shortage(int have, int comfortable)
        {
            if (have >= comfortable)
            {
                return 0F;
            }

            return 1.2F * (1F - (float)have / comfortable);
        }
    }
}
