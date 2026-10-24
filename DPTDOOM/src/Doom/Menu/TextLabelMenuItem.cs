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

namespace DPTDOOM
{
    /// <summary>
    /// A menu row whose label is text rather than a WAD patch.
    ///
    /// The classic Doom menus are assembled from graphics that id Software drew in
    /// 1994, which is why they can only offer what those graphics name. Anything the
    /// project has added since has no patch, so a row like this joins the existing
    /// menu instead of replacing it: the skull cursor, the sounds and the layout stay
    /// the same, and only the label is drawn with the game's own font.
    ///
    /// Text, not a picture, also means the row works with any IWAD - including one
    /// that replaced every menu graphic with its own.
    /// </summary>
    public sealed class TextLabelMenuItem : MenuItem
    {
        public TextLabelMenuItem(string label, int itemX, int itemY, int skullX, int skullY, MenuDef next)
            : base(skullX, skullY, next)
        {
            Label = label;
            ItemX = itemX;
            ItemY = itemY;
        }

        public string Label { get; private set; }

        /// <summary>Left edge of the label, in virtual pixels of a 320x200 screen.</summary>
        public int ItemX { get; private set; }

        /// <summary>Top edge of the label, in virtual pixels.</summary>
        public int ItemY { get; private set; }
    }
}
