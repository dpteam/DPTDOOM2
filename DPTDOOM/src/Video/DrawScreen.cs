//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
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
using System.Collections.Generic;

namespace DPTDOOM.Video
{
    public sealed class DrawScreen
    {
        private int width;
        private int height;
        private byte[] data;

        private Patch[] chars;

        public DrawScreen(Wad wad, int width, int height)
        {
            this.width = width;
            this.height = height;
            data = new byte[width * height];

            chars = new Patch[128];
            for (var i = 0; i < chars.Length; i++)
            {
                var name = "STCFN" + i.ToString("000");
                var lump = wad.GetLumpNumber(name);
                if (lump != -1)
                {
                    chars[i] = Patch.FromData(name, wad.ReadLump(lump));
                }
            }
        }

        public void DrawPatch(Patch patch, int x, int y, int scale)
        {
            var drawX = x - scale * patch.LeftOffset;
            var drawY = y - scale * patch.TopOffset;
            var drawWidth = scale * patch.Width;

            var i = 0;
            var frac = Fixed.One / scale - Fixed.Epsilon;
            var step = Fixed.One / scale;

            if (drawX < 0)
            {
                var exceed = -drawX;
                frac += exceed * step;
                i += exceed;
            }

            if (drawX + drawWidth > width)
            {
                var exceed = drawX + drawWidth - width;
                drawWidth -= exceed;
            }

            for (; i < drawWidth; i++)
            {
                DrawColumn(patch.Columns[frac.ToIntFloor()], drawX + i, drawY, scale);
                frac += step;
            }
        }

        public void DrawPatchFlip(Patch patch, int x, int y, int scale)
        {
            var drawX = x - scale * patch.LeftOffset;
            var drawY = y - scale * patch.TopOffset;
            var drawWidth = scale * patch.Width;

            var i = 0;
            var frac = Fixed.One / scale - Fixed.Epsilon;
            var step = Fixed.One / scale;

            if (drawX < 0)
            {
                var exceed = -drawX;
                frac += exceed * step;
                i += exceed;
            }

            if (drawX + drawWidth > width)
            {
                var exceed = drawX + drawWidth - width;
                drawWidth -= exceed;
            }

            for (; i < drawWidth; i++)
            {
                var col = patch.Width - frac.ToIntFloor() - 1;
                DrawColumn(patch.Columns[col], drawX + i, drawY, scale);
                frac += step;
            }
        }

        private void DrawColumn(Column[] source, int x, int y, int scale)
        {
            DrawColumnTranslated(source, x, y, scale, null);
        }

        /// <summary>
        /// One column of a patch, optionally remapped through a translation table.
        ///
        /// The scaling loop is duplicated from the original rather than sharing one
        /// with a branch inside: this runs for every pixel of every glyph and the
        /// console draws a lot of them.
        /// </summary>
        private void DrawColumnTranslated(Column[] source, int x, int y, int scale, byte[] translation)
        {
            var step = Fixed.One / scale;

            foreach (var column in source)
            {
                var exTopDelta = scale * column.TopDelta;
                var exLength = scale * column.Length;

                var sourceIndex = column.Offset;
                var drawY = y + exTopDelta;
                var drawLength = exLength;

                var i = 0;
                var p = height * x + drawY;
                var frac = Fixed.One / scale - Fixed.Epsilon;

                if (drawY < 0)
                {
                    var exceed = -drawY;
                    p += exceed;
                    frac += exceed * step;
                    i += exceed;
                }

                if (drawY + drawLength > height)
                {
                    var exceed = drawY + drawLength - height;
                    drawLength -= exceed;
                }

                if (translation == null)
                {
                    for (; i < drawLength; i++)
                    {
                        data[p] = column.Data[sourceIndex + frac.ToIntFloor()];
                        p++;
                        frac += step;
                    }
                }
                else
                {
                    for (; i < drawLength; i++)
                    {
                        data[p] = translation[column.Data[sourceIndex + frac.ToIntFloor()]];
                        p++;
                        frac += step;
                    }
                }
            }
        }

