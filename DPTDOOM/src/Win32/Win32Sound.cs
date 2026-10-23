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
using System.Runtime.InteropServices;
using System.Threading;
using DPTDOOM.Audio;
using WAVEHDR = DPTDOOM.Win32.NativeMethodsWave.WAVEHDR;
using WAVEFORMATEX = DPTDOOM.Win32.NativeMethodsWave.WAVEFORMATEX;

namespace DPTDOOM.Win32
{
    /// <summary>
    /// Standalone sound effects through winmm.dll. A small software mixer
    /// resamples the 8-bit DMX lumps, applies distance attenuation and stereo
    /// panning, and feeds waveOut. This replaces DrippyAL/OpenAL and works on
    /// Windows XP without any extra DLLs.
    /// </summary>
    internal sealed class Win32Sound : ISound, IDisposable
    {
        private const int ChannelCount = 8;
        private const int OutputRate = 44100;
        private const int OutputChannels = 2;
        private const int BufferFrames = 1024;
        private const int BufferCount = 4;

        private const float FastDecay = 0.7071F;  // half-life ~5 tics at 35 Hz
        private const float SlowDecay = 0.9805F;  // half-life ~35 tics
        private const float ClipDist = 1200;
        private const float CloseDist = 160;
        private const float Attenuator = ClipDist - CloseDist;

        private readonly Config config;
        private readonly Sample[] samples;
        private readonly float[] amplitudes;
        private readonly DoomRandom random;

        private readonly ChannelInfo[] infos;
        private readonly Voice[] voices;
        private readonly Voice uiVoice;

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

        public Win32Sound(Config config, GameContent content)
        {
            this.config = config;

            config.audio_soundvolume = MathEx.Clamp(config.audio_soundvolume, 0, MaxVolume);

            samples = new Sample[DoomInfo.SfxNames.Length];
            amplitudes = new float[DoomInfo.SfxNames.Length];

            if (config.audio_randompitch)
            {
                random = new DoomRandom();
            }

            for (var i = 0; i < DoomInfo.SfxNames.Length; i++)
            {
                var name = "DS" + DoomInfo.SfxNames[i].ToString().ToUpper();
                if (content.Wad.GetLumpNumber(name) == -1)
                {
                    continue;
                }

                var sample = ReadSample(content.Wad.ReadLump(name));
                if (sample != null)
                {
                    samples[i] = sample;
                    amplitudes[i] = GetAmplitude(sample);
                }
            }

            infos = new ChannelInfo[ChannelCount];
            voices = new Voice[ChannelCount];
            for (var i = 0; i < ChannelCount; i++)
            {
                infos[i] = new ChannelInfo();
                voices[i] = new Voice();
            }

            uiVoice = new Voice();
            uiReserved = Sfx.NONE;

            masterVolumeDecay = (float)config.audio_soundvolume / MaxVolume;
            lastUpdate = DateTime.MinValue;

            running = true;
            mixerThread = new Thread(MixerLoop);
            mixerThread.IsBackground = true;
            mixerThread.Name = "DPTDOOM sound mixer";
        }

        private static Sample ReadSample(byte[] data)
        {
            if (data.Length < 8)
            {
                return null;
            }

            var sampleRate = BitConverter.ToUInt16(data, 2);
            var sampleCount = BitConverter.ToInt32(data, 4);

            var offset = 8;
            if (ContainsDmxPadding(data))
            {
                offset += 16;
                sampleCount -= 32;
            }

            if (sampleCount <= 0 || offset + sampleCount > data.Length)
            {
                return null;
            }

            return new Sample(data, offset, sampleCount, sampleRate);
        }

        private static bool ContainsDmxPadding(byte[] data)
        {
            var sampleCount = BitConverter.ToInt32(data, 4);
            if (sampleCount < 32)
            {
                return false;
            }

            var first = data[8];
            for (var i = 1; i < 16; i++)
            {
                if (data[8 + i] != first)
                {
                    return false;
                }
            }

            var last = data[8 + sampleCount - 1];
            for (var i = 1; i < 16; i++)
            {
                if (data[8 + sampleCount - i - 1] != last)
                {
                    return false;
                }
            }

            return true;
        }

