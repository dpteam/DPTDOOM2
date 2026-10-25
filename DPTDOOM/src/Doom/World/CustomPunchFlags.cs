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



namespace DPTDOOM
{
    /// <summary>The flags of A_CustomPunch, with ZDoom's numbers from constants.zs.</summary>
    public static class CustomPunchFlags
    {
        /// <summary>Spend ammo only when the punch actually hits something.</summary>
        public const int UseAmmo = 1;

        /// <summary>Dagger-style target tracking and damage feedback; not implemented.</summary>
        public const int Dagger = 2;

        /// <summary>Pull the target toward the attacker; not implemented.</summary>
        public const int PullIn = 4;

        /// <summary>Do not randomise the puff's vertical position.</summary>
        public const int NoRandomPuffZ = 8;

        /// <summary>Do not turn the attacker to face the target.</summary>
        public const int NoTurn = 16;

        /// <summary>Steal armour instead of health; not implemented.</summary>
        public const int StealArmor = 32;

        /// <summary>The flags this engine honours.</summary>
        public const int Supported = NoRandomPuffZ | NoTurn;
    }
}
