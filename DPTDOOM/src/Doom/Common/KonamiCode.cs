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
    /// Watches for the Konami code: up, up, down, down, left, right, left, right, B, A.
    ///
    /// A separate class rather than a few lines in the menu, because the interesting part is
    /// not the sequence but what counts as "not it": a wrong key, a key held down, or a pause
    /// long enough that nobody would call it one entry. Getting that wrong is how an easter
    /// egg either fires by accident during ordinary menu use or never fires at all.
    ///
    /// The rules, and why each one is needed:
    ///
    ///  - **any wrong key starts over from zero.** No partial credit, no "find the longest
    ///    prefix that still matches". The code has repeated pairs (up up, down down), so a
    ///    lenient matcher would complete on ordinary menu scrolling - four arrow presses are
    ///    four rows - and an easter egg that fires during normal use is worse than one nobody
    ///    finds, because the player gets a mode they did not ask for and cannot explain.
    ///
    ///  - **a pause resets**, measured in tics and applied as the clock advances rather than
    ///    lazily at the next key. Somebody scrolling a menu, stopping to read, and carrying on
    ///    should not complete the code by accident.
    ///
    /// Two things this deliberately does not do, both of which were tried and dropped:
    ///
    ///  - it does not ignore keys that are not in the code. Ignoring them means "up, up,
    ///    down, down, left, right, left, right, B, x, A" completes, and the extra rule buys
    ///    nothing: nobody presses a letter by accident while typing a sequence of arrows.
    ///
    ///  - it does not recover from an overshoot. "up, up, up, down..." is a mistake and
    ///    starts over, which is how every code of this kind has always worked and is one rule
    ///    instead of two.
    /// </summary>
    public sealed class KonamiCode
    {
        private static readonly DoomKey[] Sequence =
        {
            DoomKey.Up, DoomKey.Up,
            DoomKey.Down, DoomKey.Down,
            DoomKey.Left, DoomKey.Right, DoomKey.Left, DoomKey.Right,
            DoomKey.B, DoomKey.A
        };

        /// <summary>
        /// How long a pause breaks the code, in tics.
        ///
        /// Two seconds: long enough that typing the code at a walking pace works, short enough
        /// that a player reading a menu and then moving on does not carry a half-finished code
        /// around with them.
        /// </summary>
        private const int ResetAfterTics = 2 * 35;

        private int progress;
        private int lastTic = int.MinValue;
        private int tic;

        /// <summary>Where in the sequence the player has got to, for diagnostics and tests.</summary>
        public int Progress
        {
            get { return progress; }
        }

        /// <summary>How long the whole sequence is.</summary>
        public static int Length
        {
            get { return Sequence.Length; }
        }

        /// <summary>
        /// Advances the clock by one tic. Called by the owner so that a pause can be measured
        /// without this class having to know what a clock is.
        ///
        /// The reset happens here rather than when the next key arrives, so that
        /// <see cref="Progress"/> is always the truth: a test, and a person reading a log,
        /// should not have to send a key to find out that the code has already expired.
        /// </summary>
        public void Update()
        {
            tic++;

            if (progress > 0 && unchecked(tic - lastTic) > ResetAfterTics)
            {
                progress = 0;
            }
        }

        /// <summary>
        /// Feeds one key. Returns true when this key completed the code.
        ///
        /// The caller must reset by simply not calling again: a completed code starts a fresh
        /// attempt, so the same sequence typed twice toggles twice.
        /// </summary>
        public bool Feed(DoomKey key)
        {
            lastTic = tic;

            if (key != Sequence[progress])
            {
                // No partial credit: see the class comment for why leniency here is what
                // makes an easter egg fire during ordinary menu use.
                progress = 0;
                return false;
            }

            progress++;

            if (progress == Sequence.Length)
            {
                progress = 0;
                return true;
            }

            return false;
        }
    }
}
