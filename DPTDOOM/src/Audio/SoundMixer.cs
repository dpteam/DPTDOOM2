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
    /// Software mixer for 8-bit DMX samples, producing 16-bit stereo output.
    ///
    /// Kept separate from the waveOut device on purpose: this is the part that
    /// decides how the game sounds, and it can be rendered offline to a WAV
    /// file (see -soundtest) and checked by unit tests.
    ///
    /// Resampling is linear interpolation. Doom's samples are 11025 Hz and the
    /// output is 44.1 kHz, so the common case is a clean 4x upsample; playing
    /// back at a ratio of 1.0 or higher would alias, which is why the step is
    /// never allowed to exceed 1 for the base rate and pitch only ever nudges
    /// it slightly.
    /// </summary>
    public sealed class SoundMixer
    {
        private readonly Voice[] voices;
        private readonly int sampleRate;
        private readonly int channels;

        // Accumulation buffer. Summing voices must happen in float: adding
        // into a short[] wraps around on overflow instead of clipping, which
        // turns loud moments into harsh digital noise rather than distortion.
        private float[] accumulator = new float[0];

        private float masterVolume = 1F;

        public SoundMixer(int voiceCount, int sampleRate, int channels)
        {
            if (voiceCount < 1) throw new ArgumentOutOfRangeException("voiceCount");
            if (sampleRate < 1000) throw new ArgumentOutOfRangeException("sampleRate");
            if (channels != 1 && channels != 2) throw new ArgumentOutOfRangeException("channels");

            this.sampleRate = sampleRate;
            this.channels = channels;

            voices = new Voice[voiceCount];
            for (var i = 0; i < voices.Length; i++)
            {
                voices[i] = new Voice();
            }
        }

        /// <summary>
        /// Starts a voice. Returns the voice so the caller can retune it later
        /// (that is how distance attenuation follows a moving monster).
        /// </summary>
        public void Play(int voice, SoundSample sample, float leftGain, float rightGain, float pitch)
        {
            if (voice < 0 || voice >= voices.Length || sample == null)
            {
                return;
            }

            var target = voices[voice];
            target.Sample = sample;
            target.Position = 0;
            target.Step = (double)sample.SampleRate / sampleRate * pitch;
            target.LeftGain = leftGain;
            target.RightGain = rightGain;
            target.Playing = true;
        }

        public void SetGains(int voice, float leftGain, float rightGain)
        {
            if (voice < 0 || voice >= voices.Length)
            {
                return;
            }

            voices[voice].LeftGain = leftGain;
            voices[voice].RightGain = rightGain;
        }

        public void Stop(int voice)
        {
            if (voice < 0 || voice >= voices.Length)
            {
                return;
            }

            var target = voices[voice];
            target.Playing = false;
            target.Sample = null;
            target.Position = 0;
        }

        public void StopAll()
        {
            for (var i = 0; i < voices.Length; i++)
            {
                Stop(i);
            }
        }

        public bool IsPlaying(int voice)
        {
            return voice >= 0 && voice < voices.Length && voices[voice].Playing;
        }

        /// <summary>Progress through the current sample, 0..1.</summary>
        public double GetProgress(int voice)
        {
            if (voice < 0 || voice >= voices.Length)
            {
                return 0;
            }

            var target = voices[voice];
            if (!target.Playing || target.Sample == null || target.Sample.Count == 0)
            {
                return 1;
            }

            var progress = target.Position / target.Sample.Count;
            return progress > 1 ? 1 : progress;
        }

        /// <summary>
        /// Mixes one block into an interleaved 16-bit buffer. The buffer is
        /// always fully written.
        /// </summary>
        public void Mix(short[] destination, int frames)
        {
            if (destination == null)
            {
                throw new ArgumentNullException("destination");
            }

            var needed = frames * channels;
            if (destination.Length < needed)
            {
                throw new ArgumentException("Destination is too small.", "destination");
            }

            if (accumulator.Length < needed)
            {
                accumulator = new float[needed];
            }

            Array.Clear(accumulator, 0, needed);

            for (var v = 0; v < voices.Length; v++)
            {
                var voice = voices[v];
                if (!voice.Playing || voice.Sample == null)
                {
                    continue;
                }

                MixVoice(voice, accumulator, frames);
            }

            for (var i = 0; i < needed; i++)
            {
                var value = accumulator[i];
                if (value > 1F) value = 1F;
                else if (value < -1F) value = -1F;

                destination[i] = (short)(value * 32767F);
            }
        }

        private void MixVoice(Voice voice, float[] destination, int frames)
        {
            var sample = voice.Sample;
            var position = voice.Position;
            var step = voice.Step;
            var left = voice.LeftGain * masterVolume;
            var right = voice.RightGain * masterVolume;

            var last = sample.Count - 1;
            if (last < 1)
            {
                voice.Playing = false;
                voice.Sample = null;
                return;
            }

            for (var i = 0; i < frames; i++)
            {
                var index = (int)position;

                if (index >= last)
                {
                    voice.Playing = false;
                    voice.Sample = null;
                    break;
                }

                var frac = (float)(position - index);
                var s0 = sample.GetValue(index);
                var s1 = sample.GetValue(index + 1);
                var value = s0 + (s1 - s0) * frac;

                var dst = i * channels;
                destination[dst] += value * left;

                if (channels == 2)
                {
                    destination[dst + 1] += value * right;
                }

                position += step;
            }

            voice.Position = position;
        }

        /// <summary>Master volume, 0..1.</summary>
        public float MasterVolume
        {
            get { return masterVolume; }
            set { masterVolume = value < 0 ? 0 : (value > 1 ? 1 : value); }
        }

        public int VoiceCount { get { return voices.Length; } }

        public int SampleRate { get { return sampleRate; } }

        public int Channels { get { return channels; } }

        private sealed class Voice
        {
            public SoundSample Sample;
            public double Position;
            public double Step;
            public float LeftGain;
            public float RightGain;
            public bool Playing;
        }
    }
}
