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
using System.Text;

namespace DPTDOOM
{
    /// <summary>What a console line is for, which decides how it is drawn.</summary>
    public enum ConsoleLineKind
    {
        /// <summary>Output of a command.</summary>
        Output,

        /// <summary>What the player typed, echoed back.</summary>
        Echo,

        /// <summary>Something went wrong: an unknown command, a refused one.</summary>
        Error,

        /// <summary>A quiet remark: hints, help text.</summary>
        Hint
    }

    public struct ConsoleLine
    {
        public string Text;
        public ConsoleLineKind Kind;

        public ConsoleLine(string text, ConsoleLineKind kind)
        {
            Text = text;
            Kind = kind;
        }
    }

    /// <summary>
    /// The drop-down console, opened with the key above Tab.
    ///
    /// Owns the scrollback, the input line, the history and tab completion. It does
    /// not know what a command is - <see cref="ConsoleCommands"/> does - and it does
    /// not draw itself, so all of it can be tested without a screen or a game.
    ///
    /// Like the chat, the console is not game state: nothing here enters a tic, a
    /// demo, a save or the world hash. A console that could change the simulation
    /// would be a cheat channel in a network game, which is exactly what the
    /// project refuses to have.
    /// </summary>
    public sealed class GameConsole
    {
        /// <summary>
        /// How much scrollback is kept.
        ///
        /// Bounded because a console is an append-only log and a long session with
        /// a chatty command would otherwise grow without limit. 200 lines is far
        /// more than anyone reads back.
        /// </summary>
        public const int MaxLines = 200;

        /// <summary>Longest command line, in characters.</summary>
        public const int MaxInput = 120;

        /// <summary>How many previous commands the up arrow walks through.</summary>
        public const int MaxHistory = 32;

        private readonly List<ConsoleLine> lines = new List<ConsoleLine>();
        private readonly StringBuilder input = new StringBuilder();
        private readonly List<string> history = new List<string>();

        private int historyIndex;

        /// <summary>
        /// The command names tab completion offers. Filled by the owner once the
        /// command table exists; the console never invents a name.
        /// </summary>
        private readonly List<string> completions = new List<string>();

        /// <summary>
        /// How far back the view is scrolled, in lines. Zero means "follow the
        /// output", which is what a player who just typed something expects.
        /// </summary>
        private int scroll;

        public bool Active { get; private set; }

        /// <summary>True when Enter was pressed and the line is waiting to run.</summary>
        public bool Submitted { get; private set; }

        public string Text
        {
            get { return input.ToString(); }
        }

        public int LineCount
        {
            get { return lines.Count; }
        }

        public int ScrollOffset
        {
            get { return scroll; }
        }

        public IList<ConsoleLine> Lines
        {
            get { return lines; }
        }

        /// <summary>
        /// Prints one or more lines, splitting on newlines.
        ///
        /// Splitting here rather than at every call site means a command that
        /// returns a block of text - "help" listing everything - does not have to
        /// walk its own output.
        /// </summary>
        public void Print(string text, ConsoleLineKind kind)
        {
            if (text == null)
            {
                return;
            }

            foreach (var piece in text.Replace("\r\n", "\n").Split('\n'))
            {
                lines.Add(new ConsoleLine(piece, kind));

                if (lines.Count > MaxLines)
                {
                    lines.RemoveAt(0);
                }
            }

            // New output means the player is looking at the newest line, not at
            // whatever they had scrolled back to.
            scroll = 0;
        }

        public void Print(string text)
        {
            Print(text, ConsoleLineKind.Output);
        }

