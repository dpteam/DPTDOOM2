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



namespace DPTDOOM
{
    /// <summary>
    /// The flags of A_Chase, with ZDoom's numbers from p_enemy.cpp.
    ///
    /// Constants rather than a [Flags] enum, for the same reason as <see cref="LookExFlags"/>:
    /// they arrive as one integer from a script, are read once per tic, and are never stored.
    /// </summary>
    public static class ChaseFlags
    {
        /// <summary>
        /// Boss-style chase: strafing around the target instead of closing on it.
        ///
        /// Accepted and has no effect: this engine's boss chase is the vanilla one, and there is no
        /// strafe counter to consult. The report says so, because a mod that gave a monster this
        /// flag expects it to circle rather than walk straight in.
        /// </summary>
        public const int FastChase = 1;

        /// <summary>Do not play the active (idle growl) sound while chasing.</summary>
        public const int NoPlayActive = 2;

        /// <summary>
        /// Shorten the frame's duration by half, down to three tics, in nightmare or fast mode.
        ///
        /// On top of the vanilla speed-up rather than instead of it: vanilla halves only the
        /// sergeant's run and pain frames, and this is the generic version a mod asks for by name.
        /// </summary>
        public const int NightmareFast = 4;

        /// <summary>
        /// Try to raise corpses while chasing, as the arch-vile does.
        ///
        /// Accepted and has no effect: this engine has no resurrection at all, so there is nothing
        /// for the flag to turn on. Named in the report.
        /// </summary>
        public const int Resurrect = 8;

        /// <summary>Chase without moving: a turret. Still turns, aims and attacks.</summary>
        public const int DontMove = 16;

        /// <summary>Do not pick a new random direction when the current one runs out.</summary>
        public const int NoRandomTurn = 32;

        /// <summary>Do not turn to face the direction of travel.</summary>
        public const int NoDirectionTurn = 64;

        /// <summary>Do not pick a new direction after an attack.</summary>
        public const int NoPostAttackTurn = 128;

        /// <summary>Stop when blocked instead of picking another direction.</summary>
        public const int StopIfBlocked = 256;

        /// <summary>
        /// Wander when there is no target instead of going idle.
        ///
        /// Accepted and has no effect: this engine has no wander action, and the fallback is the
        /// vanilla one - back to the spawn state. Named in the report.
        /// </summary>
        public const int DontIdle = 512;

        /// <summary>Look only forward when picking up a new target, not all around.</summary>
        public const int DontLookAllAround = 1024;
    }
}
