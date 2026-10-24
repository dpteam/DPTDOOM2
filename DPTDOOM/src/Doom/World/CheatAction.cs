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
    /// One cheat, addressed by name.
    ///
    /// The classic codes (iddqd, idkfa) and the console commands ("god", "give all")
    /// both end up here, so there is one implementation of "what does god mode do" and
    /// one place that decides whether cheats are allowed at all. Two implementations
    /// would drift, and the one that drifted would be the one nobody tested.
    /// </summary>
    public enum CheatAction
    {
        /// <summary>Nothing. Returned by a lookup that found no match.</summary>
        None = 0,

        God,
        NoClip,

        /// <summary>Every weapon and full ammo, no keys or armour.</summary>
        Weapons,

        /// <summary>idfa: weapons, full ammo, armour.</summary>
        Idfa,

        /// <summary>idkfa: weapons, full ammo, armour, all keys.</summary>
        Idkfa,

        /// <summary>All six keys, nothing else.</summary>
        Keys,

        /// <summary>The automap cheat: full map, then things on it.</summary>
        FullMap,

        /// <summary>Prints what "powerup" accepts.</summary>
        PowerUpList,

        /// <summary>One power-up, chosen by a letter: v s i r a l.</summary>
        PowerUp,

        Chainsaw,
        KillMonsters,
        ChangeLevel,
        ChangeMusic
    }
}