        private static float GetAmplitude(Sample sample)
        {
            var max = 0;
            var count = Math.Min(sample.SampleRate / 5, sample.Count);
            for (var t = 0; t < count; t++)
            {
                var a = sample.Data[sample.Offset + t] - 128;
                if (a < 0)
                {
                    a = -a;
                }

                if (a > max)
                {
                    max = a;
                }
            }

            return max / 128F;
        }

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
                var voice = voices[i];

                if (info.Playing != Sfx.NONE)
                {
                    if (voice.Playing)
                    {
                        if (info.Type == SfxType.Diffuse)
                        {
                            info.Priority *= SlowDecay;
                        }
                        else
                        {
                            info.Priority *= FastDecay;
                        }

                        SetParam(voice, info);
                    }
                    else
                    {
                        info.Playing = Sfx.NONE;
                        if (info.Reserved == Sfx.NONE)
                        {
                            info.Source = null;
                        }
                    }
                }

                if (info.Reserved != Sfx.NONE)
                {
                    var sample = samples[(int)info.Reserved];
                    if (sample != null)
                    {
                        StartVoice(voice, info, sample);
                        info.Playing = info.Reserved;
                    }

                    info.Reserved = Sfx.NONE;
                }
            }

            if (uiReserved != Sfx.NONE)
            {
                var sample = samples[(int)uiReserved];
                if (sample != null)
                {
                    StartUiVoice(sample);
                }

                uiReserved = Sfx.NONE;
            }
        }

        private void StartVoice(Voice voice, ChannelInfo info, Sample sample)
        {
            lock (mixerLock)
            {
                voice.Sample = sample;
                voice.Position = 0;
                voice.Step = (double)sample.SampleRate / OutputRate * GetPitch(info.Type, info.Playing);
                voice.Playing = true;
                SetParam(voice, info);
            }
        }

        private void StartUiVoice(Sample sample)
        {
            lock (mixerLock)
            {
                uiVoice.Sample = sample;
                uiVoice.Position = 0;
                uiVoice.Step = (double)sample.SampleRate / OutputRate;
                uiVoice.LeftGain = 0.7071F * masterVolumeDecay;
                uiVoice.RightGain = 0.7071F * masterVolumeDecay;
                uiVoice.Playing = true;
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
                priority = amplitudes[(int)sfx] * GetDistanceDecay(dist) * volume;
            }

            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                if (info.Source == mobj && info.Type == type)
                {
                    info.Reserved = sfx;
                    info.Priority = priority;
                    info.Volume = volume;
                    return;
                }
            }

            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                if (info.Reserved == Sfx.NONE && info.Playing == Sfx.NONE)
                {
                    info.Reserved = sfx;
                    info.Priority = priority;
                    info.Source = mobj;
                    info.Type = type;
                    info.Volume = volume;
                    return;
                }
            }

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
                var info = infos[minChannel];
                info.Reserved = sfx;
                info.Priority = priority;
                info.Source = mobj;
                info.Type = type;
                info.Volume = volume;
            }
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
                for (var i = 0; i < infos.Length; i++)
                {
                    voices[i].Playing = false;
                    voices[i].Sample = null;
                    infos[i].Clear();
                }

                uiVoice.Playing = false;
                uiVoice.Sample = null;
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

        private void SetParam(Voice voice, ChannelInfo info)
        {
            if (info.Type == SfxType.Diffuse || listener == null)
            {
                voice.LeftGain = 0.7071F * masterVolumeDecay * 0.01F * info.Volume;
                voice.RightGain = voice.LeftGain;
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
                voice.LeftGain = 0.7071F * masterVolumeDecay * 0.01F * info.Volume;
                voice.RightGain = voice.LeftGain;
            }
            else
            {
                var dist = MathF.Sqrt(x * x + y * y);
                var angle = MathF.Atan2(y, x) - (float)listener.Angle.ToRadian();
                var pan = (MathF.Sin(angle) + 1F) * 0.5F;

                var gain = masterVolumeDecay * 0.01F * GetDistanceDecay(dist) * info.Volume;
                voice.LeftGain = (float)Math.Sqrt(1.0 - pan) * gain;
                voice.RightGain = (float)Math.Sqrt(pan) * gain;
            }
        }

        private static float GetDistanceDecay(float dist)
        {
            if (dist < CloseDist)
            {
                return 1F;
            }

            var decay = (ClipDist - dist) / Attenuator;
            return decay > 0F ? decay : 0F;
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

        private void MixerLoop()
        {
            if (!OpenWaveOut())
            {
                return;
            }

            var silent = new short[BufferFrames * OutputChannels];
            var written = 0;

            while (running)
            {
                var idle = true;

                for (var i = 0; i < BufferCount && running; i++)
                {
                    var header = headers[i];
                    var done = (header.dwFlags & NativeMethodsWave.WHDR_DONE) != 0;
                    if (!done && written >= BufferCount)
                    {
                        continue;
                    }

                    idle = false;

                    if (paused)
                    {
                        Array.Clear(silent, 0, silent.Length);
                        Marshal.Copy(silent, 0, buffers[i], silent.Length);
                    }
                    else
                    {
                        Mix(mixBuffer);
                        Marshal.Copy(mixBuffer, 0, buffers[i], mixBuffer.Length);
                    }

                    headers[i].dwFlags &= ~NativeMethodsWave.WHDR_DONE;
                    headers[i].dwBufferLength = (uint)(BufferFrames * OutputChannels * 2);
                    headers[i].dwBytesRecorded = 0;
                    NativeMethodsWave.waveOutWrite(waveOut, ref headers[i], (uint)Marshal.SizeOf(typeof(WAVEHDR)));
                    written++;
                }

                if (idle)
                {
                    Thread.Sleep(5);
                }
            }

            CloseWaveOut();
        }

        private void Mix(short[] destination)
        {
            Array.Clear(destination, 0, destination.Length);

            lock (mixerLock)
            {
                for (var v = -1; v < voices.Length; v++)
                {
                    var voice = v < 0 ? uiVoice : voices[v];
                    if (!voice.Playing || voice.Sample == null)
                    {
                        continue;
                    }

                    var sample = voice.Sample;
                    var position = voice.Position;
                    var step = voice.Step;
                    var left = voice.LeftGain;
                    var right = voice.RightGain;

                    for (var i = 0; i < BufferFrames; i++)
                    {
                        var index = (int)position;
                        if (index >= sample.Count - 1)
                        {
                            voice.Playing = false;
                            break;
                        }

                        var frac = (float)(position - index);
                        var s0 = (sample.Data[sample.Offset + index] - 128) / 128F;
                        var s1 = (sample.Data[sample.Offset + index + 1] - 128) / 128F;
                        var value = s0 + (s1 - s0) * frac;

                        var dst = i * 2;
                        destination[dst] = Clamp(destination[dst] + value * left);
                        destination[dst + 1] = Clamp(destination[dst + 1] + value * right);

                        position += step;
                    }

                    voice.Position = position;
                }
            }
        }

        private static short Clamp(float value)
        {
            if (value > 1F)
            {
                return short.MaxValue;
            }

            if (value < -1F)
            {
                return short.MinValue;
            }

            return (short)(value * 32767F);
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
                    Console.WriteLine("Sound unavailable (waveOutOpen error " + result + ").");
                    waveOut = IntPtr.Zero;
                    return false;
                }

                mixBuffer = new short[BufferFrames * OutputChannels];
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
                    NativeMethodsWave.waveOutPrepareHeader(waveOut, ref headers[i], (uint)headerSize);
                }

                mixerStarted = true;
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("Sound unavailable: " + e.Message);
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
                mixerThread.Join(500);
            }
        }

        public int MaxVolume => 15;

        public int Volume
        {
            get => config.audio_soundvolume;
            set
            {
                config.audio_soundvolume = value;
                masterVolumeDecay = (float)config.audio_soundvolume / MaxVolume;
            }
        }

        private sealed class Sample
        {
            public readonly byte[] Data;
            public readonly int Offset;
            public readonly int Count;
            public readonly int SampleRate;

            public Sample(byte[] data, int offset, int count, int sampleRate)
            {
                Data = data;
                Offset = offset;
                Count = count;
                SampleRate = sampleRate;
            }
        }

        private sealed class Voice
        {
            public Sample Sample;
            public double Position;
            public double Step;
            public float LeftGain;
            public float RightGain;
            public bool Playing;
        }

        private sealed class ChannelInfo
        {
            public Sfx Reserved;
            public Sfx Playing;
            public float Priority;

            public Mobj Source;
            public SfxType Type;
            public int Volume;
            public Fixed LastX;
            public Fixed LastY;

            public void Clear()
            {
                Reserved = Sfx.NONE;
                Playing = Sfx.NONE;
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
