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

namespace DPTDOOM
{
    /// <summary>
    /// Turns a decoded PNG into the palette-indexed format Doom expects.
    ///
    /// Doom stores everything as indices into PLAYPAL: a flat is exactly 4096
    /// bytes, one index per pixel of a 64x64 tile, and the renderer looks up the
    /// colour. A PNG carries RGB, so it has to be mapped back into the palette.
    ///
    /// The mapping is nearest colour in RGB space. That is not perceptually
    /// ideal — a proper quantiser would work in a different colour space and
    /// dither — but it is what every Doom port does for a converted image, it is
    /// predictable, and it is cheap.
    ///
    /// Two things make it fast enough to do at load time:
    ///
    ///   - the palette is only 256 entries, so a nearest search is 256 distance
    ///     computations per distinct colour;
    ///   - results are cached per colour, and a Doom flat has far fewer than
    ///     4096 distinct colours.
    /// </summary>
    public static class Pk3Graphics
    {
        /// <summary>Doom flats are always 64x64.</summary>
        public const int FlatSize = 64;

        /// <summary>
        /// Converts an image to a 64x64 flat.
        ///
        /// A PNG of any size is accepted and scaled with nearest-neighbour
        /// sampling: flats are tiled across a floor, so a smooth resample would
        /// blur the seams that the tile is supposed to have.
        /// </summary>
        public static byte[] ToFlat(PngImage image, byte[] playpal)
        {
            if (image == null)
            {
                throw new ArgumentNullException("image");
            }

            if (playpal == null || playpal.Length < 256 * 3)
            {
                throw new Exception("PLAYPAL is too short to convert an image.");
            }

            var result = new byte[FlatSize * FlatSize];
            var cache = new Dictionary<int, byte>();

            for (var y = 0; y < FlatSize; y++)
            {
                var sourceY = y * image.Height / FlatSize;

                if (sourceY >= image.Height)
                {
                    sourceY = image.Height - 1;
                }

                for (var x = 0; x < FlatSize; x++)
                {
                    var sourceX = x * image.Width / FlatSize;

                    if (sourceX >= image.Width)
                    {
                        sourceX = image.Width - 1;
                    }

                    var offset = (sourceY * image.Width + sourceX) * 4;

                    var r = image.Rgba[offset];
                    var g = image.Rgba[offset + 1];
                    var b = image.Rgba[offset + 2];
                    var a = image.Rgba[offset + 3];

                    // Fully transparent pixels become index 0. Flats are opaque
                    // in Doom, so this only matters for a broken resource, and
                    // picking the first palette entry is the least surprising
                    // answer.
                    if (a == 0)
                    {
                        result[y * FlatSize + x] = 0;
                        continue;
                    }

                    var key = (r << 16) | (g << 8) | b;

                    byte index;

                    if (!cache.TryGetValue(key, out index))
                    {
                        index = NearestIndex(r, g, b, playpal);
                        cache[key] = index;
                    }

                    result[y * FlatSize + x] = index;
                }
            }

            return result;
        }

        /// <summary>
        /// Converts an image to a Doom patch: the column-and-post format the
        /// renderer draws from.
        ///
        /// This is the format sprites and wall textures use. A patch is a set of
        /// columns, each column a list of opaque runs ("posts"); the gaps between
        /// posts are the transparent parts. So transparency is not a colour, it is
        /// an absence — which is why a PNG cannot simply be dropped in as a patch
        /// and why this conversion exists.
        ///
        /// <paramref name="spriteOffsets"/> puts the anchor point in the middle of
        /// the bottom edge, which is how a sprite standing on the floor has to be
        /// positioned. Textures pass false: their anchor is the top-left corner.
        /// </summary>
        public static Patch ToPatch(PngImage image, byte[] playpal, string name, bool spriteOffsets)
        {
            if (image == null)
            {
                throw new ArgumentNullException("image");
            }

            if (playpal == null || playpal.Length < 256 * 3)
            {
                throw new Exception("PLAYPAL is too short to convert an image.");
            }

            var cache = new Dictionary<int, byte>();
            var columns = new Column[image.Width][];

            for (var x = 0; x < image.Width; x++)
            {
                var posts = new List<Column>();
                var y = 0;

                while (y < image.Height)
                {
                    // Skip transparent pixels.
                    while (y < image.Height && image.Rgba[(y * image.Width + x) * 4 + 3] < 128)
                    {
                        y++;
                    }

                    if (y >= image.Height)
                    {
                        break;
                    }

                    var top = y;

                    // Collect one opaque run.
                    while (y < image.Height && image.Rgba[(y * image.Width + x) * 4 + 3] >= 128)
                    {
                        y++;
                    }

                    var length = y - top;
                    var data = new byte[length];

                    for (var i = 0; i < length; i++)
                    {
                        var offset = ((top + i) * image.Width + x) * 4;

                        var r = image.Rgba[offset];
                        var g = image.Rgba[offset + 1];
                        var b = image.Rgba[offset + 2];
                        var key = (r << 16) | (g << 8) | b;

                        byte index;

                        if (!cache.TryGetValue(key, out index))
                        {
                            index = NearestIndex(r, g, b, playpal);
                            cache[key] = index;
                        }

                        data[i] = index;
                    }

                    // A Doom post is limited to 255 rows, and its top delta is a
                    // single byte too, so a run taller than that has to be split.
                    // 200-pixel columns do occur in large sprites.
                    if (length <= 255 && top <= 255)
                    {
                        posts.Add(new Column(top, data, 0, length));
                    }
                    else
                    {
                        var written = 0;

                        while (written < length)
                        {
                            var row = top + written;
                            var chunk = Math.Min(255, length - written);

                            if (row > 255)
                            {
                                // Past 255 the format cannot express the offset.
                                // Give up on the rest of the column rather than
                                // draw it in the wrong place.
                                break;
                            }

                            var piece = new byte[chunk];
                            Array.Copy(data, written, piece, 0, chunk);

                            posts.Add(new Column(row, piece, 0, chunk));
                            written += chunk;
                        }
                    }
                }

                columns[x] = posts.ToArray();
            }

            var leftOffset = spriteOffsets ? image.Width / 2 : 0;
            var topOffset = spriteOffsets ? image.Height : 0;

            return new Patch(name, image.Width, image.Height, leftOffset, topOffset, columns);
        }

