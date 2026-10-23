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

namespace DPTDOOM.Shared
{
    /// <summary>
    /// The 132-byte session record published to the master. Layout is fixed by
    /// the wire protocol:
    ///
    ///   0..63    session name (printable ASCII, null padded)
    ///   64..83   build fingerprint (SHA-1 of the version string)
    ///   84..103  resource digest (WAD set fingerprint)
    ///   104..115 reserved (zero)
    ///   116..123 map text ("MAP01" or "E1M1")
    ///   124..125 game port
    ///   126      skill (1..5)
    ///   127      rule bits
    ///   128      game protocol
    ///   129..131 reserved (zero)
    /// </summary>
    public sealed class MasterSessionInfo
    {
        public string Name = "DPT.DOOM COOP";
        public byte[] BuildFingerprint = new byte[20];
        public byte[] ResourceDigest = new byte[20];
        public string MapText = "MAP01";
        public int Port = 5029;
        public int Skill = 3;
        public int RuleBits;
        public int GameProtocol = MasterProtocol.GameProtocol;

        public byte[] Encode()
        {
            var buffer = new byte[MasterProtocol.InfoSize];

            MasterProtocol.WriteAscii(buffer, 0, 64, Name);
            Array.Copy(BuildFingerprint, 0, buffer, 64, 20);
            Array.Copy(ResourceDigest, 0, buffer, 84, 20);
            MasterProtocol.WriteAscii(buffer, 116, 8, MapText);
            MasterProtocol.Put16(buffer, 124, Port);
            buffer[126] = (byte)Skill;
            buffer[127] = (byte)RuleBits;
            buffer[128] = (byte)GameProtocol;

            return buffer;
        }

        public static MasterSessionInfo Decode(byte[] buffer, int offset)
        {
            var info = new MasterSessionInfo();

            info.Name = MasterProtocol.ReadAscii(buffer, offset, 64);
            Array.Copy(buffer, offset + 64, info.BuildFingerprint, 0, 20);
            Array.Copy(buffer, offset + 84, info.ResourceDigest, 0, 20);
            info.MapText = MasterProtocol.ReadAscii(buffer, offset + 116, 8);
            info.Port = MasterProtocol.Get16(buffer, offset + 124);
            info.Skill = buffer[offset + 126];
            info.RuleBits = buffer[offset + 127];
            info.GameProtocol = buffer[offset + 128];

            return info;
        }

        /// <summary>Returns null when valid, otherwise the reason.</summary>
        public static string Validate(byte[] buffer, int offset)
        {
            var name = MasterProtocol.ReadAscii(buffer, offset, 64);
            if (name.Length == 0 || !MasterProtocol.IsPrintableAscii(name))
            {
                return "invalid-name";
            }

            var map = MasterProtocol.ReadAscii(buffer, offset + 116, 8);
            if (map.Length == 0 || !MasterProtocol.IsPrintableAscii(map))
            {
                return "invalid-map-text";
            }

            if (MasterProtocol.Get16(buffer, offset + 124) == 0)
            {
                return "invalid-game-port";
            }

            var skill = buffer[offset + 126];
            if (skill < 1 || skill > 5)
            {
                return "invalid-skill";
            }

            if (buffer[offset + 127] > 31)
            {
                return "unknown-rule-bits";
            }

            if (buffer[offset + 128] != MasterProtocol.GameProtocol)
            {
                return "game-protocol-mismatch";
            }

            for (var i = offset + 129; i < offset + 132; i++)
            {
                if (buffer[i] != 0)
                {
                    return "nonzero-reserved";
                }
            }

            for (var i = offset + 104; i < offset + 116; i++)
            {
                if (buffer[i] != 0)
                {
                    return "nonzero-reserved";
                }
            }

            if (map.StartsWith("MAP", StringComparison.Ordinal))
            {
                int number;
                if (map.Length != 5 || !int.TryParse(map.Substring(3), out number) || number < 1 || number > 32)
                {
                    return "invalid-map-name";
                }
            }
            else
            {
                if (map.Length != 4 || map[0] != 'E' || map[2] != 'M' ||
                    map[1] < '1' || map[1] > '4' || map[3] < '1' || map[3] > '9')
                {
                    return "invalid-map-name";
                }
            }

            return null;
        }
    }

    public sealed class MasterServerEntry
    {
        public byte[] Id = new byte[16];
        public byte[] Address = new byte[4];
        public uint LeaseSecondsLeft;
        public MasterSessionInfo Info;
        public bool RelayAvailable;

        public string AddressText
        {
            get
            {
                return Address[0] + "." + Address[1] + "." + Address[2] + "." + Address[3];
            }
        }
    }
}
