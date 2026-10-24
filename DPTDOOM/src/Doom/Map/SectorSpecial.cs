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
    public enum SectorSpecial
    {
        Normal = 0
    }

    /// <summary>
    /// Reading Boom's sector special field.
    ///
    /// Boom keeps its flags in the same 16-bit value as the vanilla effect: 0x80
    /// marks a secret, 0x100 is friction, 0x200 is a pusher. A map that wants
    /// nukage plus wind writes 0x207, and a plain `switch ((int)special)` matches
    /// nothing - the damage silently disappears, which is worse than not
    /// supporting wind at all.
    ///
    /// Vanilla maps are unaffected: every vanilla value is below 32, so masking is a
    /// no-op for them. PrBoom dispatches on `special & 31` for the same reason.
    /// </summary>
    public static class SectorSpecials
    {
        /// <summary>Boom: this sector is a secret.</summary>
        public const int SecretMask = 0x80;

        /// <summary>Boom: this sector has its own friction.</summary>
        public const int FrictionMask = 0x100;

        /// <summary>Boom: this sector pushes things (wind, current, point source).</summary>
        public const int PushMask = 0x200;

        /// <summary>The vanilla effect number, with Boom's flags removed.</summary>
        public static int EffectOf(Sector sector)
        {
            return (int)sector.Special & 31;
        }

        /// <summary>True when the sector carries Boom's pusher flag.</summary>
        public static bool IsPusher(Sector sector)
        {
            return ((int)sector.Special & PushMask) != 0;
        }
    }
}
