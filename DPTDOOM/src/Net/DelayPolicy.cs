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

namespace DPTDOOM.Net
{
    /// <summary>
    /// How many tics of input to keep in hand, and when to change that.
    ///
    /// Lockstep advances one tic per network round trip unless commands are sent
    /// ahead of simulation: the tic cannot be simulated until the peer's command for
    /// it has arrived, and the peer only produces that command when it reaches the
    /// same tic. Sending <c>Delay</c> tics ahead breaks that coupling at the cost of
    /// <c>Delay</c> tics of input latency, which is the trade every lockstep game
    /// makes. See docs/NETWORK.md for the measurements that led here.
    ///
    /// Kept as pure functions on purpose. The interesting part is arithmetic on
    /// noisy numbers, and arithmetic on noisy numbers is exactly what cannot be
    /// checked by playing the game: a session on loopback has no jitter to measure,
    /// so the branch that matters is the one a live test never reaches.
    /// </summary>
    public static class DelayPolicy
    {
        /// <summary>
        /// Beyond this the buffer stops being compensation and becomes playing
        /// blind: ten tics is 286 ms of input latency.
        /// </summary>
        public const int MaxTics = 10;

        /// <summary>One tic, rounded down, in milliseconds.</summary>
        public const int TicMs = 1000 / NetProtocol.TicsPerSecond;

        /// <summary>
        /// What to assume when the round trip could not be measured at all.
        ///
        /// Four tics is about 115 ms of cover, which is the better guess for a real
        /// network than assuming a LAN and then stalling once a second.
        /// </summary>
        public const int UnmeasuredTics = 4;

        /// <summary>
        /// How many consecutive clean measurement windows are needed before the
        /// buffer is allowed to shrink by one tic.
        ///
        /// This is the number that stops the buffer from flapping. A stall raises it
        /// by one; lowering it again is only worth one tic of 28 ms, so it needs a
        /// long run of quiet to be worth risking another stall. At the two-second
        /// probe period this is a minute.
        /// </summary>
        public const int QuietWindowsBeforeShrinking = 30;

