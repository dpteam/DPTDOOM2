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
    [Flags]
    public enum MobjFlags
    {
        // Call P_SpecialThing when touched.
        Special = 1,

        // Blocks.
        Solid = 2,

        // Can be hit.
        Shootable = 4,

        // Don't use the sector links (invisible but touchable).
        NoSector = 8,

        // Don't use the blocklinks (inert but displayable).
        NoBlockMap = 16,

        // Not to be activated by sound, deaf monster.
        Ambush = 32,

        // Will try to attack right back.
        JustHit = 64,

        // Will take at least one step before attacking.
        JustAttacked = 128,

        // On level spawning (initial position),
        // hang from ceiling instead of stand on floor.
        SpawnCeiling = 256,

        // Don't apply gravity (every tic),
        // that is, object will float, keeping current height
        // or changing it actively.
        NoGravity = 512,

        // Movement flags.
        // This allows jumps from high places.
        DropOff = 0x400,

        // For players, will pick up items.
        PickUp = 0x800,

        // Player cheat. ???
        NoClip = 0x1000,

        // Player: keep info about sliding along walls.
        Slide = 0x2000,

        // Allow moves to any height, no gravity.
        // For active floaters, e.g. cacodemons, pain elementals.
        Float = 0x4000,

        // Don't cross lines
        // ??? or look at heights on teleport.
        Teleport = 0x8000,

        // Don't hit same species, explode on block.
        // Player missiles as well as fireballs of various kinds.
        Missile = 0x10000,

        // Dropped by a demon, not level spawned.
        // E.g. ammo clips dropped by dying former humans.
        Dropped = 0x20000,

        // Use fuzzy draw (shadow demons or spectres),
        // temporary player invisibility powerup.
        Shadow = 0x40000,

        // Flag: don't bleed when shot (use puff),
        // barrels and shootable furniture shall not bleed.
        NoBlood = 0x80000,

        // Don't stop moving halfway off a step,
        // that is, have dead bodies slide down all the way.
        Corpse = 0x100000,

        // Floating to a height for a move, ???
        // don't auto float to target's height.
        InFloat = 0x200000,

        // On kill, count this enemy object
        // towards intermission kill total.
        // Happy gathering.
        CountKill = 0x400000,

        // On picking up, count this item object
        // towards intermission item total.
        CountItem = 0x800000,

        // Special handling: skull in flight.
        // Neither a cacodemon nor a missile.
        SkullFly = 0x1000000,

        // Don't spawn this object
        // in death match mode (e.g. key cards).
        NotDeathmatch = 0x2000000,

        // Player sprites in multiplayer modes are modified
        // using an internal color lookup table for re-indexing.
        // If 0x4 0x8 or 0xc,
        // use a translation table for player colormaps
        Translation = 0xc000000,

        // Hmm ???.
        TransShift = 26,

        // ---------------------------------------------------------------
        // Flags this engine did not have before DECORATE needed them.
        //
        // Appended after Translation, which occupies bits 26 and 27, so these
        // start at 28. The order above is vanilla Doom's and is left alone: the
        // numbers go into save files and into the world hash, and moving one
        // would make an old save describe a different creature.
        //
        // A vanilla actor has these bits clear, so the world is bit for bit what
        // it was - that is what makes adding them safe.
        // ---------------------------------------------------------------

        /// <summary>
        /// Does not enter its pain state, and plays no pain sound.
        ///
        /// DECORATE's +PAINLESS. The engine already had PainChance, which is a
        /// probability; this is the certainty, and a mod means it - a boss that
        /// shrugs off every hit is a design decision, not a small chance.
        /// </summary>
        Painless = 0x10000000,

        /// <summary>
        /// Cannot be killed: damage takes it down to one point of health and stops.
        ///
        /// DECORATE's +BUDDHA. Distinct from invulnerability, which a player gets
        /// from a powerup and which refuses all damage: this one still flinches,
        /// still shows damage, and simply will not die.
        /// </summary>
        Buddha = 0x20000000,

        /// <summary>
        /// Monsters do not pick it as a target, and a monster that has it does not
        /// look for one.
        ///
        /// DECORATE's +NOTARGET. Not the same as Ambush, which only stops the
        /// monster noticing a sound.
        /// </summary>
        NoTarget = 0x40000000
    }
}
