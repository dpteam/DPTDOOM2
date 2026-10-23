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
    public enum FloorMoveType
    {
        // Lower floor to highest surrounding floor.
        LowerFloor,

        // Lower floor to lowest surrounding floor.
        LowerFloorToLowest,

        // Lower floor to highest surrounding floor very fast.
        TurboLower,

        // Raise floor to lowest surrounding ceiling.
        RaiseFloor,

        // Raise floor to next highest surrounding floor.
        RaiseFloorToNearest,

        // Raise floor to shortest height texture around it.
        RaiseToTexture,

        // Lower floor to lowest surrounding floor and
        // change floor texture.
        LowerAndChange,

        RaiseFloor24,
        RaiseFloor24AndChange,
        RaiseFloorCrush,

        // Raise to next highest floor, turbo-speed.
        RaiseFloorTurbo,
        DonutRaise,
        RaiseFloor512,

        /// <summary>
        /// Lower the floor to the next lower neighbouring floor, not the lowest
        /// one. Boom added it for 219 and 220; vanilla has no equivalent, because
        /// "lower to the highest neighbour" and "lower to the lowest neighbour"
        /// skip the step in between.
        /// </summary>
        LowerFloorToNearest,

        /// <summary>
        /// A Boom generalized floor: the destination, speed and crush flag were
        /// computed from the line's bit fields, so the type only says "move".
        /// </summary>
        Generalized,

        /// <summary>
        /// A Boom generalized floor that also replaces the sector's floor texture
        /// and special when it arrives.
        /// </summary>
        GeneralizedChange
    }
}