        /// <summary>
        /// A robust measure of how much the round trip moves around.
        ///
        /// Median absolute deviation, not the spread. The spread of a handful of
        /// probes is decided by whichever single probe was unluckiest, so one 40 ms
        /// hiccup on an otherwise clean link permanently bought an extra tic of
        /// latency that was never needed again. The MAD ignores that one probe and
        /// reports what the link does most of the time - and when the link really
        /// does jitter, the MAD grows with it, which is what makes the buffer size
        /// graded instead of a yes/no decision.
        ///
        /// Deliberately not covering a rare multi-hundred-millisecond stall. Paying
        /// a permanent four tics so that a once-a-minute hitch is smoothed would
        /// cost every player on every clean session; a stall that does happen raises
        /// the buffer afterwards, through <see cref="Adjust"/>.
        /// </summary>
        public static int JitterMs(IList<int> samples)
        {
            if (samples == null || samples.Count == 0)
            {
                return 0;
            }

            var sorted = new List<int>(samples);
            sorted.Sort();

            var median = sorted[sorted.Count / 2];

            if (sorted.Count % 2 == 0)
            {
                median = (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            }

            var deviations = new List<int>(sorted.Count);

            foreach (var sample in sorted)
            {
                deviations.Add(Math.Abs(sample - median));
            }

            deviations.Sort();

            var jitter = deviations[deviations.Count / 2];

            if (deviations.Count % 2 == 0)
            {
                jitter = (deviations[deviations.Count / 2 - 1] + deviations[deviations.Count / 2]) / 2;
            }

            return jitter;
        }

        /// <summary>The middle of the samples, which is the latency itself.</summary>
        public static int MedianMs(IList<int> samples)
        {
            if (samples == null || samples.Count == 0)
            {
                return -1;
            }

            var sorted = new List<int>(samples);
            sorted.Sort();

            if (sorted.Count % 2 == 0)
            {
                return (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            }

            return sorted[sorted.Count / 2];
        }

        /// <summary>
        /// How long a wait has to be before it counts as a stall.
        ///
        /// A tic is 28 ms and the frame pacer absorbs a wait shorter than that: the
        /// tic still completes inside its own budget and the player sees nothing. Only
        /// a wait past a whole tic means a tic was actually late.
        ///
        /// This is what keeps the adaptation sane at the bottom end. On loopback with
        /// no buffer every tic waits a few milliseconds for the peer - a session at
        /// full speed, which counted as a stall would raise the buffer one tic every
        /// two seconds until the cap, and then the player would really feel the
        /// latency the report insisted on adding.
        /// </summary>
        public const int FeltStallMs = TicMs;

        /// <summary>
        /// Whether a wait of this length is long enough for a player to notice.
        /// </summary>
        public static bool IsFelt(int waitedMs)
        {
            return waitedMs >= FeltStallMs;
        }

        /// <summary>
        /// The share of late tics in a window that means something systematic is
        /// wrong, in percent.
        ///
        /// One late tic in three hundred is a hiccup: the buffer was fine, something
        /// else in the machine paused for a moment, and buying a permanent tic of
        /// input latency for it is a bad trade that would also never be given back.
        /// A tenth of the tics late is a link that cannot carry this session at this
        /// buffer size, which is what the buffer is for.
        ///
        /// A share rather than a count, because the window is two seconds of however
        /// fast the game is running - 70 tics in a paced game, several hundred in an
        /// unthrottled test run - and a fixed count would mean different things in
        /// each.
        /// </summary>
        public const int FeltStallShare = 5;

        /// <summary>Whether this window's stalls are systematic rather than a hiccup.</summary>
        public static bool StallsMatter(int stalls, int tics)
        {
            if (stalls <= 0)
            {
                return false;
            }

            if (tics <= 0)
            {
                return true;
            }

            return stalls * 100 >= tics * FeltStallShare;
        }

        /// <summary>
        /// How far past the measured need the buffer may grow.
        ///
        /// The measurement is the evidence and it is right about clean links; two
        /// tics is the room for it to be wrong about a link that is worse in practice
        /// than the probes suggested, which happens because the probes run before the
        /// session's own traffic. Beyond that a link's own jitter dominates, no buffer
        /// size removes it, and more cover would be pure input latency.
        ///
        /// Without a bound the buffer ratchets to the cap and stays: a channel with a
        /// few late packets every window never reaches zero stalls, so "raise while
        /// stalls happen" has no stopping point that the link can reach.
        /// </summary>
        public const int SlackTics = 2;

        /// <summary>
        /// The largest buffer worth growing to for this link.
        /// </summary>
        public static int Ceiling(int roundTripMs, int jitterMs)
        {
            return MathEx.Clamp(Initial(roundTripMs, jitterMs) + SlackTics, 0, MaxTics);
        }

        /// <summary>
        /// The buffer a link needs, before anything has gone wrong.
        /// </summary>
        /// <param name="roundTripMs">Measured round trip, or a negative value for none.</param>
        /// <param name="jitterMs">From <see cref="JitterMs"/>.</param>
        public static int Initial(int roundTripMs, int jitterMs)
        {
            if (roundTripMs < 0)
            {
                return UnmeasuredTics;
            }

            if (roundTripMs < TicMs / 2)
            {
                // Same machine or a fast LAN: no buffer, no added latency.
                return 0;
            }

            var tics = (roundTripMs + TicMs - 1) / TicMs;

            // Jitter breaks an exactly-sized buffer: the first packet that takes
            // longer than the measurement stalls the tic. Graded, because a link
            // that swings by two tics needs two tics of cover, not one.
            if (jitterMs > 0)
            {
                var margin = (jitterMs + TicMs - 1) / TicMs;

                if (margin > 2)
                {
                    margin = 2;
                }

                tics += margin;
            }

            return MathEx.Clamp(tics, 1, MaxTics);
        }

        /// <summary>
        /// The next value for the buffer, given what happened since the last look.
        ///
        /// Raising is fast and based on evidence: tics that actually had to wait prove
        /// the buffer was too small, and no estimate beats that. When the current
        /// measurement already says the link needs more than the buffer has, the
        /// buffer jumps straight to the measured value - a session that is already
        /// stuttering does not need to be eased towards the number that would fix it.
        /// Otherwise it grows a tic at a time, within the ceiling that keeps it from
        /// ratcheting to the cap.
        ///
        /// Lowering is slow and never below what the measurement asks for, because the
        /// reward is one tic of latency while the risk is a stall the player feels.
        /// </summary>
        /// <param name="current">The buffer in use.</param>
        /// <param name="roundTripMs">Current measurement, or a negative value for none.</param>
        /// <param name="jitterMs">Current jitter.</param>
        /// <param name="stalls">Late tics in this window, as counted by the driver.</param>
        /// <param name="tics">Tics simulated in this window.</param>
        /// <param name="quietWindows">
        /// Consecutive windows with no significant stall and no change, reset by a
        /// raise.
        /// </param>
        public static int Adjust(
            int current, int roundTripMs, int jitterMs, int stalls, int tics, int quietWindows)
        {
            if (StallsMatter(stalls, tics))
            {
                var ceiling = Ceiling(roundTripMs, jitterMs);

                if (current >= ceiling)
                {
                    // Already as deep as this link justifies. The rest is its own
                    // jitter, which no buffer removes; growing further would only add
                    // input latency on top of the stutter.
                    return current;
                }

                var needed = Initial(roundTripMs, jitterMs);

                if (needed > current)
                {
                    return needed;
                }

                return current + 1;
            }

            if (current <= 0 || quietWindows < QuietWindowsBeforeShrinking)
            {
                return current;
            }

            // Never shrink below what this link needs on its own merits. Doing so
            // would just cause the stall that raises it again, and the player would
            // feel the same hitch over and over.
            if (current <= Initial(roundTripMs, jitterMs))
            {
                return current;
            }

            return current - 1;
        }
    }
}
