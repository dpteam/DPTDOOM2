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
    /// The panel takes the lower part of the screen and leaves the top of the view
    /// visible on purpose: a console that covered everything would be a menu, and
    /// the point of this one is to see the game while reading the output.
    /// </summary>
    public sealed class ConsoleRenderer
    {
        /// <summary>Height of the panel, in virtual pixels of a 320x200 screen.</summary>
        private const int PanelHeight = 78;

        /// <summary>Height of one text line, in virtual pixels.</summary>
        private const int LineHeight = 8;

        private readonly DrawScreen screen;

        // Tint tables rather than palette indices: the font is shaded, and a flat
        // colour would turn every glyph into a silhouette.
        private readonly byte[] normal;
        private readonly byte[] dim;
        private readonly byte[] error;
        private readonly byte[] echo;

        private readonly int accent;
        private readonly int panel;

        public ConsoleRenderer(Wad wad, DrawScreen screen)
        {
            this.screen = screen;

            var playpal = wad.ReadLump("PLAYPAL");

            normal = PaletteLookup.Tint(playpal, 255, 255, 255);
            dim = PaletteLookup.Tint(playpal, 150, 150, 150);
            error = PaletteLookup.Tint(playpal, 235, 70, 60);
            echo = PaletteLookup.Tint(playpal, 0, 136, 255);

            accent = PaletteLookup.Accent(playpal);
            panel = PaletteLookup.Panel(playpal);
        }

        public void Render(GameConsole console, int tics)
        {
            var scale = screen.Width / 320;

            var width = screen.Width;
            var height = Math.Min(screen.Height, PanelHeight * scale);
            var top = screen.Height - height;

            // Solid rather than dithered: a translucent panel over a bright room is
            // unreadable, and this renderer has no alpha to blend with.
            screen.FillRect(0, top, width, height, panel);

            // A single accent rule across the top edge, which separates the console
            // from the game behind it without needing a full border.
            screen.FillRect(0, top, width, Math.Max(1, scale), accent);

            var left = 4 * scale;
            var right = width - 4 * scale;

            // Bottom-up layout: the hint sits on the last row, the input above it,
            // and the scrollback fills whatever is left. Laying it out top-down made
            // the input line move whenever the panel height changed.
            var hintY = top + height - 10 * scale;
            var inputY = hintY - LineHeight * scale;

            DrawHint(left, hintY, right, scale);

            var input = "> " + console.Text;
            DrawClipped(input, left, inputY, right, scale, normal);

            if (tics / 8 % 2 == 0)
            {
                var cursorX = left + screen.MeasureText(input, scale);

                if (cursorX + 4 * scale < right)
                {
                    screen.FillRect(cursorX, inputY - 7 * scale, Math.Max(1, scale), 7 * scale, accent);
                }
            }

            var available = (inputY - top) / (LineHeight * scale) - 1;

            if (available <= 0)
            {
                return;
            }

            var count = console.Lines.Count;
            var offset = console.ScrollOffset;
            var last = count - 1 - offset;

            var y = inputY - LineHeight * scale;

            for (var i = 0; i < available && last - i >= 0; i++)
            {
                var line = console.Lines[last - i];

                DrawClipped(line.Text, left, y, right, scale, TableOf(line.Kind));
                y -= LineHeight * scale;
            }

            // A scroll indicator, so it is obvious that PageUp did something and
            // that the view is no longer following the output.
            if (offset > 0)
            {
                var marker = "^ " + offset;
                var markerWidth = screen.MeasureText(marker, scale);

                screen.DrawTextColored(marker, right - markerWidth, top + 9 * scale, scale, dim);
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
            const string hint = "TAB COMPLETE   PGUP SCROLL   ~ CLOSE";

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
