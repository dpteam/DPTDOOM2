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
using System.Text;

namespace DPTDOOM.Master
{
    /// <summary>
    /// Operator configuration. Everything is optional; the defaults listen on
    /// loopback only, which is the safe choice for a first run.
    /// </summary>
    internal sealed class MasterConfig
    {
        public string Bind = "127.0.0.1";
        public int Port = 25135;
        public int LeaseSeconds = 60;
        public string BanListPath;
        public string AllowListPath;
        public bool AllowListOnly;
        public string LogPath;
        public int MaxClients = 24;

        // Relay is opt-in. Enabling it turns the master into a proxy for other
        // people's game traffic, which is a different promise than "a directory
        // that answers two UDP-sized questions" - so it is never on by default.
        public bool RelayEnabled;
        public int RelayPort; // 0 = Port + 1
        public int RelaySessions = 8;
        public int RelayPerSource = 4;
        public int RelayRate = 65536;
        public int RelayBuffer = 4096;
        public int RelayIdleSeconds = 120;
        public int RelayLifetimeSeconds = 21600;

        /// <summary>
        /// Guest tunnels one session may hold open at once.
        ///
        /// The relay exists for people who cannot forward a port, and it is paid
        /// for by one operator. A co-op session is 2-4 players, so three guest
        /// tunnels is the whole of it; more than that is either a mistake or
        /// somebody using the master as free hosting.
        /// </summary>
        public int RelayGuestsPerSession = 3;

        /// <summary>
        /// Relay-enabled sessions one source address may hold at once.
        ///
        /// Two, not one: a household with two machines behind the same router is
        /// a real case, and NAT hairpin is exactly why such a household needs the
        /// relay. More than two from one address is not two people playing.
        /// </summary>
        public int RelaySessionsPerSource = 2;

        public static MasterConfig Load(string path, List<string> warnings)
        {
            var config = new MasterConfig();

            if (path == null)
            {
                return config;
            }

            if (!File.Exists(path))
            {
                throw new Exception("cannot open config '" + path + "'");
            }

            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path));

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();

                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                {
                    continue;
                }

                var split = line.IndexOf('=');
                if (split <= 0)
                {
                    warnings.Add("ignored config line: " + line);
                    continue;
                }

                var key = line.Substring(0, split).Trim().ToLowerInvariant();
                var value = line.Substring(split + 1).Trim();

