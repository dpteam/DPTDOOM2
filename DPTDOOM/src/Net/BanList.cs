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
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace DPTDOOM.Net
{
    /// <summary>
    /// Who is not allowed into this session.
    ///
    /// The grammar is a superset of the master's address rules on purpose: the same
    /// file can be pointed at either, and an operator who has already worked out how
    /// to write "10.0.0.0/8 until ..." does not have to learn a second dialect. The
    /// only addition is a name rule, because the master never sees a nickname and the
    /// host does.
    ///
    ///     # comments run to the end of the line
    ///     10.0.0.0/8                        an address or a subnet
    ///     203.0.113.7/32 until 2026-01-01T00:00:00Z
    ///     2001:db8::/32                     IPv6 works too
    ///     name troll                        one nickname, exact
    ///     name bad*                         a wildcard
    ///     name bad* until 2026-01-01T00:00:00Z
    ///
    /// Every rule carries an optional expiry, because a ban that cannot be lifted by
    /// the clock is a ban that nobody dares to set.
    /// </summary>
    public sealed class BanList
    {
        private readonly List<BanRule> rules = new List<BanRule>();

        public int Count => rules.Count;

        public static BanList Empty()
        {
            return new BanList();
        }

        /// <summary>
        /// Reads a rule file. A missing file is not an error, and a malformed line is
        /// a warning rather than a failure: a typo on line nine must not silently stop
        /// the other eight from working, and it must not stop the game from starting.
        /// </summary>
        public static BanList Load(string path, List<string> warnings)
        {
            var list = new BanList();

            if (string.IsNullOrEmpty(path))
            {
                return list;
            }

            if (!File.Exists(path))
            {
                warnings.Add("ban list not found: " + path);
                return list;
            }

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = StripComment(rawLine).Trim();

                if (line.Length == 0)
                {
                    continue;
                }

                BanRule rule;
                string error;

                if (TryParse(line, out rule, out error))
                {
                    list.rules.Add(rule);
                }
                else
                {
                    warnings.Add("bad ban rule '" + line + "': " + error);
                }
            }

            return list;
        }

        /// <summary>
        /// Whether this address is banned right now.
        ///
        /// A null address means "not known", which happens for a relayed guest: the
        /// host sees the master's socket, not the player's. Unknown must not match an
        /// address rule - the alternative is that a "ban" quietly matches the master
        /// and cuts off every relayed player at once.
        /// </summary>
        public bool IsAddressBanned(IPAddress address, DateTime utcNow)
        {
            if (address == null)
            {
                return false;
            }

            foreach (var rule in rules)
            {
                if (rule.MatchesAddress(address, utcNow))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether this nickname is banned right now.
        ///
        /// Case-insensitive: nicks are typed freely, and "Troll" is not a different
        /// person from "troll" - it is the same person a second later.
        /// </summary>
        public bool IsNameBanned(string name, DateTime utcNow)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (var rule in rules)
            {
                if (rule.MatchesName(name, utcNow))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The rules that matched, for the log line that explains a refusal.</summary>
        public string Explain(IPAddress address, string name, DateTime utcNow)
        {
            foreach (var rule in rules)
            {
                if (rule.MatchesAddress(address, utcNow) || rule.MatchesName(name, utcNow))
                {
                    return rule.Text;
                }
            }

            return "";
        }

        public static bool TryParse(string text, out BanRule rule, out string error)
        {
            rule = null;
            error = null;

            var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                error = "empty rule";
                return false;
            }

            var parsed = new BanRule();
            parsed.Text = text;
            parsed.IsName = parts[0] == "name";

            var start = 1;

            if (parsed.IsName)
            {
                if (parts.Length < 2)
                {
                    error = "a name rule needs a nickname";
                    return false;
                }

                parsed.NamePattern = parts[1].ToLowerInvariant();
                start = 2;
            }
            else
            {
                if (!TryParseNetwork(parts[0], parsed, out error))
                {
                    return false;
                }
            }

            for (var i = start; i < parts.Length; i++)
            {
                if (parts[i] == "until" && i + 1 < parts.Length)
                {
                    if (!TryParseUtc(parts[i + 1], out parsed.Expires))
                    {
                        error = "bad UTC timestamp, expected yyyy-MM-ddTHH:mm:ssZ";
                        return false;
                    }

                    i++;
                }
                else
                {
                    error = "unexpected token '" + parts[i] + "'";
                    return false;
                }
            }

            rule = parsed;
            return true;
        }

        private static bool TryParseNetwork(string text, BanRule rule, out string error)
        {
            error = null;

            var slash = text.IndexOf('/');
            var addressText = slash < 0 ? text : text.Substring(0, slash);

            IPAddress address;

            if (!IPAddress.TryParse(addressText, out address))
            {
                error = "not an IP address";
                return false;
            }

            var bits = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

            var prefix = bits;

            if (slash >= 0 &&
                (!int.TryParse(text.Substring(slash + 1), out prefix) || prefix < 0 || prefix > bits))
            {
                error = "bad prefix, expected 0.." + bits;
                return false;
            }

            rule.Address = address;
            rule.Prefix = prefix;
            return true;
        }

        /// <summary>
        /// Cuts a trailing comment.
        ///
        /// Only a '#' at the start of a token counts, so a rule cannot be broken by a
        /// '#' inside a nickname - and there is no quoting to get wrong.
        /// </summary>
        private static string StripComment(string line)
        {
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '#' && (i == 0 || line[i - 1] == ' ' || line[i - 1] == '\t'))
                {
                    return line.Substring(0, i);
                }
            }

            return line;
        }

        private static bool TryParseUtc(string text, out long seconds)
        {
            seconds = 0;

            DateTime parsed;

            if (!DateTime.TryParseExact(
                text,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out parsed))
            {
                return false;
            }

            seconds = (long)(parsed - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return true;
        }
    }

    /// <summary>One line of a ban file.</summary>
    public sealed class BanRule
    {
        /// <summary>The line as written, so a refusal can quote the rule that did it.</summary>
        public string Text;

        public bool IsName;
        public IPAddress Address;
        public int Prefix;
        public string NamePattern;

        /// <summary>Unix seconds, 0 = permanent.</summary>
        public long Expires;

        public bool MatchesAddress(IPAddress address, DateTime utcNow)
        {
            if (IsName || Address == null || address == null)
            {
                return false;
            }

            if (Expired(utcNow))
            {
                return false;
            }

            if (Address.AddressFamily != address.AddressFamily)
            {
                return false;
            }

            var expected = Address.GetAddressBytes();
            var actual = address.GetAddressBytes();

            if (expected.Length != actual.Length)
            {
                return false;
            }

            // Whole bytes first, then the leftover bits of the last byte. Comparing
            // bit by bit would also work and would be slower for no gain.
            var whole = Prefix / 8;
            var bits = Prefix % 8;

            for (var i = 0; i < whole; i++)
            {
                if (expected[i] != actual[i])
                {
                    return false;
                }
            }

            if (bits == 0)
            {
                return true;
            }

            var mask = (byte)(0xFF << (8 - bits));
            return (expected[whole] & mask) == (actual[whole] & mask);
        }

        public bool MatchesName(string name, DateTime utcNow)
        {
            if (!IsName || NamePattern == null || name == null || Expired(utcNow))
            {
                return false;
            }

            return Wildcard(NamePattern, name.ToLowerInvariant());
        }

        private bool Expired(DateTime utcNow)
        {
            if (Expires == 0)
            {
                return false;
            }

            var now = (long)(utcNow.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return now >= Expires;
        }

        /// <summary>
        /// A '*' glob, case already folded by the caller.
        ///
        /// Written out rather than pulled from a library because there is no library to
        /// pull: the whole project ships as one executable with no third-party code.
        /// </summary>
        private static bool Wildcard(string pattern, string value)
        {
            var p = 0;
            var v = 0;
            var star = -1;
            var mark = 0;

            while (v < value.Length)
            {
                if (p < pattern.Length && pattern[p] == '*')
                {
                    // Remember where the star was and try to match the rest from here.
                    star = p++;
                    mark = v;
                }
                else if (p < pattern.Length && pattern[p] == value[v])
                {
                    p++;
                    v++;
                }
                else if (star >= 0)
                {
                    // Backtrack: let the star swallow one more character.
                    p = star + 1;
                    v = ++mark;
                }
                else
                {
                    return false;
                }
            }

            while (p < pattern.Length && pattern[p] == '*')
            {
                p++;
            }

            return p == pattern.Length;
        }
    }
}