        /// <summary>
        /// Registers a name for tab completion. Duplicates are ignored so an owner
        /// that rebuilds its table does not grow this list.
        /// </summary>
        public void AddCompletion(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            foreach (var existing in completions)
            {
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            completions.Add(name);
            completions.Sort(StringComparer.OrdinalIgnoreCase);
        }

        public void ClearCompletions()
        {
            completions.Clear();
        }

        public void Clear()
        {
            lines.Clear();
            scroll = 0;
        }

        public void Open()
        {
            Active = true;
            Submitted = false;
            scroll = 0;
        }

        public void Close()
        {
            Active = false;
            Submitted = false;
            input.Length = 0;
            historyIndex = history.Count;
        }

        public void Toggle()
        {
            if (Active)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>
        /// Consumes one event while the console is open. Returns true when the event
        /// belonged to the console, so the caller stops processing it.
        ///
        /// Every event is swallowed, not only the ones this method acts on: while
        /// the console is open, a letter must not also select a weapon, a digit must
        /// not also switch the view size, and typing "iddqd" must not enable a cheat.
        /// </summary>
        public bool Handle(DoomEvent e)
        {
            if (!Active)
            {
                return false;
            }

            if (e.Type != EventType.KeyDown)
            {
                return true;
            }

            switch (e.Key)
            {
                case DoomKey.Escape:
                    Close();
                    return true;

                case DoomKey.Tilde:
                    // The same key that opened it closes it, which is how every
                    // Quake-lineage console behaves and the one thing a player will
                    // try first.
                    Close();
                    return true;

                case DoomKey.Enter:
                    // Submitted, but not closed: the console stays open so several
                    // commands can be typed in a row, which is the whole reason to
                    // have one.
                    Submitted = true;
                    return true;

                case DoomKey.Backspace:
                    if (input.Length > 0)
                    {
                        input.Length--;
                    }

                    return true;

                case DoomKey.Up:
                    History(-1);
                    return true;

                case DoomKey.Down:
                    History(1);
                    return true;

                case DoomKey.PageUp:
                    ScrollBack(+1);
                    return true;

                case DoomKey.PageDown:
                    ScrollBack(-1);
                    return true;

                case DoomKey.Tab:
                    Complete();
                    return true;
            }

            var ch = e.Key.GetChar();

            // Printable ASCII only, for the same reason the chat is: the Doom font
            // has nothing beyond it, and a command the player cannot see themselves
            // typing is worse than one that is refused.
            if (ch >= ' ' && ch <= '~' && input.Length < MaxInput)
            {
                input.Append(ch);
            }

            return true;
        }

        /// <summary>
        /// Called by the owner after it has run the submitted line. Stores the
        /// command in the history so the up arrow can find it again.
        /// </summary>
        public void Remember(string line)
        {
            input.Length = 0;
            Submitted = false;

            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            // Repeating the previous command is common (looking something up twice)
            // and pressing up twice to get past a duplicate is annoying.
            if (history.Count > 0 &&
                string.Equals(history[history.Count - 1], line, StringComparison.Ordinal))
            {
                historyIndex = history.Count;
                return;
            }

            history.Add(line);

            if (history.Count > MaxHistory)
            {
                history.RemoveAt(0);
            }

            historyIndex = history.Count;
        }

        private void History(int direction)
        {
            if (history.Count == 0)
            {
                return;
            }

            historyIndex += direction;

            if (historyIndex < 0)
            {
                historyIndex = 0;
            }

            if (historyIndex >= history.Count)
            {
                historyIndex = history.Count;

                // Past the newest entry is the empty line the player was typing on,
                // not the last command again.
                input.Length = 0;
                return;
            }

            input.Length = 0;
            input.Append(history[historyIndex]);
        }

        private void ScrollBack(int direction)
        {
            scroll += direction * 4;

            if (scroll < 0)
            {
                scroll = 0;
            }

            var limit = Math.Max(0, lines.Count - 1);

            if (scroll > limit)
            {
                scroll = limit;
            }
        }

        /// <summary>
        /// Completes the current word against the known command names.
        ///
        /// Completes to the longest common prefix rather than to the first match: on
        /// "s" with "sensitivity" and "set" registered, jumping straight to one of
        /// them is a guess, while "se" is the part that is certainly right.
        /// </summary>
        private void Complete()
        {
            var text = input.ToString();

            var start = text.LastIndexOf(' ') + 1;
            var word = text.Substring(start);

            if (word.Length == 0)
            {
                return;
            }

            string common = null;
            var matches = 0;

            foreach (var name in completions)
            {
                if (name.Length < word.Length)
                {
                    continue;
                }

                if (string.Compare(name, 0, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    continue;
                }

                matches++;

                if (common == null)
                {
                    common = name;
                    continue;
                }

                var limit = Math.Min(common.Length, name.Length);
                var shared = 0;

                while (shared < limit && common[shared] == name[shared])
                {
                    shared++;
                }

                common = common.Substring(0, shared);
            }

            if (common == null)
            {
                return;
            }

            // A single match gets a trailing space, so the next thing typed is an
            // argument; a shared prefix does not, because there is more to type.
            var replacement = matches == 1 ? common + " " : common;

            input.Length = start;
            input.Append(replacement);
        }
    }
}
