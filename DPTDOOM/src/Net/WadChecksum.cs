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

namespace DPTDOOM.Net
{
    /// <summary>
    /// A cheap fingerprint of the loaded WAD set (names and sizes of every
    /// lump). Two players with different WADs would desynchronise immediately,
    /// so the session refuses to start unless these match.
    /// </summary>
    public static class WadChecksum
    {
        public static uint Compute(Wad wad)
        {
            var hash = 2166136261u;

            for (var i = 0; i < wad.LumpInfos.Count; i++)
            {
                var lump = wad.LumpInfos[i];
                var name = lump.Name ?? "";

                for (var c = 0; c < name.Length; c++)
                {
                    hash = (hash ^ name[c]) * 16777619u;
                }

                hash = (hash ^ (uint)lump.Size) * 16777619u;
            }

            return hash;
        }
    }
}