        public void DrawText(IList<char> text, int x, int y, int scale)
        {
            var drawX = x;
            var drawY = y - 7 * scale;
            foreach (var ch in text)
            {
                if (ch >= chars.Length)
                {
                    continue;
                }

                if (ch == 32)
                {
                    drawX += 4 * scale;
                    continue;
                }

                var index = (int)ch;
                if ('a' <= index && index <= 'z')
                {
                    index = index - 'a' + 'A';
                }

                var patch = chars[index];
                if (patch == null)
                {
                    continue;
                }

                DrawPatch(patch, drawX, drawY, scale);

                drawX += scale * patch.Width;
            }
        }

        public void DrawChar(char ch, int x, int y, int scale)
        {
            var drawX = x;
            var drawY = y - 7 * scale;

            if (ch >= chars.Length)
            {
                return;
            }

            if (ch == 32)
            {
                return;
            }

            var index = (int)ch;
            if ('a' <= index && index <= 'z')
            {
                index = index - 'a' + 'A';
            }

            var patch = chars[index];
            if (patch == null)
            {
                return;
            }

            DrawPatch(patch, drawX, drawY, scale);
        }

        public void DrawText(string text, int x, int y, int scale)
        {
            DrawTextTranslated(text, x, y, scale, null);
        }

        /// <summary>
        /// The pixel width of one character at this scale, including the space's own advance.
        ///
        /// Public because the message line has to know where to break, and the renderer has no way
        /// to measure a font it does not own. A zero width means "no glyph", which is how a
        /// character this font cannot draw is skipped rather than drawn as a gap.
        /// </summary>
        public int CharWidth(char ch, int scale)
        {
            if (ch == ' ')
            {
                return 4 * scale;
            }

            var index = (int)ch;

            if (index >= chars.Length)
            {
                return 0;
            }

            if ('a' <= index && index <= 'z')
            {
                index = index - 'a' + 'A';
            }

            var patch = chars[index];

            return patch == null ? 0 : scale * patch.Width;
        }

        /// <summary>
        /// Text wrapped to the screen width, from the top, never past the bottom.
        ///
        /// Returns how many lines were drawn, so a caller can say so - and so a test can check that a
        /// message too long for the screen is cut at a line boundary rather than in the middle of a
        /// glyph run.
        /// </summary>
        public int DrawTextWrapped(string text, int x, int y, int scale, int bottom)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var lines = TextLayout.Wrap(text, Width - 2 * x, c => CharWidth(c, scale));

            // The font is 7 pixels tall and the line step is 8, which is what the status bar and
            // the menus already assume.
            var lineHeight = 8 * scale;

            var fits = TextLayout.LinesThatFit(y, lineHeight, bottom);

            var drawn = 0;

            foreach (var line in lines)
            {
                if (drawn >= fits)
                {
                    break;
                }

                DrawText(line, x, y, scale);

                y += lineHeight;
                drawn++;
            }

            return drawn;
        }

        /// <summary>
        /// Text in a tinted colour.
        ///
        /// <paramref name="translation"/> is a 256-entry table that maps every
        /// palette index to another one; the console builds these from PLAYPAL so a
        /// tint keeps the glyph's own shading instead of flattening it into a
        /// silhouette. Null means "the colours the patch already has".
        /// </summary>
        public void DrawTextColored(string text, int x, int y, int scale, byte[] translation)
        {
            DrawTextTranslated(text, x, y, scale, translation);
        }

        private void DrawTextTranslated(string text, int x, int y, int scale, byte[] translation)
        {
            var drawX = x;
            var drawY = y - 7 * scale;
            foreach (var ch in text)
            {
                if (ch >= chars.Length)
                {
                    continue;
                }

                if (ch == 32)
                {
                    drawX += 4 * scale;
                    continue;
                }

                var index = (int)ch;
                if ('a' <= index && index <= 'z')
                {
                    index = index - 'a' + 'A';
                }

                var patch = chars[index];
                if (patch == null)
                {
                    continue;
                }

                DrawPatchTranslated(patch, drawX, drawY, scale, translation);

                drawX += scale * patch.Width;
            }
        }

