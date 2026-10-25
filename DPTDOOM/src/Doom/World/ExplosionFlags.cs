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
    /// The flags of A_Explode, with ZDoom's numbers from wadsrc/static/zscript/constants.zs.
    ///
    /// Only two of them change anything in this engine. The rest are accepted and reported rather
    /// than silently dropped, because each asks for something a mod author would notice: splash on
    /// water, vertical thrust, no allies hurt, a circular rather than square blast.
    /// </summary>
    public static class ExplosionFlags
    {
        /// <summary>The blast hurts the actor that caused it. This is ZDoom's **default**.</summary>
        public const int HurtSource = 1;

        /// <summary>
        /// The explosion's source is the spot itself rather than whoever caused it.
        ///
        /// In this engine the source and the spot are already the same actor for A_Explode, so this
        /// changes nothing - but it is not silently swallowed, because with another source it
        /// would matter.
        /// </summary>
        public const int NotMissile = 4;

        /// <summary>Do not use the actor's own damage type for the blast.</summary>
        public const int ExplicitDamageType = 8;

        /// <summary>Do not splash on water.</summary>
        public const int NoSplash = 16;

        /// <summary>Also throw the victims upwards.</summary>
        public const int ThrustZ = 32;

        /// <summary>Do not throw the victims at all.</summary>
        public const int ThrustLess = 64;

        /// <summary>Do not hurt actors friendly to the source.</summary>
        public const int NoAllies = 128;

        /// <summary>A round blast instead of Doom's square one.</summary>
        public const int Circular = 256;

        /// <summary>A round blast, including its thrust.</summary>
        public const int CircularThrust = 512;
    }
}
