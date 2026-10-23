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
using System.Text;
using System.Threading;

namespace DPTDOOM.Video
{
    /// <summary>
    /// Paces the main loop to a fixed frame rate and, when asked, records the
    /// timing of every frame.
    ///
    /// A naive "render, then sleep the remainder of the frame" loop drifts: the
    /// sleep is truncated to whole milliseconds, so every frame runs slightly
    /// long, and once the drift passes a whole frame the deadline has to be
    /// reset, which is visible as a periodic hitch. This clock keeps an absolute
    /// deadline instead, and never sleeps past it.
    /// </summary>
    public sealed class FrameClock : IDisposable
    {
        // Thread.Sleep cannot resolve better than about a millisecond, so the
        // last part of the wait is spun out. 1.2 ms at 70 fps is a couple of
        // percent of one core, and only while the frame is being paced.
        private const double SpinMarginSeconds = 0.0012;

        private readonly Stopwatch stopwatch = new Stopwatch();
        private readonly double frameTime;

        private double deadline;
        private double lastFrameStart;
        private bool started;

        private int frames;
        private int lateFrames;
        private double worstLateness;
        private double totalInterval;

        private StreamWriter log;
        private StringBuilder line;

        public FrameClock(double targetFps)
        {
            if (targetFps < 1)
            {
                targetFps = 1;
            }

            frameTime = 1.0 / targetFps;
            stopwatch.Start();
        }

        /// <summary>
        /// Opens a per-frame timing log. Columns: frame, nominal time, measured
        /// interval, whether the game advanced, interpolation fraction.
        /// </summary>
        public void OpenLog(string path)
        {
            line = new StringBuilder(128);
            log = new StreamWriter(path, false, Encoding.UTF8);
            log.WriteLine("frame\tnominal_ms\tinterval_ms\trender_ms\tpresent_ms\twait_ms\tupdate\tfrac");
            log.AutoFlush = true;
        }

        /// <summary>
        /// Blocks until the next frame is due. Call once per frame, before
        /// rendering.
        /// </summary>
        public double LastWaitMs { get; private set; }

        public void WaitForNextFrame()
        {
            var now = stopwatch.Elapsed.TotalSeconds;

            if (!started)
            {
                started = true;
                deadline = now;
            }

            deadline += frameTime;

            var waitStart = stopwatch.Elapsed.TotalSeconds;
            var remaining = deadline - stopwatch.Elapsed.TotalSeconds;

            if (remaining <= -frameTime)
            {
                // More than a whole frame late: catching up would mean drawing
                // several frames back to back, so start a fresh deadline instead
                // of compounding the backlog.
                lateFrames++;
                if (-remaining > worstLateness)
                {
                    worstLateness = -remaining;
                }

                deadline = stopwatch.Elapsed.TotalSeconds;
                LastWaitMs = 0;
                return;
            }

            while (true)
            {
                remaining = deadline - stopwatch.Elapsed.TotalSeconds;
                if (remaining <= 0)
                {
                    break;
                }

                if (remaining > SpinMarginSeconds)
                {
                    var sleep = (int)((remaining - SpinMarginSeconds) * 1000);
                    if (sleep > 0)
                    {
                        Thread.Sleep(sleep);
                        continue;
                    }
                }

                Thread.SpinWait(64);
            }

            LastWaitMs = (stopwatch.Elapsed.TotalSeconds - waitStart) * 1000.0;
        }

        /// <summary>
        /// Records one frame after it has been drawn, so the interval is the
        /// real one.
        /// </summary>
        public void EndFrame(FrameStep step, double renderMs, double presentMs, double waitMs)
        {
            var now = stopwatch.Elapsed.TotalSeconds;
            var interval = lastFrameStart > 0 ? (now - lastFrameStart) * 1000.0 : 0;
            lastFrameStart = now;

            frames++;

            if (interval > 0)
            {
                totalInterval += interval;
            }

            if (log == null)
            {
                return;
            }

            line.Length = 0;
            line.Append(step.Index).Append('\t');
            line.Append((step.Index * frameTime * 1000).ToString("0.000", CultureInfo.InvariantCulture)).Append('\t');
            line.Append(interval.ToString("0.000", CultureInfo.InvariantCulture)).Append('\t');
            line.Append(renderMs.ToString("0.000", CultureInfo.InvariantCulture)).Append('\t');
            line.Append(presentMs.ToString("0.000", CultureInfo.InvariantCulture)).Append('\t');
            line.Append(waitMs.ToString("0.000", CultureInfo.InvariantCulture)).Append('\t');
            line.Append(step.Update ? '1' : '0').Append('\t');
            line.Append((step.Fraction.ToDouble()).ToString("0.000", CultureInfo.InvariantCulture));

            log.WriteLine(line.ToString());
        }

        /// <summary>
        /// Prints a summary. A healthy run averages close to the target with
        /// very few late frames; a regular pattern of late frames points at the
        /// loop, an irregular one at the system.
        /// </summary>
        public void WriteSummary(TextWriter output)
        {
            var target = 1.0 / frameTime;
            var average = frames > 1 ? totalInterval / (frames - 1) : 0;

            output.WriteLine("Frames: " + frames);
            output.WriteLine("Target: " + target.ToString("0.0", CultureInfo.InvariantCulture) + " fps (" +
                             (frameTime * 1000).ToString("0.00", CultureInfo.InvariantCulture) + " ms)");
            output.WriteLine("Average interval: " + average.ToString("0.00", CultureInfo.InvariantCulture) + " ms" +
                             (average > 0 ? " (" + (1000.0 / average).ToString("0.0", CultureInfo.InvariantCulture) + " fps)" : ""));
            output.WriteLine("Late frames: " + lateFrames + ", worst " +
                             (worstLateness * 1000).ToString("0.00", CultureInfo.InvariantCulture) + " ms");
        }

        public void Dispose()
        {
            if (log != null)
            {
                try
                {
                    log.Dispose();
                }
                catch
                {
                }

                log = null;
            }
        }
    }
}
