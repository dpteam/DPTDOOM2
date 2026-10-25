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

namespace DPTDOOM.Audio
{
    public interface IMusic
    {
        void StartMusic(Bgm bgm, bool loop);

        /// <summary>
        /// Plays a music lump by name, with or without the "D_" prefix.
        ///
        /// Separate from <see cref="StartMusic(Bgm, bool)"/> because a mod's track is not in the
        /// engine's enumeration and never could be: the enum lists the tracks the IWAD ships.
        /// A backend that cannot play arbitrary lumps may treat this as a request for silence,
        /// which is what happens when the lump is missing anyway.
        /// </summary>
        void StartMusic(string lumpName, bool loop);

        public int MaxVolume { get; }
        public int Volume { get; set; }
    }
}
