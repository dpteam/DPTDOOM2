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
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace DPTDOOM.Video
{
    /// <summary>
    /// Where the frame actually goes, section by section.
    ///
    /// A frame rate alone says nothing actionable: 40 fps could mean the BSP
    /// traversal is too slow, that the flats are being drawn on a huge screen, or
    /// that a hundred sprites are overlapping. This splits the draw into the parts
    /// that can be worked on separately, and counts the units each part processed,
    /// so a slow section can be compared against its own workload rather than
    /// against the previous frame's luck.
    ///
    /// The sections are partly nested, which is deliberate and is what the
    /// indented report shows: <see cref="RenderSection.Walls"/> and
    /// <see cref="RenderSection.Flats"/> happen *inside* the BSP traversal, so
    /// "bsp minus walls minus flats" is the traversal's own cost. Wall and flat
    /// columns are timed inside the column routines rather than around the segment
    /// loop, because a segment loop draws both and their costs differ by an order
    /// of magnitude on a walls-heavy map.
    /// </summary>
    public sealed class RenderProfile
    {
        public enum Section
        {
            /// <summary>Interpolation, view setup, clearing the buffers.</summary>
            Setup,

            /// <summary>The whole BSP traversal. Contains Walls and Flats.</summary>
            Bsp,

            /// <summary>Wall columns. Also includes the masked mid-texture columns.</summary>
            Walls,

            /// <summary>Floor and ceiling columns. Inside the BSP traversal.</summary>
            Flats,

            Sprites,
            Masked,
            PlayerSprites,
            Sky,

            /// <summary>The palette lookup that turns the 8-bit screen into pixels.</summary>
            Convert,

            /// <summary>Status bar, HUD, message line, automap.</summary>
            Hud,

            Count
        }

        public enum Counter
        {
            Segments,
            WallColumns,
            FlatColumns,
            Sprites,
            MaskedTextures,
            WallRanges,
            Count
        }

        private readonly long[] ticks = new long[(int)Section.Count];
        private readonly long[] worstTicks = new long[(int)Section.Count];
        private readonly long[] frameTicks = new long[(int)Section.Count];
        private readonly long[] lastTicks = new long[(int)Section.Count];

        private readonly long[] counters = new long[(int)Counter.Count];
        private readonly long[] frameCounters = new long[(int)Counter.Count];

        private long frames;
        private long worstFrameTicks;
        private long windowTicks;
        private long windowStart;

        private bool enabled;

        private static readonly string[] SectionNames =
        {
            "setup", "bsp", "walls", "flats", "sprites", "masked", "player", "sky", "convert", "hud"
        };

        private static readonly string[] CounterNames =
        {
            "segs", "wall columns", "flat columns", "sprites", "masked", "wall ranges"
        };

        /// <summary>
        /// Off by default. The hooks cost one branch per column while disabled and
        /// a timestamp pair per column while enabled, so leaving this on for a
        /// normal game would be measuring the measuring.
        /// </summary>
        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public long Frames => frames;

        /// <summary>
        /// Starts a frame.
        ///
        /// Frames are counted only while enabled, so turning the profiler on in
        /// the middle of a run averages the frames that were actually measured
        /// rather than diluting them with the ones that were not.
        /// </summary>
        public void BeginFrame()
        {
            if (!enabled)
            {
                return;
            }

            frames++;

            for (var i = 0; i < frameTicks.Length; i++)
            {
                frameTicks[i] = 0;
            }

            for (var i = 0; i < frameCounters.Length; i++)
            {
                frameCounters[i] = 0;
            }

            windowStart = Stopwatch.GetTimestamp();
        }

        public void EndFrame()
        {
            if (!enabled)
            {
                return;
            }

            var total = Stopwatch.GetTimestamp() - windowStart;

            if (total > worstFrameTicks)
            {
                worstFrameTicks = total;
            }

            for (var i = 0; i < frameTicks.Length; i++)
            {
                ticks[i] += frameTicks[i];
                lastTicks[i] = frameTicks[i];

                if (frameTicks[i] > worstTicks[i])
                {
                    worstTicks[i] = frameTicks[i];
                }
            }

            for (var i = 0; i < frameCounters.Length; i++)
            {
                counters[i] += frameCounters[i];
            }

            windowTicks += total;
        }

        public long Stamp()
        {
            return enabled ? Stopwatch.GetTimestamp() : 0;
        }

        public void Add(Section section, long elapsed)
        {
            if (enabled)
            {
                frameTicks[(int)section] += elapsed;
            }
        }

        public void Count(Counter counter)
        {
            if (enabled)
            {
                frameCounters[(int)counter]++;
            }
        }

        public void Count(Counter counter, long value)
        {
            if (enabled)
            {
                frameCounters[(int)counter] += value;
            }
        }

        private double ToMs(long value)
        {
            return value * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>How long one section took in the last completed frame.</summary>
        public double LastMs(Section section)
        {
            return ToMs(lastTicks[(int)section]);
        }

        /// <summary>
        /// A compact one-line breakdown for the on-screen debug text. The full
        /// report is for the log; a player watching the overlay wants the four
        /// numbers that roll while turning.
        /// </summary>
        public string ShortLine()
        {
            return "prof bsp " + LastMs(Section.Bsp).ToString("0.00", CultureInfo.InvariantCulture) +
                   " (wall " + LastMs(Section.Walls).ToString("0.00", CultureInfo.InvariantCulture) +
                   " flat " + LastMs(Section.Flats).ToString("0.00", CultureInfo.InvariantCulture) + ")" +
                   " spr " + LastMs(Section.Sprites).ToString("0.00", CultureInfo.InvariantCulture) +
                   " hud " + LastMs(Section.Hud).ToString("0.00", CultureInfo.InvariantCulture) +
                   " conv " + LastMs(Section.Convert).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private double MsPerFrame(long value)
        {
            if (frames == 0)
            {
                return 0;
            }

            return ToMs(value) / frames;
        }

        /// <summary>
        /// Writes the report. Per-frame averages rather than totals, because a
        /// total only says how long the run was.
        ///
        /// The counters are printed next to the timings on purpose: "flats 4.2 ms"
        /// means something different at 640x400 than at 1600x1200, and the number of
        /// columns drawn is what makes the two comparable.
        /// </summary>
        public void WriteSummary(TextWriter output)
        {
            if (!enabled)
            {
                output.WriteLine("Render profile: disabled.");
                return;
            }

            if (frames == 0)
            {
                output.WriteLine("Render profile: enabled, but no frames were rendered.");
                return;
            }

            output.WriteLine("Render profile over " + frames + " frames (" +
                             MsPerFrame(windowTicks).ToString("0.00", CultureInfo.InvariantCulture) +
                             " ms per frame, worst " +
                             ToMs(worstFrameTicks).ToString("0.00", CultureInfo.InvariantCulture) +
                             " ms):");

            for (var i = 0; i < (int)Section.Count; i++)
            {
                // Walls and flats are nested inside the BSP traversal; the indent
                // is the only thing that stops the four numbers being added up.
                var indent = i == (int)Section.Walls || i == (int)Section.Flats ? "    " : "  ";

                output.WriteLine(indent + SectionNames[i].PadRight(8) +
                                 MsPerFrame(ticks[i]).ToString("0.00", CultureInfo.InvariantCulture).PadLeft(7) +
                                 " ms" +
                                 (worstTicks[i] > 0
                                     ? "   worst " + ToMs(worstTicks[i]).ToString("0.00", CultureInfo.InvariantCulture)
                                     : ""));
            }

            output.Write("  per frame:");
            for (var i = 0; i < (int)Counter.Count; i++)
            {
                output.Write(" " + CounterNames[i] + " " +
                             ((double)counters[i] / frames).ToString("0.0", CultureInfo.InvariantCulture));
            }

            output.WriteLine();
        }
    }
}
