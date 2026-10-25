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
using System.Collections.Generic;

namespace DPTDOOM
{
    /// <summary>
    /// Splits a message into lines that fit a width, and says how tall it is.
    ///
    /// The message line used to draw one string at the top of the screen and advance until it ran
    /// out of characters. At 320 pixels and the Doom font's own widths that is about forty
    /// characters, so a chat message - which may be ninety-six - was cut off at the right edge with
    /// no sign that anything was missing. A player typing a sentence saw the first half of it.
    ///
    /// Wrapping lives here rather than in the renderer because it is arithmetic over glyph widths,
    /// and because the renderer cannot be tested without a screen. The width table is passed in
    /// rather than read from a texture, so this class knows nothing about fonts.
    ///
    /// Two rules that matter and are easy to get wrong:
    ///
    ///  - **an explicit newline is always a break.** Several of the engine's own strings contain
    ///    one, and drawing them as one line silently swallowed the break, because a newline has no
    ///    glyph and the drawing loop skipped it without moving the cursor.
    ///  - **a word longer than the line is broken, not dropped.** A URL or a run of digits in a
    ///    chat message has no spaces in it, and refusing the whole word would lose the message.
    /// </summary>
    public static class TextLayout
    {
        /// <summary>
        /// Wraps <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/>.
        ///
        /// <paramref name="widthOf"/> returns the pixel width of one character at the scale being
        /// drawn, including the character's own spacing.
        /// </summary>
        public static IList<string> Wrap(string text, int maxWidth, Func<char, int> widthOf)
        {
            var lines = new List<string>();

            if (string.IsNullOrEmpty(text))
            {
                return lines;
            }

            if (maxWidth <= 0)
            {
                // Nothing fits. The honest answer is one line per explicit line, unbroken: the
                // caller asked for a layout that cannot be satisfied, and truncating to nothing
                // would look like an empty message.
                foreach (var part in text.Split('\n'))
                {
                    lines.Add(part);
                }

                return lines;
            }

            foreach (var paragraph in text.Split('\n'))
            {
                WrapParagraph(paragraph, maxWidth, widthOf, lines);
            }

            return lines;
        }

        private static void WrapParagraph(string paragraph, int maxWidth, Func<char, int> widthOf,
                                          List<string> lines)
        {
            if (paragraph.Length == 0)
            {
                // An empty paragraph is a blank line, which is what a double newline means.
                lines.Add("");
                return;
            }

            var start = 0;
            var lastSpace = -1;
            var width = 0;

            for (var i = 0; i < paragraph.Length; i++)
            {
                var c = paragraph[i];
                var w = widthOf(c);

                if (c == ' ')
                {
                    lastSpace = i;
                }

                if (width + w > maxWidth && i > start)
                {
                    // Break at the last space when there is one; otherwise break the word, because
                    // an unbreakable run must still be readable.
                    var cut = lastSpace > start ? lastSpace : i;
                    var line = paragraph.Substring(start, cut - start);

                    lines.Add(line.TrimEnd());

                    // The space that ended the line is consumed; a word broken mid-run is not.
                    start = lastSpace > start ? lastSpace + 1 : i;
                    width = 0;
                    lastSpace = -1;

                    // Re-measure from the new start, so a word broken mid-run is measured from its
                    // own first character.
                    for (var k = start; k < i; k++)
                    {
                        width += widthOf(paragraph[k]);
                    }
                }

                width += w;
            }

            if (start < paragraph.Length)
            {
                lines.Add(paragraph.Substring(start).TrimEnd());
            }
            else if (lines.Count == 0)
            {
                lines.Add("");
            }
        }

        /// <summary>
        /// How many lines fit below a starting height, given the screen's height.
        ///
        /// Used to refuse to draw past the bottom: a message long enough to fill the screen would
        /// otherwise spill over the status bar, which reads as a rendering fault.
        /// </summary>
        public static int LinesThatFit(int startY, int lineHeight, int screenHeight)
        {
            if (lineHeight <= 0)
            {
                return 0;
            }

            var room = screenHeight - startY;

            return room <= 0 ? 0 : room / lineHeight;
        }
    }
}
