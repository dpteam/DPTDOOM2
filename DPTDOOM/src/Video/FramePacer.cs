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

        /// <summary>How far between the previous and the current tic, in (0, 1].</summary>
        public Fixed Fraction;

        /// <summary>Frame number, starting at 0.</summary>
        public int Index;
    }

    /// <summary>
    /// Decides, for every rendered frame, whether the game state advances and
    /// how far between the previous and the current tic the frame sits.
    ///
    /// Doom simulates at 35 Hz while the screen runs at 35 * fpsScale, so frames
    /// between two tics are interpolated. Within one tic the fraction must grow
    /// monotonically (0.5, 1.0, ...) and only wrap back when a new tic starts.
    ///
    /// This type exists because that ordering is easy to get wrong, and getting
    /// it wrong is not subtle: an earlier version advanced the game first and
    /// then derived the fraction from the *next* frame index. The result was
    /// 1.0 followed by 0.5, i.e. the image stepped a full tic forward and then
    /// half a tic back, every tic. The picture juddered constantly.
    ///
    /// The whole sequence is computed in <see cref="Advance"/> so the update
    /// flag and the fraction can never disagree about which frame they belong to.
    /// </summary>
    public sealed class FramePacer
    {
        private readonly int fpsScale;
        private int frame = -1;

        public FramePacer(int fpsScale)
        {
            this.fpsScale = fpsScale < 1 ? 1 : fpsScale;
        }

        /// <summary>
        /// Moves to the next frame and returns both decisions for it.
        /// </summary>
        public FrameStep Advance()
        {
            frame++;

            var step = new FrameStep();
            step.Index = frame;
            step.Update = frame % fpsScale == 0;
            step.Fraction = Fixed.FromInt(frame % fpsScale + 1) / fpsScale;
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
            get { return 35.0 * fpsScale; }
        }
    }
}