                switch (key)
                {
                    case "bind":
                        config.Bind = value;
                        break;

                    case "port":
                        config.Port = ParseInt(value, 1, 65535, "port");
                        break;

                    case "lease-seconds":
                        config.LeaseSeconds = ParseInt(value, 3, 300, "lease-seconds");
                        break;

                    case "max-clients":
                        config.MaxClients = ParseInt(value, 1, 128, "max-clients");
                        break;

                    case "banlist":
                        config.BanListPath = Resolve(baseDirectory, value);
                        break;

                    case "allowlist":
                        config.AllowListPath = Resolve(baseDirectory, value);
                        break;

                    case "access-mode":
                        if (value == "allowlist")
                        {
                            config.AllowListOnly = true;
                        }
                        else if (value == "open")
                        {
                            config.AllowListOnly = false;
                        }
                        else
                        {
                            throw new Exception("access-mode must be 'open' or 'allowlist'");
                        }

                        break;

                    case "log":
                        config.LogPath = Resolve(baseDirectory, value);
                        break;

                    case "relay":
                        if (value == "on" || value == "yes" || value == "true")
                        {
                            config.RelayEnabled = true;
                        }
                        else if (value == "off" || value == "no" || value == "false")
                        {
                            config.RelayEnabled = false;
                        }
                        else
                        {
                            throw new Exception("relay must be 'on' or 'off'");
                        }

                        break;

                    case "relay-port":
                        config.RelayPort = ParseInt(value, 1, 65535, "relay-port");
                        config.RelayEnabled = true;
                        break;

                    case "relay-sessions":
                        config.RelaySessions = ParseInt(value, 1, 64, "relay-sessions");
                        break;

                    case "relay-per-source":
                        config.RelayPerSource = ParseInt(value, 1, 16, "relay-per-source");
                        break;

                    case "relay-rate":
                        config.RelayRate = ParseInt(value, 4096, 1048576, "relay-rate");
                        break;

                    case "relay-buffer":
                        config.RelayBuffer = ParseInt(value, 512, 65536, "relay-buffer");
                        break;

                    case "relay-idle-seconds":
                        config.RelayIdleSeconds = ParseInt(value, 10, 3600, "relay-idle-seconds");
                        break;

                    case "relay-lifetime-seconds":
                        config.RelayLifetimeSeconds = ParseInt(value, 60, 86400, "relay-lifetime-seconds");
                        break;

                    case "relay-guests-per-session":
                        config.RelayGuestsPerSession = ParseInt(value, 1, 16, "relay-guests-per-session");
                        break;

                    case "relay-sessions-per-source":
                        config.RelaySessionsPerSource = ParseInt(value, 1, 16, "relay-sessions-per-source");
                        break;

                    default:
                        warnings.Add("ignored unknown config key: " + key);
                        break;
                }
            }

            return config;
        }

        private static string Resolve(string baseDirectory, string value)
        {
            return Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value);
        }

        private static int ParseInt(string value, int min, int max, string name)
        {
            int result;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ||
                result < min || result > max)
            {
                throw new Exception(name + " must be " + min + ".." + max);
            }

            return result;
        }

        public static IPAddress ParseBind(string text)
        {
            IPAddress address;
            if (!IPAddress.TryParse(text, out address))
            {
                throw new Exception("bind must be a numeric IPv4 address");
            }

            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                throw new Exception("bind must be an IPv4 address");
            }

            return address;
        }
    }

    /// <summary>
    /// Raised relay quotas for one trusted source.
    ///
    /// Zero means "not specified, use the server's value". That makes the common case
    /// - a rule that only grants access, with no quota change - cost nothing and stay
    /// readable: nothing in the file, nothing in the config.
    ///
    /// These exist because the relay's quotas are **policy, not capability**: three
    /// guest tunnels is what a four-player co-op needs, and two sessions per address is
    /// what a household with two machines needs. Neither number is an engine limit, and
    /// an operator's own test rig or a family behind one router should not have to edit
    /// the server's config to get past a guess about strangers.
    /// </summary>
    internal sealed class RelayLimits
    {
        /// <summary>Guest tunnels one session may hold. Maps to relay-guests-per-session.</summary>
        public int GuestsPerSession;

        /// <summary>Sessions one address may hold. Maps to relay-sessions-per-source.</summary>
        public int SessionsPerSource;

        /// <summary>Channels (waiting or paired) one address may hold. Maps to relay-per-source.</summary>
        public int LinksPerSource;

        /// <summary>
        /// This source's own ceiling on total channels, past the server's global cap.
        ///
        /// Past, not instead of: an untrusted host still stops at the global cap, so
        /// raising this for one address cannot let strangers use the extra room.
        /// </summary>
        public int SessionsTotal;

        public static readonly RelayLimits None = new RelayLimits();

        public bool IsEmpty
        {
            get
            {
                return GuestsPerSession == 0 && SessionsPerSource == 0 &&
                       LinksPerSource == 0 && SessionsTotal == 0;
            }
        }

        public string Describe()
        {
            var parts = new List<string>();

            if (GuestsPerSession > 0) { parts.Add("guests/session=" + GuestsPerSession); }
            if (SessionsPerSource > 0) { parts.Add("sessions/source=" + SessionsPerSource); }
            if (LinksPerSource > 0) { parts.Add("links/source=" + LinksPerSource); }
            if (SessionsTotal > 0) { parts.Add("sessions/total=" + SessionsTotal); }

            return parts.Count == 0 ? "none" : string.Join(" ", parts.ToArray());
        }
    }

    /// <summary>
    /// A CIDR rule with an optional UTC expiry, matched against the TCP source
    /// address only.
    ///
    /// An allow-list rule may also carry raised relay quotas, which is how a trusted
    /// address gets past the relay's policy limits without the limits being removed for
    /// everybody.
    /// </summary>
    internal sealed class AddressRule
    {
        public uint Network;
        public int Prefix;
        public long Expires; // Unix seconds, 0 = permanent

        /// <summary>Raised relay quotas, or <see cref="RelayLimits.None"/>.</summary>
        public RelayLimits Lift = RelayLimits.None;

        public bool Matches(uint address, long now)
        {
            if (Expires != 0 && now >= Expires)
            {
                return false;
            }

            var mask = Prefix == 0 ? 0u : uint.MaxValue << (32 - Prefix);
            return (address & mask) == (Network & mask);
        }

        /// <summary>
        /// Matches an address, ignoring anything that is not IPv4.
        ///
        /// The rule format is IPv4-only, and treating an IPv6 address as if it were
        /// IPv4 would compare the first four bytes of something else - so an IPv6 peer
        /// simply never matches rather than matching by accident.
        /// </summary>
        public bool Matches(IPAddress address, long now)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            return Matches(ToUInt32(address), now);
        }

        /// <summary>
        /// The largest lift any matching rule grants, or <see cref="RelayLimits.None"/>.
        ///
        /// Largest, not first: overlapping rules are normal in a hand-written file - a
        /// /8 for the family and a /32 for the one machine that needs more - and taking
        /// the first match would make the answer depend on line order in a way nobody
        /// could predict. The biggest grant wins, which is also the reading a person
        /// expects.
        /// </summary>
        public static RelayLimits LiftFor(List<AddressRule> rules, IPAddress address, long now)
        {
            if (rules == null || address == null)
            {
                return RelayLimits.None;
            }

            RelayLimits best = RelayLimits.None;

            foreach (var rule in rules)
            {
                if (rule.Lift.IsEmpty || !rule.Matches(address, now))
                {
                    continue;
                }

                if (best.IsEmpty)
                {
                    best = rule.Lift;
                    continue;
                }

                best = new RelayLimits
                {
                    GuestsPerSession = Math.Max(best.GuestsPerSession, rule.Lift.GuestsPerSession),
                    SessionsPerSource = Math.Max(best.SessionsPerSource, rule.Lift.SessionsPerSource),
                    LinksPerSource = Math.Max(best.LinksPerSource, rule.Lift.LinksPerSource),
                    SessionsTotal = Math.Max(best.SessionsTotal, rule.Lift.SessionsTotal)
                };
            }

            return best;
        }

        public static List<AddressRule> Load(string path, List<string> warnings)
        {
            var rules = new List<AddressRule>();

            if (path == null)
            {
                return rules;
            }

            if (!File.Exists(path))
            {
                warnings.Add("rule file not found: " + path);
                return rules;
            }

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();

                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                AddressRule rule;
                string error;
                if (TryParse(line, out rule, out error))
                {
                    rules.Add(rule);
                }
                else
                {
                    warnings.Add("bad rule '" + line + "': " + error);
                }
            }

            return rules;
        }

        public static bool TryParse(string text, out AddressRule rule, out string error)
        {
            rule = null;
            error = null;

            var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = "empty rule";
                return false;
            }

            var cidr = parts[0];
            var slash = cidr.IndexOf('/');
            var addressText = slash < 0 ? cidr : cidr.Substring(0, slash);

            IPAddress address;
            if (!IPAddress.TryParse(addressText, out address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                error = "not an IPv4 address";
                return false;
            }

            var prefix = 32;
            if (slash >= 0)
            {
                if (!int.TryParse(cidr.Substring(slash + 1), out prefix) || prefix < 0 || prefix > 32)
                {
                    error = "bad prefix";
                    return false;
                }
            }

            rule = new AddressRule();
            rule.Network = ToUInt32(address);
            rule.Prefix = prefix;
            rule.Expires = 0;

            for (var i = 1; i < parts.Length; i++)
            {
                if (parts[i] == "until" && i + 1 < parts.Length)
                {
                    long expires;
                    if (!TryParseUtc(parts[i + 1], out expires))
                    {
                        error = "bad UTC timestamp";
                        return false;
                    }

                    rule.Expires = expires;
                    i++;
                }
                else if (parts[i] == "scope" && i + 1 < parts.Length)
                {
                    // Scopes only matter once the relay is implemented; the
                    // grammar is accepted so existing files keep working.
                    var scope = parts[i + 1];
                    if (scope != "both" && scope != "control" && scope != "relay")
                    {
                        error = "bad scope";
                        return false;
                    }

                    i++;
                }
                else if (TryParseLift(parts, ref i, rule, out error))
                {
                    if (error != null)
                    {
                        return false;
                    }
                }
                else
                {
                    error = "unexpected token '" + parts[i] + "'";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// A raised quota on an allow-list rule: "guests-per-session 6" and friends.
        ///
        /// Names match the configuration keys on purpose. An operator who already tuned
        /// relay-guests-per-session in the config should not have to work out that the
        /// same number is called something else in a rule file.
        /// </summary>
        private static bool TryParseLift(string[] parts, ref int i, AddressRule rule, out string error)
        {
            error = null;

            var name = parts[i];
            var needsValue =
                name == "guests-per-session" || name == "sessions-per-source" ||
                name == "links-per-source" || name == "sessions-total";

            if (!needsValue)
            {
                return false;
            }

            if (i + 1 >= parts.Length)
            {
                error = "'" + name + "' needs a number";
                return true;
            }

            int value;

            if (!int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                value < 1 || value > 1024)
            {
                error = "'" + name + "' must be a number from 1 to 1024";
                return true;
            }

            if (rule.Lift.IsEmpty)
            {
                rule.Lift = new RelayLimits();
            }

            switch (name)
            {
                case "guests-per-session":
                    rule.Lift.GuestsPerSession = value;
                    break;

                case "sessions-per-source":
                    rule.Lift.SessionsPerSource = value;
                    break;

                case "links-per-source":
                    rule.Lift.LinksPerSource = value;
                    break;

                default:
                    rule.Lift.SessionsTotal = value;
                    break;
            }

            i++;
            return true;
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

        public static uint ToUInt32(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        }
    }
}
