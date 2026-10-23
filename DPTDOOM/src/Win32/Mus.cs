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

namespace DPTDOOM.Win32
{
    /// <summary>
    /// Converts a Doom MUS lump into a timed list of MIDI channel messages.
    /// The rules follow the classic mus2mid converter (Ben Ryves / Chocolate
    /// Doom) so that the result sounds like the original game.
    /// </summary>
    internal static class Mus
    {
        public const int TicksPerSecond = 140;

        private static readonly int[] ControllerMap = new int[]
        {
            0x00, 0x20, 0x01, 0x07, 0x0A, 0x0B, 0x5B, 0x5D,
            0x40, 0x43, 0x78, 0x7B, 0x7E, 0x7F, 0x79
        };

        public struct Event
        {
            public int Time;
            public byte Status;
            public byte Data1;
            public byte Data2;

            public Event(int time, byte status, byte data1, byte data2)
            {
                Time = time;
                Status = status;
                Data1 = data1;
                Data2 = data2;
            }
        }

        public static List<Event> Parse(byte[] data)
        {
            var events = new List<Event>();

            if (data == null || data.Length < 16)
            {
                return events;
            }

            if (data[0] != (byte)'M' || data[1] != (byte)'U' || data[2] != (byte)'S' || data[3] != 0x1A)
            {
                return events;
            }

            var scoreStart = BitConverter.ToUInt16(data, 6);
            if (scoreStart >= data.Length)
            {
                return events;
            }

            var channelMap = new int[16];
            for (var i = 0; i < channelMap.Length; i++)
            {
                channelMap[i] = -1;
            }

            var velocity = new byte[16];
            for (var i = 0; i < velocity.Length; i++)
            {
                velocity[i] = 127;
            }

            var position = (int)scoreStart;
            var time = 0;

            while (position < data.Length)
            {
                var descriptor = data[position++];
                var type = (descriptor >> 4) & 0x07;
                var last = (descriptor & 0x80) != 0;
                var musChannel = descriptor & 0x0F;

                if (type == 6)
                {
                    break;
                }

                var channel = MapChannel(channelMap, musChannel, events, time);

                switch (type)
                {
                    case 0: // release note
                    {
                        if (position >= data.Length) return events;
                        var note = data[position++];
                        events.Add(new Event(time, (byte)(0x80 | channel), (byte)(note & 0x7F), 0));
                        break;
                    }

                    case 1: // play note
                    {
                        if (position >= data.Length) return events;
                        var note = data[position++];
                        if ((note & 0x80) != 0)
                        {
                            if (position >= data.Length) return events;
                            velocity[musChannel] = (byte)(data[position++] & 0x7F);
                        }

                        events.Add(new Event(
                            time,
                            (byte)(0x90 | channel),
                            (byte)(note & 0x7F),
                            velocity[musChannel]));
                        break;
                    }

                    case 2: // pitch wheel
                    {
                        if (position >= data.Length) return events;
                        var value = data[position++] * 64;
                        events.Add(new Event(
                            time,
                            (byte)(0xE0 | channel),
                            (byte)(value & 0x7F),
                            (byte)((value >> 7) & 0x7F)));
                        break;
                    }

                    case 3: // system event
                    {
                        if (position >= data.Length) return events;
                        var controller = data[position++];
                        if (controller >= 10 && controller <= 14)
                        {
                            events.Add(new Event(
                                time,
                                (byte)(0xB0 | channel),
                                (byte)ControllerMap[controller],
                                0));
                        }

                        break;
                    }

                    case 4: // controller change
                    {
                        if (position + 1 >= data.Length) return events;
                        var controller = data[position++];
                        var value = data[position++];

                        if (controller == 0)
                        {
                            events.Add(new Event(time, (byte)(0xC0 | channel), value, 0));
                        }
                        else if (controller >= 1 && controller <= 9)
                        {
                            events.Add(new Event(
                                time,
                                (byte)(0xB0 | channel),
                                (byte)ControllerMap[controller],
                                value));
                        }

                        break;
                    }

                    case 5: // measure end
                        break;
                }

                if (last)
                {
                    var delay = 0;
                    byte value;
                    do
                    {
                        if (position >= data.Length) return events;
                        value = data[position++];
                        delay = delay * 128 + (value & 0x7F);
                    }
                    while ((value & 0x80) != 0);

                    time += delay;
                }
            }

            return events;
        }

        private static int MapChannel(int[] channelMap, int musChannel, List<Event> events, int time)
        {
            if (musChannel == 15)
            {
                return 9;
            }

            if (channelMap[musChannel] >= 0)
            {
                return channelMap[musChannel];
            }

            var midiChannel = 0;
            while (midiChannel == 9 || IsUsed(channelMap, midiChannel))
            {
                midiChannel++;
            }

            channelMap[musChannel] = midiChannel;

            // First use of a channel: silence it, matching mus2mid.
            events.Add(new Event(time, (byte)(0xB0 | midiChannel), 0x7B, 0));

            return midiChannel;
        }

        private static bool IsUsed(int[] channelMap, int midiChannel)
        {
            for (var i = 0; i < channelMap.Length; i++)
            {
                if (channelMap[i] == midiChannel)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
