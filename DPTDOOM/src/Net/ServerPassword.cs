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

using System.Security.Cryptography;
using System.Text;

namespace DPTDOOM.Net
{
    /// <summary>
    /// The session password: what it does, and - just as important - what it does not.
    ///
    /// It keeps strangers out of a game hosted for friends. It is **not** transport
    /// security: the password itself travels in the clear inside the handshake, and a
    /// relayed session is a plain byte pipe through the master. Anyone who can watch
    /// the traffic has the password and the game. Saying otherwise in the UI or the
    /// documentation would be worse than not having the feature, because somebody
    /// would trust it.
    ///
    /// Because it travels in the clear, the only thing worth hardening is how the
    /// host *compares* it. A byte-by-byte comparison that stops at the first
    /// difference tells an attacker how much of a guess was right, one character per
    /// probe, and a hundred probes is not a lot of probes. So both sides are reduced
    /// to fixed-size digests and those are compared without an early exit: the work
    /// no longer depends on where the two differ, or on how long the guess was.
    /// </summary>
    public static class ServerPassword
    {
        /// <summary>
        /// The longest password the wire format carries, counted in UTF-8 bytes.
        ///
        /// Bytes, not characters, because that is what the frame holds: the length
        /// prefix is a single byte, and a password of 64 emoji is 256 bytes, which
        /// would wrap that byte to zero and arrive as an empty password - a session
        /// nobody can join, for a reason the host cannot see.
        ///
        /// Bounded at all because a passphrase typed on a command line is not a key
        /// file: long enough for a sentence, short enough that nobody pastes a
        /// certificate.
        /// </summary>
        public const int MaxLength = 64;

        /// <summary>
        /// Whether a host with this password should let a guest with that one in.
        ///
        /// An empty requirement means the host is open: any guest passes, including
        /// one that sent nothing. That is the default, and it must stay cheap and
        /// silent - a public game should not care about this code at all.
        /// </summary>
        public static bool Matches(string expected, string given)
        {
            if (string.IsNullOrEmpty(expected))
            {
                return true;
            }

            var wanted = Digest(expected);
            var offered = Digest(given ?? "");

            // No early exit. The result is accumulated over the whole digest, so the
            // time taken is the same whether the first byte differs or the last.
            var difference = 0;

            for (var i = 0; i < wanted.Length; i++)
            {
                difference |= wanted[i] ^ offered[i];
            }

            return difference == 0;
        }

        /// <summary>
        /// Whether this password is usable at all, and why not if it is not.
        ///
        /// Checked when the host starts rather than when a guest arrives: a password
        /// silently truncated to the wire limit is not the password the host thinks
        /// they set, and the host is the only person who can fix that.
        /// </summary>
        public static bool TryValidate(string password, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(password))
            {
                return true;
            }

            if (Encoding.UTF8.GetByteCount(password) > MaxLength)
            {
                error = "a password may be at most " + MaxLength + " bytes when encoded, " +
                        "which is fewer characters for non-Latin text";
                return false;
            }

            // A space-only password is almost certainly a quoting mistake - an
            // unquoted argument that got split, or a stray space after the flag.
            // Accepting it would produce a session nobody can join for a reason the
            // host cannot see.
            var blank = true;

            foreach (var c in password)
            {
                if (c != ' ')
                {
                    blank = false;
                    break;
                }
            }

            if (blank)
            {
                error = "a password of only spaces looks like a quoting mistake";
                return false;
            }

            return true;
        }

        private static byte[] Digest(string value)
        {
            using (var hash = SHA256.Create())
            {
                return hash.ComputeHash(Encoding.UTF8.GetBytes(value));
            }
        }
    }
}
