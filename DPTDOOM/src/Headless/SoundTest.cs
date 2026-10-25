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
using DPTDOOM.Audio;
using DPTDOOM.Net;

namespace DPTDOOM.Headless
{
    /// <summary>
    /// Renders sound effects offline to a WAV file.
    ///
    /// The mixer is the part of the audio path that decides how the game
    /// sounds, and it is impossible to judge from a screenshot. This tool
    /// plays each effect through exactly the same code the game uses, so the
    /// result can be listened to and measured without starting the game or
    /// owning a sound device.
    /// </summary>
    internal static class SoundTest
    {
        private const int OutputRate = 44100;
        private const int Channels = 2;

        /// <summary>
        /// Effects rendered when the user does not name any. These cover the
        /// categories that sound different: player weapons, a monster attack,
        /// a door, an item pickup and a UI sound.
        /// </summary>
        private static readonly string[] DefaultEffects = new[]
        {
            "PISTOL", "SHOTGN", "SAWIDL", "PLASMA", "PUNCH",
            "CLAW", "DOROPN", "ITEMUP", "POSIT1", "TELEPT"
        };

        public static int Run(Wad wad, string outputPath, string[] requested)
        {
            var names = requested != null && requested.Length > 0 ? requested : DefaultEffects;

            var rendered = new List<short[]>();
            var descriptions = new List<string>();

            var mixer = new SoundMixer(1, OutputRate, Channels);
            var buffer = new short[OutputRate * Channels / 10]; // 100 ms per block

            foreach (var rawName in names)
            {
                var name = NormalizeName(rawName);

                // SNDINFO first, exactly as the game does it: a sound test that ignored the mapping
                // would render a lump the game never plays, and the tool would be lying about the one
                // thing it exists to answer.
                var mapped = SoundInfo.LumpsFor(rawName == null ? "" : rawName.Trim());

                if (mapped != null && mapped.Count > 0)
                {
                    name = mapped[0].ToUpperInvariant();
                }

                var lump = wad.GetLumpNumber(name);

                if (lump == -1)
                {
                    Console.WriteLine("  " + name + ": not in this WAD");
                    continue;
                }

                // Named explicitly, because with SNDINFO in play the name asked for and the lump
                // played are no longer the same word, and a tool that hides the difference hides
                // the answer.
                if (!string.Equals(name, NormalizeName(rawName), StringComparison.Ordinal))
                {
                    Console.WriteLine("  " + NormalizeName(rawName) + " -> " + name);
                }

                var sample = SoundSample.Parse(wad.ReadLump(name));
                if (sample == null)
                {
                    Console.WriteLine("  " + name + ": not a valid DMX lump");
                    continue;
                }

                var frames = (int)(sample.Duration * OutputRate) + OutputRate / 20;
                var output = new short[frames * Channels];

                mixer.Play(0, sample, 1F, 1F, 1F);

                var written = 0;
                while (written < frames)
                {
                    var block = Math.Min(buffer.Length / Channels, frames - written);
                    mixer.Mix(buffer, block);

                    Array.Copy(buffer, 0, output, written * Channels, block * Channels);
                    written += block;
                }

                rendered.Add(output);
                descriptions.Add(
                    name + " (" + sample.SampleRate + " Hz, " + sample.Count + " samples, " +
                    sample.Duration.ToString("0.000") + " s" +
                    (sample.HasPadding ? ", padded" : "") + ")");

                Console.WriteLine("  " + descriptions[descriptions.Count - 1]);
            }

            if (rendered.Count == 0)
            {
                Console.WriteLine("Nothing to render.");
                return 1;
            }

            WriteWav(outputPath, rendered);

            Console.WriteLine("Wrote " + outputPath);

            // The concatenated file above plays effects one at a time, which
            // never stresses the mixer. A second file plays them all at once,
            // which is what happens in a busy fight and what exposes an
            // overflow in the accumulation path.
            var mixed = RenderAllAtOnce(wad, names);
            if (mixed != null)
            {
                var mixedPath = Path.Combine(
                    Path.GetDirectoryName(outputPath) ?? ".",
                    Path.GetFileNameWithoutExtension(outputPath) + "-mixed.wav");

                WriteWav(mixedPath, new List<short[]> { mixed });
                Console.WriteLine("Wrote " + mixedPath + " (all effects at once)");
            }

            return 0;
        }

        /// <summary>
        /// Starts every named effect at the same moment on separate voices,
        /// which is the worst case for the mixer's headroom.
        /// </summary>
        private static short[] RenderAllAtOnce(Wad wad, string[] names)
        {
            var mixer = new SoundMixer(NetProtocol.MaxPlayers * 2, OutputRate, Channels);
            var used = 0;
            var longest = 0.0;

            foreach (var rawName in names)
            {
                var name = NormalizeName(rawName);
                if (wad.GetLumpNumber(name) == -1)
                {
                    continue;
                }

                var sample = SoundSample.Parse(wad.ReadLump(name));
                if (sample == null)
                {
                    continue;
                }

                if (used >= mixer.VoiceCount)
                {
                    break;
                }

                mixer.Play(used, sample, 1F, 1F, 1F);
                used++;

                if (sample.Duration > longest)
                {
                    longest = sample.Duration;
                }
            }

            if (used == 0)
            {
                return null;
            }

            var frames = (int)(longest * OutputRate) + OutputRate / 20;
            var output = new short[frames * Channels];
            var buffer = new short[OutputRate * Channels / 10];

            var written = 0;
            while (written < frames)
            {
                var block = Math.Min(buffer.Length / Channels, frames - written);
                mixer.Mix(buffer, block);

                Array.Copy(buffer, 0, output, written * Channels, block * Channels);
                written += block;
            }

            return output;
        }

        private static string NormalizeName(string name)
        {
            var upper = (name ?? "").Trim().ToUpperInvariant();

            if (upper.Length == 0)
            {
                return "NONE";
            }

            if (upper.StartsWith("DS", StringComparison.Ordinal))
            {
                return upper;
            }

            return "DS" + upper;
        }

        private static void WriteWav(string path, List<short[]> blocks)
        {
            var total = 0;
            foreach (var block in blocks)
            {
                total += block.Length;
            }

            var dataSize = total * 2;

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)'R');
                writer.Write((byte)'I');
                writer.Write((byte)'F');
                writer.Write((byte)'F');
                writer.Write(36 + dataSize);
                writer.Write((byte)'W');
                writer.Write((byte)'A');
                writer.Write((byte)'V');
                writer.Write((byte)'E');

                writer.Write((byte)'f');
                writer.Write((byte)'m');
                writer.Write((byte)'t');
                writer.Write((byte)' ');
                writer.Write(16);
                writer.Write((short)1);              // PCM
                writer.Write((short)Channels);
                writer.Write(OutputRate);
                writer.Write(OutputRate * Channels * 2);
                writer.Write((short)(Channels * 2));
                writer.Write((short)16);

                writer.Write((byte)'d');
                writer.Write((byte)'a');
                writer.Write((byte)'t');
                writer.Write((byte)'a');
                writer.Write(dataSize);

                foreach (var block in blocks)
                {
                    for (var i = 0; i < block.Length; i++)
                    {
                        writer.Write(block[i]);
                    }
                }
            }
        }
    }
}
