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
    /// Debug toggles that are not part of the game state.
    ///
    /// Deliberately kept out of GameOptions: nothing here is saved, nothing is
    /// sent over the network and nothing reaches a demo. Several of these change
    /// what is simulated (freezing monsters, slow motion), so they are only
    /// allowed in a plain single-player game — a demo or a network session that
    /// diverged would be worse than useless.
    ///
    /// A static holder is acceptable here precisely because this is developer
    /// state, not game state: there is one screen, one set of eyes.
    /// </summary>
    public static class DebugSettings
    {
        /// <summary>Draw wall outlines instead of textures.</summary>
        public static bool Wireframe;

        /// <summary>Draw only the automap instead of the 3D view.</summary>
        public static bool MapOnly;

        /// <summary>Monsters stop thinking and stand still.</summary>
        public static bool FreezeMonsters;

        /// <summary>Skip every Nth tic. 1 is normal speed, 2 is half, 4 is a quarter.</summary>
        public static int SlowMotion = 1;

        /// <summary>Show the map's linedef specials and sector tags on the automap.</summary>
        public static bool ShowTriggers;

        /// <summary>Draw the blockmap grid over the automap.</summary>
        public static bool ShowBlockmap;

        /// <summary>Marks the geometry pass so the overlay can report it.</summary>
        public static int LastLineCount;

        /// <summary>
        /// True when the current state is a plain single-player game, where a
        /// simulation-changing debug toggle cannot desynchronise anything.
        /// </summary>
        public static bool SimulationChangesAllowed(Doom doom)
        {
            if (doom == null || doom.State != DoomState.Game)
            {
                return false;
            }

            var options = doom.Options;
            return options != null && !options.NetGame && !options.DemoPlayback;
        }

        public static void Reset()
        {
            Wireframe = false;
            MapOnly = false;
            FreezeMonsters = false;
            SlowMotion = 1;
            ShowTriggers = false;
            ShowBlockmap = false;
            LastLineCount = 0;
        }
    }
}
