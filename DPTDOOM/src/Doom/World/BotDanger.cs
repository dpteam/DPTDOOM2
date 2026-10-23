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
using System.Text;

namespace DPTDOOM
{
    /// <summary>
    /// A bot's memory of where it gets hurt.
    ///
    /// This is learning of a different kind from the route replay in
    /// <see cref="BotBrain"/>. The brain remembers *what worked*; this remembers
    /// *what hurt*, and the two answer different questions. A route can be the
    /// shortest way across a level and still be the wrong way if a room full of
    /// imps lives on it.
    ///
    /// The mechanism is a heatmap over sectors:
    ///
    ///   - every time the bot loses health, the sector it is standing in gets
    ///     warmer;
    ///   - heat fades over time, so an old bad room does not scare the bot
    ///     forever, and a room it has since cleared stops being avoided;
    ///   - the heat becomes a cost multiplier in pathfinding, so a warm sector is
    ///     walked through only when there is no better way.
    ///
    /// It is the same shape as a pheromone trail, and it is deliberately crude:
    /// no attribution of who did the damage, no line of sight, no memory of the
    /// monster. The bot only knows "this place costs me health", which is exactly
    /// as much as a player knows after being shot from somewhere.
    ///
    /// Persisted with the brain, so a bot that died in the crusher room last time
    /// is warier of it this time.
    /// </summary>
    public sealed class BotDanger
    {
        /// <summary>Heat added per point of damage taken.</summary>
        private const float DamageWeight = 0.06F;

        /// <summary>Heat is multiplied by this every <see cref="DecayTics"/>.</summary>
        private const float DecayFactor = 0.75F;

        /// <summary>How often heat fades.</summary>
        private const int DecayTics = 10 * 35;

        /// <summary>Cap, so one bad room cannot make the whole map unroutable.</summary>
        private const float MaxHeat = 6F;

        private readonly float[] heat;
        private int lastDecay;
        private int lastHealth = 100;

        public BotDanger(int sectorCount)
        {
            heat = new float[sectorCount];
        }

        public int SectorCount { get { return heat.Length; } }

        /// <summary>Current heat of a sector, 0 when unknown.</summary>
        public float Heat(int sector)
        {
            if (sector < 0 || sector >= heat.Length)
            {
                return 0F;
            }

            return heat[sector];
        }

        /// <summary>
        /// Notes the bot's health so damage can be measured on the next call.
        /// Returns true when this observation made a sector warmer, so the caller
        /// knows to refresh the pathfinding costs.
        /// </summary>
        public bool Observe(int sector, int health, int levelTime)
        {
            if (levelTime - lastDecay > DecayTics)
            {
                lastDecay = levelTime;

                for (var i = 0; i < heat.Length; i++)
                {
                    heat[i] *= DecayFactor;

                    if (heat[i] < 0.01F)
                    {
                        heat[i] = 0F;
                    }
                }
            }

            var lost = lastHealth - health;

            lastHealth = health;

            if (lost <= 0 || sector < 0 || sector >= heat.Length)
            {
                return false;
            }

            // Capped: a single crusher hit should not write off a room, and a
            // hundred deaths in it should not make the map impossible.
            heat[sector] = Math.Min(MaxHeat, heat[sector] + lost * DamageWeight);

            return true;
        }

        /// <summary>
        /// Cost multiplier for pathfinding through a sector. One means "no
        /// opinion"; a warm room costs more, up to five times as much.
        /// </summary>
        public float CostMultiplier(int sector)
        {
            var value = Heat(sector);

            if (value <= 0F)
            {
                return 1F;
            }

            return 1F + Math.Min(4F, value);
        }

        /// <summary>The hottest sector, for diagnostics.</summary>
        public int Hottest()
        {
            var best = -1;
            var value = 0F;

            for (var i = 0; i < heat.Length; i++)
            {
                if (heat[i] > value)
                {
                    value = heat[i];
                    best = i;
                }
            }

            return best;
        }

        ////////////////////////////////////////////////////////////
        // Persistence
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Serialises the non-zero entries as "sector:heat" pairs. Sparse because
        /// most maps have most sectors cold, and a full array would bloat the
        /// brain file for nothing.
        /// </summary>
        public string Serialize()
        {
            var text = new StringBuilder();

            for (var i = 0; i < heat.Length; i++)
            {
                if (heat[i] <= 0F)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(i).Append(':').Append(heat[i].ToString("0.##", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }

        /// <summary>
        /// Reads what <see cref="Serialize"/> wrote. Unknown or malformed entries
        /// are skipped: a brain file is a convenience, and a damaged one must not
        /// stop the game.
        /// </summary>
        public void Deserialize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            foreach (var part in text.Split(' '))
            {
                var colon = part.IndexOf(':');

                if (colon <= 0)
                {
                    continue;
                }

                int sector;
                float value;

                if (!int.TryParse(part.Substring(0, colon), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out sector))
                {
                    continue;
                }

                if (!float.TryParse(part.Substring(colon + 1), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value))
                {
                    continue;
                }

                if (sector >= 0 && sector < heat.Length && value > 0F)
                {
                    heat[sector] = Math.Min(MaxHeat, value);
                }
            }
        }
    }
}
