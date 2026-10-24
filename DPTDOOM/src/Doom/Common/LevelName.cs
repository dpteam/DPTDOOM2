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
    /// Turns a level name into an episode and a map.
    ///
    /// Separate from the console command so it can be tested without a running game -
    /// a copy of the rules inside a test would be a test of the copy, which is the
    /// failure mode that makes a parser test worthless.
    ///
    /// Names, not numbers: "MAP32" and "E1M1" are how every mod, walkthrough and
    /// idgames text file refers to a level, and a PWAD that replaces MAP32 is the
    /// standard way to ship a secret map. A bare number still works and means the
    /// current game's own numbering, so "32" is MAP32 in Doom II and refused in Doom 1
    /// rather than clamped into E1M9.
    /// </summary>
    public static class LevelName
    {
        public static bool TryParse(string text, GameMode mode, int currentEpisode,
                                    out int episode, out int map)
        {
            episode = currentEpisode;
            map = 0;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var value = text.Trim().ToUpperInvariant();

            if (value.StartsWith("MAP") && value.Length > 3)
            {
                int number;

                if (!int.TryParse(value.Substring(3), out number) || number < 1 || number > 32)
                {
                    return false;
                }

                episode = 1;
                map = number;
                return true;
            }

            if (value.Length >= 4 && value[0] == 'E' && value[2] == 'M')
            {
                int ep;
                int mn;

                if (!int.TryParse(value.Substring(1, 1), out ep) ||
                    !int.TryParse(value.Substring(3), out mn))
                {
                    return false;
                }

                if (ep < 1 || ep > 4 || mn < 1 || mn > 9)
                {
                    return false;
                }

                episode = ep;
                map = mn;
                return true;
            }

            int bare;

            if (!int.TryParse(value, out bare))
            {
                return false;
            }

            if (mode == GameMode.Commercial)
            {
                if (bare < 1 || bare > 32)
                {
                    return false;
                }

                episode = 1;
                map = bare;
                return true;
            }

            if (bare < 1 || bare > 9)
            {
                return false;
            }

            map = bare;
            return true;
        }

        /// <summary>The name a player would write for this level.</summary>
        public static string Format(GameMode mode, int episode, int map)
        {
            if (mode == GameMode.Commercial)
            {
                return "MAP" + map.ToString("00");
            }

            return "E" + episode + "M" + map;
        }
    }
}
