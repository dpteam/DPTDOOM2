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

namespace DPTDOOM.Win32
{
    internal static class NativeMethodsWave
    {
        public const uint WAVE_MAPPER = 0xFFFFFFFF;
        public const uint CALLBACK_NULL = 0x00000000;
        public const uint WHDR_DONE = 0x00000001;
        public const uint WHDR_PREPARED = 0x00000002;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")]
        public static extern uint waveOutOpen(
            out IntPtr hWaveOut,
            uint deviceId,
            ref WAVEFORMATEX format,
            IntPtr callback,
            IntPtr instance,
            uint flags);

        [DllImport("winmm.dll")]
        public static extern uint waveOutClose(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        public static extern uint waveOutPrepareHeader(IntPtr hWaveOut, ref WAVEHDR header, uint size);

        [DllImport("winmm.dll")]
        public static extern uint waveOutUnprepareHeader(IntPtr hWaveOut, ref WAVEHDR header, uint size);

        [DllImport("winmm.dll")]
        public static extern uint waveOutWrite(IntPtr hWaveOut, ref WAVEHDR header, uint size);

        [DllImport("winmm.dll")]
        public static extern uint waveOutReset(IntPtr hWaveOut);
    }
}
