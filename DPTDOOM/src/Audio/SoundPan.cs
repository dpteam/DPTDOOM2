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

namespace DPTDOOM.Audio
{
    /// <summary>
    /// Turns the angle between the listener's facing direction and a sound
    /// source into left/right gains.
    ///
    /// Doom's angle convention matters here and is easy to get backwards:
    /// angles increase counterclockwise, so a **positive** relative angle means
    /// the source is to the listener's **left**. Mapping it to the right
    /// channel mirrors the whole soundstage, which is audible immediately when
    /// a monster growls behind you.
    ///
    /// The pan law is constant power: left² + right² = 1, so a source moving
    /// across the listener keeps the same loudness instead of dipping in the
    /// middle the way a linear crossfade does.
    /// </summary>
    public static class SoundPan
    {
        public static void FromRelativeAngle(float angleRadians, out float left, out float right)
        {
            var pan = MathF.Sin(angleRadians);

            if (pan > 1F) pan = 1F;
            else if (pan < -1F) pan = -1F;

            // pan = +1 is hard left, -1 is hard right.
            left = (float)Math.Sqrt((1.0 + pan) * 0.5);
            right = (float)Math.Sqrt((1.0 - pan) * 0.5);
        }

        /// <summary>
        /// Distance attenuation in 0..1. Sources closer than <paramref name="close"/>
        /// are not attenuated at all; beyond <paramref name="clip"/> they are
        /// silent.
        /// </summary>
        public static float DistanceDecay(float distance, float close, float clip)
        {
            if (distance < close)
            {
                return 1F;
            }

            var decay = (clip - distance) / (clip - close);
            return decay > 0F ? decay : 0F;
        }
    }
}
