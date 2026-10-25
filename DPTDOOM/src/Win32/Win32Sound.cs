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
using System.Runtime.InteropServices;
using System.Threading;
using DPTDOOM.Audio;
using WAVEHDR = DPTDOOM.Win32.NativeMethodsWave.WAVEHDR;
using WAVEFORMATEX = DPTDOOM.Win32.NativeMethodsWave.WAVEFORMATEX;

namespace DPTDOOM.Win32
{
    /// <summary>
    /// Sound effects through winmm.dll. Channel selection, priorities and
    /// attenuation follow the original Doom rules; the actual signal path is
    /// <see cref="SoundMixer"/>, which is also used by the offline -soundtest
    /// renderer so what you hear is what the tests measure.
    /// </summary>
    internal sealed class Win32Sound : ISound, IDisposable
    {
        private const int ChannelCount = 8;
        private const int UiChannel = ChannelCount;

        private const int OutputRate = 44100;
        private const int OutputChannels = 2;

        // 512 frames at 44.1 kHz is ~11.6 ms per buffer. Four of them give a
        // comfortable safety margin without audible latency.
        private const int BufferFrames = 512;
        private const int BufferCount = 6;

        private const float FastDecay = 0.7071F;  // half-life ~5 tics at 35 Hz
        private const float SlowDecay = 0.9805F;  // half-life ~35 tics
        private const float ClipDist = 1200;
        private const float CloseDist = 160;
        private const float Attenuator = ClipDist - CloseDist;

        private readonly Config config;

        // One or more samples per sound: SNDINFO's $random gives a name several lumps, and the whole
        // point of that directive is that the choice varies. A single sample per sound was the shape
        // before SNDINFO existed and cannot express it.
        private readonly SoundSample[][] samples;
        private readonly float[] amplitudes;
        private readonly DoomRandom random;

        /// <summary>
        /// Samples played by sound name rather than by <see cref="Sfx"/>.
        ///
        /// Lazy, because the names are not known until a mod asks for one: TERRAIN names its
        /// splashes in the file, and a WAD may carry hundreds of sound lumps that are never
        /// played. Resolution goes through SNDINFO first, so a name the mod defined there means
        /// what the mod says it means - including its several recordings.
        /// </summary>
        private readonly NamedSounds namedSounds;

        private readonly ChannelInfo[] infos;
        private readonly SoundMixer mixer;
        private readonly object mixerLock = new object();

        private readonly Thread mixerThread;
        private volatile bool running;
        private volatile bool paused;
        private bool mixerStarted;

        private Mobj listener;
        private float masterVolumeDecay;
        private DateTime lastUpdate;

        private IntPtr waveOut;
        private WAVEHDR[] headers;
        private IntPtr[] buffers;
        private short[] mixBuffer;

        private Sfx uiReserved;

        /// <summary>
        /// Picks one of a sound's variants.
        ///
        /// A plain <see cref="Random"/> rather than the engine's <see cref="DoomRandom"/>, and that
        /// is deliberate: which footstep plays is presentation, not simulation. Drawing it from the
        /// game's generator would move the world forward by one number and put a sound choice into
        /// the world hash - the exact mistake the chat and the view tilt are documented against.
        ///
        /// One variant is the common case and skips the generator entirely, so a mod that does not
        /// use $random pays nothing.
        /// </summary>
        private readonly Random variation = new Random();

        private SoundSample Pick(SoundSample[] variants)
        {
            if (variants == null || variants.Length == 0)
            {
                return null;
            }

            if (variants.Length == 1)
            {
                return variants[0];
            }

            return variants[variation.Next(variants.Length)];
        }

