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

namespace DPTDOOM.Video
{
    /// <summary>
    /// Writes a BGRA framebuffer as a 24-bit BMP.
    ///
    /// BMP on purpose: no encoder, no dependency, and every Windows version can
    /// open it. Used by headless frame capture and by the debug menu.
    /// </summary>
    public static class BmpWriter
    {
        public static void Save(string path, byte[] pixels, int width, int height)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException("pixels");
            }

            if (pixels.Length < width * height * 4)
            {
                throw new ArgumentException("The framebuffer is smaller than the given size.", "pixels");
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var rowSize = ((width * 3) + 3) & ~3;
            var imageSize = rowSize * height;
            var fileSize = 14 + 40 + imageSize;

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)'B');
                writer.Write((byte)'M');
                writer.Write(fileSize);
                writer.Write((short)0);
                writer.Write((short)0);
                writer.Write(14 + 40);

                writer.Write(40);
                writer.Write(width);
                writer.Write(height);
                writer.Write((short)1);
                writer.Write((short)24);
                writer.Write(0);
                writer.Write(imageSize);
                writer.Write(2835);
                writer.Write(2835);
                writer.Write(0);
                writer.Write(0);

                var row = new byte[rowSize];
                for (var y = height - 1; y >= 0; y--)
                {
                    var src = y * width * 4;
                    var dst = 0;
                    for (var x = 0; x < width; x++)
                    {
                        row[dst++] = pixels[src + 2];
                        row[dst++] = pixels[src + 1];
                        row[dst++] = pixels[src + 0];
                        src += 4;
                    }

                    writer.Write(row);
                }
            }
        }
    }
}
