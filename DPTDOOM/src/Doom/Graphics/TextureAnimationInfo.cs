//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
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

namespace DPTDOOM
{
    /// <summary>
    /// One animation cycle: which pictures it shows, in which order, for how long each.
    ///
    /// Two shapes, because the engine's own table and ANIMDEFS describe different things and
    /// pretending otherwise would lose one of them:
    ///
    ///  - **a contiguous range**, which is what vanilla's ANIMATED and SWITCHES lumps mean: pictures
    ///    <c>BasePic</c> to <c>BasePic + NumPics - 1</c>, all with the same speed;
    ///  - **an explicit list**, which is what ANIMDEFS means: any pictures in any order, each with its
    ///    own duration.
    ///
    /// The list form is not a generalisation for its own sake. A mod's animation routinely uses
    /// textures that are not adjacent in the WAD - a lamp that flickers through three frames taken
    /// from elsewhere - and a range cannot express that at all.
    /// </summary>
    public sealed class TextureAnimationInfo
    {
        private readonly bool isTexture;
        private readonly int picNum;
        private readonly int basePic;
        private readonly int numPics;
        private readonly int speed;

        /// <summary>
        /// Explicit pictures, or null for a contiguous range.
        ///
        /// When set, <c>NumPics</c> is the list's length and the range fields are only kept so that
        /// existing code reading them does not have to care which shape it got.
        /// </summary>
        private readonly int[] frames;

        /// <summary>
        /// Tics each frame lasts, or null when every frame uses <see cref="Speed"/>.
        ///
        /// Only the list form carries this, because only ANIMDEFS has a per-frame duration: vanilla's
        /// ANIMATED lump has one number for the whole cycle.
        /// </summary>
        private readonly int[] durations;

        public TextureAnimationInfo(bool isTexture, int picNum, int basePic, int numPics, int speed)
        {
            this.isTexture = isTexture;
            this.picNum = picNum;
            this.basePic = basePic;
            this.numPics = numPics;
            this.speed = speed;
        }

        public TextureAnimationInfo(bool isTexture, int[] frames, int[] durations)
        {
            this.isTexture = isTexture;
            this.frames = frames;
            this.durations = durations;

            numPics = frames.Length;

            // Kept for callers that ask where the cycle lives rather than which pictures it shows.
            // The minimum is the honest answer for a list: it is the lowest picture the animation
            // touches, and the range from there is what the translation table has to cover.
            var lowest = int.MaxValue;

            foreach (var frame in frames)
            {
                if (frame < lowest)
                {
                    lowest = frame;
                }
            }

            basePic = lowest;
            picNum = lowest;

            // A list with no durations is legal through this constructor and means "one speed for the
            // lot". Reading durations[0] without checking was a null dereference that only a test ever
            // hit, because ANIMDEFS always writes them - but a public constructor cannot rely on that.
            speed = durations != null && durations.Length > 0 ? durations[0] : 1;
        }

        public bool IsTexture => isTexture;
        public int PicNum => picNum;
        public int BasePic => basePic;
        public int NumPics => numPics;
        public int Speed => speed;

        /// <summary>The pictures in order, or null for a contiguous range.</summary>
        public int[] Frames => frames;

        /// <summary>
        /// Which of the cycle's pictures is showing at this level time.
        ///
        /// Two paths because the two shapes measure time differently: a range advances every
        /// <c>Speed</c> tics for ever, while a list walks its own durations and repeats when it
        /// reaches the end. The list path is a loop over a handful of frames, which is cheaper than
        /// the division the range path needs.
        /// </summary>
        public int PictureAt(int levelTime)
        {
            if (frames == null)
            {
                return basePic + ((levelTime / speed) % numPics);
            }

            if (durations == null)
            {
                return frames[(levelTime / speed) % numPics];
            }

            var total = 0;

            foreach (var duration in durations)
            {
                total += duration;
            }

            if (total <= 0)
            {
                return frames[0];
            }

            var at = levelTime % total;

            for (var i = 0; i < frames.Length; i++)
            {
                if (at < durations[i])
                {
                    return frames[i];
                }

                at -= durations[i];
            }

            return frames[frames.Length - 1];
        }
    }
}
