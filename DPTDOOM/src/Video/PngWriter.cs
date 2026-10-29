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
using System.IO.Compression;

namespace DPTDOOM.Video
{
    /// <summary>
    /// Writes a true-colour PNG, for the frame-sequence render mode.
    ///
    /// Why PNG and not the BMP the screenshot key writes: the frame sequence exists to be fed to
    /// ffmpeg, and every video tool takes numbered PNGs. BMP works too, but at 640x400 it is three
    /// times the bytes and it carries no compression, so a ten-second clip is hundreds of megabytes
    /// on disk before ffmpeg ever reads it.
    ///
    /// Written by hand rather than through a library because the project ships as one executable
    /// with no third-party code: the format is a signature, three chunks and a checksum, and
    /// <see cref="DeflateStream"/> in the framework already does the compression. What the framework
    /// does **not** do is the zlib wrapper PNG requires - a two-byte header and an Adler-32 of the
    /// uncompressed data around the deflate stream - so that is done here.
    ///
    /// Colour type 2 (RGB, eight bits per channel), no interlacing, filter 0 on every scanline.
    /// Filtering would shrink the files further, but each filter type is a different predictor that
    /// has to be right for every pixel; the deflate pass already compresses a Doom frame well,
    /// because most of it is flat colour.
    /// </summary>
    public static class PngWriter
    {
        private static readonly byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>
        /// Writes <paramref name="width"/> x <paramref name="height"/> pixels.
        ///
        /// <paramref name="pixels"/> is the renderer's own layout: four bytes per pixel in RGBA
        /// order, rows top to bottom. The alpha channel is dropped, because a Doom frame is opaque
        /// and RGB is a third smaller.
        /// </summary>
        public static void Save(string path, byte[] pixels, int width, int height)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException("pixels");
            }

            if (pixels.Length < width * height * 4)
            {
                throw new ArgumentException("the pixel buffer is smaller than the frame", "pixels");
            }

            // Scanlines with their filter byte: one extra byte per row, and the row is RGB.
            var stride = width * 3 + 1;
            var raw = new byte[stride * height];

            for (var y = 0; y < height; y++)
            {
                var source = y * width * 4;
                var destination = y * stride;

                // Filter 0: none. The value is written, not implied.
                raw[destination] = 0;

                for (var x = 0; x < width; x++)
                {
                    raw[destination + 1 + x * 3] = pixels[source + x * 4];
                    raw[destination + 2 + x * 3] = pixels[source + x * 4 + 1];
                    raw[destination + 3 + x * 3] = pixels[source + x * 4 + 2];
                }
            }

            Write(path, raw, width, height, 2);
        }

        /// <summary>
        /// Writes the frame **with** its alpha channel, for a layer meant to be composited.
        ///
        /// A HUD or a weapon rendered on its own has to arrive as a cut-out, so the editor can put
        /// it over a different background. Colour type 6 keeps the alpha the renderer produced;
        /// flattening it to RGB would give a rectangle of black around the weapon.
        /// </summary>
        public static void SaveRgba(string path, byte[] pixels, int width, int height)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException("pixels");
            }

            if (pixels.Length < width * height * 4)
            {
                throw new ArgumentException("the pixel buffer is smaller than the frame", "pixels");
            }

            var stride = width * 4 + 1;
            var raw = new byte[stride * height];

            for (var y = 0; y < height; y++)
            {
                var source = y * width * 4;
                var destination = y * stride;

                raw[destination] = 0;

                for (var x = 0; x < width * 4; x++)
                {
                    raw[destination + 1 + x] = pixels[source + x];
                }
            }

            Write(path, raw, width, height, 6);
        }

        private static void Write(string path, byte[] raw, int width, int height, int colourType)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                file.Write(signature, 0, signature.Length);

                var header = new byte[13];
                WriteInt32(header, 0, width);
                WriteInt32(header, 4, height);
                header[8] = 8;      // bit depth
                header[9] = (byte)colourType;
                header[10] = 0;     // compression: deflate
                header[11] = 0;     // filter method: adaptive
                header[12] = 0;     // interlace: none

                WriteChunk(file, "IHDR", header, header.Length);
                WriteChunk(file, "IDAT", ZlibCompress(raw), -1);
                WriteChunk(file, "IEND", new byte[0], 0);
            }
        }

        /// <summary>
        /// Deflate, wrapped the way PNG wants it: a zlib header in front and an Adler-32 behind.
        /// </summary>
        private static byte[] ZlibCompress(byte[] data)
        {
            byte[] deflated;

            using (var buffer = new MemoryStream())
            {
                // Level 6 rather than "optimal": a render is written while the game runs, and the
                // difference between 6 and 9 on a Doom frame is a few percent of size for several
                // times the CPU, which would make the renderer wait on the disk instead of the
                // simulation.
                using (var deflate = new DeflateStream(buffer, CompressionMode.Compress, true))
                {
                    deflate.Write(data, 0, data.Length);
                }

                deflated = buffer.ToArray();
            }

            var result = new byte[deflated.Length + 6];

            // 0x78 0x9C: deflate, 32K window, default compression. These two bytes are a zlib
            // header, not part of the deflate stream, and PNG requires them.
            result[0] = 0x78;
            result[1] = 0x9C;

            Array.Copy(deflated, 0, result, 2, deflated.Length);

            var adler = Adler32(data);
            WriteInt32(result, result.Length - 4, adler);

            return result;
        }

        private static int Adler32(byte[] data)
        {
            const uint modulus = 65521;

            uint a = 1;
            uint b = 0;

            for (var i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % modulus;
                b = (b + a) % modulus;
            }

            return (int)((b << 16) | a);
        }

        private static void WriteChunk(Stream stream, string type, byte[] data, int length)
        {
            if (length < 0)
            {
                length = data.Length;
            }

            var header = new byte[4];
            WriteInt32(header, 0, length);
            stream.Write(header, 0, 4);

            var name = new byte[4];

            for (var i = 0; i < 4; i++)
            {
                name[i] = (byte)type[i];
            }

            stream.Write(name, 0, 4);
            stream.Write(data, 0, length);

            // The CRC covers the chunk type and its data, but not the length.
            var crc = Crc32(name, data, length);
            var trailer = new byte[4];
            WriteInt32(trailer, 0, (int)crc);
            stream.Write(trailer, 0, 4);
        }

        private static uint[] crcTable;

        private static uint Crc32(byte[] name, byte[] data, int length)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];

                for (uint i = 0; i < 256; i++)
                {
                    var value = i;

                    for (var bit = 0; bit < 8; bit++)
                    {
                        value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
                    }

                    crcTable[i] = value;
                }
            }

            var crc = 0xFFFFFFFF;

            for (var i = 0; i < 4; i++)
            {
                crc = crcTable[(crc ^ name[i]) & 0xFF] ^ (crc >> 8);
            }

            for (var i = 0; i < length; i++)
            {
                crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFF;
        }

        private static void WriteInt32(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}
