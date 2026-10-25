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

namespace DPTDOOM.Video
{
    /// <summary>
    /// Draws the chat log at the top of the view.
    ///
    /// Separate from the renderer for the same reason <see cref="TextLayout"/> is: this is layout
    /// and can be checked against a real screen without running a game. It also keeps one rule in
    /// one place - the log stops above the status bar, and nothing it draws can reach the health
    /// and ammo numbers.
    ///
    /// The oldest line is drawn first and the newest last, so a new message appears below the ones
    /// before it and the eye is already there. Drawing newest-first would put each new line above
    /// the previous and push the conversation down the screen.
    /// </summary>
    public static class ChatLogRenderer
    {
        /// <summary>The gap between the top of the view and the first line, in scaled units.</summary>
        public const int TopMargin = 6;

        /// <summary>
        /// Draws every line that fits, and returns how many were drawn.
        ///
        /// A line too long for the room is dropped whole rather than cut in half: half a sentence
        /// reads as a rendering fault, while a missing line reads as a message that has gone.
        /// </summary>
        public static int Render(DrawScreen screen, IList<ChatEntry> entries, int scale, int bottom)
        {
            if (screen == null || entries == null || entries.Count == 0)
            {
                return 0;
            }

            var lineHeight = 8 * scale;
            var width = screen.Width;

            // Wrapped first, so the decision of what fits is made against real line counts rather
            // than against a guess.
            var blocks = new List<List<string>>(entries.Count);
            var total = 0;

            foreach (var entry in entries)
            {
                var lines = TextLayout.Wrap(ChatLog.LineOf(entry), width, c => screen.CharWidth(c, scale));

                blocks.Add(new List<string>(lines));
                total += blocks[blocks.Count - 1].Count;
            }

            // Drop from the oldest end until the rest fits. The newest message is the one somebody
            // is waiting for, so it survives.
            var first = 0;

            while (first < blocks.Count && total > TextLayout.LinesThatFit(TopMargin * scale, lineHeight, bottom))
            {
                total -= blocks[first].Count;
                first++;
            }

            var y = TopMargin * scale;

            for (var i = first; i < blocks.Count; i++)
            {
                foreach (var line in blocks[i])
                {
                    if (y + lineHeight > bottom + lineHeight)
                    {
                        return Math.Max(0, (y - TopMargin * scale) / lineHeight);
                    }

                    screen.DrawText(line, 0, y, scale);
                    y += lineHeight;
                }
            }

            return (y - TopMargin * scale) / lineHeight;
        }
    }
}
