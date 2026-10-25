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
using System.Diagnostics;
using System.Threading;
using DPTDOOM.Audio;

namespace DPTDOOM.Win32
{
    /// <summary>
    /// Doom music through the system MIDI synthesizer (winmm.dll). MUS lumps
    /// are converted to timed MIDI messages by <see cref="Mus"/>. On Windows XP
    /// and later this uses the built-in Microsoft GS Wavetable synth, so no
    /// soundfont or third-party library is shipped.
    /// </summary>
    internal sealed class Win32Music : IMusic, IDisposable
    {
        private readonly Config config;
        private readonly Wad wad;

        private IntPtr device;
        private Playback current;
        private bool disposed;

        public Win32Music(Config config, GameContent content)
        {
            this.config = config;
            wad = content.Wad;

            config.audio_musicvolume = MathEx.Clamp(config.audio_musicvolume, 0, MaxVolume);

            var result = NativeMethodsMidi.midiOutOpen(
                out device,
                NativeMethodsMidi.MIDI_MAPPER,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethodsMidi.CALLBACK_NULL);

            if (result != 0)
            {
                ConsoleUi.Warn("music unavailable, midiOutOpen error " + result);
                device = IntPtr.Zero;
            }
        }

        public void StartMusic(Bgm bgm, bool loop)
        {
            if (device == IntPtr.Zero || bgm == Bgm.NONE)
            {
                Stop();
                return;
            }

            var index = (int)bgm;
            if (index < 0 || index >= DoomInfo.BgmNames.Length)
            {
                Stop();
                return;
            }

            PlayLump("D_" + DoomInfo.BgmNames[index].ToString().ToUpper(), loop);
        }

        /// <summary>
        /// Plays a music lump by name, for MAPINFO.
        ///
        /// The "D_" prefix is optional here and added when missing, because both spellings appear
        /// in MAPINFO files: ZDoom accepts "D_E1M2" and a bare "E1M2", and a mod author who wrote
        /// one of them should not have to learn which this port wants.
        /// </summary>
        public void StartMusic(string lumpName, bool loop)
        {
            if (device == IntPtr.Zero || string.IsNullOrEmpty(lumpName))
            {
                Stop();
                return;
            }

            var name = lumpName.ToUpperInvariant();

            if (!name.StartsWith("D_", StringComparison.Ordinal))
            {
                name = "D_" + name;
            }

            PlayLump(name, loop);
        }

        private void PlayLump(string lumpName, bool loop)
        {
            if (wad.GetLumpNumber(lumpName) == -1)
            {
                Stop();
                return;
            }

            var events = Mus.Parse(wad.ReadLump(lumpName));
            if (events.Count == 0)
            {
                Stop();
                return;
            }

            Stop();

            var playback = new Playback();
            playback.Events = events;
            playback.Loop = loop;
            playback.Thread = new Thread(() => Play(playback));
            playback.Thread.IsBackground = true;
            playback.Thread.Name = "DPTDOOM music";
            current = playback;

            SendAllNotesOff();
            SendVolume();
            playback.Thread.Start();
        }

        private void Play(Playback playback)
        {
            NativeMethods.timeBeginPeriod(1);

            try
            {
                var stopwatch = new Stopwatch();

                while (!playback.Stop && !disposed)
                {
                    stopwatch.Restart();

                    foreach (var e in playback.Events)
                    {
                        if (playback.Stop || disposed)
                        {
                            return;
                        }

                        var target = (double)e.Time / Mus.TicksPerSecond;

                        while (!playback.Stop && !disposed)
                        {
                            var wait = target - stopwatch.Elapsed.TotalSeconds;
                            if (wait <= 0)
                            {
                                break;
                            }

                            if (wait > 0.002)
                            {
                                Thread.Sleep((int)(wait * 1000));
                            }
                            else
                            {
                                Thread.SpinWait(50);
                            }
                        }

                        if (playback.Stop || disposed)
                        {
                            return;
                        }

                        Send(e.Status, e.Data1, e.Data2);
                    }

                    if (!playback.Loop)
                    {
                        return;
                    }
                }
            }
            finally
            {
                NativeMethods.timeEndPeriod(1);
            }
        }

        private void Send(byte status, byte data1, byte data2)
        {
            if (device == IntPtr.Zero)
            {
                return;
            }

            // A track's own channel-volume change is scaled by our volume instead of
            // being passed through.
            //
            // This is why the music got louder between maps, and why it sometimes did
            // it in the middle of a song: a MUS file can set controller 7 (channel
            // volume) or 11 (expression) itself, and those messages arrived after
            // SendVolume and overwrote it. The configured volume is the player's
            // choice; a track may ask to be quieter than the maximum, never louder
            // than what the player set.
            if ((status & 0xF0) == 0xB0 && (data1 == 7 || data1 == 11))
            {
                var scale = config.audio_musicvolume * 127 / MaxVolume;
                data2 = (byte)MathEx.Clamp(data2 * scale / 127, 0, 127);
            }

            var message = (uint)(status | (data1 << 8) | (data2 << 16));
            NativeMethodsMidi.midiOutShortMsg(device, message);
        }

        private void SendAllNotesOff()
        {
            if (device == IntPtr.Zero)
            {
                return;
            }

            for (var channel = 0; channel < 16; channel++)
            {
                Send((byte)(0xB0 | channel), 0x78, 0); // all sounds off
                Send((byte)(0xB0 | channel), 0x7B, 0); // all notes off
            }
        }

        private void SendVolume()
        {
            if (device == IntPtr.Zero)
            {
                return;
            }

            var value = (byte)MathEx.Clamp(config.audio_musicvolume * 127 / MaxVolume, 0, 127);
            for (var channel = 0; channel < 16; channel++)
            {
                Send((byte)(0xB0 | channel), 7, value);
            }
        }

        private void Stop()
        {
            var playback = current;
            if (playback == null)
            {
                return;
            }

            current = null;
            playback.Stop = true;

            if (playback.Thread.IsAlive)
            {
                playback.Thread.Join(300);
            }

            SendAllNotesOff();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            Stop();

            if (device != IntPtr.Zero)
            {
                NativeMethodsMidi.midiOutReset(device);
                NativeMethodsMidi.midiOutClose(device);
                device = IntPtr.Zero;
            }
        }

        public int MaxVolume => 15;

        public int Volume
        {
            get => config.audio_musicvolume;
            set
            {
                // Clamped and stored, so a slider at either end cannot write a value
                // the MIDI layer would then have to fix up.
                config.audio_musicvolume = MathEx.Clamp(value, 0, MaxVolume);
                SendVolume();
            }
        }

        private sealed class Playback
        {
            public List<Mus.Event> Events;
            public bool Loop;
            public Thread Thread;
            public volatile bool Stop;
        }
    }
}
