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
using System.Globalization;

namespace DPTDOOM
{
    /// <summary>
    /// A bot's character: five numbers that bias every decision it makes.
    ///
    /// This is the part that is not taken from anywhere. Every Doom bot I have
    /// seen — and every version of this one until now — decides the same way
    /// every time, so two bots on the same map play the same game. Real players
    /// do not: one runs past the monsters, one picks up everything, one retreats
    /// at half health. The difference is not a better algorithm, it is a
    /// different set of priorities.
    ///
    /// So each bot is given a temperament, drawn from a seed. Nothing here is
    /// clever; it is deliberately arbitrary. The value is that the same code
    /// produces visibly different play, and that the brain can then learn which
    /// kind of play this map rewards.
    ///
    /// The axes are named after what they change, not after a personality
    /// theory, because the mapping is what matters:
    ///
    ///   - <see cref="Greed"/>      — how far out of the way it goes for loot;
    ///   - <see cref="Caution"/>    — how early it breaks off a fight;
    ///   - <see cref="Aggression"/> — how close it wants to be to a monster;
    ///   - <see cref="Curiosity"/>  — how much of the map it wants to see;
    ///   - <see cref="Patience"/>   — how long it waits for a lift or a door.
    ///
    /// The seed is derived from the map and the bot's slot, so a bot keeps its
    /// character on a given map between runs, and two bots in the same game are
    /// not the same person.
    /// </summary>
    public sealed class BotTemperament
    {
        private BotTemperament()
        {
        }

        /// <summary>0 = ignores loot, 1 = detours across the level for it.</summary>
        public float Greed { get; private set; }

        /// <summary>0 = fights to the death, 1 = retreats at the first scratch.</summary>
        public float Caution { get; private set; }

        /// <summary>0 = keeps its distance, 1 = gets in the monster's face.</summary>
        public float Aggression { get; private set; }

        /// <summary>0 = beelines for the exit, 1 = wants to see everything.</summary>
        public float Curiosity { get; private set; }

        /// <summary>0 = gives up on a door at once, 1 = waits it out.</summary>
        public float Patience { get; private set; }

        /// <summary>
        /// Builds a temperament from a seed. The axes are independent draws, so
        /// the spread includes odd combinations — a greedy coward, a curious
        /// brawler — which is exactly the variety wanted.
        /// </summary>
        public static BotTemperament FromSeed(int seed)
        {
            var random = new Random(seed);

            // Discard a few draws first. System.Random seeded with nearby values
            // produces strongly correlated first numbers — two bots ended up with
            // 0.01 on the same two axes, which is the opposite of the variety this
            // exists for.
            for (var i = 0; i < 8; i++)
            {
                random.NextDouble();
            }

            return new BotTemperament
            {
                Greed = (float)random.NextDouble(),
                Caution = (float)random.NextDouble(),
                Aggression = (float)random.NextDouble(),
                Curiosity = (float)random.NextDouble(),
                Patience = (float)random.NextDouble()
            };
        }

        /// <summary>A temperament with every axis in the middle.</summary>
        public static BotTemperament Neutral
        {
            get { return new BotTemperament { Greed = 0.5F, Caution = 0.5F, Aggression = 0.5F, Curiosity = 0.5F, Patience = 0.5F }; }
        }

        /// <summary>
        /// A short name for the character, for the overlay and the console. It is
        /// built from the two strongest axes, which is how a person would
        /// describe somebody after watching them play.
        /// </summary>
        public string Describe()
        {
            // Named by the axes that are furthest from the middle, not by the
            // highest numbers: with five draws, the maximum is usually around
            // 0.8 for everybody, so "highest" describes most bots the same way.
            // Distance from 0.5 is what makes them different.
            var best = MostExtreme(-1);
            var second = MostExtreme(best);

            return Name(best) + "-" + Name(second);
        }

        private int MostExtreme(int exclude)
        {
            var index = -1;
            var value = -1F;

            for (var i = 0; i < 5; i++)
            {
                if (i == exclude)
                {
                    continue;
                }

                var distance = Math.Abs(Axis(i) - 0.5F);

                if (distance > value)
                {
                    value = distance;
                    index = i;
                }
            }

            return index < 0 ? 0 : index;
        }

        private float Axis(int index)
        {
            switch (index)
            {
                case 0: return Greed;
                case 1: return Caution;
                case 2: return Aggression;
                case 3: return Curiosity;
                default: return Patience;
            }
        }

        private string Name(int axis)
        {
            // Both directions are named: a bot with low greed is not "slightly
            // greedy", it is a bot that ignores loot, and that is worth saying.
            var high = Axis(axis) >= 0.5F;

            switch (axis)
            {
                case 0: return high ? "жадный" : "равнодушный";
                case 1: return high ? "осторожный" : "безрассудный";
                case 2: return high ? "злой" : "уравновешенный";
                case 3: return high ? "любопытный" : "прямой";
                default: return high ? "терпеливый" : "нетерпеливый";
            }
        }

        /// <summary>Full numbers, for traces and for the brain file.</summary>
        public string ToNumbers()
        {
            return Greed.ToString("0.00", CultureInfo.InvariantCulture) + "/" +
                   Caution.ToString("0.00", CultureInfo.InvariantCulture) + "/" +
                   Aggression.ToString("0.00", CultureInfo.InvariantCulture) + "/" +
                   Curiosity.ToString("0.00", CultureInfo.InvariantCulture) + "/" +
                   Patience.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Stable seed for a bot on a map. string.GetHashCode is randomised per
        /// process on modern .NET, so it would give a bot a new character on every
        /// launch and stop headless runs from reproducing.
        /// </summary>
        public static int SeedFor(string mapKey, int slot)
        {
            var hash = 2166136261u;

            foreach (var c in mapKey ?? "")
            {
                hash = (hash ^ c) * 16777619u;
            }

            hash = (hash ^ (uint)(slot + 1)) * 16777619u;
            hash = (hash ^ 0x5EED) * 16777619u;

            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
