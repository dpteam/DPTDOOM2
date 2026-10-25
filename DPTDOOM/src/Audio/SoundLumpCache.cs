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
using System.Collections.Generic;

namespace DPTDOOM.Audio
{
    /// <summary>
    /// Sound lumps loaded by name, once each.
    ///
    /// The engine's own sounds are an enumeration: the game asks for "a door opening" and SNDINFO
    /// decides which lump that is. A mod can go further and name a lump the engine has never heard
    /// of - TERRAIN names its splashes this way - and for that there is no enumeration member.
    ///
    /// Two things make this its own class rather than a few lines inside the mixer:
    ///
    ///  - **it is lazy**, because the names are not known until the content is read, and a WAD
    ///    carries hundreds of sound lumps that are never played;
    ///  - **a missing lump is remembered as missing**, so a mod that names a splash it forgot to
    ///    ship does not cause a WAD lookup on every step into the liquid.
    ///
    /// Keeping it separate also means it can be tested without a sound device, which the mixer
    /// itself cannot.
    /// </summary>
    public sealed class SoundLumpCache
    {
        private readonly Wad wad;
        private readonly Dictionary<string, SoundSample> samples =
            new Dictionary<string, SoundSample>();

        private readonly object gate = new object();

        public SoundLumpCache(Wad wad)
        {
            this.wad = wad;
        }

        /// <summary>How many lumps were found and parsed, for a report a person can act on.</summary>
        public int Loaded
        {
            get
            {
                lock (gate)
                {
                    var loaded = 0;

                    foreach (var sample in samples.Values)
                    {
                        if (sample != null)
                        {
                            loaded++;
                        }
                    }

                    return loaded;
                }
            }
        }

        /// <summary>How many names were asked for, found or not.</summary>
        public int Asked
        {
            get
            {
                lock (gate)
                {
                    return samples.Count;
                }
            }
        }

        /// <summary>
        /// The sample for this lump, or null when the WAD has no such lump or it does not parse.
        ///
        /// Case is ignored: lump names in a WAD are upper case, and a file that wrote `watersplash`
        /// meant the lump called WATERSPLASH. The result, including null, is cached.
        /// </summary>
        public SoundSample Get(string lumpName)
        {
            if (string.IsNullOrEmpty(lumpName))
            {
                return null;
            }

            var key = lumpName.ToUpperInvariant();

            lock (gate)
            {
                SoundSample cached;

                if (samples.TryGetValue(key, out cached))
                {
                    return cached;
                }

                SoundSample sample = null;

                var number = wad.GetLumpNumber(key);

                if (number != -1)
                {
                    sample = SoundSample.Parse(wad.ReadLump(number));
                }

                samples[key] = sample;
                return sample;
            }
        }
    }
}
