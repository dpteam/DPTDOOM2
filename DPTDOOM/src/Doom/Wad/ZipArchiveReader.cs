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
    /// A minimal reader for ZIP archives, which is what PK3 files are.
    ///
    /// Written by hand for two reasons, and both of them matter here:
    ///
    ///   - <c>System.IO.Compression.ZipArchive</c> arrived in .NET Framework 4.5.
    ///     This project targets 4.0 so that it runs on Windows XP, so the class
    ///     simply does not exist.
    ///
    ///   - Adding a package to get one would break the promise that the shipped
    ///     build has no third-party libraries.
    ///
    /// <c>DeflateStream</c> has been in the framework since 2.0, so the only part
    /// that has to be written is the container format: find the central directory
    /// at the end of the file, walk the entry records, and inflate each one.
    ///
    /// Only what a Doom resource container needs is supported: stored and
    /// deflated entries, no encryption, no ZIP64, no multi-disk. A PK3 that needs
    /// any of those is not a Doom resource container.
    /// </summary>
    public sealed class ZipArchiveReader
    {
        private const uint EndOfCentralDirectory = 0x06054b50;
        private const uint CentralFileHeader = 0x02014b50;
        private const uint LocalFileHeader = 0x04034b50;

        /// <summary>One file in the archive.</summary>
        public sealed class Entry
        {
            public string Name;
            public int Method;
            public int CompressedSize;
            public int UncompressedSize;
            public int LocalHeaderOffset;
        }

        private readonly string path;
        private readonly List<Entry> entries = new List<Entry>();

        public ZipArchiveReader(string path)
        {
            this.path = path;

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                ReadCentralDirectory(stream);
            }
        }

        public IList<Entry> Entries { get { return entries; } }

        /// <summary>Reads and decompresses one entry.</summary>
        public byte[] ReadEntry(Entry entry)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                // The local header repeats the name and carries its own extra
                // field, whose length differs from the central one. The data
                // starts after both, so the offset has to be read from the local
                // header rather than assumed.
                stream.Seek(entry.LocalHeaderOffset, SeekOrigin.Begin);

                var header = new byte[30];

                if (stream.Read(header, 0, header.Length) != header.Length)
                {
                    throw new Exception("Truncated ZIP entry header.");
                }

                if (BitConverter.ToUInt32(header, 0) != LocalFileHeader)
                {
                    throw new Exception("Bad ZIP local header.");
                }

                var nameLength = BitConverter.ToUInt16(header, 26);
                var extraLength = BitConverter.ToUInt16(header, 28);

                stream.Seek(nameLength + extraLength, SeekOrigin.Current);

                var compressed = new byte[entry.CompressedSize];

                if (stream.Read(compressed, 0, compressed.Length) != compressed.Length)
                {
                    throw new Exception("Truncated ZIP entry data.");
                }

                switch (entry.Method)
                {
                    case 0:
                        return compressed;

                    case 8:
                        return Inflate(compressed, entry.UncompressedSize);

                    default:
                        throw new Exception(
                            "Unsupported ZIP compression method " + entry.Method +
                            " in " + entry.Name + ".");
                }
            }
        }

        private static byte[] Inflate(byte[] compressed, int expectedSize)
        {
            using (var input = new MemoryStream(compressed))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream(Math.Max(expectedSize, 1024)))
            {
                var buffer = new byte[8192];
                int read;

                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                }

                return output.ToArray();
            }
        }

        private void ReadCentralDirectory(Stream stream)
        {
            if (stream.Length < 22)
            {
                throw new Exception("The file is too small to be a ZIP archive.");
            }

            // The end-of-central-directory record sits at the end, followed by a
            // comment of up to 64 KiB. Scan backwards for its signature.
            var tailLength = (int)Math.Min(stream.Length, 22 + 0xFFFF);
            var tail = new byte[tailLength];

            stream.Seek(stream.Length - tailLength, SeekOrigin.Begin);
            if (stream.Read(tail, 0, tailLength) != tailLength)
            {
                throw new Exception("Failed to read the ZIP archive.");
            }

            var endOffset = -1;

            for (var i = tailLength - 22; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(tail, i) == EndOfCentralDirectory)
                {
                    endOffset = i;
                    break;
                }
            }

            if (endOffset < 0)
            {
                throw new Exception("The file is not a ZIP archive.");
            }

            var entryCount = BitConverter.ToUInt16(tail, endOffset + 10);
            var directorySize = (int)BitConverter.ToUInt32(tail, endOffset + 12);
            var directoryOffset = (int)BitConverter.ToUInt32(tail, endOffset + 16);

            if (directorySize <= 0 || directoryOffset < 0 ||
                directoryOffset + directorySize > stream.Length)
            {
                throw new Exception("Corrupt ZIP central directory.");
            }

            var directory = new byte[directorySize];
            stream.Seek(directoryOffset, SeekOrigin.Begin);

            if (stream.Read(directory, 0, directorySize) != directorySize)
            {
                throw new Exception("Failed to read the ZIP central directory.");
            }

            var position = 0;

            for (var i = 0; i < entryCount; i++)
            {
                if (position + 46 > directory.Length ||
                    BitConverter.ToUInt32(directory, position) != CentralFileHeader)
                {
                    break;
                }

                var method = BitConverter.ToUInt16(directory, position + 10);
                var compressedSize = (int)BitConverter.ToUInt32(directory, position + 20);
                var uncompressedSize = (int)BitConverter.ToUInt32(directory, position + 24);
                var nameLength = BitConverter.ToUInt16(directory, position + 28);
                var extraLength = BitConverter.ToUInt16(directory, position + 30);
                var commentLength = BitConverter.ToUInt16(directory, position + 32);
                var localOffset = (int)BitConverter.ToUInt32(directory, position + 42);

                if (position + 46 + nameLength > directory.Length)
                {
                    break;
                }

                var name = Encoding.UTF8.GetString(directory, position + 46, nameLength)
                    .Replace('\\', '/');

                // Directories are entries with a trailing slash and no data.
                if (!name.EndsWith("/"))
                {
                    entries.Add(new Entry
                    {
                        Name = name,
                        Method = method,
                        CompressedSize = compressedSize,
                        UncompressedSize = uncompressedSize,
                        LocalHeaderOffset = localOffset
                    });
                }

                position += 46 + nameLength + extraLength + commentLength;
            }
        }
    }
}
