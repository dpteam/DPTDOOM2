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
    /// A settings screen drawn with the game's own font instead of WAD patches.
    ///
    /// The classic menus are patch-based, which is why they can only offer what id
    /// Software drew in 1994. Everything this project has added since - the
    /// crosshair, vertical look, the frame profiler - has no patch, so it lives
    /// here.
    ///
    /// Escape goes back to the parent menu rather than closing everything, because
    /// this screen is always opened from somewhere and a player who wants to change
    /// one more setting should not have to walk the whole menu again.
    /// </summary>
    public sealed class TextMenu : MenuDef
    {
        private readonly string title;
        private readonly TextMenuItem[] items;

        private int index;

        public TextMenu(DoomMenu menu, string title, MenuDef parent, params TextMenuItem[] items)
            : base(menu)
        {
            this.title = title;
            Parent = parent;
            this.items = items;

            index = FirstSelectable(0, 1);
        }

        public string Title
        {
            get { return title; }
        }

        public IList<TextMenuItem> Items
        {
            get { return items; }
        }

        public int Index
        {
            get { return index; }
        }

        public TextMenuItem Current
        {
            get { return items[index]; }
        }

        public MenuDef Parent { get; set; }

        public override void Open()
        {
            // Reopening always starts at the top: coming back to a screen scrolled
            // to the bottom, because that is where the player was last time, makes
            // the first press of Up do nothing visible.
            index = FirstSelectable(0, 1);
        }

        /// <summary>
        /// Finds the next row that can be selected, in the given direction.
        ///
        /// Sections are not selectable, so a menu that starts with one has to skip
        /// it on open - otherwise the highlight would sit on a heading and the first
        /// Enter would do nothing.
        /// </summary>
        private int FirstSelectable(int from, int direction)
        {
            for (var i = 0; i < items.Length; i++)
            {
                var at = from + i * direction;

                if (at < 0)
                {
                    at = items.Length - 1;
                }

                if (at >= items.Length)
                {
                    at = 0;
                }

                if (items[at] as SectionTextItem == null)
                {
                    return at;
                }
            }

            return 0;
        }

        private void Move(int direction)
        {
            index = FirstSelectable(index + direction, direction);
        }

        public override bool DoEvent(DoomEvent e)
        {
            if (e.Type != EventType.KeyDown)
            {
                return true;
            }

            switch (e.Key)
            {
                case DoomKey.Up:
                    Move(-1);
                    Menu.StartSound(Sfx.PSTOP);
                    return true;

                case DoomKey.Down:
                    Move(1);
                    Menu.StartSound(Sfx.PSTOP);
                    return true;

                case DoomKey.Left:
                    if (Current.Adjust(-1))
                    {
                        Menu.StartSound(Sfx.STNMOV);
                    }

                    return true;

                case DoomKey.Right:
                    if (Current.Adjust(1))
                    {
                        Menu.StartSound(Sfx.STNMOV);
                    }

                    return true;

                case DoomKey.Enter:
                case DoomKey.Space:
                    if (Current.Enabled)
                    {
                        Current.Activate();
                        Menu.StartSound(Sfx.PISTOL);
                    }

                    return true;

                case DoomKey.Escape:
                    Back();
                    return true;
            }

            return true;
        }

        private void Back()
        {
            Menu.StartSound(Sfx.SWTCHX);

            if (Parent != null)
            {
                Menu.SetCurrent(Parent);
                return;
            }

            Menu.Close();
        }
    }
}
