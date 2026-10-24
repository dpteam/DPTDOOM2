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
    /// Draws a <see cref="TextMenu"/>.
    ///
    /// The layout is a grid with four fixed numbers - panel width, row height,
    /// header height, padding - and every coordinate is derived from them. That is
    /// the whole reason this screen does not come out crooked: nothing is positioned
    /// by adding a hand-picked offset to the previous element, which is how menus
    /// end up with one row a pixel out of line.
    ///
    /// Everything is in virtual pixels of a 320x200 screen and scaled once, so the
    /// proportions hold at every window size.
    /// </summary>
    public sealed class TextMenuRenderer
    {
        private const int PanelWidth = 300;
        private const int RowHeight = 9;
        private const int Pad = 5;

        /// <summary>From the top of the panel to the first row, including the title.</summary>
        private const int RowsTop = 15;

        /// <summary>
        /// From the bottom of the last row to the bottom of the panel.
        ///
        /// The hint lives here, and it needs a whole line plus a little air above it.
        /// It used to be positioned from the panel's bottom edge while the panel was
        /// sized from the rows, and the two numbers disagreed by exactly enough to
        /// draw the hint on top of the last row. One constant used by both cannot
        /// disagree with itself.
        /// </summary>
        private const int FooterHeight = 14;

        /// <summary>Width of the value column, measured from the right edge.</summary>
        private const int ValueWidth = 74;

        /// <summary>Width of a slider's bar.</summary>
        private const int BarWidth = 40;

        private readonly DrawScreen screen;

        // Tint tables rather than palette indices: the font is shaded, and a flat
        // colour would turn every glyph into a silhouette.
        private readonly byte[] normal;
        private readonly byte[] dim;
        private readonly byte[] accentText;
        private readonly byte[] disabled;

        private readonly int accent;
        private readonly int panel;
        private readonly int highlight;
        private readonly int shadow;

        public TextMenuRenderer(Wad wad, DrawScreen screen)
        {
            this.screen = screen;

            var playpal = wad.ReadLump("PLAYPAL");

            normal = PaletteLookup.Tint(playpal, 255, 255, 255);
            dim = PaletteLookup.Tint(playpal, 150, 150, 150);
            accentText = PaletteLookup.Tint(playpal, 0, 136, 255);
            disabled = PaletteLookup.Tint(playpal, 92, 92, 100);

            accent = PaletteLookup.Accent(playpal);
            panel = PaletteLookup.Panel(playpal);
            highlight = PaletteLookup.Highlight(playpal);
            shadow = PaletteLookup.Black(playpal);
        }

        private int VisibleRows(int scale)
        {
            var viewHeight = screen.Height - StatusBarRenderer.Height * scale;

            var available = viewHeight / scale - RowsTop - FooterHeight;

            var rows = available / RowHeight;

            return rows < 1 ? 1 : rows;
        }

        public void Render(TextMenu menu, int tics)
        {
            var scale = screen.Width / 320;

            var visible = VisibleRows(scale);
            var count = menu.Items.Count;

            var shown = Math.Min(visible, count);

            var width = Math.Min(PanelWidth * scale, screen.Width - 8 * scale);
            var height = (RowsTop + shown * RowHeight + FooterHeight) * scale;

            var viewHeight = screen.Height - StatusBarRenderer.Height * scale;

            var x = (screen.Width - width) / 2;
            var y = (viewHeight - height) / 2;

            if (y < 0)
            {
                y = 0;
            }

            DrawFrame(x, y, width, height, scale);

            // Title, centred in the header band.
            var titleWidth = screen.MeasureText(menu.Title, scale);
            screen.DrawTextColored(menu.Title, x + (width - titleWidth) / 2,
                                   y + (Pad + 7) * scale, scale, normal);

            // Rule under the title, at the bottom of the header band.
            screen.FillRect(x + Pad * scale, y + (RowsTop - 3) * scale,
                            width - Pad * 2 * scale, Math.Max(1, scale / 2), accent);

            // Scroll the window so the selected row is always visible.
            var first = 0;

            if (menu.Index >= visible)
            {
                first = menu.Index - visible + 1;
            }

            var rowY = y + RowsTop * scale;

            for (var i = 0; i < shown; i++)
            {
                var item = menu.Items[first + i];
                DrawRow(item, first + i == menu.Index, x, rowY, width, scale);

                // A row waiting for a key says so where its value would be. Without
                // this the screen looks frozen: the player pressed Enter, nothing
                // moved, and nothing tells them the next key will be captured.
                var binding = item as KeyBindingTextItem;

                if (binding != null && ReferenceEquals(binding, menu.Capturing))
                {
                    const string prompt = "PRESS KEY";
                    var promptWidth = screen.MeasureText(prompt, scale);

                    screen.FillRect(x + width - Pad * scale - 6 * scale - promptWidth - 2 * scale,
                                    rowY - 1 * scale, promptWidth + 4 * scale, 9 * scale, highlight);

                    screen.DrawTextColored(prompt,
                                           x + width - Pad * scale - 6 * scale - promptWidth,
                                           rowY + 6 * scale, scale, accentText);
                }

                rowY += RowHeight * scale;
            }

            // Scroll markers, so a menu longer than the screen does not look like it
            // is missing its last rows. Drawn as text rather than as little triangles:
            // at this size a three-pixel arrow reads as a smudge.
            if (first > 0)
            {
                screen.DrawTextColored("^", x + width - 8 * scale, y + (RowsTop + 6) * scale,
                                       scale, accentText);
            }

            if (first + shown < count)
            {
                screen.DrawTextColored("v", x + width - 8 * scale,
                                       y + (RowsTop + (shown - 1) * RowHeight + 6) * scale,
                                       scale, accentText);
            }

            DrawHint(menu, x, y + (RowsTop + shown * RowHeight + 11) * scale, width, scale);
        }

        /// <summary>
        /// Two nested rules: a bright one outside and a dark one inside.
        ///
        /// That pair is what makes a rectangle read as a raised panel instead of as
        /// a stray box drawn on top of the game.
        /// </summary>
        private void DrawFrame(int x, int y, int w, int h, int scale)
        {
            var thickness = Math.Max(1, scale);

            screen.FillRect(x, y, w, h, panel);

            screen.FillRect(x, y, w, thickness, accent);
            screen.FillRect(x, y + h - thickness, w, thickness, accent);
            screen.FillRect(x, y, thickness, h, accent);
            screen.FillRect(x + w - thickness, y, thickness, h, accent);

            var inset = 2 * thickness;

            screen.FillRect(x + inset, y + inset, w - inset * 2, Math.Max(1, scale / 2), shadow);
            screen.FillRect(x + inset, y + h - inset - Math.Max(1, scale / 2),
                            w - inset * 2, Math.Max(1, scale / 2), shadow);
        }

        private void DrawRow(TextMenuItem item, bool selected, int x, int y, int width, int scale)
        {
            var section = item as SectionTextItem;

            var rowX = x + Pad * scale;
            var rowW = width - Pad * 2 * scale;

            if (selected && section == null)
            {
                screen.FillRect(rowX, y - 2 * scale, rowW, RowHeight * scale, highlight);
                screen.FillRect(rowX, y - 2 * scale, Math.Max(1, 2 * scale), RowHeight * scale, accent);
            }

            if (section != null)
            {
                // A heading: dim, indented by nothing, no value column.
                screen.DrawTextColored(item.Label, rowX + 2 * scale, y + 7 * scale, scale, accentText);
                return;
            }

            var labelTable = !item.Enabled ? disabled : (selected ? normal : dim);
            var valueTable = !item.Enabled ? disabled : normal;

            var labelX = rowX + 6 * scale;
            var labelLimit = rowW - ValueWidth * scale;

            DrawClipped(item.Label, labelX, y + 7 * scale, labelLimit, scale, labelTable);

            var value = item.Value;

            if (value != null)
            {
                var valueWidth = screen.MeasureText(value, scale);
                var valueX = x + width - Pad * scale - 6 * scale - valueWidth;

                screen.DrawTextColored(value, valueX, y + 7 * scale, scale, valueTable);
            }
            else
            {
                var slider = item as SliderTextItem;

                if (slider != null && slider.HasBar)
                {
                    DrawBar(slider, x + width - Pad * scale - 6 * scale - BarWidth * scale,
                            y + 2 * scale, scale);
                }
            }
        }

        /// <summary>
        /// A slider's bar: a trough with the filled part in the accent colour.
        ///
        /// Divided by (max - min) rather than by max, so a slider from 1 to 9 starts
        /// at a quarter full instead of almost empty.
        /// </summary>
        private void DrawBar(SliderTextItem item, int x, int y, int scale)
        {
            var width = BarWidth * scale;
            var height = 6 * scale;

            screen.FillRect(x, y, width, height, shadow);

            var span = item.Max - item.Min;
            var value = item.Position - item.Min;

            if (span <= 0)
            {
                return;
            }

            var filled = width * value / span;

            if (filled > 0)
            {
                screen.FillRect(x, y, filled, height, accent);
            }
        }

        private void DrawHint(TextMenu menu, int x, int y, int width, int scale)
        {
            var hint = menu.Current != null ? menu.Current.Hint : null;

            // While capturing, the hint slot explains the capture rather than the row:
            // the row's own hint is about the action, and the question right now is
            // what the next key press will do.
            if (menu.Capturing != null)
            {
                hint = "PRESS A KEY, ESC CANCELS";
            }

            if (string.IsNullOrEmpty(hint))
            {
                hint = "ENTER SELECT   ESC BACK";
            }

            var hintWidth = screen.MeasureText(hint, scale);

            // Centred when it fits, clipped when it does not. The hints are written
            // short enough to fit at 640x400; a longer one is cut rather than allowed
            // to run out of the panel.
            var hintX = hintWidth < width - Pad * 2 * scale
                ? x + (width - hintWidth) / 2
                : x + Pad * scale;

            DrawClipped(hint, hintX, y, x + width - Pad * scale, scale, dim);
        }

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

            screen.DrawTextColored(end == text.Length ? text : text.Substring(0, end),
                                   x, y, scale, table);
        }
    }
}
