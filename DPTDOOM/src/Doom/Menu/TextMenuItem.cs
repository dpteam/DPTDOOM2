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
    /// One row of a text menu.
    ///
    /// A text row exists because the classic Doom menus are made of WAD patches,
    /// and there is no patch for "Крест" or "Обзор мышью". Everything the settings
    /// screen shows is therefore drawn with the game's own font, which also means
    /// the screen works with any IWAD - including one that replaced every menu
    /// graphic.
    ///
    /// Rows know how to display themselves and how to be adjusted; they do not know
    /// where they are drawn, so the renderer decides the layout and the item decides
    /// the content.
    /// </summary>
    public abstract class TextMenuItem
    {
        protected TextMenuItem(string label)
        {
            Label = label;
        }

        public string Label { get; private set; }

        /// <summary>
        /// The value shown on the right. Null for a row that has none.
        /// </summary>
        public virtual string Value
        {
            get { return null; }
        }

        /// <summary>
        /// True when left and right change something. A row that only navigates
        /// should not pretend it can be adjusted.
        /// </summary>
        public virtual bool Adjustable
        {
            get { return false; }
        }

        /// <summary>
        /// True when the row can be selected at all. A disabled row is still shown:
        /// hiding it would make the screen change shape as the game state changes,
        /// and a player looking for an option would not find out why it is missing.
        /// </summary>
        public virtual bool Enabled
        {
            get { return true; }
        }

        /// <summary>Left/right. Returns true when something changed.</summary>
        public virtual bool Adjust(int direction)
        {
            return false;
        }

        /// <summary>Enter. Returns true when the menu should close.</summary>
        public virtual bool Activate()
        {
            return false;
        }

        /// <summary>
        /// A quiet note under the row, shown only while it is selected.
        ///
        /// This is where a setting explains itself. Putting it in the label would
        /// make every row two lines tall and the screen unreadable.
        /// </summary>
        public virtual string Hint
        {
            get { return null; }
        }
    }

    /// <summary>An on/off switch.</summary>
    public sealed class ToggleTextItem : TextMenuItem
    {
        private readonly Func<bool> get;
        private readonly Action<bool> set;
        private readonly string hint;

        public ToggleTextItem(string label, Func<bool> get, Action<bool> set, string hint)
            : base(label)
        {
            this.get = get;
            this.set = set;
            this.hint = hint;
        }

        public override string Value
        {
            get { return get() ? "ON" : "OFF"; }
        }

        public override bool Adjustable
        {
            get { return true; }
        }

        public override string Hint
        {
            get { return hint; }
        }

        public override bool Adjust(int direction)
        {
            set(!get());
            return true;
        }

        public override bool Activate()
        {
            set(!get());
            return false;
        }
    }

    /// <summary>A number in a range, stepped with left and right.</summary>
    public sealed class SliderTextItem : TextMenuItem
    {
        private readonly int min;
        private readonly int max;
        private readonly Func<int> get;
        private readonly Action<int> set;
        private readonly string hint;

        /// <summary>
        /// Draws a bar as well as the number.
        ///
        /// A bare digit does not tell the player whether 8 out of 9 is nearly full
        /// or barely anything, and that is the one question a slider answers.
        /// </summary>
        private readonly bool bar;

        public SliderTextItem(string label, int min, int max,
                              Func<int> get, Action<int> set, string hint, bool bar = false)
            : base(label)
        {
            this.min = min;
            this.max = max;
            this.get = get;
            this.set = set;
            this.hint = hint;
            this.bar = bar;
        }

        public int Min
        {
            get { return min; }
        }

        public int Max
        {
            get { return max; }
        }

        public int Position
        {
            get { return get(); }
        }

        public bool HasBar
        {
            get { return bar; }
        }

        public override string Value
        {
            get { return bar ? null : get().ToString(); }
        }

        public override bool Adjustable
        {
            get { return true; }
        }

        public override string Hint
        {
            get { return hint; }
        }

        public override bool Adjust(int direction)
        {
            var next = get() + direction;

            if (next < min)
            {
                next = min;
            }

            if (next > max)
            {
                next = max;
            }

            if (next == get())
            {
                return false;
            }

            set(next);
            return true;
        }
    }

    /// <summary>A value picked from a list.</summary>
    public sealed class ChoiceTextItem : TextMenuItem
    {
        private readonly string[] options;
        private readonly Func<int> get;
        private readonly Action<int> set;
        private readonly string hint;

        public ChoiceTextItem(string label, string[] options,
                              Func<int> get, Action<int> set, string hint)
            : base(label)
        {
            this.options = options;
            this.get = get;
            this.set = set;
            this.hint = hint;
        }

        public int Count
        {
            get { return options.Length; }
        }

        public int Position
        {
            get
            {
                var index = get();

                if (index < 0 || index >= options.Length)
                {
                    return 0;
                }

                return index;
            }
        }

        public override string Value
        {
            get { return options[Position]; }
        }

        public override bool Adjustable
        {
            get { return true; }
        }

        public override string Hint
        {
            get { return hint; }
        }

        public override bool Adjust(int direction)
        {
            var index = Position + direction;

            // Wrapping rather than clamping: a three-item list is quicker to cycle
            // than to walk back from the end.
            if (index < 0)
            {
                index = options.Length - 1;
            }

            if (index >= options.Length)
            {
                index = 0;
            }

            set(index);
            return true;
        }
    }

    /// <summary>A row that does something when activated.</summary>
    public sealed class ActionTextItem : TextMenuItem
    {
        private readonly Action action;
        private readonly string hint;
        private readonly Func<bool> enabled;

        public ActionTextItem(string label, Action action, string hint, Func<bool> enabled = null)
            : base(label)
        {
            this.action = action;
            this.hint = hint;
            this.enabled = enabled;
        }

        public override bool Enabled
        {
            get { return enabled == null || enabled(); }
        }

        public override string Hint
        {
            get { return hint; }
        }

        public override bool Activate()
        {
            if (!Enabled || action == null)
            {
                return false;
            }

            action();
            return false;
        }
    }

    /// <summary>
    /// A row that only shows something: a version number, a session id.
    ///
    /// Selectable so the player can read its hint, but nothing changes.
    /// </summary>
    public sealed class InfoTextItem : TextMenuItem
    {
        private readonly Func<string> get;
        private readonly string hint;

        public InfoTextItem(string label, Func<string> get, string hint)
            : base(label)
        {
            this.get = get;
            this.hint = hint;
        }

        public override string Value
        {
            get { return get(); }
        }

        public override string Hint
        {
            get { return hint; }
        }
    }

    /// <summary>
    /// A heading inside the list.
    ///
    /// Not selectable, and drawn without a value column, so the eye can find the
    /// groups in a screen of twelve rows without a second frame.
    /// </summary>
    public sealed class SectionTextItem : TextMenuItem
    {
        public SectionTextItem(string label)
            : base(label)
        {
        }

        public override bool Enabled
        {
            get { return false; }
        }

        public bool Selectable
        {
            get { return false; }
        }
    }
}
