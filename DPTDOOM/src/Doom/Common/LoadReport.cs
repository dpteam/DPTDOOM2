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
    /// Everything the loader had to work around, collected in one place.
    ///
    /// Doom ports run into broken content constantly: a PWAD whose animation cycle
    /// points at a missing flat, a sprite set with only three of eight rotations, a map
    /// with a thing type nobody has heard of. Throwing on the first one means the
    /// player sees an exception instead of a level with one static texture; carrying
    /// on silently means nobody ever finds out why a texture does not animate.
    ///
    /// So: load what can be loaded, substitute what has a sane substitute, and collect
    /// the rest here. The report is printed once at the end of the load, as a summary
    /// plus the details - not interleaved, because during a load the player is watching
    /// a progress list, and forty "skipping x" lines in the middle of it is noise.
    ///
    /// When a port's feature is being added, this is where a missing piece shows up
    /// instead of as a crash three maps later.
    /// </summary>
    public static class LoadReport
    {
        /// <summary>One kind of problem, with everything that has it.</summary>
        private sealed class Entry
        {
            public string Kind;
            public string Consequence;
            public readonly List<string> Items = new List<string>();

            /// <summary>
            /// How many items have already been shown by <see cref="Print"/>.
            ///
            /// The report is printed twice per game: once when the content is loaded and
            /// once when each level is built, because a broken flat is only noticed
            /// before the map and a broken thing only during it. Without this count the
            /// second print would repeat the first one, and a player who changed level
            /// would read the same warning again on every map.
            /// </summary>
            public int Printed;
        }

        private static readonly List<Entry> entries = new List<Entry>();

        /// <summary>
        /// How many examples of each kind are shown.
        ///
        /// A mod with eighty broken flats would otherwise bury the report it is
        /// supposed to be part of. The count is always exact; only the listing is
        /// limited.
        /// </summary>
        private const int ExamplesShown = 4;

        public static void Clear()
        {
            entries.Clear();
        }

        /// <summary>
        /// Records a problem.
        /// </summary>
        /// <param name="kind">What went wrong, as a noun phrase: "bad animation cycle".</param>
        /// <param name="subject">Which one: the flat pair, the sprite name, the thing type.</param>
        /// <param name="consequence">
        /// What the game did instead. Required, because "something was skipped" without
        /// "and this is what you see" is a message a player can do nothing with.
        /// </param>
        public static void Add(string kind, string subject, string consequence)
        {
            Entry target = null;

            foreach (var entry in entries)
            {
                if (string.Equals(entry.Kind, kind, StringComparison.Ordinal))
                {
                    target = entry;
                    break;
                }
            }

            if (target == null)
            {
                target = new Entry { Kind = kind, Consequence = consequence };
                entries.Add(target);
            }

            target.Items.Add(subject);
        }

        /// <summary>
        /// Records a map lump whose size is not a whole number of records, and returns
        /// how many complete records it holds.
        ///
        /// A truncated lump is the usual symptom of a PWAD that was cut short by a
        /// failed download or an editor that crashed, and every map reader used to
        /// throw a bare <c>new Exception()</c> on it - an exception with no message, no
        /// lump name and no map, which tells the player nothing and the author less.
        /// Reading the records that are there is what the game did before doom-ports
        /// started validating: the map is playable, the last object or line is missing,
        /// and the report says which lump lost how many bytes.
        /// </summary>
        public static int WholeRecords(string lumpName, int length, int recordSize)
        {
            var whole = length / recordSize;
            var leftover = length - whole * recordSize;

            if (leftover > 0)
            {
                Add(
                    "truncated map lump",
                    lumpName + " (" + leftover + (leftover == 1 ? " leftover byte, " : " leftover bytes, ") +
                        whole + " records)",
                    "the partial record is ignored");
            }

            return whole;
        }

        /// <summary>Total number of problems, so a test can assert on it.</summary>
        public static int Count
        {
            get
            {
                var count = 0;

                foreach (var entry in entries)
                {
                    count += entry.Items.Count;
                }

                return count;
            }
        }

        public static bool HasProblems
        {
            get { return Count > 0; }
        }

        /// <summary>
        /// Prints what has been recorded since the last call, if anything has.
        ///
        /// Silent when the content was fine: a clean load should not end with a
        /// heading that says "warnings: none", because that trains people to ignore the
        /// heading.
        /// </summary>
        public static void Print()
        {
            var pending = 0;

            foreach (var entry in entries)
            {
                pending += entry.Items.Count - entry.Printed;
            }

            if (pending == 0)
            {
                return;
            }

            ConsoleUi.Section("problems");

            foreach (var entry in entries)
            {
                if (entry.Items.Count == entry.Printed)
                {
                    continue;
                }

                ConsoleUi.Warn(entry.Kind + ": " + entry.Items.Count + " - " + entry.Consequence);

                var shown = 0;

                for (var i = entry.Printed; i < entry.Items.Count; i++)
                {
                    if (shown == ExamplesShown)
                    {
                        ConsoleUi.Detail("... and " + (entry.Items.Count - entry.Printed - ExamplesShown) + " more");
                        break;
                    }

                    ConsoleUi.Detail(entry.Items[i]);
                    shown++;
                }

                entry.Printed = entry.Items.Count;
            }
        }

        /// <summary>
        /// The report as plain text, for tests and for a bug report.
        ///
        /// Same content as <see cref="Print"/>, without the console decoration, so a
        /// test does not have to strip escape sequences to read it.
        /// </summary>
        public static string Describe()
        {
            if (!HasProblems)
            {
                return "";
            }

            var text = new System.Text.StringBuilder();

            foreach (var entry in entries)
            {
                text.Append(entry.Kind).Append(": ").Append(entry.Items.Count)
                    .Append(" - ").Append(entry.Consequence).Append('\n');

                var shown = 0;

                foreach (var item in entry.Items)
                {
                    if (shown == ExamplesShown)
                    {
                        text.Append("  ... and ").Append(entry.Items.Count - ExamplesShown)
                            .Append(" more\n");
                        break;
                    }

                    text.Append("  ").Append(item).Append('\n');
                    shown++;
                }
            }

            return text.ToString();
        }
    }
}
