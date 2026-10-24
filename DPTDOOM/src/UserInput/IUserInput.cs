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

namespace DPTDOOM.UserInput
{
    public interface IUserInput
    {
        void BuildTicCmd(TicCmd cmd);
        void Reset();
        void GrabMouse();
        void ReleaseMouse();

        /// <summary>
        /// When true the input produces no commands.
        ///
        /// Needed because movement is read from the *state* of the keys, not from
        /// events: while a player types a message, the letters w/a/s/d would keep
        /// driving the player around the level. Suppressing the whole input is the
        /// only place that can be fixed, since the keyboard state is polled below
        /// this interface.
        /// </summary>
        bool Suppressed { get; set; }

        public int MaxMouseSensitivity { get; }
        public int MouseSensitivity { get; set; }
    }
}
