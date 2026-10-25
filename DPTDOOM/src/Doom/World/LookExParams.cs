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
    /// The flags of A_LookEx, with ZDoom's numbers.
    ///
    /// Kept as constants rather than as a [Flags] enum because they are never stored anywhere: they
    /// arrive as one integer from the script, are tested once per look, and are gone. A stored enum
    /// would suggest they belong in the world or in a save, and they do not.
    /// </summary>
    public static class LookExFlags
    {
        /// <summary>Do not look at all; only react to a noise.</summary>
        public const int NoSightCheck = 1;

        /// <summary>Ignore the noise that woke the sector; only look.</summary>
        public const int NoSoundCheck = 2;

        /// <summary>
        /// Do not fall back to a patrol goal when no player is in sight.
        ///
        /// Accepted and has no effect: this engine has no patrol goals at all, so there is nothing
        /// to fall back to. It is not silently dropped either - the report says so, because a mod
        /// that gave a monster a patrol route expects it to walk it.
        /// </summary>
        public const int DontChaseGoal = 4;

        /// <summary>Take the target without playing the see sound.</summary>
        public const int NoSeeSound = 8;

        /// <summary>Play the see sound at full volume, ignoring distance.</summary>
        public const int FullVolSeeSound = 16;

        /// <summary>Take the target but do not switch to the see state.</summary>
        public const int NoJump = 32;
    }

    /// <summary>
    /// What A_LookEx asks for that A_Look does not.
    ///
    /// A null instance means plain A_Look. That is not a convenience: the vanilla path runs on
    /// every monster every tic and its behaviour goes into the world hash, so "no parameters" has
    /// to be the same code path, not a copy with different defaults.
    ///
    /// Distances are in map units and zero means "no limit", which is how DECORATE spells it.
    /// <see cref="Fov"/> is the whole cone, not half of it, and zero means "no cone" - the caller
    /// turns DECORATE's zero into 180 degrees before building this.
    /// </summary>
    public sealed class LookExParams
    {
        public int Flags;
        public Fixed MinSeeDist;
        public Fixed MaxSeeDist;
        public Fixed MaxHearDist;
        public Angle Fov;
        public MobjState SeeState = MobjState.Null;
    }
}
