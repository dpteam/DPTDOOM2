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
    /// A CIDR rule with an optional UTC expiry, matched against the TCP source
    /// address only.
    /// </summary>
    internal sealed class AddressRule
    {
        public uint Network;
        public int Prefix;
        public long Expires; // Unix seconds, 0 = permanent

        public bool Matches(uint address, long now)
        {
            if (Expires != 0 && now >= Expires)
            {
                return false;
            }

            var mask = Prefix == 0 ? 0u : uint.MaxValue << (32 - Prefix);
            return (address & mask) == (Network & mask);
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
                else
                {
                    error = "unexpected token '" + parts[i] + "'";
                    return false;
                }
            }

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
