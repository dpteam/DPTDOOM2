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
using System.IO;
using System.Text;

namespace DPTDOOM.Net
{
    /// <summary>
    /// DPT.DOOM game protocol v1.
    ///
    /// Deliberately separate from the master directory protocol: the master
    /// only helps players find each other, the game session speaks this
    /// protocol directly between host and guests.
    ///
    /// Every frame is length prefixed:
    ///   magic "DPTN" | version u16 | type u16 | payload length u32 | payload
    /// </summary>
    public static class NetProtocol
    {
        public const int DefaultPort = 5029;

        /// <summary>
        /// Doom's simulation runs at 35 tics per second. Named here because the
        /// network layer has to size its input buffer in tics, and a second copy
        /// of this constant would quietly disagree with the game loop.
        /// </summary>
        public const int TicsPerSecond = 35;

        public const int Version = 1;

        public const int HeaderSize = 12;

        public const int MaxPayload = 4096;

        public const int MaxPlayers = 4;

        public const int MaxNameLength = 31;

        /// <summary>
        /// Bits in the optional tail of a HELLO frame.
        ///
        /// The tail exists so that adding something to the handshake does not break
        /// older builds: a peer that does not know a bit ignores the bytes behind it,
        /// and a peer that sends nothing at all is still readable. Order matters -
        /// the resume block comes first, so a build that predates the password still
        /// finds the token where it expects it.
        /// </summary>
        public const byte HelloResume = 1;
        public const byte HelloPassword = 2;

        public static readonly byte[] Magic = new byte[] { (byte)'D', (byte)'P', (byte)'T', (byte)'N' };

        public const ushort TypeHello = 1;
        public const ushort TypeWelcome = 2;
        public const ushort TypeReject = 3;
        public const ushort TypeTic = 4;
        public const ushort TypeBye = 5;
        public const ushort TypePing = 6;
        public const ushort TypePong = 7;
        public const ushort TypeChat = 8;

        /// <summary>
        /// Host to guest: the input buffer has changed size.
        ///
        /// The buffer only decides how far ahead each side generates its own
        /// commands, so it can change mid-session without touching the simulation.
        /// It still has to change on both sides at once, because the session runs at
        /// the pace of the smallest buffer in it - hence a frame rather than each end
        /// deciding for itself.
        /// </summary>
        public const ushort TypeDelay = 9;

        /// <summary>
        /// Host to guests: a player is gone for good and their slot has been
        /// abandoned.
        ///
        /// The slot is not freed and the session does not end: the host keeps sending
        /// that player's commands as empty ones, so the world stays identical on every
        /// machine and the remaining players continue. Nothing but a notice has to
        /// travel, because the commands that carry the absence travel already.
        /// </summary>
        public const ushort TypeLeave = 10;

        /// <summary>
        /// Host to guests: the session is paused waiting for a peer, and for how long.
        ///
        /// Guests have to know, because on their side nothing is wrong: their own link
        /// is fine and no command is missing that they could ask for. Without this they
        /// simply stop - the tic never completes, no message appears, and the player
        /// sees a frozen game with no explanation.
        /// </summary>
        public const ushort TypeHold = 11;

        public static byte[] BuildFrame(ushort type, byte[] payload)
        {
            var frame = new byte[HeaderSize + payload.Length];

            frame[0] = Magic[0];
            frame[1] = Magic[1];
            frame[2] = Magic[2];
            frame[3] = Magic[3];

            WriteUInt16(frame, 4, Version);
            WriteUInt16(frame, 6, type);
            WriteUInt32(frame, 8, (uint)payload.Length);

            Array.Copy(payload, 0, frame, HeaderSize, payload.Length);

            return frame;
        }

        public static void WriteUInt16(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        public static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        public static int ReadUInt16(byte[] buffer, int offset)
        {
            return buffer[offset] | (buffer[offset + 1] << 8);
        }

        public static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }

        public static void WriteString(BinaryWriter writer, string value)
        {
            WriteString(writer, value, MaxNameLength);
        }

        public static void WriteString(BinaryWriter writer, string value, int max)
        {
            var bytes = Encoding.UTF8.GetBytes(Truncate(value, max));
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }

        public static string ReadString(BinaryReader reader)
        {
            return ReadString(reader, MaxNameLength);
        }

        public static string ReadString(BinaryReader reader, int max)
        {
            var length = reader.ReadByte();
            if (length > max)
            {
                throw new InvalidDataException("String too long.");
            }

            var bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Strings that are not names: the session password.
        ///
        /// Separate from <see cref="WriteString"/> because a name is trimmed and
        /// defaulted to "player", and a password must be neither: trimming would
        /// silently change what the host typed, and defaulting would invent one.
        ///
        /// Clipped at a character boundary rather than by character count: the limit is
        /// in bytes, and cutting a multi-byte character in half would corrupt it.
        /// </summary>
        public static void WritePassword(BinaryWriter writer, string value)
        {
            var text = value ?? "";

            var bytes = Encoding.UTF8.GetBytes(text);

            if (bytes.Length > ServerPassword.MaxLength)
            {
                bytes = Encoding.UTF8.GetBytes(ClipToBytes(text, ServerPassword.MaxLength));
            }

            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }

        /// <summary>
        /// The longest prefix of <paramref name="text"/> that fits in this many bytes.
        /// </summary>
        private static string ClipToBytes(string text, int max)
        {
            var used = 0;
            var index = 0;

            while (index < text.Length)
            {
                // A surrogate pair is one character and four bytes; moving one char at a
                // time would split it, so ask the encoder rather than guess.
                var step = char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
                var size = Encoding.UTF8.GetByteCount(text.Substring(index, step));

                if (used + size > max)
                {
                    break;
                }

                used += size;
                index += step;
            }

            return text.Substring(0, index);
        }

        public static string ReadPassword(BinaryReader reader)
        {
            return ReadString(reader, ServerPassword.MaxLength);
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "player";
            }

            value = value.Trim();
            if (value.Length == 0)
            {
                return "player";
            }

            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
