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

namespace DPTDOOM.Video
{
    /// <summary>
    /// One frame's timing, for the on-screen counter.
    ///
    /// A plain record with no behaviour: the window fills it, the renderer draws it,
    /// and neither needs to know how the other got the numbers. Kept out of the
    /// world on purpose - a frame rate is not game state and must never reach a
    /// demo or a hash.
    /// </summary>
    public sealed class FrameStats
    {
        public double Fps;
        public int TicsThisFrame;
        public double RenderMs;
        public double PresentMs;

        /// <summary>Interpolation fraction, 0 to 1 within the current tic.</summary>
        public double Fraction;

        /// <summary>
        /// How many tics the simulation has run in the last second.
        ///
        /// Reported next to the frame rate because they are two different numbers and
        /// the difference is the bug this counter exists for: a run can show a
        /// healthy frame rate while the game itself is slow.
        /// </summary>
        public double TicsPerSecond;

        public void Clear()
        {
            Fps = 0;
            TicsThisFrame = 0;
            RenderMs = 0;
            PresentMs = 0;
            Fraction = 0;
            TicsPerSecond = 0;
        }
    }

    /// <summary>
    /// What the on-screen counter shows at each level.
    ///
    /// Named after Doom's own skill levels, because "how much detail do you want" is
    /// a question the game already has a vocabulary for, and a row of numbers 0-5
    /// says nothing about what each one adds. The names are also a joke that fits:
    /// turning it up to Nightmare does show everything.
    /// </summary>
    public static class ProfilerLevel
    {
        public const int Count = 6;

        public static readonly string[] Names =
        {
            "OFF",
            "I'M TOO YOUNG TO DIE",
            "HEY, NOT TOO ROUGH",
            "HURT ME PLENTY",
            "ULTRA-VIOLENCE",
            "NIGHTMARE!"
        };

        /// <summary>Short names, for the menu row where the long ones do not fit.</summary>
        public static readonly string[] ShortNames =
        {
            "OFF",
            "TOO YOUNG",
            "NOT TOO ROUGH",
            "HURT ME PLENTY",
            "ULTRA-VIOLENCE",
            "NIGHTMARE"
        };

        public static string Describe(int level)
        {
            switch (level)
            {
                case 0:
                    return "The counter is hidden.";

                case 1:
                    return "Frames per second only.";

                case 2:
                    return "Also tics per second and tics this frame.";

                case 3:
                    return "Also render and present time.";

                case 4:
                    return "Also BSP, walls and flats.";

                case 5:
                    return "Everything, including the sections nobody optimises.";

                default:
                    return "";
            }
        }

        public static int Clamp(int level)
        {
            if (level < 0)
            {
                return 0;
            }

            if (level >= Count)
            {
                return Count - 1;
            }

            return level;
        }

        /// <summary>
        /// The lines to draw for a level. Empty when the counter is off.
        ///
        /// Built as strings here rather than in the renderer so the level's meaning
        /// lives in one place: the renderer only decides where to put them.
        /// </summary>
        public static string[] Lines(int level, FrameStats stats, RenderProfile profile)
        {
            level = Clamp(level);

            if (level <= 0 || stats == null)
            {
                return new string[0];
            }

            var lines = new System.Collections.Generic.List<string>();

            lines.Add("FPS " + stats.Fps.ToString("0", CultureInfo.InvariantCulture));

            if (level >= 2)
            {
                lines.Add("TIC/S " + stats.TicsPerSecond.ToString("0", CultureInfo.InvariantCulture) +
                          "  TICS " + stats.TicsThisFrame);
            }

            if (level >= 3)
            {
                lines.Add("RND " + stats.RenderMs.ToString("0.0", CultureInfo.InvariantCulture) +
                          "  PRS " + stats.PresentMs.ToString("0.0", CultureInfo.InvariantCulture));
            }

            if (level >= 4 && profile != null && profile.Enabled)
            {
                lines.Add("BSP " + profile.LastMs(RenderProfile.Section.Bsp).ToString("0.00", CultureInfo.InvariantCulture) +
                          "  WAL " + profile.LastMs(RenderProfile.Section.Walls).ToString("0.00", CultureInfo.InvariantCulture) +
                          "  FLT " + profile.LastMs(RenderProfile.Section.Flats).ToString("0.00", CultureInfo.InvariantCulture));
            }

            if (level >= 5 && profile != null && profile.Enabled)
            {
                lines.Add("SPR " + profile.LastMs(RenderProfile.Section.Sprites).ToString("0.00", CultureInfo.InvariantCulture) +
                          "  MSK " + profile.LastMs(RenderProfile.Section.Masked).ToString("0.00", CultureInfo.InvariantCulture) +
                          "  HUD " + profile.LastMs(RenderProfile.Section.Hud).ToString("0.00", CultureInfo.InvariantCulture) +
                          "  CNV " + profile.LastMs(RenderProfile.Section.Convert).ToString("0.00", CultureInfo.InvariantCulture));
            }

            return lines.ToArray();
        }
    }
}
