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
    /// <summary>
    /// A colour grade applied to the 256-entry palette table.
    ///
    /// Deliberately a transform of the **palette**, not of the framebuffer. Doom draws in palette
    /// indices, so one pass over 256 colours grades a whole frame - walls, sprites, HUD and all -
    /// and the world is untouched, because the simulation never sees a colour. That is what makes
    /// it safe for a machinima: the same run with and without a grade has the same world hash.
    ///
    /// The order is saturation, then tint, then brightness and contrast, then the optional
    /// posterise. Any fixed order is arbitrary; this one is written down so two runs agree.
    /// </summary>
    public sealed class PaletteGrade
    {
        /// <summary>1 is unchanged, 0 is grey, above 1 exaggerates.</summary>
        public double Saturation = 1.0;

        /// <summary>Multipliers for the red, green and blue channels; 1 is unchanged.</summary>
        public double TintRed = 1.0;
        public double TintGreen = 1.0;
        public double TintBlue = 1.0;

        /// <summary>Added to every channel, -1 to 1, where 0 is unchanged.</summary>
        public double Brightness;

        /// <summary>Scales around the midpoint; 1 is unchanged.</summary>
        public double Contrast = 1.0;

        /// <summary>Levels per channel after grading, 0 for off. 4 is a hard poster look.</summary>
        public int Posterise;

        /// <summary>True when the grade would leave every colour as it is.</summary>
        public bool IsIdentity
        {
            get
            {
                return Math.Abs(Saturation - 1.0) < 1e-9 &&
                       Math.Abs(TintRed - 1.0) < 1e-9 &&
                       Math.Abs(TintGreen - 1.0) < 1e-9 &&
                       Math.Abs(TintBlue - 1.0) < 1e-9 &&
                       Math.Abs(Brightness) < 1e-9 &&
                       Math.Abs(Contrast - 1.0) < 1e-9 &&
                       Posterise <= 0;
            }
        }

        /// <summary>
        /// Applies the grade to a palette table, writing into <paramref name="destination"/>.
        ///
        /// The palette is packed the way the renderer hands it to the window: one uint per entry,
        /// which is already the platform's own byte order, so the channels are extracted from the
        /// low three bytes in the order the renderer wrote them.
        /// </summary>
        public void Apply(uint[] source, uint[] destination)
        {
            for (var i = 0; i < source.Length && i < destination.Length; i++)
            {
                var colour = source[i];

                var red = (byte)(colour & 0xFF);
                var green = (byte)((colour >> 8) & 0xFF);
                var blue = (byte)((colour >> 16) & 0xFF);

                var r = red / 255.0;
                var g = green / 255.0;
                var b = blue / 255.0;

                // Saturation: pull towards the luminance the eye actually sees, not the average,
                // or a grey looks wrong next to a coloured one.
                if (Math.Abs(Saturation - 1.0) > 1e-9)
                {
                    var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;

                    r = luma + (r - luma) * Saturation;
                    g = luma + (g - luma) * Saturation;
                    b = luma + (b - luma) * Saturation;
                }

                r *= TintRed;
                g *= TintGreen;
                b *= TintBlue;

                if (Math.Abs(Contrast - 1.0) > 1e-9)
                {
                    r = (r - 0.5) * Contrast + 0.5;
                    g = (g - 0.5) * Contrast + 0.5;
                    b = (b - 0.5) * Contrast + 0.5;
                }

                r += Brightness;
                g += Brightness;
                b += Brightness;

                if (Posterise > 0)
                {
                    var levels = Posterise;
                    r = Math.Floor(r * levels) / levels;
                    g = Math.Floor(g * levels) / levels;
                    b = Math.Floor(b * levels) / levels;
                }

                destination[i] = Pack(ToByte(r), ToByte(g), ToByte(b));
            }
        }

        private static byte ToByte(double value)
        {
            var scaled = value * 255.0 + 0.5;

            if (scaled <= 0)
            {
                return 0;
            }

            if (scaled >= 255)
            {
                return 255;
            }

            return (byte)scaled;
        }

        private static uint Pack(byte red, byte green, byte blue)
        {
            return (uint)(red | (green << 8) | (blue << 16) | (0xFF << 24));
        }
    }
}
