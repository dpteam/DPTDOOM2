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
    /// <summary>What an elevator does when triggered.</summary>
    public enum ElevatorType
    {
        /// <summary>Down to the next lower neighbouring floor (231/232).</summary>
        Down,

        /// <summary>Up to the next higher neighbouring floor (227/228).</summary>
        Up,

        /// <summary>
        /// To the floor height of the activating line's own sector (235/236),
        /// which is how a switch calls a lift from across the room.
        /// </summary>
        Current
    }

    /// <summary>
    /// A floor and a ceiling moving together, keeping the space between them.
    ///
    /// This is Boom's elevator (227/228, 231/232, 235/236). It is not two movers
    /// that happen to start at once: the gap between floor and ceiling stays
    /// constant, so a room 128 units tall is still 128 units tall after it has
    /// travelled, just at a different height. That is what makes it look like a
    /// lift rather than like a room being squashed.
    ///
    /// The order the two planes move in is the part that matters and the part that
    /// is easy to get wrong. Going up, the floor moves first and the ceiling
    /// follows, so the gap never shrinks below its final size. Going down, the
    /// ceiling moves first and the floor follows, for the same reason in reverse.
    /// Move them in the wrong order and things standing inside get crushed by a
    /// lift that was supposed to carry them.
    ///
    /// If the leading plane is blocked, the other one does not move at all. Boom
    /// says the same in a comment: "don't move ceil if blocked". A half-moved
    /// elevator is worse than a stuck one.
    /// </summary>
    public sealed class Elevator : Thinker
    {
        private readonly World world;

        private ElevatorType type;
        private Sector sector;
        private Fixed speed;
        private Fixed floorDestHeight;
        private Fixed ceilingDestHeight;
        private int direction;

        public Elevator(World world)
        {
            this.world = world;
        }

        public override void Run()
        {
            var sa = world.SectorAction;

            SectorActionResult result;

            if (direction < 0)
            {
                // Down: the ceiling leads. Moving it first keeps the gap from
                // closing on whatever is inside.
                result = sa.MovePlane(
                    sector, speed, ceilingDestHeight, false, 1, direction);

                if (result == SectorActionResult.OK || result == SectorActionResult.PastDestination)
                {
                    sa.MovePlane(sector, speed, floorDestHeight, false, 0, direction);
                }
            }
            else
            {
                // Up: the floor leads.
                result = sa.MovePlane(
                    sector, speed, floorDestHeight, false, 0, direction);

                if (result == SectorActionResult.OK || result == SectorActionResult.PastDestination)
                {
                    sa.MovePlane(sector, speed, ceilingDestHeight, false, 1, direction);
                }
            }

            if (((world.LevelTime + sector.Number) & 7) == 0)
            {
                world.StartSound(sector.SoundOrigin, Sfx.STNMOV, SfxType.Misc);
            }

            if (result == SectorActionResult.PastDestination)
            {
                // The elevator owns both the floor and the ceiling slot, so both
                // have to be released or the sector can never move again.
                sector.SpecialData = null;

                world.Thinkers.Remove(this);
                sector.DisableFrameInterpolationForOneFrame();

                world.StartSound(sector.SoundOrigin, Sfx.PSTOP, SfxType.Misc);
            }
        }

        public ElevatorType Type
        {
            get { return type; }
            set { type = value; }
        }

        public Sector Sector
        {
            get { return sector; }
            set { sector = value; }
        }

        public Fixed Speed
        {
            get { return speed; }
            set { speed = value; }
        }

        public Fixed FloorDestHeight
        {
            get { return floorDestHeight; }
            set { floorDestHeight = value; }
        }

        public Fixed CeilingDestHeight
        {
            get { return ceilingDestHeight; }
            set { ceilingDestHeight = value; }
        }

        public int Direction
        {
            get { return direction; }
            set { direction = value; }
        }
    }
}
