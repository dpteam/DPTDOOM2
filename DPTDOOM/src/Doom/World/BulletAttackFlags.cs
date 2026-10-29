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
    /// The flags of A_CustomBulletAttack, with ZDoom's numbers from constants.zs.
    ///
    /// Kept separate from the A_FireBullets set even though most names repeat: the numbers differ
    /// (CBAF_NORANDOM is 2 and FBF_NORANDOM is 2, but CBAF_NORANDOMPUFFZ is 16 and
    /// FBF_NORANDOMPUFFZ is 32), so one shared table would silently apply the wrong bit.
    /// </summary>
    public static class CustomBulletAttackFlags
    {
        /// <summary>Fire along the actor's facing without turning to the reference target.</summary>
        public const int AimFacing = 1;

        /// <summary>Do not multiply the damage by 1..3.</summary>
        public const int NoRandom = 2;

        /// <summary>Spread values are absolute angles, not a random cone.</summary>
        public const int ExplicitAngle = 4;

        /// <summary>Do not aim vertically at the reference target.</summary>
        public const int NoPitch = 8;

        /// <summary>Do not randomise the puff's vertical position.</summary>
        public const int NoRandomPuffZ = 16;

        /// <summary>Puff pointer assignments; this engine has no puff pointers.</summary>
        public const int PuffTarget = 32;
        public const int PuffMaster = 64;
        public const int PuffTracer = 128;

        /// <summary>The flags this engine honours.</summary>
        public const int Supported = AimFacing | NoRandom | ExplicitAngle | NoPitch | NoRandomPuffZ;
    }

    /// <summary>
    /// The flags of A_FireBullets, with ZDoom's numbers from constants.zs.
    ///
    /// Note the default: A_FireBullets defaults to <see cref="UseAmmo"/>, while
    /// A_CustomBulletAttack defaults to no flags at all. That is why the two actions cannot share
    /// one reader.
    /// </summary>
    public static class FireBulletsFlags
    {
        /// <summary>Spend one shot of the ready weapon.</summary>
        public const int UseAmmo = 1;

        /// <summary>Do not multiply the damage by 1..3.</summary>
        public const int NoRandom = 2;

        /// <summary>Spread values are absolute angles, not a random cone.</summary>
        public const int ExplicitAngle = 4;

        /// <summary>Do not aim vertically at the reference target.</summary>
        public const int NoPitch = 8;

        /// <summary>
        /// Do not play the weapon's attack flash. Always true here: a DECORATE weapon state is not
        /// a thing this engine has, so there is no flash to suppress.
        /// </summary>
        public const int NoFlash = 16;

        /// <summary>Do not randomise the puff's vertical position.</summary>
        public const int NoRandomPuffZ = 32;

        /// <summary>Puff pointer assignments; this engine has no puff pointers.</summary>
        public const int PuffTarget = 64;
        public const int PuffMaster = 128;
        public const int PuffTracer = 256;

        /// <summary>The flags this engine honours.</summary>
        public const int Supported = UseAmmo | NoRandom | ExplicitAngle | NoPitch | NoFlash | NoRandomPuffZ;
    }
}
