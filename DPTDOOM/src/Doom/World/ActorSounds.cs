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

namespace DPTDOOM
{
    /// <summary>Which of an actor's sounds a caller means.</summary>
    public enum ActorSoundSlot
    {
        See,
        Attack,
        Pain,
        Death,
        Active,

        Count
    }

    /// <summary>
    /// An actor's sounds, named rather than numbered.
    ///
    /// <see cref="MobjInfo"/> holds a <see cref="Sfx"/> for each of its five sounds, and that is
    /// the right shape for the engine's own content: the game chooses a sound by meaning, and
    /// SNDINFO decides which lump that is. A mod goes further - `SeeSound "imp/sight"` in DECORATE
    /// names a sound that has no enumeration member - so the name is kept here, beside the number,
    /// instead of widening <see cref="MobjInfo"/> (whose constructor is called once per actor in
    /// the table, and five extra parameters would have to be threaded through every one).
    ///
    /// A name, when set, **wins** over the number. That ordering is what makes the conversion
    /// honest: the script sets the name, and the engine's fallback stays whatever the actor
    /// inherited, so removing the name restores the old sound rather than leaving a hole.
    ///
    /// This is presentation only. Nothing here is consulted by the simulation except to choose a
    /// sound, and the choice never draws from the world's random generator - a mod's sound must
    /// not be able to move the world.
    /// </summary>
    public static class ActorSounds
    {
        /// <summary>Type index to its five names; null entries mean "not overridden".</summary>
        private static readonly Dictionary<int, string[]> names = new Dictionary<int, string[]>();

        public static void Clear()
        {
            names.Clear();
        }

        /// <summary>How many slots were overridden, for a report a person can act on.</summary>
        public static int Count
        {
            get
            {
                var count = 0;

                foreach (var entry in names.Values)
                {
                    foreach (var name in entry)
                    {
                        if (name != null)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }

        /// <summary>The name for this slot, or null when the actor's own <see cref="Sfx"/> applies.</summary>
        public static string Get(int type, ActorSoundSlot slot)
        {
            string[] entry;

            if (!names.TryGetValue(type, out entry))
            {
                return null;
            }

            return entry[(int)slot];
        }

        public static bool Has(int type, ActorSoundSlot slot)
        {
            return Get(type, slot) != null;
        }

        /// <summary>
        /// Names a sound for this slot. An empty name removes the override, which is how a script
        /// says "never mind, use the actor's own".
        /// </summary>
        public static void Set(int type, ActorSoundSlot slot, string name)
        {
            if (type < 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(name))
            {
                string[] existing;

                if (names.TryGetValue(type, out existing))
                {
                    existing[(int)slot] = null;
                }

                return;
            }

            string[] entry;

            if (!names.TryGetValue(type, out entry))
            {
                entry = new string[(int)ActorSoundSlot.Count];
                names[type] = entry;
            }

            entry[(int)slot] = name;
        }

        /// <summary>Every override, for a test that has to put the table back.</summary>
        public static IList<KeyValuePair<int, string[]>> Entries
        {
            get
            {
                var list = new List<KeyValuePair<int, string[]>>();

                foreach (var entry in names)
                {
                    list.Add(entry);
                }

                return list;
            }
        }
    }
}
