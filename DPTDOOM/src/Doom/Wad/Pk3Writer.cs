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
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DPTDOOM
{
    /// <summary>
    /// Writes a PK3 - which is a ZIP - without a third-party library.
    ///
    /// The reader already exists and has its own tests (<see cref="ZipArchiveReader"/>); this is its
    /// other half, so the engine can produce the archive it also consumes. Both are hand-written for
    /// the same reason: <c>ZipArchive</c> arrived in .NET 4.5 and the shipped build targets 4.0 for
    /// Windows XP. <c>DeflateStream</c> has been there since 2.0, so the compression itself is the
    /// framework's - only the container is ours.
    ///
    /// Deliberately minimal. No ZIP64, no encryption, no directories as entries, no comment, and
    /// **stored** rather than deflated by default: the files this writes are a few kilobytes of text
    /// that get read once at startup, and a stored entry cannot be corrupted by a compressor whose
    /// output someone has to trust. <see cref="Deflate"/> is there for when that stops being true.
    ///
    /// What is written is checked by reading it back with the engine's own reader, not by inspecting
    /// bytes: the only claim that matters is that the two halves agree.
    /// </summary>
    public static class Pk3Writer
    {
        private const uint LocalFileHeader = 0x04034b50;
        private const uint CentralFileHeader = 0x02014b50;
        private const uint EndOfCentralDirectory = 0x06054b50;

        private const ushort StoreMethod = 0;
        private const ushort DeflateMethod = 8;

        private const ushort Version = 20;

        private sealed class Entry
        {
            public string Name;
            public byte[] Data;
            public uint Crc;
            public uint Offset;
            public ushort Method;
            public int CompressedSize;
        }

        /// <summary>
        /// Writes one entry per file, uncompressed.
        ///
        /// <paramref name="files"/> is name to contents; the name is a path inside the archive, with
        /// forward slashes, as a ZIP requires.
        /// </summary>
        public static void Write(string path, IEnumerable<KeyValuePair<string, byte[]>> files,
                                 bool compress)
        {
            var entries = new List<Entry>();

            // Written beside the destination and moved into place at the end: an interrupted write
            // would otherwise leave half an archive where a working one used to be, and the next run
            // would read it as corrupt content instead of as a version to regenerate.
            var temp = path + ".tmp";

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                foreach (var file in files)
                {
                    var entry = new Entry();
                    entry.Name = file.Key.Replace('\\', '/');
                    entry.Data = file.Value ?? new byte[0];
                    entry.Crc = Crc32(entry.Data);
                    entry.Offset = (uint)stream.Position;
                    entry.Method = compress ? DeflateMethod : StoreMethod;

                    var payload = compress ? Deflate(entry.Data) : entry.Data;

                    entry.CompressedSize = payload.Length;

                    WriteLocalHeader(writer, entry, payload.Length);

                    writer.Write(payload);

                    entries.Add(entry);
                }

                var directoryOffset = (uint)stream.Position;

                foreach (var entry in entries)
                {
                    WriteCentralHeader(writer, entry);
                }

                var directorySize = (uint)stream.Position - directoryOffset;

                WriteEndOfDirectory(writer, entries.Count, directorySize, directoryOffset);
            }

            // The archive is dropped into place rather than written in place: an interrupted write
            // would otherwise leave a half file where a working one used to be.
            ReplaceAtomically(path);
        }

        private static void WriteLocalHeader(BinaryWriter writer, Entry entry, int compressedSize)
        {
            writer.Write(LocalFileHeader);
            writer.Write(Version);
            writer.Write((ushort)0);          // no flags
            writer.Write(entry.Method);
            writer.Write((ushort)0);          // time
            writer.Write((ushort)0);          // date
            writer.Write(entry.Crc);
            writer.Write((uint)compressedSize);
            writer.Write((uint)entry.Data.Length);
            writer.Write((ushort)Encoding.ASCII.GetByteCount(entry.Name));
            writer.Write((ushort)0);          // no extra field
            writer.Write(Encoding.ASCII.GetBytes(entry.Name));
        }

        private static void WriteCentralHeader(BinaryWriter writer, Entry entry)
        {
            writer.Write(CentralFileHeader);
            writer.Write(Version);            // version made by
            writer.Write(Version);            // version needed
            writer.Write((ushort)0);          // no flags
            writer.Write(entry.Method);
            writer.Write((ushort)0);          // time
            writer.Write((ushort)0);          // date
            writer.Write(entry.Crc);
            writer.Write((uint)entry.CompressedSize);
            writer.Write((uint)entry.Data.Length);
            writer.Write((ushort)Encoding.ASCII.GetByteCount(entry.Name));
            writer.Write((ushort)0);          // extra
            writer.Write((ushort)0);          // comment
            writer.Write((ushort)0);          // disk number
            writer.Write((ushort)0);          // internal attributes
            writer.Write(0u);                 // external attributes
            writer.Write(entry.Offset);
            writer.Write(Encoding.ASCII.GetBytes(entry.Name));
        }

        private static void WriteEndOfDirectory(BinaryWriter writer, int count, uint size,
                                                uint offset)
        {
            writer.Write(EndOfCentralDirectory);
            writer.Write((ushort)0);          // this disk
            writer.Write((ushort)0);          // disk with the directory
            writer.Write((ushort)count);
            writer.Write((ushort)count);
            writer.Write(size);
            writer.Write(offset);
            writer.Write((ushort)0);          // no comment
        }

        private static byte[] Deflate(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
                {
                    deflate.Write(data, 0, data.Length);
                }

                return output.ToArray();
            }
        }

        private static void ReplaceAtomically(string path)
        {
            // File.Replace is not available on every filesystem this runs on, and File.Move fails
            // when the target exists. Delete-then-move is the portable pair, and the gap is one
            // instruction wide: a crash exactly there leaves no archive at all, which the loader
            // treats as "not written yet" and regenerates.
            var temp = path + ".tmp";

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
            }
            finally
            {
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch (IOException)
                {
                    // Nothing to be done, and nothing worth failing the load over.
                }
            }
        }

        // ------------------------------------------------------------ CRC-32

        private static uint[] crcTable;

        /// <summary>
        /// The CRC-32 a ZIP requires. The polynomial is the one every ZIP uses, byte-reflected.
        /// </summary>
        private static uint Crc32(byte[] data)
        {
            if (crcTable == null)
            {
                var table = new uint[256];

                for (uint i = 0; i < 256; i++)
                {
                    var value = i;

                    for (var bit = 0; bit < 8; bit++)
                    {
                        value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                    }

                    table[i] = value;
                }

                crcTable = table;
            }

            var crc = 0xFFFFFFFFu;

            foreach (var b in data)
            {
                crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