        /// <summary>
        /// A patch drawn through a colour translation table.
        ///
        /// The transparent parts of a patch are the columns it does not have, so
        /// they stay transparent; only the drawn pixels are remapped.
        /// </summary>
        private void DrawPatchTranslated(Patch patch, int x, int y, int scale, byte[] translation)
        {
            if (translation == null)
            {
                DrawPatch(patch, x, y, scale);
                return;
            }

            var drawX = x - scale * patch.LeftOffset;
            var drawY = y - scale * patch.TopOffset;
            var drawWidth = scale * patch.Width;

            var i = 0;
            var frac = Fixed.One / scale - Fixed.Epsilon;
            var step = Fixed.One / scale;

            if (drawX < 0)
            {
                var exceed = -drawX;
                frac += exceed * step;
                i += exceed;
            }

            if (drawX + drawWidth > width)
            {
                drawWidth -= drawX + drawWidth - width;
            }

            for (; i < drawWidth; i++)
            {
                DrawColumnTranslated(patch.Columns[frac.ToIntFloor()], drawX + i, drawY, scale, translation);
                frac += step;
            }
        }

        public int MeasureChar(char ch, int scale)
        {
            if (ch >= chars.Length)
            {
                return 0;
            }

            if (ch == 32)
            {
                return 4 * scale;
            }

            var index = (int)ch;
            if ('a' <= index && index <= 'z')
            {
                index = index - 'a' + 'A';
            }

            var patch = chars[index];
            if (patch == null)
            {
                return 0;
            }

            return scale * patch.Width;
        }

        public int MeasureText(IList<char> text, int scale)
        {
            var width = 0;

            foreach (var ch in text)
            {
                if (ch >= chars.Length)
                {
                    continue;
                }

                if (ch == 32)
                {
                    width += 4 * scale;
                    continue;
                }

                var index = (int)ch;
                if ('a' <= index && index <= 'z')
                {
                    index = index - 'a' + 'A';
                }

                var patch = chars[index];
                if (patch == null)
                {
                    continue;
                }

                width += scale * patch.Width;
            }

            return width;
        }

        public int MeasureText(string text, int scale)
        {
            var width = 0;

            foreach (var ch in text)
            {
                if (ch >= chars.Length)
                {
                    continue;
                }

                if (ch == 32)
                {
                    width += 4 * scale;
                    continue;
                }

                var index = (int)ch;
                if ('a' <= index && index <= 'z')
                {
                    index = index - 'a' + 'A';
                }

                var patch = chars[index];
                if (patch == null)
                {
                    continue;
                }

                width += scale * patch.Width;
            }

            return width;
        }

        public void FillRect(int x, int y, int w, int h, int color)
        {
            var x1 = x;
            var x2 = x + w;
            for (var drawX = x1; drawX < x2; drawX++)
            {
                var pos = height * drawX + y;
                for (var i = 0; i < h; i++)
                {
                    data[pos] = (byte)color;
                    pos++;
                }
            }
        }



        [Flags]
        private enum OutCode
        {
            Inside = 0,
            Left = 1,
            Right = 2,
            Bottom = 4,
            Top = 8
        }

        private OutCode ComputeOutCode(float x, float y)
        {
            var code = OutCode.Inside;

            if (x < 0)
            {
                code |= OutCode.Left;
            }
            else if (x > width)
            {
                code |= OutCode.Right;
            }

            if (y < 0)
            {
                code |= OutCode.Bottom;
            }
            else if (y > height)
            {
                code |= OutCode.Top;
            }

            return code;
        }

