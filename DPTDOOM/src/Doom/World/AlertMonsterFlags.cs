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
    /// <summary>The flags of A_AlertMonsters, with ZDoom's numbers from constants.zs.</summary>
    public static class AlertMonsterFlags
    {
        /// <summary>A monster alerts others to itself instead of requiring a player target.</summary>
        public const int TargetEmitter = 1;

        /// <summary>A monster may alert others to a non-player target.</summary>
        public const int TargetNonPlayer = 2;

        /// <summary>Propagate the sound from the target's sector, not the caller's.</summary>
        public const int EmitFromTarget = 4;

        public const int Supported = TargetEmitter | TargetNonPlayer | EmitFromTarget;
    }
}
