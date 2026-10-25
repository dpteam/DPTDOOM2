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

namespace DPTDOOM
{
    public static class TicCmdButtons
    {
        public static readonly byte Attack = 1;

        // Use button, to open doors, activate switches.
        public static readonly byte Use = 2;

        // Flag: game events, not really buttons.
        public static readonly byte Special = 128;
        public static readonly byte SpecialMask = 3;

        // Flag, weapon change pending.
        // If true, the next 3 bits hold weapon num.
        public static readonly byte Change = 4;

        // The 3bit weapon mask and shift, convenience.
        public static readonly byte WeaponMask = 8 + 16 + 32;
        public static readonly byte WeaponShift = 3;

        // Pause the game.
        public static readonly byte Pause = 1;

        /// <summary>
        /// The player in this slot has left for good, and their body should stop standing
        /// there.
        ///
        /// Carried **inside** the command rather than as a separate notice, and that is the
        /// whole reason it exists: every end must apply the removal at the same tic, or the
        /// worlds part company. A notice arrives at a different moment on every machine; a
        /// command is part of the agreed sequence.
        ///
        /// Set only by the host, only for slots it has given up on, and never by a local
        /// input - so a demo can never contain it.
        /// </summary>
        public static readonly byte Gone = 64;
    }
}
