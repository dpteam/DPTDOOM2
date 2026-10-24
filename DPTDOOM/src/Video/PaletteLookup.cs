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
    /// Finds palette entries by colour.
    ///
    /// Needed because a palette index is not a colour: index 4 is a dark red in
    /// Doom II and something else in Freedoom, and the console, the crosshair and
    /// any other interface drawn with the game's own 8-bit screen have to look the
    /// same in both. Looking the colour up in PLAYPAL costs 256 iterations once and
    /// removes the guesswork.
    /// </summary>
    public static class PaletteLookup
    {
        /// <summary>
        /// Index of the palette entry closest to the requested colour, searched
        /// over the base palette (the first 256 entries, before any damage or item
        /// tinting).
        ///
        /// Squared distance, so there is no square root per candidate.
        /// </summary>
        public static int Nearest(byte[] playpal, int r, int g, int b)
        {
            if (playpal == null || playpal.Length < 256 * 3)
            {
                return 0;
            }

            var best = 0;
            var bestDistance = int.MaxValue;

            for (var i = 0; i < 256; i++)
            {
                var dr = playpal[i * 3 + 0] - r;
                var dg = playpal[i * 3 + 1] - g;
                var db = playpal[i * 3 + 2] - b;

                var distance = dr * dr + dg * dg + db * db;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>The closest entry to pure white. Used for ordinary text.</summary>
        public static int White(byte[] playpal)
        {
            return Nearest(playpal, 255, 255, 255);
        }

        /// <summary>The closest entry to pure black. Used for shadows and panels.</summary>
        public static int Black(byte[] playpal)
        {
            return Nearest(playpal, 0, 0, 0);
        }

        /// <summary>
        /// A readable red, for errors. Not the pure red of the palette: that one is
        /// so bright it vibrates against a dark panel.
        /// </summary>
        public static int Red(byte[] playpal)
        {
            return Nearest(playpal, 220, 40, 40);
        }

        /// <summary>A dim grey, for hints and for text that is not the point.</summary>
        public static int Grey(byte[] playpal)
        {
            return Nearest(playpal, 150, 150, 150);
        }

        /// <summary>
        /// The project's primary blue, for the selection bar. The same #0088FF the
        /// logo and the launcher use, so the console does not look like it belongs
        /// to a different program.
        /// </summary>
        public static int Accent(byte[] playpal)
        {
            return Nearest(playpal, 0, 136, 255);
        }

        /// <summary>A dark blue-grey, for the console background.</summary>
        public static int Panel(byte[] playpal)
        {
            return Nearest(playpal, 8, 12, 26);
        }

        /// <summary>
        /// A step lighter than <see cref="Panel"/>, for the selected row.
        ///
        /// The selection is a filled band rather than a blinking cursor because a
        /// cursor on its own is easy to lose in a list of twelve rows, and this
        /// renderer has no alpha to make a highlight subtle.
        /// </summary>
        public static int Highlight(byte[] playpal)
        {
            return Nearest(playpal, 26, 38, 68);
        }

        /// <summary>
        /// Builds a translation table that tints everything towards a colour while
        /// keeping its own brightness.
        ///
        /// A flat "every pixel becomes the same index" table would turn the Doom
        /// font into a silhouette, because the glyphs are shaded. This scales the
        /// target colour by each source entry's luminance and looks up the nearest
        /// palette entry to the result, so the letter keeps its shading and only its
        /// hue changes.
        ///
        /// 256 × 256 distance computations, once per colour at startup.
        /// </summary>
        public static byte[] Tint(byte[] playpal, int r, int g, int b)
        {
            var table = new byte[256];

            if (playpal == null || playpal.Length < 256 * 3)
            {
                for (var i = 0; i < 256; i++)
                {
                    table[i] = (byte)i;
                }

                return table;
            }

            for (var i = 0; i < 256; i++)
            {
                var sr = playpal[i * 3 + 0];
                var sg = playpal[i * 3 + 1];
                var sb = playpal[i * 3 + 2];

                // Perceived brightness, so a blue and a green of the same nominal
                // value do not come out equally light.
                var luminance = (sr * 30 + sg * 59 + sb * 11) / 100;

                table[i] = (byte)Nearest(
                    playpal,
                    r * luminance / 255,
                    g * luminance / 255,
                    b * luminance / 255);
            }

            return table;
        }
    }
}
