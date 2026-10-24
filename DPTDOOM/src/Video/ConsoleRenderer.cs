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
    /// Draws the drop-down console.
    ///
    /// Laid out in virtual pixels of a 320x200 screen and scaled from there, the way
    /// everything else in this renderer is, so the console looks the same at every
    /// window size instead of growing a different number of lines.
    ///
    /// It drops from the **top**, like Quake's, and leaves the bottom of the view
    /// visible. The first version came up from the bottom, which put it on top of the
    /// status bar and hid the player's own health while they read their own output.
    /// </summary>
    public sealed class ConsoleRenderer
    {
        /// <summary>
        /// Height of the panel, in virtual pixels of a 320x200 screen.
        ///
        /// Sized to hold the longest line "help" prints without wrapping: a console
        /// that clips its own help text is a console nobody can learn from.
        /// </summary>
        private const int PanelHeight = 116;

        /// <summary>Height of one text line, in virtual pixels.</summary>
        private const int LineHeight = 8;

        /// <summary>Left margin, in virtual pixels.</summary>
        private const int LeftMargin = 3;

        private readonly DrawScreen screen;

        // Tint tables rather than palette indices: the font is shaded, and a flat
        // colour would turn every glyph into a silhouette.
        private readonly byte[] normal;
        private readonly byte[] dim;
        private readonly byte[] error;
        private readonly byte[] echo;

        /// <summary>
        /// The colormap row used to darken whatever is under the panel.
        ///
        /// A real darkening rather than a checkerboard: the first version stippled
        /// every other pixel with a flat colour, which read as a screen-door pattern
        /// instead of as glass. Doom already has a table that maps every palette index
        /// to a darker one, so the panel uses it - the same mechanism the game uses for
        /// distance shading and for the fuzz effect.
        ///
        /// Row 24 of 32: dark enough that light text reads on top, light enough that
        /// the room is still visible through it.
        /// </summary>
        private readonly byte[] darken;

        private readonly int accent;
        private readonly int panel;

        public ConsoleRenderer(Wad wad, DrawScreen screen, ColorMap colorMap)
        {
            this.screen = screen;

            var playpal = wad.ReadLump("PLAYPAL");

            normal = PaletteLookup.Tint(playpal, 255, 255, 255);
            dim = PaletteLookup.Tint(playpal, 150, 150, 150);
            error = PaletteLookup.Tint(playpal, 235, 70, 60);
            echo = PaletteLookup.Tint(playpal, 0, 136, 255);

            darken = colorMap != null ? colorMap[DarkenLevel] : null;

            accent = PaletteLookup.Accent(playpal);
            panel = PaletteLookup.Panel(playpal);
        }

        /// <summary>Which colormap row darkens the panel. 0 is brightest, 31 darkest.</summary>
        private const int DarkenLevel = 29;

        public void Render(GameConsole console, int tics)
        {
            var scale = screen.Width / 320;

            var width = screen.Width;
            var height = Math.Min(screen.Height, PanelHeight * scale);
            var top = 0;

            // Slightly translucent: three pixels in four are panel, so the game is
            // still faintly visible underneath without the text becoming hard to
            // read.
            //
            // Dithered rather than alpha-blended because the screen is 8-bit palette
            // indices and the blend happens later, when WriteData maps indices to
            // colours - there is no alpha channel here to blend with. An ordered
            // pattern is also what Doom itself used for every translucent surface it
            // ever had, so it looks like it belongs.
            Darken(0, top, width, height);

            // The rules stay solid: a broken-up line reads as a drawing mistake.
            screen.FillRect(0, top, width, Math.Max(1, scale), accent);
            screen.FillRect(0, height - Math.Max(1, scale), width, Math.Max(1, scale), accent);

            var left = LeftMargin * scale;
            var right = width - LeftMargin * scale;

            // Quake's layout: the scrollback fills the panel from the top down,
            // oldest at the top and newest at the bottom, and the input line sits on
            // the last row - where the eye already is after reading the output.
            //
            // The first version had the input at the top and the newest line under
            // it, which reads like a chat window and is not what a console is: the
            // thing you type belongs below the thing you read.
            // Rows are counted in virtual pixels and then divided by the line height.
            //
            // The first version subtracted the two row numbers directly, which gave 91
            // "rows" where the panel only holds eleven: the output ran out of the panel
            // and over the game below it. Two units that look alike - a row index and a
            // pixel coordinate - are exactly the pair that must not be subtracted from
            // each other.
            var hintRow = height / scale - 9;
            var inputRow = hintRow - 10;

            // The first row's baseline, not its top: DrawText anchors at the baseline
            // and the glyphs grow upward by their full height, so a baseline at the
            // panel's top edge draws the first line half outside it. That is what put
            // the newest messages above the panel.
            //
            // +10 rather than +7: the accent rule takes the first two pixels, and a
            // line sitting right under it reads as clipped even when it is not.
            var firstRow = LeftMargin + 10;
            var available = (inputRow - firstRow - 1) / LineHeight;

            if (available <= 0)
            {
                return;
            }

            var count = console.Lines.Count;
            var offset = console.ScrollOffset;

            // The window ends `offset` lines above the newest one, and never starts
            // before the oldest.
            var last = count - 1 - offset;
            var first = last - available + 1;

            if (first < 0)
            {
                first = 0;
            }

            var y = firstRow * scale;

            for (var i = first; i <= last; i++)
            {
                var line = console.Lines[i];

                DrawClipped(line.Text, left, y, right, scale, TableOf(line.Kind));
                y += LineHeight * scale;
            }

            // Separator above the input line, so the line being typed is never
            // confused with the output above it.
            var inputY = inputRow * scale;
            // A thin line, darkened rather than drawn: a solid rule across the panel
            // would cut it in two.
            Darken(left, inputY - 9 * scale, right - left, Math.Max(1, scale / 2));

            // "] " rather than "> ": the Doom font has no glyph for ">" and draws a
            // blank where it should be, which made the prompt invisible.
            var input = "] " + console.Text;
            DrawClipped(input, left, inputY, right, scale, normal);

            if (tics / 8 % 2 == 0)
            {
                var cursorX = left + screen.MeasureText(input, scale);

                if (cursorX + 4 * scale < right)
                {
                    screen.FillRect(cursorX, inputY - 7 * scale, Math.Max(1, scale), 7 * scale, accent);
                }
            }

            // A scroll indicator, so it is obvious that PageUp did something and that
            // the view is no longer following the output.
            if (offset > 0)
            {
                var marker = "^ " + offset;
                var markerWidth = screen.MeasureText(marker, scale);

                screen.DrawTextColored(marker, right - markerWidth, inputY, scale, dim);
            }

            DrawHint(left, hintRow * scale, right, scale);
        }

        /// <summary>
        /// Darkens a rectangle of whatever is already on screen.
        ///
        /// Written against <see cref="DrawScreen.Data"/> directly because the screen is
        /// stored column-major - index x*height+y - and a row-major loop here would
        /// paint diagonal streaks.
        ///
        /// Falls back to a solid fill when there is no colormap, which happens only in
        /// a test that builds a renderer without a WAD.
        /// </summary>
        private void Darken(int x, int y, int w, int h)
        {
            var data = screen.Data;
            var screenHeight = screen.Height;

            for (var i = 0; i < w; i++)
            {
                var column = (x + i) * screenHeight;

                for (var j = 0; j < h; j++)
                {
                    if (darken == null)
                    {
                        data[column + y + j] = (byte)panel;
                        continue;
                    }

                    data[column + y + j] = darken[data[column + y + j]];
                }
            }
        }

        private byte[] TableOf(ConsoleLineKind kind)
        {
            switch (kind)
            {
                case ConsoleLineKind.Error:
                    return error;

                case ConsoleLineKind.Echo:
                    return echo;

                case ConsoleLineKind.Hint:
                    return dim;

                default:
                    return normal;
            }
        }

        private void DrawHint(int left, int y, int right, int scale)
        {
            const string hint = "TAB COMPLETE   UP/DOWN HISTORY   PGUP/PGDN SCROLL   ~ CLOSE";

            // The hint is the least important thing here, so it is dropped rather
            // than shrunk when it does not fit: a hint at half size is unreadable,
            // and the console is not the place to prove a point.
            if (screen.MeasureText(hint, scale) > right - left)
            {
                return;
            }

            screen.DrawTextColored(hint, left, y, scale, dim);
        }

        /// <summary>
        /// Draws text clipped to the panel, truncating at a character boundary.
        ///
        /// The alternative - letting DrawText run off the edge - is what made the
        /// first version print console output over the status bar.
        /// </summary>
        private void DrawClipped(string text, int x, int y, int right, int scale, byte[] table)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var limit = right - x;
            var width = 0;
            var end = text.Length;

            for (var i = 0; i < text.Length; i++)
            {
                var w = screen.MeasureChar(text[i], scale);

                if (width + w > limit)
                {
                    end = i;
                    break;
                }

                width += w;
            }

            if (end <= 0)
            {
                return;
            }

            var visible = end == text.Length ? text : text.Substring(0, end);

            screen.DrawTextColored(visible, x, y, scale, table);
        }
    }
}