        public void DrawLine(float x1, float y1, float x2, float y2, int color)
        {
            var outCode1 = ComputeOutCode(x1, y1);
            var outCode2 = ComputeOutCode(x2, y2);

            var accept = false;

            while (true)
            {
                if ((outCode1 | outCode2) == 0)
                {
                    accept = true;
                    break;
                }
                else if ((outCode1 & outCode2) != 0)
                {
                    break;
                }
                else
                {
                    var x = 0.0F;
                    var y = 0.0F;

                    var outcodeOut = outCode2 > outCode1 ? outCode2 : outCode1;

                    if ((outcodeOut & OutCode.Top) != 0)
                    {
                        x = x1 + (x2 - x1) * (height - y1) / (y2 - y1);
                        y = height;
                    }
                    else if ((outcodeOut & OutCode.Bottom) != 0)
                    {
                        x = x1 + (x2 - x1) * (0 - y1) / (y2 - y1);
                        y = 0;
                    }
                    else if ((outcodeOut & OutCode.Right) != 0)
                    {
                        y = y1 + (y2 - y1) * (width - x1) / (x2 - x1);
                        x = width;
                    }
                    else if ((outcodeOut & OutCode.Left) != 0)
                    {
                        y = y1 + (y2 - y1) * (0 - x1) / (x2 - x1);
                        x = 0;
                    }

                    if (outcodeOut == outCode1)
                    {
                        x1 = x;
                        y1 = y;
                        outCode1 = ComputeOutCode(x1, y1);
                    }
                    else
                    {
                        x2 = x;
                        y2 = y;
                        outCode2 = ComputeOutCode(x2, y2);
                    }
                }
            }

            if (accept)
            {
                var bx1 = MathEx.Clamp((int)x1, 0, width - 1);
                var by1 = MathEx.Clamp((int)y1, 0, height - 1);
                var bx2 = MathEx.Clamp((int)x2, 0, width - 1);
                var by2 = MathEx.Clamp((int)y2, 0, height - 1);
                Bresenham(bx1, by1, bx2, by2, color);
            }
        }

        private void Bresenham(int x1, int y1, int x2, int y2, int color)
        {
            var dx = x2 - x1;
            var ax = 2 * (dx < 0 ? -dx : dx);
            var sx = dx < 0 ? -1 : 1;

            var dy = y2 - y1;
            var ay = 2 * (dy < 0 ? -dy : dy);
            var sy = dy < 0 ? -1 : 1;

            var x = x1;
            var y = y1;

            if (ax > ay)
            {
                var d = ay - ax / 2;

                while (true)
                {
                    data[height * x + y] = (byte)color;

                    if (x == x2)
                    {
                        return;
                    }

                    if (d >= 0)
                    {
                        y += sy;
                        d -= ax;
                    }

                    x += sx;
                    d += ay;
                }
            }
            else
            {
                var d = ax - ay / 2;
                while (true)
                {
                    data[height * x + y] = (byte)color;

                    if (y == y2)
                    {
                        return;
                    }

                    if (d >= 0)
                    {
                        x += sx;
                        d -= ay;
                    }

                    y += sy;
                    d += ax;
                }
            }
        }

        public int Width => width;
        public int Height => height;
        public byte[] Data => data;

        /// <summary>
        /// Flips a rectangle left to right, in place.
        ///
        /// Used by ROFL mode, which mirrors the view rather than the level - see
        /// <see cref="RoflMode"/> for why that is the honest way to build the joke. The
        /// rectangle is clamped to the screen, so a caller that asks for a view window at an
        /// unusual size gets a correct flip of whatever part of it exists instead of an
        /// exception in the middle of a frame.
        ///
        /// The buffer is **column-major** - index <c>x * height + y</c> - and that is not a
        /// detail: reading it as rows produces a picture that is scrambled in a way that looks
        /// almost like a mirror, which is exactly how this was written wrong the first time.
        /// The renderer draws columns, so the buffer is stored the way it is drawn.
        /// </summary>
        public void MirrorRect(int x, int y, int w, int h)
        {
            var left = Math.Max(0, x);
            var top = Math.Max(0, y);
            var right = Math.Min(width, x + w);
            var bottom = Math.Min(height, y + h);

            if (right - left < 2 || bottom <= top)
            {
                return;
            }

            for (var column = left; column < left + (right - left) / 2; column++)
            {
                var opposite = right - 1 - (column - left);

                for (var row = top; row < bottom; row++)
                {
                    var a = column * height + row;
                    var b = opposite * height + row;

                    var swap = data[a];
                    data[a] = data[b];
                    data[b] = swap;
                }
            }
        }
    }
}
