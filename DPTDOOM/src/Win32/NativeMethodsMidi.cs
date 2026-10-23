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
    internal static class NativeMethodsMidi
    {
        public const uint MIDI_MAPPER = 0xFFFFFFFF;
        public const uint CALLBACK_NULL = 0x00000000;

        [DllImport("winmm.dll")]
        public static extern uint midiOutOpen(
            out IntPtr device,
            uint deviceId,
            IntPtr callback,
            IntPtr instance,
            uint flags);

        [DllImport("winmm.dll")]
        public static extern uint midiOutClose(IntPtr device);

        [DllImport("winmm.dll")]
        public static extern uint midiOutReset(IntPtr device);

        [DllImport("winmm.dll")]
        public static extern uint midiOutShortMsg(IntPtr device, uint message);
    }
}
