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
    public sealed class TicCmd
    {
        private sbyte forwardMove;
        private sbyte sideMove;
        private short angleTurn;
        private byte buttons;

        /// <summary>
        /// Whether the player asked to jump this tic.
        ///
        /// A field of its own rather than a bit in <see cref="Buttons"/>, and that is a
        /// decision with consequences: `Buttons` has one free bit left and it belongs to
        /// "this player has left", which travels over the network. Jump has to travel in a
        /// demo as well, so it needs a byte of its own in the demo format - see
        /// <see cref="DemoRecorder"/> for how the old format is still written when nobody
        /// jumped.
        ///
        /// It is a byte rather than a bool because it is stored in a byte on the wire and in
        /// a demo, and pretending otherwise would mean converting in three places.
        /// </summary>
        private byte jump;

        public void Clear()
        {
            forwardMove = 0;
            sideMove = 0;
            angleTurn = 0;
            buttons = 0;
            jump = 0;
        }

        public void CopyFrom(TicCmd cmd)
        {
            forwardMove = cmd.forwardMove;
            sideMove = cmd.sideMove;
            angleTurn = cmd.angleTurn;
            buttons = cmd.buttons;
            jump = cmd.jump;
        }

        public sbyte ForwardMove
        {
            get => forwardMove;
            set => forwardMove = value;
        }

        public sbyte SideMove
        {
            get => sideMove;
            set => sideMove = value;
        }

        public short AngleTurn
        {
            get => angleTurn;
            set => angleTurn = value;
        }

        public byte Buttons
        {
            get => buttons;
            set => buttons = value;
        }

        public byte Jump
        {
            get => jump;
            set => jump = value;
        }
    }
}
