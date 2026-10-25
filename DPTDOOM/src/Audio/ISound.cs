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
    public interface ISound
    {
        public void SetListener(Mobj listener);
        public void Update();
        public void StartSound(Sfx sfx);
        public void StartSound(Mobj mobj, Sfx sfx, SfxType type);
        public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume);

        /// <summary>
        /// Plays a sound by name, rather than one of the engine's own.
        ///
        /// The engine's sounds are an enumeration because the game chooses them by meaning: a door
        /// opening is <c>Sfx.DOROPN</c>, and SNDINFO only decides which lump that is. A mod can go
        /// further and name a sound the engine has never heard of - a splash named in TERRAIN, or
        /// an actor's own sound from DECORATE - and there is no enumeration member for that. This
        /// is the way in.
        ///
        /// The name is a **sound name**: SNDINFO decides what it means, including the several
        /// recordings <c>$random</c> can give it. Only when SNDINFO says nothing about the name is
        /// it taken to be a lump. That order is why the same call serves both "the mod defined
        /// this name" and "the mod wrote a DS-prefixed lump".
        /// </summary>
        public void StartSound(Mobj mobj, string soundName, SfxType type, int volume);
        public void StopSound(Mobj mobj);
        public void Reset();
        public void Pause();
        public void Resume();

        public int MaxVolume { get; }
        public int Volume { get; set; }
    }
}