        public Win32Sound(Config config, GameContent content)
        {
            this.config = config;
            namedSounds = new NamedSounds(content.Wad);

            config.audio_soundvolume = MathEx.Clamp(config.audio_soundvolume, 0, MaxVolume);

            samples = new SoundSample[DoomInfo.SfxNames.Length][];
            amplitudes = new float[DoomInfo.SfxNames.Length];

            if (config.audio_randompitch)
            {
                random = new DoomRandom();
            }

            var loaded = 0;

            for (var i = 0; i < DoomInfo.SfxNames.Length; i++)
            {
                var name = DoomInfo.SfxNames[i].ToString().ToUpper();

                // SNDINFO first: a mod that gave this sound its own lump means it, and the
                // DS-prefix convention is only what happens when nobody said otherwise.
                var lumps = SoundInfo.LumpsFor(name);

                if (lumps == null)
                {
                    var conventional = new List<string>();
                    conventional.Add("DS" + name);
                    lumps = conventional;
                }

                var variants = new List<SoundSample>();
                var loudest = 0F;

                foreach (var lumpName in lumps)
                {
                    if (content.Wad.GetLumpNumber(lumpName) == -1)
                    {
                        // A named lump that is not in the WAD is worth saying once: it is the
                        // difference between "the modder forgot to include the file" and "the sound
                        // system is broken", and only one of those is fixable by the player.
                        if (SoundInfo.LumpsFor(name) != null)
                        {
                            LoadReport.Add("missing sound",
                                lumpName + " for " + DoomInfo.SfxNames[i],
                                "that sound plays nothing");
                        }

                        continue;
                    }

                    var parsed = SoundSample.Parse(content.Wad.ReadLump(lumpName));

                    if (parsed == null)
                    {
                        continue;
                    }

                    variants.Add(parsed);

                    if (parsed.GetAmplitude() > loudest)
                    {
                        loudest = parsed.GetAmplitude();
                    }
                }

                if (variants.Count == 0)
                {
                    continue;
                }

                samples[i] = variants.ToArray();

                // The loudest of the variants, not the first: priority is compared against other
                // sounds competing for a channel, and a quiet variant of a loud sound must not make
                // the whole sound lose those comparisons.
                amplitudes[i] = loudest;
                loaded++;
            }

            ConsoleUi.Line("sound", loaded + " effects loaded");

            infos = new ChannelInfo[ChannelCount];
            for (var i = 0; i < ChannelCount; i++)
            {
                infos[i] = new ChannelInfo();
            }

            mixer = new SoundMixer(ChannelCount + 1, OutputRate, OutputChannels);

            uiReserved = Sfx.NONE;
            masterVolumeDecay = (float)config.audio_soundvolume / MaxVolume;
            mixer.MasterVolume = masterVolumeDecay;

            lastUpdate = DateTime.MinValue;

            running = true;
            mixerThread = new Thread(MixerLoop);
            mixerThread.IsBackground = true;
            mixerThread.Name = "DPTDOOM sound mixer";
        }

        ////////////////////////////////////////////////////////////
        // Game-facing API
        ////////////////////////////////////////////////////////////

        public void SetListener(Mobj listener)
        {
            this.listener = listener;
        }

        public void Update()
        {
            var now = DateTime.Now;
            if ((now - lastUpdate).TotalSeconds < 0.01)
            {
                return;
            }

            lastUpdate = now;

            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];

                if (info.Playing != Sfx.NONE || info.PlayingName != null)
                {
                    if (mixer.IsPlaying(i))
                    {
                        if (info.Type == SfxType.Diffuse)
                        {
                            info.Priority *= SlowDecay;
                        }
                        else
                        {
                            info.Priority *= FastDecay;
                        }

                        ApplyPan(i, info);
                    }
                    else
                    {
                        info.Playing = Sfx.NONE;
                        info.PlayingName = null;
                        if (info.Reserved == Sfx.NONE && info.ReservedName == null)
                        {
                            info.Source = null;
                        }
                    }
                }

                if (info.Reserved != Sfx.NONE)
                {
                    var sample = Pick(samples[(int)info.Reserved]);
                    if (sample != null)
                    {
                        StartVoice(i, info, sample);
                        info.Playing = info.Reserved;
                    }

                    info.Reserved = Sfx.NONE;
                }
                else if (info.ReservedName != null)
                {
                    var sample = Pick(namedSounds.Get(info.ReservedName));

                    if (sample != null)
                    {
                        StartVoice(i, info, sample);
                        info.PlayingName = info.ReservedName;
                    }

                    info.ReservedName = null;
                }
            }

