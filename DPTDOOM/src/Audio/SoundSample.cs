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

namespace DPTDOOM.Audio
{
    /// <summary>
    /// A Doom sound effect: 8-bit unsigned mono PCM, exactly as stored in a
    /// DMX lump ("DSxxx").
    ///
    /// DMX header layout:
    ///   0..1   format (3)
    ///   2..3   sample rate, little endian
    ///   4..7   sample count, little endian
    ///   8..15  padding
    ///   16..   samples, unsigned 8-bit
    ///
    /// Some WADs (notably those made by DeuTex and a few editors) pad the first
    /// and last 16 samples. Doom itself always plays those pad bytes, so they
    /// are kept here; <see cref="HasPadding"/> only records that they exist.
    /// </summary>
    public sealed class SoundSample
    {
        public const int HeaderSize = 8;
        public const int PaddingSize = 16;

        private readonly byte[] data;
        private readonly int offset;
        private readonly int count;
        private readonly int sampleRate;

        private SoundSample(byte[] data, int offset, int count, int sampleRate, bool hasPadding)
        {
            this.data = data;
            this.offset = offset;
            this.count = count;
            this.sampleRate = sampleRate;
            HasPadding = hasPadding;
        }

        /// <summary>
        /// Parses a DMX lump. Returns null when the lump is too short or
        /// malformed, so a broken PWAD cannot crash the mixer.
        /// </summary>
        public static SoundSample Parse(byte[] lump)
        {
            if (lump == null || lump.Length < HeaderSize + 1)
            {
                return null;
            }

            var sampleRate = BitConverter.ToUInt16(lump, 2);
            var sampleCount = BitConverter.ToInt32(lump, 4);

            if (sampleRate < 1000 || sampleRate > 48000)
            {
                return null;
            }

            var offset = HeaderSize;
            var hasPadding = false;

            // The count is authoritative: trust it only if the data is really
            // there, otherwise fall back to whatever the lump holds.
            if (sampleCount <= 0 || offset + sampleCount > lump.Length)
            {
                sampleCount = lump.Length - offset;
                if (sampleCount <= 0)
                {
                    return null;
                }
            }
            else if (sampleCount >= 2 * PaddingSize && LooksPadded(lump, offset, sampleCount))
            {
                hasPadding = true;
            }

            return new SoundSample(lump, offset, sampleCount, sampleRate, hasPadding);
        }

        /// <summary>
        /// True when the first and last 16 samples are constant, which is the
        /// DeuTex padding convention. The samples are still played: skipping
        /// them clips the attack of short sounds, which is audible.
        /// </summary>
        private static bool LooksPadded(byte[] lump, int offset, int count)
        {
            var first = lump[offset];
            for (var i = 1; i < PaddingSize; i++)
            {
                if (lump[offset + i] != first)
                {
                    return false;
                }
            }

            var last = lump[offset + count - 1];
            for (var i = 1; i < PaddingSize; i++)
            {
                if (lump[offset + count - i - 1] != last)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Unsigned 8-bit sample, 0..255.</summary>
        public byte this[int index]
        {
            get { return data[offset + index]; }
        }

        /// <summary>Sample as a float in -1..1, the form the mixer works in.</summary>
        public float GetValue(int index)
        {
            return (data[offset + index] - 128) / 128F;
        }

        /// <summary>
        /// Peak amplitude of the first fifth of a second, which is what Doom
        /// uses to prioritise channels.
        /// </summary>
        public float GetAmplitude()
        {
            var max = 0;
            var length = Math.Min(sampleRate / 5, count);

            for (var i = 0; i < length; i++)
            {
                var value = data[offset + i] - 128;
                if (value < 0)
                {
                    value = -value;
                }

                if (value > max)
                {
                    max = value;
                }
            }

            return max / 128F;
        }

        public int Count { get { return count; } }
        public int SampleRate { get { return sampleRate; } }
        public bool HasPadding { get; private set; }

        /// <summary>Length in seconds.</summary>
        public double Duration { get { return (double)count / sampleRate; } }
    }
}
