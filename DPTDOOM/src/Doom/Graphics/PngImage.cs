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

namespace DPTDOOM
{
    /// <summary>
    /// A PNG decoder, written here because the shipped build may not use any
    /// third-party library.
    ///
    /// PNG is what a PK3 uses for flats, textures and sprites, so supporting PK3
    /// properly means decoding it. The format is well specified and the part a
    /// Doom resource needs is small: the IHDR, PLTE, tRNS and IDAT chunks, the
    /// five scanline filters, and the four colour types at eight bits per channel.
    ///
    /// Deliberately **not** supported, each with a clear error rather than a
    /// wrong picture:
    ///
    ///   - Adam7 interlacing. No Doom resource in the wild uses it, and getting
    ///     it subtly wrong is worse than refusing it.
    ///   - Bit depths below eight for non-palette images, and sixteen-bit
    ///     channels. Doom textures are 8-bit by definition.
    ///
    /// The output is always 8-bit RGBA, so the caller never has to think about
    /// the source format.
    /// </summary>
    public sealed class PngImage
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        private PngImage(int width, int height, byte[] rgba)
        {
            Width = width;
            Height = height;
            Rgba = rgba;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>Row-major RGBA, four bytes per pixel, no padding.</summary>
        public byte[] Rgba { get; private set; }

        /// <summary>True when the data starts with the PNG signature.</summary>
        public static bool LooksLikePng(byte[] data)
        {
            if (data == null || data.Length < Signature.Length)
            {
                return false;
            }

            for (var i = 0; i < Signature.Length; i++)
            {
                if (data[i] != Signature[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static PngImage Decode(byte[] data)
        {
            if (!LooksLikePng(data))
            {
                throw new Exception("Not a PNG file.");
            }

            var position = Signature.Length;

            var width = 0;
            var height = 0;
            var bitDepth = 0;
            var colorType = 0;
            var interlace = 0;

            byte[] palette = null;
            byte[] transparency = null;
            var idat = new MemoryStream();

            while (position + 8 <= data.Length)
            {
                var length = ReadInt32BigEndian(data, position);
                var type = ReadChunkType(data, position + 4);

                position += 8;

                if (length < 0 || position + length + 4 > data.Length)
                {
                    throw new Exception("Truncated PNG chunk '" + type + "'.");
                }

                switch (type)
                {
                    case "IHDR":
                        width = ReadInt32BigEndian(data, position);
                        height = ReadInt32BigEndian(data, position + 4);
                        bitDepth = data[position + 8];
                        colorType = data[position + 9];
                        interlace = data[position + 12];
                        break;

                    case "PLTE":
                        palette = new byte[length];
                        Array.Copy(data, position, palette, 0, length);
                        break;

                    case "tRNS":
                        transparency = new byte[length];
                        Array.Copy(data, position, transparency, 0, length);
                        break;

                    case "IDAT":
                        idat.Write(data, position, length);
                        break;
                }

                // The four-byte CRC follows the data and is not verified: a
                // corrupt resource shows up as a wrong picture either way, and
                // computing the CRC costs more than it tells us.
                position += length + 4;

                if (type == "IEND")
                {
                    break;
                }
            }

            if (width <= 0 || height <= 0)
            {
                throw new Exception("PNG has no valid header.");
            }

            if (interlace != 0)
            {
                throw new Exception("Interlaced PNG is not supported.");
            }

            var channels = ChannelsFor(colorType);

            if (bitDepth != 8 && !(colorType == 3 && bitDepth <= 8) && !(colorType == 0 && bitDepth <= 8))
            {
                throw new Exception("Unsupported PNG bit depth " + bitDepth + ".");
            }

            if (colorType == 3 && palette == null)
            {
                throw new Exception("Palette PNG without a PLTE chunk.");
            }

            var raw = Inflate(idat.ToArray());

            var bitsPerPixel = channels * bitDepth;
            var stride = (width * bitsPerPixel + 7) / 8;
            var expected = height * (stride + 1);

            if (raw.Length < expected)
            {
                throw new Exception(
                    "PNG image data is short: " + raw.Length + " bytes, expected " + expected + ".");
            }

            var pixels = Unfilter(raw, width, height, stride, channels, bitDepth);

            var rgba = ToRgba(pixels, width, height, colorType, bitDepth, palette, transparency);

            return new PngImage(width, height, rgba);
        }

        private static int ChannelsFor(int colorType)
        {
            switch (colorType)
            {
                case 0: return 1; // grayscale
                case 2: return 3; // RGB
                case 3: return 1; // palette index
                case 4: return 2; // grayscale + alpha
                case 6: return 4; // RGBA
            }

            throw new Exception("Unsupported PNG colour type " + colorType + ".");
        }

        /// <summary>
        /// Inflates the concatenated IDAT data.
        ///
        /// PNG wraps the deflate stream in zlib: a two-byte header and a trailing
        /// Adler-32. <c>ZLibStream</c> arrived in .NET 6 and this build targets
        /// 4.0, so the wrapper is skipped by hand — the first two bytes are
        /// dropped, and the checksum at the end is simply left unread because
        /// DeflateStream stops at the end of the deflate data.
        /// </summary>
        private static byte[] Inflate(byte[] compressed)
        {
            if (compressed.Length < 2)
            {
                throw new Exception("PNG image data is empty.");
            }

            // The zlib header is two bytes: the first holds the compression
            // method in its low nibble and the window size in its high nibble,
            // the second holds flags. Reading the method from the 16-bit value
            // instead of the byte is an easy mistake — 0x78 0x01 would then look
            // like method 1 rather than 8.
            var compression = compressed[0] & 0x0F;

            if (compression != 8)
            {
                throw new Exception("PNG uses unsupported compression method " + compression + ".");
            }

            using (var input = new MemoryStream(compressed, 2, compressed.Length - 2))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[16384];
                int read;

                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                }

                return output.ToArray();
            }
        }

        /// <summary>
        /// Undoes the per-scanline filters. Each row is prefixed with a filter
        /// byte; the five filters are all defined in terms of the pixel to the
        /// left, the pixel above and the one above-left, which is why the rows
        /// have to be processed in order.
        /// </summary>
        private static byte[] Unfilter(
            byte[] raw, int width, int height, int stride, int channels, int bitDepth)
        {
            var bytesPerPixel = Math.Max(1, (channels * bitDepth + 7) / 8);

            var previous = new byte[stride];
            var current = new byte[stride];
            var result = new byte[stride * height];

            var source = 0;

            for (var y = 0; y < height; y++)
            {
                var filter = raw[source++];
                Array.Copy(raw, source, current, 0, stride);
                source += stride;

                switch (filter)
                {
                    case 0:
                        break;

                    case 1:
                        for (var i = bytesPerPixel; i < stride; i++)
                        {
                            current[i] = (byte)(current[i] + current[i - bytesPerPixel]);
                        }
                        break;

                    case 2:
                        for (var i = 0; i < stride; i++)
                        {
                            current[i] = (byte)(current[i] + previous[i]);
                        }
                        break;

                    case 3:
                        for (var i = 0; i < stride; i++)
                        {
                            var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                            current[i] = (byte)(current[i] + ((left + previous[i]) >> 1));
                        }
                        break;

                    case 4:
                        for (var i = 0; i < stride; i++)
                        {
                            var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                            var upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;

                            current[i] = (byte)(current[i] + Paeth(left, previous[i], upLeft));
                        }
                        break;

                    default:
                        throw new Exception("Unknown PNG filter " + filter + ".");
                }

                Array.Copy(current, 0, result, y * stride, stride);
                Array.Copy(current, previous, stride);
            }

            return result;
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);

            if (pa <= pb && pa <= pc)
            {
                return a;
            }

            return pb <= pc ? b : c;
        }

        private static byte[] ToRgba(
            byte[] pixels, int width, int height,
            int colorType, int bitDepth, byte[] palette, byte[] transparency)
        {
            var rgba = new byte[width * height * 4];

            var bitsPerPixel = ChannelsFor(colorType) * bitDepth;
            var stride = (width * bitsPerPixel + 7) / 8;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var target = (y * width + x) * 4;

                    switch (colorType)
                    {
                        case 0:
                        {
                            var value = ReadSample(pixels, y * stride, x, bitDepth);
                            var level = (byte)(bitDepth == 8 ? value : value * 255 / ((1 << bitDepth) - 1));

                            rgba[target] = level;
                            rgba[target + 1] = level;
                            rgba[target + 2] = level;
                            rgba[target + 3] = 255;
                            break;
                        }

                        case 2:
                        {
                            var offset = y * stride + x * 3;

                            rgba[target] = pixels[offset];
                            rgba[target + 1] = pixels[offset + 1];
                            rgba[target + 2] = pixels[offset + 2];
                            rgba[target + 3] = 255;
                            break;
                        }

                        case 3:
                        {
                            var index = ReadSample(pixels, y * stride, x, bitDepth);
                            var entry = index * 3;

                            if (entry + 2 < palette.Length)
                            {
                                rgba[target] = palette[entry];
                                rgba[target + 1] = palette[entry + 1];
                                rgba[target + 2] = palette[entry + 2];
                            }

                            rgba[target + 3] = 255;

                            if (transparency != null && index < transparency.Length)
                            {
                                rgba[target + 3] = transparency[index];
                            }

                            break;
                        }

                        case 4:
                        {
                            var offset = y * stride + x * 2;
                            var level = pixels[offset];

                            rgba[target] = level;
                            rgba[target + 1] = level;
                            rgba[target + 2] = level;
                            rgba[target + 3] = pixels[offset + 1];
                            break;
                        }

                        case 6:
                        {
                            var offset = y * stride + x * 4;

                            rgba[target] = pixels[offset];
                            rgba[target + 1] = pixels[offset + 1];
                            rgba[target + 2] = pixels[offset + 2];
                            rgba[target + 3] = pixels[offset + 3];
                            break;
                        }
                    }
                }
            }

            return rgba;
        }

        /// <summary>Reads one sample of the given bit depth at pixel x.</summary>
        private static int ReadSample(byte[] pixels, int rowStart, int x, int bitDepth)
        {
            switch (bitDepth)
            {
                case 8:
                    return pixels[rowStart + x];

                case 4:
                {
                    var b = pixels[rowStart + x / 2];
                    return (x % 2 == 0) ? (b >> 4) : (b & 0x0F);
                }

                case 2:
                {
                    var b = pixels[rowStart + x / 4];
                    var shift = 6 - 2 * (x % 4);
                    return (b >> shift) & 0x03;
                }

                case 1:
                {
                    var b = pixels[rowStart + x / 8];
                    var shift = 7 - (x % 8);
                    return (b >> shift) & 0x01;
                }
            }

            throw new Exception("Unsupported PNG bit depth " + bitDepth + ".");
        }

        private static int ReadInt32BigEndian(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) |
                   (data[offset + 2] << 8) | data[offset + 3];
        }

        private static string ReadChunkType(byte[] data, int offset)
        {
            return new string(new[]
            {
                (char)data[offset], (char)data[offset + 1],
                (char)data[offset + 2], (char)data[offset + 3]
            });
        }
    }
}
