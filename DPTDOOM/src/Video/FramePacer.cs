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

namespace DPTDOOM.Video
{
    /// <summary>What one rendered frame should do.</summary>
    public struct FrameStep
    {
        /// <summary>True when the game must advance before this frame is drawn.</summary>
        public bool Update;

        /// <summary>
        /// How many tics to run for this frame. Usually 0 or 1, and more than one
        /// only when the frame took longer than a tic to draw.
        /// </summary>
        public int UpdateCount;

        /// <summary>How far into the current tic this frame sits, in (0, 1].</summary>
        public Fixed Fraction;

        /// <summary>Frame number, starting at 0.</summary>
        public int Index;
    }

    /// <summary>
    /// Decides, for every rendered frame, how many simulation tics to run and how
    /// far between the previous and the current tic the frame sits.
    ///
    /// Doom simulates at a fixed 35 Hz no matter what the screen does, so frames
    /// between two tics are interpolated. Two rules hold and are worth stating,
    /// because breaking either one is visible:
    ///
    ///   - **The simulation runs on the clock, not on the frame counter.** 35 tics
    ///     must happen in every real second even if only 40 frames were drawn.
    ///     The earlier version advanced the game when
    ///     <c>frame % fpsScale == 0</c>, which ties the tic rate to the number of
    ///     frames the machine managed. Draw slower and the game runs slower: at
    ///     large window sizes the pistol fired noticeably slower than at 320x200
    ///     while the frame counter still read 70, because the counter measures
    ///     frames and the gun measures tics. That is the bug this design removes.
    ///
    ///   - **The fraction grows within a tic and only wraps on an update.** It was
    ///     1.0 followed by 0.5 in an early version, which steps the picture a full
    ///     tic forward and half a tic back, every tic.
    ///
    /// Catching up is capped twice, on purpose. A frame may run at most
    /// <see cref="MaxUpdatesPerFrame"/> tics, and a single stall may contribute at
    /// most <see cref="MaxCatchUpSeconds"/> of backlog. Without the caps, one slow
    /// frame (dragging the window, a disk stall) would queue up dozens of tics,
    /// drawing them would take longer still, and the loop would spiral instead of
    /// recovering. Dropping the backlog is the right trade: the game runs slightly
    /// slow for a moment rather than freezing.
    /// </summary>
    public sealed class FramePacer
    {
        /// <summary>Doom's simulation rate.</summary>
        public const double TicRate = 35.0;

        private const double TicSeconds = 1.0 / TicRate;

        /// <summary>Most tics one frame may run.</summary>
        private const int MaxUpdatesPerFrame = 4;

        /// <summary>Most time one stall may contribute to the backlog.</summary>
        private const double MaxCatchUpSeconds = 0.25;

        /// <summary>
        /// Fraction is never exactly zero: the renderer interpolates between the
        /// previous and the current tic, and a fraction of zero would show the
        /// previous tic, adding a whole tic of latency.
        /// </summary>
        private const double MinFraction = 0.001;

        private readonly int fpsScale;
        private double accumulator;
        private int frame = -1;

        public FramePacer(int fpsScale)
        {
            this.fpsScale = fpsScale < 1 ? 1 : fpsScale;

            // Start one frame in, so the very first frame advances the game. At
            // zero the first frame would draw the state the constructor left
            // behind, and the level would visibly start a frame late.
            accumulator = 1.0 / TargetFps;
        }

        /// <summary>
        /// Advances by one frame at the target rate. Equivalent to
        /// <see cref="AdvanceBy"/> with an ideal frame time, and kept because it
        /// makes the arithmetic testable without a clock.
        /// </summary>
        public FrameStep Advance()
        {
            return AdvanceBy(1.0 / TargetFps);
        }

        /// <summary>
        /// Advances by the real time the last frame took, in seconds.
        ///
        /// This is the call the game loop makes. Passing the measured interval is
        /// what keeps the simulation at 35 Hz when drawing is slower than that.
        /// </summary>
        public FrameStep AdvanceBy(double elapsedSeconds)
        {
            frame++;

            if (double.IsNaN(elapsedSeconds) || elapsedSeconds < 0)
            {
                elapsedSeconds = 0;
            }

            if (elapsedSeconds > MaxCatchUpSeconds)
            {
                elapsedSeconds = MaxCatchUpSeconds;
            }

            accumulator += elapsedSeconds;

            var updates = 0;

            while (accumulator >= TicSeconds && updates < MaxUpdatesPerFrame)
            {
                accumulator -= TicSeconds;
                updates++;
            }

            // Still behind after the cap: this frame was far too slow. Drop the
            // backlog instead of carrying it, or the next frame inherits the same
            // problem and the loop never recovers.
            if (accumulator > TicSeconds)
            {
                accumulator = TicSeconds;
            }

            var step = new FrameStep();
            step.Index = frame;
            step.UpdateCount = updates;
            step.Update = updates > 0;

            var fraction = accumulator / TicSeconds;

            if (fraction < MinFraction)
            {
                fraction = MinFraction;
            }
            else if (fraction > 1.0)
            {
                fraction = 1.0;
            }

            step.Fraction = Fixed.FromDouble(fraction);

            return step;
        }

        public int FpsScale
        {
            get { return fpsScale; }
        }

        public int FrameIndex
        {
            get { return frame; }
        }

        /// <summary>Frames per second this pacer targets, given Doom's 35 Hz.</summary>
        public double TargetFps
        {
            get { return TicRate * fpsScale; }
        }

        /// <summary>
        /// How much time is queued for the next frame, in seconds. Exposed so a
        /// test can show that a slow frame leaves a backlog rather than losing
        /// tics.
        /// </summary>
        public double BacklogSeconds
        {
            get { return accumulator; }
        }
    }
}
