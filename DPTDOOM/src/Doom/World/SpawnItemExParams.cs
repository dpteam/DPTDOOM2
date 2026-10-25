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
    /// <summary>The flags of A_SpawnItemEx, with ZDoom's numbers from constants.zs.</summary>
    public static class SpawnItemExFlags
    {
        public const int TransferTranslation = 1 << 0;
        public const int AbsolutePosition = 1 << 1;
        public const int AbsoluteAngle = 1 << 2;
        public const int AbsoluteVelocity = 1 << 3;
        public const int AbsoluteMomentum = AbsoluteVelocity;
        public const int SetMaster = 1 << 4;
        public const int NoCheckPosition = 1 << 5;
        public const int Telefrag = 1 << 6;
        public const int ClientSide = 1 << 7;
        public const int TransferAmbushFlag = 1 << 8;
        public const int TransferPitch = 1 << 9;
        public const int TransferPointers = 1 << 10;
        public const int UseBloodColor = 1 << 11;
        public const int ClearCallerTid = 1 << 12;
        public const int MultiplySpeed = 1 << 13;
        public const int TransferScale = 1 << 14;
        public const int TransferSpecial = 1 << 15;
        public const int ClearCallerSpecial = 1 << 16;
        public const int TransferStencilCol = 1 << 17;
        public const int TransferAlpha = 1 << 18;
        public const int TransferRenderStyle = 1 << 19;
        public const int SetTarget = 1 << 20;
        public const int SetTracer = 1 << 21;
        public const int NoPointers = 1 << 22;
        public const int Originator = 1 << 23;
        public const int TransferSpriteFrame = 1 << 24;
        public const int TransferRoll = 1 << 25;
        public const int IsTarget = 1 << 26;
        public const int IsMaster = 1 << 27;
        public const int IsTracer = 1 << 28;

        /// <summary>
        /// The flags this engine can honour without pretending to have fields it does not.
        /// Master, pitch, rendering, scale, specials and TIDs are deliberately outside this set.
        /// </summary>
        public const int Supported =
            TransferTranslation | AbsolutePosition | AbsoluteAngle | AbsoluteVelocity |
            NoCheckPosition | TransferAmbushFlag | TransferPointers | MultiplySpeed |
            SetTarget | SetTracer | NoPointers | Originator | TransferSpriteFrame |
            IsTarget | IsTracer;
    }

    /// <summary>
    /// Everything one A_SpawnItemEx call needs, already converted to engine units.
    ///
    /// Expressions are evaluated before this object is built, so the spawn itself never parses
    /// text and never uses floating point. The actor type and the flags were resolved at load
    /// time: a typo belongs in the load report, not in a silently missing object.
    /// </summary>
    public sealed class SpawnItemExParams
    {
        public MobjType Type;
        public Fixed XOffset;
        public Fixed YOffset;
        public Fixed ZOffset;
        public Fixed XVelocity;
        public Fixed YVelocity;
        public Fixed ZVelocity;
        public Angle Angle;
        public int Flags;
        public int FailChance;
    }
}
