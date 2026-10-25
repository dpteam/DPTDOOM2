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
    /// Boom's numeric model: which sector's picture a generalized floor or ceiling should copy.
    ///
    /// A generalized mover can change the picture when it arrives, and there are two ways to say
    /// which picture. The **trigger** model takes it from the line's own front sector, which is
    /// what this port did at first and what Boom does when the model bit is clear. The **numeric**
    /// model instead looks for a neighbouring sector whose floor (or ceiling) sits at the
    /// destination height, and copies that one's picture.
    ///
    /// The difference is not decoration. The idiom the numeric model exists for is a pool that
    /// drains and turns from water into slime as it drops: there is no sector at the line with a
    /// slime floor, because the slime appears only when the surface is lower than the ledge. The
    /// trigger model copies the water again and the pool looks unchanged.
    ///
    /// The rules are from `P_FindModelFloorSector` and `P_FindModelCeilingSector` in
    /// `refs/prboom-plus/prboom2/src/p_spec.c`, and they carry one piece of Boom's own history
    /// worth knowing: the search walks the sector's **two-sided** lines only, because a one-sided
    /// line has no other sector to be the model.
    /// </summary>
    public static class NumericModel
    {
        /// <summary>
        /// Whether a floor's numeric model looks at ceilings rather than floors.
        ///
        /// Boom's rule, and it reads backwards until you see why: if the destination is a
        /// *ceiling* height, the floor cannot be level with any neighbour's floor - it is on its
        /// way to a ceiling - so the sector to copy is the one whose ceiling is where this floor
        /// will end up.
        /// </summary>
        public static bool FloorLooksAtCeiling(int target)
        {
            // FtoLnC = 3 (lowest neighbouring ceiling), FtoC = 4 (ceiling).
            return target == 3 || target == 4;
        }

        /// <summary>The same question for a ceiling: it looks at floors when its target is a floor.</summary>
        public static bool CeilingLooksAtFloor(int target)
        {
            // CtoHnF = 3 (highest neighbouring floor), CtoF = 4 (floor).
            return target == 3 || target == 4;
        }

        /// <summary>
        /// The first sector next to this one whose floor sits at this height, or null.
        ///
        /// "First" is Boom's definition too: it walks the sector's lines in their stored order and
        /// returns the first match. Choosing the nearest or the largest would be a better-looking
        /// rule and a different map than the one the author drew.
        /// </summary>
        public static Sector FloorAt(Sector sector, Fixed height)
        {
            return Find(sector, height, false);
        }

        /// <summary>The first sector next to this one whose ceiling sits at this height, or null.</summary>
        public static Sector CeilingAt(Sector sector, Fixed height)
        {
            return Find(sector, height, true);
        }

        private static Sector Find(Sector sector, Fixed height, bool ceiling)
        {
            if (sector == null)
            {
                return null;
            }

            var lines = sector.Lines;

            if (lines == null)
            {
                return null;
            }

            foreach (var line in lines)
            {
                // A one-sided line has nothing on the other side, and Boom skips it for that
                // reason: the model has to be a sector you could stand in.
                if (line.BackSector == null)
                {
                    continue;
                }

                var other = line.FrontSector == sector ? line.BackSector : line.FrontSector;

                if (other == null || other == sector)
                {
                    continue;
                }

                var candidate = ceiling ? other.CeilingHeight : other.FloorHeight;

                if (candidate == height)
                {
                    return other;
                }
            }

            return null;
        }
    }
}
