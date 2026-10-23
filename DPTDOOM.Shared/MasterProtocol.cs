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
using System.Text;

namespace DPTDOOM.Shared
{
    /// <summary>
    /// DPT.DOOM master directory protocol v1.
    ///
    /// One bounded request/response per TCP connection. The wire format is
    /// unchanged from the original C implementation so that old clients,
    /// launchers and tests keep working.
    ///
    ///   header (16 bytes)
    ///     "DPTM" | version u16 | operation u16 | body length u32 |
    ///     status u16 | reserved u16
    /// </summary>
    public static class MasterProtocol
    {
        public const int Version = 1;
        public const int GameProtocol = 2;

        public const int HeaderSize = 16;
        public const int InfoSize = 132;
        public const int RowSize = 160;
        public const int LeaseSize = 48;
        public const int AuthSize = 40;

        public const int MaxServers = 128;
        public const int MaxServersPerIp = 4;
        public const int MaxBodySize = 4 + RowSize * MaxServers;

        public const int DefaultPort = 25135;

        public const ushort OpList = 1;
        public const ushort OpRegister = 2;
        public const ushort OpHeartbeat = 3;
        public const ushort OpRemove = 4;
        public const ushort OpRelayHost = 5;
        public const ushort OpRelayJoin = 6;

        public const ushort StatusOk = 0;
        public const ushort StatusBadRequest = 1;
        public const ushort StatusUnauthorized = 2;
        public const ushort StatusConflict = 3;
        public const ushort StatusCapacity = 4;
        public const ushort StatusRedirect = 5;

        public const uint RelayAvailable = 1;

        public static readonly byte[] Magic = new byte[] { (byte)'D', (byte)'P', (byte)'T', (byte)'M' };

        public static int Get16(byte[] buffer, int offset)
        {
            return buffer[offset] | (buffer[offset + 1] << 8);
        }

        public static uint Get32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] |
                          (buffer[offset + 1] << 8) |
                          (buffer[offset + 2] << 16) |
                          (buffer[offset + 3] << 24));
        }

        public static void Put16(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        public static void Put32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        public static byte[] Header(ushort operation, int bodyLength, ushort status)
        {
            var header = new byte[HeaderSize];
            Array.Copy(Magic, header, 4);
            Put16(header, 4, Version);
            Put16(header, 6, operation);
            Put32(header, 8, (uint)bodyLength);
            Put16(header, 12, status);
            Put16(header, 14, 0);
            return header;
        }

        /// <summary>
        /// Returns the expected body length for a request, or -1 when the
        /// header is malformed.
        /// </summary>
        public static int RequestLength(byte[] header)
        {
            var operation = Get16(header, 6);
            var length = (int)Get32(header, 8);

            if (header[0] != Magic[0] || header[1] != Magic[1] ||
                header[2] != Magic[2] || header[3] != Magic[3])
            {
                return -1;
            }

            if (Get16(header, 4) != Version || Get16(header, 12) != 0 || Get16(header, 14) != 0)
            {
                return -1;
            }

            switch (operation)
            {
                case OpList:
                    return length == 0 ? 0 : -1;

                case OpRegister:
                    return length == InfoSize ? InfoSize : -1;

                case OpHeartbeat:
                case OpRemove:
                case OpRelayHost:
                    return length == AuthSize ? AuthSize : -1;

                case OpRelayJoin:
                    return length == 16 ? 16 : -1;
            }

            return -1;
        }

        public static string HeaderError(byte[] header)
        {
            if (header[0] != Magic[0] || header[1] != Magic[1] ||
                header[2] != Magic[2] || header[3] != Magic[3])
            {
                return "magic-not-DPTM";
            }

            if (Get16(header, 4) != Version)
            {
                return "directory-version-mismatch";
            }

            if (Get16(header, 12) != 0 || Get16(header, 14) != 0)
            {
                return "nonzero-request-status";
            }

            var operation = Get16(header, 6);
            if (operation < OpList || operation > OpRelayJoin)
            {
                return "unknown-operation";
            }

            if (RequestLength(header) < 0)
            {
                return "invalid-body-length";
            }

            return null;
        }

        public static bool IsPrintableAscii(string text)
        {
            foreach (var ch in text)
            {
                if (ch < 0x20 || ch > 0x7E)
                {
                    return false;
                }
            }

            return true;
        }

        public static string ReadAscii(byte[] buffer, int offset, int length)
        {
            var end = offset;
            while (end < offset + length && buffer[end] != 0)
            {
                end++;
            }

            return Encoding.ASCII.GetString(buffer, offset, end - offset);
        }

        public static void WriteAscii(byte[] buffer, int offset, int length, string text)
        {
            Array.Clear(buffer, offset, length);

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var count = Math.Min(text.Length, length - 1);
            for (var i = 0; i < count; i++)
            {
                buffer[offset + i] = (byte)text[i];
            }
        }
    }
}
