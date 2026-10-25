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
    /// ROFL mode: the joke where the level looks mirrored and the monsters are clowns.
    ///
    /// Turned on by typing the Konami code in the menus. Nothing hints at it, which is the
    /// point of an easter egg, but it announces itself when it happens - a secret nobody can
    /// tell they found is a bug report waiting to be written.
    ///
    /// ## What is mirrored, and what is not
    ///
    /// The **view** is mirrored, not the map. That choice is the whole design and it deserves
    /// the paragraph:
    ///
    /// Mirroring the geometry for real means mirroring the vertices *and* the BSP nodes, the
    /// blockmap and the segs' texture offsets - all four of which are read from the WAD and
    /// all four of which have to stay consistent with each other. Get one wrong and the
    /// renderer draws the inside of a wall; get the seg offsets wrong and every texture is
    /// stretched from the wrong end. It is a lot of subtle geometry for a joke.
    ///
    /// Mirroring the view gives the same thing the player feels - the corridor that went
    /// right now goes left, the door that was on your right is on your left - with none of
    /// that risk, and with one property that turned out to matter more: **the world is not
    /// touched at all**. The world hash is identical, so a demo recorded in ROFL mode plays
    /// back exactly, and nothing about lockstep has to know this mode exists.
    ///
    /// The price, stated plainly because it is a real difference: the automap had to be
    /// mirrored separately to stay consistent with the view, and the two players of a network
    /// game can disagree about which side of a corridor is which if only one of them has it
    /// on. Both are acceptable for a joke mode that nobody is required to use.
    ///
    /// ## Why the input is negated
    ///
    /// A mirrored image with ordinary controls is disorienting in a way that reads as broken
    /// rather than funny: press left, the view swings right. So turning and strafing are
    /// negated **before the command is built**, which makes the mirrored view behave like a
    /// normal one. Because the negation happens before the command exists, a demo records the
    /// negated command and replays correctly without knowing anything about this mode.
    /// </summary>
    public static class RoflMode
    {
        /// <summary>
        /// Applies the April 1 default, once, at startup.
        ///
        /// Only when the player has never made a choice. A mode that switches itself on every
        /// April 1 and cannot be switched off would be a prank played on the owner of the
        /// machine rather than an easter egg, so an explicit choice always wins - including an
        /// explicit "off" made last April.
        /// </summary>
        public static void ApplyDefault(Config config, DateTime now)
        {
            if (config.rofl_set)
            {
                return;
            }

            config.rofl_mode = now.Month == 4 && now.Day == 1;
        }

        /// <summary>
        /// Flips the mode and remembers that the player chose, so the date default stops
        /// applying.
        /// </summary>
        public static void Toggle(Config config)
        {
            config.rofl_mode = !config.rofl_mode;
            config.rofl_set = true;
        }

        /// <summary>The line shown when the mode changes, or null.</summary>
        public static string Message(bool enabled)
        {
            return enabled ? "ROFL MODE ENABLED" : "ROFL MODE DISABLED";
        }
    }
}