            if (uiReserved != Sfx.NONE)
            {
                var sample = Pick(samples[(int)uiReserved]);
                if (sample != null)
                {
                    StartUiVoice(sample);
                }

                uiReserved = Sfx.NONE;
            }
        }

        private void StartVoice(int channel, ChannelInfo info, SoundSample sample)
        {
            lock (mixerLock)
            {
                mixer.Play(
                    channel,
                    sample,
                    0F,
                    0F,
                    GetPitch(info.Type, info.Playing != Sfx.NONE ? info.Playing : info.Reserved));

                ApplyPan(channel, info);
            }
        }

        private void StartUiVoice(SoundSample sample)
        {
            lock (mixerLock)
            {
                // UI sounds are centred and are not attenuated: they are the
                // player's own weapon, menu clicks and pickups.
                var gain = 0.7071F;
                mixer.Play(UiChannel, sample, gain, gain, 1F);
            }
        }

        public void StartSound(Sfx sfx)
        {
            if (samples[(int)sfx] == null)
            {
                return;
            }

            uiReserved = sfx;
        }

        public void StartSound(Mobj mobj, Sfx sfx, SfxType type)
        {
            StartSound(mobj, sfx, type, 100);
        }

        public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume)
        {
            if (samples[(int)sfx] == null)
            {
                return;
            }

            Queue(mobj, sfx, null, amplitudes[(int)sfx], type, volume);
        }

        public void StartSound(Mobj mobj, string soundName, SfxType type, int volume)
        {
            var variants = namedSounds.Get(soundName);

            if (variants.Length == 0)
            {
                return;
            }

            // How loud it is has to be measured: this name was never in the engine's table, so
            // there is no tabulated amplitude. The loudest variant, for the same reason the
            // engine's own sounds use the loudest: priority is compared against other sounds
            // competing for a channel, and a quiet variant must not make the whole sound lose.
            var amplitude = 0F;

            foreach (var variant in variants)
            {
                if (variant.GetAmplitude() > amplitude)
                {
                    amplitude = variant.GetAmplitude();
                }
            }

            Queue(mobj, Sfx.NONE, soundName, amplitude, type, volume);
        }

        /// <summary>
        /// Puts a sound on a channel: replaces the same object's sound of the same kind, else takes
        /// a free channel, else steals the least important one if this is at least as important.
        ///
        /// One path for both kinds of sound, because the choice of channel is about priority and
        /// distance and has nothing to do with where the sample came from. Splitting it would mean
        /// two copies of these rules, and the copies would drift.
        /// </summary>
        private void Queue(Mobj mobj, Sfx sfx, string lump, float amplitude, SfxType type, int volume)
        {
            var x = (mobj.X - listener.X).ToFloat();
            var y = (mobj.Y - listener.Y).ToFloat();
            var dist = MathF.Sqrt(x * x + y * y);

            float priority;
            if (type == SfxType.Diffuse)
            {
                priority = volume;
            }
            else
            {
                priority = amplitude * GetDistanceDecay(dist) * volume;
            }

            // A sound already playing from this object replaces itself.
            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                if (info.Source == mobj && info.Type == type)
                {
                    info.Reserved = sfx;
                    info.ReservedName = lump;
                    info.Priority = priority;
                    info.Volume = volume;
                    return;
                }
            }

            // Then a free channel.
            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                if (!info.Busy)
                {
                    Assign(info, mobj, sfx, lump, priority, type, volume);
                    return;
                }
            }

            // Otherwise steal the least important one, but only if this sound
            // is at least as important.
            var minPriority = float.MaxValue;
            var minChannel = -1;

            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                if (info.Priority < minPriority)
                {
                    minPriority = info.Priority;
                    minChannel = i;
                }
            }

            if (priority >= minPriority && minChannel >= 0)
            {
                Assign(infos[minChannel], mobj, sfx, lump, priority, type, volume);
            }
        }

        private static void Assign(ChannelInfo info, Mobj mobj, Sfx sfx, string lump,
                                   float priority, SfxType type, int volume)
        {
            info.Reserved = sfx;
            info.ReservedName = lump;
            info.Priority = priority;
            info.Source = mobj;
            info.Type = type;
            info.Volume = volume;
        }

        public void StopSound(Mobj mobj)
        {
            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                if (info.Source == mobj)
                {
                    info.LastX = info.Source.X;
                    info.LastY = info.Source.Y;
                    info.Source = null;
                    info.Volume /= 5;
                }
            }
        }

        public void Reset()
        {
            if (random != null)
            {
                random.Clear();
            }

            lock (mixerLock)
            {
                mixer.StopAll();

                // Re-assert the configured volume. The mixer is not recreated on a
                // level change, so this is belt and braces - but it makes the
                // invariant explicit: the player's volume is configuration, and
                // nothing that happens at level start is allowed to change it.
                masterVolumeDecay = (float)config.audio_soundvolume / MaxVolume;
                mixer.MasterVolume = masterVolumeDecay;

                for (var i = 0; i < infos.Length; i++)
                {
                    infos[i].Clear();
                }
            }

            listener = null;
        }

        public void Pause()
        {
            paused = true;
        }

        public void Resume()
        {
            paused = false;
        }

        ////////////////////////////////////////////////////////////
        // Panning and attenuation
        ////////////////////////////////////////////////////////////

        private void ApplyPan(int channel, ChannelInfo info)
        {
            if (info.Type == SfxType.Diffuse || listener == null)
            {
                var gain = 0.7071F * 0.01F * info.Volume;
                mixer.SetGains(channel, gain, gain);
                return;
            }

            Fixed sourceX;
            Fixed sourceY;

            if (info.Source == null)
            {
                sourceX = info.LastX;
                sourceY = info.LastY;
            }
            else
            {
                sourceX = info.Source.X;
                sourceY = info.Source.Y;
            }

            var x = (sourceX - listener.X).ToFloat();
            var y = (sourceY - listener.Y).ToFloat();

            if (Math.Abs(x) < 16 && Math.Abs(y) < 16)
            {
                var gain = 0.7071F * 0.01F * info.Volume;
                mixer.SetGains(channel, gain, gain);
                return;
            }

            var dist = MathF.Sqrt(x * x + y * y);
            var angle = MathF.Atan2(y, x) - (float)listener.Angle.ToRadian();

            float left;
            float right;
            SoundPan.FromRelativeAngle(angle, out left, out right);

            var attenuation = 0.01F * SoundPan.DistanceDecay(dist, CloseDist, ClipDist) * info.Volume;
            mixer.SetGains(channel, left * attenuation, right * attenuation);
        }

        private static float GetDistanceDecay(float dist)
        {
            return SoundPan.DistanceDecay(dist, CloseDist, ClipDist);
        }

        private float GetPitch(SfxType type, Sfx sfx)
        {
            if (random == null)
            {
                return 1F;
            }

            if (sfx == Sfx.ITEMUP || sfx == Sfx.TINK || sfx == Sfx.RADIO)
            {
                return 1F;
            }

            if (type == SfxType.Voice)
            {
                return 1F + 0.075F * (random.Next() - 128) / 128;
            }

            return 1F + 0.025F * (random.Next() - 128) / 128;
        }

        ////////////////////////////////////////////////////////////
        // waveOut
        ////////////////////////////////////////////////////////////

        private void MixerLoop()
        {
            if (!OpenWaveOut())
            {
                ConsoleUi.Warn("sound unavailable, continuing without audio");
                return;
            }

            mixBuffer = new short[BufferFrames * OutputChannels];

            // Fill every buffer before the device starts asking for more, so
            // playback begins with real audio instead of an underrun.
            for (var i = 0; i < BufferCount; i++)
            {
                if (!Submit(i))
                {
                    break;
                }
            }

            while (running)
            {
                var wrote = false;

                for (var i = 0; i < BufferCount && running; i++)
                {
                    if ((headers[i].dwFlags & NativeMethodsWave.WHDR_DONE) == 0)
                    {
                        continue;
                    }

                    if (!Submit(i))
                    {
                        continue;
                    }

                    wrote = true;
                }

                if (!wrote)
                {
                    // Nothing is free yet; 2 ms keeps the latency low without
                    // spinning a core.
                    Thread.Sleep(2);
                }
            }

            CloseWaveOut();
        }

        private bool Submit(int index)
        {
            if (paused)
            {
                Array.Clear(mixBuffer, 0, mixBuffer.Length);
            }
            else
            {
                lock (mixerLock)
                {
                    mixer.Mix(mixBuffer, BufferFrames);
                }
            }

            Marshal.Copy(mixBuffer, 0, buffers[index], mixBuffer.Length);

            // Pass the array element by ref so the driver writes WHDR_DONE
            // into the very struct we poll. Copying it into a local and back
            // would drop whatever the driver sets in the meantime.
            headers[index].dwFlags &= ~NativeMethodsWave.WHDR_DONE;
            headers[index].dwBufferLength = (uint)(mixBuffer.Length * 2);
            headers[index].dwBytesRecorded = 0;

            var result = NativeMethodsWave.waveOutWrite(
                waveOut,
                ref headers[index],
                (uint)Marshal.SizeOf(typeof(WAVEHDR)));

            return result == 0;
        }

        private bool OpenWaveOut()
        {
            try
            {
                var format = new WAVEFORMATEX();
                format.wFormatTag = 1;
                format.nChannels = OutputChannels;
                format.nSamplesPerSec = OutputRate;
                format.wBitsPerSample = 16;
                format.nBlockAlign = (ushort)(OutputChannels * 2);
                format.nAvgBytesPerSec = (uint)(OutputRate * format.nBlockAlign);
                format.cbSize = 0;

                var result = NativeMethodsWave.waveOutOpen(
                    out waveOut,
                    NativeMethodsWave.WAVE_MAPPER,
                    ref format,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    NativeMethodsWave.CALLBACK_NULL);

                if (result != 0)
                {
                    Console.WriteLine("waveOutOpen failed with error " + result + ".");
                    waveOut = IntPtr.Zero;
                    return false;
                }

                headers = new WAVEHDR[BufferCount];
                buffers = new IntPtr[BufferCount];

                var headerSize = Marshal.SizeOf(typeof(WAVEHDR));
                var byteCount = BufferFrames * OutputChannels * 2;

                for (var i = 0; i < BufferCount; i++)
                {
                    buffers[i] = Marshal.AllocHGlobal(byteCount);
                    headers[i] = new WAVEHDR();
                    headers[i].lpData = buffers[i];
                    headers[i].dwBufferLength = (uint)byteCount;
                    headers[i].dwFlags = NativeMethodsWave.WHDR_DONE;

                    var prepare = NativeMethodsWave.waveOutPrepareHeader(
                        waveOut, ref headers[i], (uint)headerSize);

                    if (prepare != 0)
                    {
                        Console.WriteLine("waveOutPrepareHeader failed with error " + prepare + ".");
                        CloseWaveOut();
                        return false;
                    }
                }

                mixerStarted = true;
                return true;
            }
            catch (Exception e)
            {
                ConsoleUi.Warn("sound unavailable: " + e.Message);
                return false;
            }
        }

        private void CloseWaveOut()
        {

            if (waveOut == IntPtr.Zero)
            {
                return;
            }

            NativeMethodsWave.waveOutReset(waveOut);

            if (headers != null)
            {
                var headerSize = Marshal.SizeOf(typeof(WAVEHDR));

                for (var i = 0; i < headers.Length; i++)
                {
                    if (headers[i].lpData != IntPtr.Zero)
                    {
                        NativeMethodsWave.waveOutUnprepareHeader(waveOut, ref headers[i], (uint)headerSize);
                        Marshal.FreeHGlobal(headers[i].lpData);
                        headers[i].lpData = IntPtr.Zero;
                    }
                }
            }

            NativeMethodsWave.waveOutClose(waveOut);
            waveOut = IntPtr.Zero;
        }

        public void Start()
        {
            if (!mixerStarted && !mixerThread.IsAlive)
            {
                mixerThread.Start();
            }
        }

        public void Dispose()
        {
            running = false;

            if (mixerThread.IsAlive)
            {
                mixerThread.Join(1000);
            }
        }

        public int MaxVolume { get { return 15; } }

        public int Volume
        {
            get { return config.audio_soundvolume; }
            set
            {
                config.audio_soundvolume = value;
                masterVolumeDecay = (float)config.audio_soundvolume / MaxVolume;
                mixer.MasterVolume = masterVolumeDecay;
            }
        }

        private sealed class ChannelInfo
        {
            public Sfx Reserved;
            public Sfx Playing;
            public float Priority;

            /// <summary>
            /// The lump queued by name, when the sound is not one of the engine's own.
            ///
            /// Kept beside <see cref="Reserved"/> rather than instead of it: the engine's sounds
            /// are still identified by their <see cref="Sfx"/> for pitch and for the "a sound from
            /// this object replaces itself" rule, and a lump has neither.
            /// </summary>
            public string ReservedName;

            /// <summary>The lump currently playing, or null when it is an <see cref="Sfx"/>.</summary>
            public string PlayingName;

            public Mobj Source;
            public SfxType Type;
            public int Volume;
            public Fixed LastX;
            public Fixed LastY;

            /// <summary>True when the channel has something queued or playing.</summary>
            public bool Busy
            {
                get { return Reserved != Sfx.NONE || Playing != Sfx.NONE || ReservedName != null || PlayingName != null; }
            }

            public void Clear()
            {
                Reserved = Sfx.NONE;
                Playing = Sfx.NONE;
                ReservedName = null;
                PlayingName = null;
                Priority = 0;
                Source = null;
                Type = 0;
                Volume = 0;
                LastX = Fixed.Zero;
                LastY = Fixed.Zero;
            }
        }
    }
}
