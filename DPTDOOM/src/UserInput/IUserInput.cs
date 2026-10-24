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

        /// <summary>
        /// Vertical look accumulated since the previous call, in virtual pixels of
        /// a 320x200 screen.
        ///
        /// A separate channel from <see cref="BuildTicCmd"/> on purpose. Pitch is
        /// presentation: it must not enter a TicCmd, or a lockstep session would
        /// have to agree on it and a demo would have to store it. It is also read
        /// once per *frame* rather than once per tic, because the view has to move
        /// smoothly even when the tic rate is lower than the frame rate.
        ///
        /// Returns 0 when free look is off, which is what keeps the mouse wheel and
        /// the mouse Y axis behaving exactly as they did before it existed.
        /// </summary>
        /// <param name="center">
        /// Set when the player asked to recentre the view. A separate flag rather
        /// than a huge delta, because "go back to level" is not a movement and must
        /// not be scaled by sensitivity.
        /// </param>
        int TakeViewPitch(out bool center);

        public int MaxMouseSensitivity { get; }
        public int MouseSensitivity { get; set; }
    }
}
