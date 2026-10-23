using System;
using System.IO;
using System.IO.Compression;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Builds PNG files for the tests.
    ///
    /// The decoder's whole job is to handle the format correctly, so the tests
    /// have to control the format: colour type, bit depth, and especially the
    /// five scanline filters, which is where a decoder usually gets it wrong.
    /// Writing the encoder here also keeps binary fixtures out of the repository.
    /// </summary>
    internal static class PngBuilder
    {
        public static byte[] Build(
            int width, int height, int colorType, int bitDepth,
            byte[] rawRows, int filterPerRow, byte[] palette)
        {
            using (var output = new MemoryStream())
            {
                output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

                var header = new byte[13];
                WriteInt32(header, 0, width);
                WriteInt32(header, 4, height);
                header[8] = (byte)bitDepth;
                header[9] = (byte)colorType;

                WriteChunk(output, "IHDR", header);

                if (palette != null)
                {
                    WriteChunk(output, "PLTE", palette);
                }

                var channels = colorType == 2 ? 3 : (colorType == 6 ? 4 : 1);
                var stride = (width * channels * bitDepth + 7) / 8;
                var bytesPerPixel = Math.Max(1, channels * bitDepth / 8);

                var filtered = new byte[height * (stride + 1)];

                for (var y = 0; y < height; y++)
                {
                    var destination = y * (stride + 1);
                    filtered[destination] = (byte)filterPerRow;

                    for (var i = 0; i < stride; i++)
                    {
                        var value = rawRows[y * stride + i];

                        var left = i >= bytesPerPixel ? rawRows[y * stride + i - bytesPerPixel] : 0;
                        var above = y > 0 ? rawRows[(y - 1) * stride + i] : 0;
                        var aboveLeft = (y > 0 && i >= bytesPerPixel)
                            ? rawRows[(y - 1) * stride + i - bytesPerPixel]
                            : 0;

                        var predictor = 0;

                        switch (filterPerRow)
                        {
                            case 0: predictor = 0; break;
                            case 1: predictor = left; break;
                            case 2: predictor = above; break;
                            case 3: predictor = (left + above) >> 1; break;
                            case 4: predictor = Paeth(left, above, aboveLeft); break;
                        }

                        filtered[destination + 1 + i] = (byte)(value - predictor);
                    }
                }

                WriteChunk(output, "IDAT", ZlibCompress(filtered));
                WriteChunk(output, "IEND", new byte[0]);

                return output.ToArray();
            }
        }

        /// <summary>A solid-colour RGB image, the usual shape for a flat.</summary>
        public static byte[] SolidRgb(int width, int height, byte r, byte g, byte b)
        {
            var raw = new byte[width * height * 3];

            for (var i = 0; i < width * height; i++)
            {
                raw[i * 3] = r;
                raw[i * 3 + 1] = g;
                raw[i * 3 + 2] = b;
            }

            return Build(width, height, 2, 8, raw, 0, null);
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);

            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        private static byte[] ZlibCompress(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x01);

                using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
                {
                    deflate.Write(data, 0, data.Length);
                }

                var adler = Adler32(data);
                output.WriteByte((byte)(adler >> 24));
                output.WriteByte((byte)(adler >> 16));
                output.WriteByte((byte)(adler >> 8));
                output.WriteByte((byte)adler);

                return output.ToArray();
            }
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1;
            uint b = 0;

            foreach (var value in data)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }

            return (b << 16) | a;
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var header = new byte[8];
            WriteInt32(header, 0, data.Length);

            header[4] = (byte)type[0];
            header[5] = (byte)type[1];
            header[6] = (byte)type[2];
            header[7] = (byte)type[3];

            output.Write(header, 0, 8);
            output.Write(data, 0, data.Length);

            // CRC is not verified by the decoder, so zeroes are enough here.
            output.Write(new byte[4], 0, 4);
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
