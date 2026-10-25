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
    /// Samples for a sound **name**, resolved the way the game resolves its own.
    ///
    /// Two kinds of caller need this. The engine asks for a sound by meaning ("a door opening")
    /// and SNDINFO says which lumps that is. A mod asks for one by name - TERRAIN names its
    /// splashes, and DECORATE names an actor's sounds - and the same SNDINFO answers, because in
    /// ZDoom `sound waterspl` and `SeeSound "imp/sight"` are sound *names*, not lump names.
    ///
    /// Resolution order, and the difference matters:
    ///
    ///  1. **SNDINFO**, which may give several lumps for one name. That is the whole point of
    ///     `$random`: four footsteps are four recordings, not one played four times;
    ///  2. otherwise **the name is taken as a lump**. A mod that writes `DSDOROPN` where a sound
    ///     name was expected still plays something, and that is the convention every WAD already
    ///     follows.
    ///
    /// Two things make this its own class rather than a few lines inside the mixer: it is lazy,
    /// because a WAD carries hundreds of sound lumps that are never played and the names are not
    /// known until the content is read; and it can be tested without a sound device, which the
    /// mixer cannot.
    /// </summary>
    public sealed class NamedSounds
    {
        private readonly Wad wad;

        /// <summary>Name to its samples. An entry with no samples means "asked, nothing there".</summary>
        private readonly Dictionary<string, SoundSample[]> cache =
            new Dictionary<string, SoundSample[]>(StringComparer.OrdinalIgnoreCase);

        private readonly object gate = new object();

        public NamedSounds(Wad wad)
        {
            this.wad = wad;
        }

        /// <summary>How many names have been asked for, found or not.</summary>
        public int Asked
        {
            get
            {
                lock (gate)
                {
                    return cache.Count;
                }
            }
        }

        /// <summary>
        /// The samples for a sound name, or an empty array when there are none.
        ///
        /// Empty rather than null so a caller can pick from it without a check, and cached either
        /// way so a name that is not there is looked up once rather than on every step into the
        /// liquid.
        /// </summary>
        public SoundSample[] Get(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Empty;
            }

            lock (gate)
            {
                SoundSample[] cached;

                if (cache.TryGetValue(name, out cached))
                {
                    return cached;
                }

                var samples = Resolve(name);

                cache[name] = samples;
                return samples;
            }
        }

        private static readonly SoundSample[] Empty = new SoundSample[0];

        private SoundSample[] Resolve(string name)
        {
            // SNDINFO first: if the file described this name, that is what it means. Falling
            // through to the lump only when it said nothing keeps the DS-prefix convention working
            // for content that never had an SNDINFO.
            var lumps = SoundInfo.LumpsFor(name);

            if (lumps != null)
            {
                var samples = new List<SoundSample>();

                foreach (var lump in lumps)
                {
                    var sample = Load(lump);

                    if (sample != null)
                    {
                        samples.Add(sample);
                    }
                }

                // A name SNDINFO described but whose lumps are all absent is still "nothing to
                // play", not a reason to try the name as a lump: the modder said what it is.
                return samples.ToArray();
            }

            var single = Load(name);

            return single == null ? Empty : new[] { single };
        }

        private SoundSample Load(string lumpName)
        {
            var number = wad.GetLumpNumber(lumpName.ToUpperInvariant());

            if (number == -1)
            {
                return null;
            }

            return SoundSample.Parse(wad.ReadLump(number));
        }
    }
}