        /// <summary>
        /// Index of the palette entry closest to a colour, by squared distance in
        /// RGB. Squared, so no square root is needed to compare.
        /// </summary>
        public static byte NearestIndex(byte r, byte g, byte b, byte[] playpal)
        {
            var best = 0;
            var bestDistance = int.MaxValue;

            for (var i = 0; i < 256; i++)
            {
                var offset = i * 3;

                var dr = playpal[offset] - r;
                var dg = playpal[offset + 1] - g;
                var db = playpal[offset + 2] - b;

                var distance = dr * dr + dg * dg + db * db;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;

                    if (distance == 0)
                    {
                        break;
                    }
                }
            }

            return (byte)best;
        }

        /// <summary>
        /// Whether a PK3 entry path names a flat. PK3 puts flats under
        /// <c>flats/</c>; a few archives also accept them at the root, so both are
        /// recognised.
        /// </summary>
        public static bool IsFlatPath(string path)
        {
            if (path == null)
            {
                return false;
            }

            var lower = path.ToLowerInvariant();

            if (!lower.EndsWith(".png"))
            {
                return false;
            }

            return lower.StartsWith("flats/") || lower.IndexOf('/') < 0;
        }

        /// <summary>Whether a PK3 entry path names a wall texture.</summary>
        public static bool IsTexturePath(string path)
        {
            if (path == null)
            {
                return false;
            }

            var lower = path.ToLowerInvariant();

            return lower.StartsWith("textures/") && lower.EndsWith(".png");
        }

        /// <summary>
        /// Whether a PK3 entry path names a sprite, and which one.
        ///
        /// Two naming conventions are in use and both are accepted:
        ///
        ///   - <c>sprites/POSSA1.png</c> — the lump name as the file name;
        ///   - <c>sprites/POSS A1.png</c> — sprite, space, frame and rotation,
        ///     which is how ZDoom documentation writes it.
        /// </summary>
        public static bool IsSpritePath(string path)
        {
            return ParseSpriteName(path) != null;
        }

        /// <summary>
        /// Splits a PK3 sprite path into its eight-character lump name, or null
        /// when the path is not a sprite or the name is malformed.
        /// </summary>
        public static string ParseSpriteName(string path)
        {
            if (path == null)
            {
                return null;
            }

            var lower = path.ToLowerInvariant();

            if (!lower.StartsWith("sprites/") || !lower.EndsWith(".png"))
            {
                return null;
            }

            var slash = path.LastIndexOf('/');
            var name = path.Substring(slash + 1);

            var dot = name.LastIndexOf('.');
            if (dot >= 0)
            {
                name = name.Substring(0, dot);
            }

            name = name.Replace(" ", "").ToUpperInvariant();

            if (name.Length < 6 || name.Length > 8)
            {
                return null;
            }

            // Four letters of sprite name, a frame letter, a rotation digit.
            for (var i = 0; i < 4; i++)
            {
                if (name[i] < 'A' || name[i] > 'Z')
                {
                    return null;
                }
            }

            if (name[4] < 'A' || name[4] > 'Z')
            {
                return null;
            }

            if (name[5] < '0' || name[5] > '9')
            {
                return null;
            }

            if (name.Length > 6 && (name[6] < 'A' || name[6] > 'Z'))
            {
                return null;
            }

            if (name.Length > 7 && (name[7] < '0' || name[7] > '9'))
            {
                return null;
            }

            return name;
        }

        /// <summary>
        /// The lump name a PK3 graphic provides: the file name without extension,
        /// upper case, at most eight characters. Returns null when the name cannot
        /// be a Doom lump name.
        /// </summary>
        public static string LumpNameOf(string path)
        {
            if (path == null)
            {
                return null;
            }

            var slash = path.LastIndexOf('/');
            var name = slash >= 0 ? path.Substring(slash + 1) : path;

            var dot = name.LastIndexOf('.');
            if (dot >= 0)
            {
                name = name.Substring(0, dot);
            }

            if (name.Length == 0 || name.Length > 8)
            {
                return null;
            }

            return name.ToUpperInvariant();
        }
    }
}
