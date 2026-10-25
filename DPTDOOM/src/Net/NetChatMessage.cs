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
    /// One received chat line, with the speaker kept separate from what they said.
    ///
    /// The two used to arrive glued together as "name: text", which meant every caller that wanted
    /// to do anything with the name - colour it, align it, sort by speaker - had to find the
    /// separator again, and a name containing that separator would be split in the wrong place.
    /// Keeping them apart is also what lets the wire format stay as it is: the sender's index
    /// travels, and the name is looked up on the receiving side where the welcome already put it.
    /// </summary>
    public struct NetChatMessage
    {
        /// <summary>Who said it, as the name is known to this machine.</summary>
        public string Name;

        /// <summary>What they said.</summary>
        public string Text;
    }
}
