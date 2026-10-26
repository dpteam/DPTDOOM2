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
    /// The AAPTR values actions use to choose which actor they affect.
    ///
    /// Master and the player/line pointers have no counterpart here. Target and tracer are real
    /// mobj references, so actions that use them can be supported instead of silently operating on
    /// the wrong actor.
    /// </summary>
    public static class ActorPointer
    {
        public const int Default = 0;
        public const int Null = 1;
        public const int Target = 2;
        public const int Master = 4;
        public const int Tracer = 8;

        /// <summary>Whether this pointer can be resolved by this engine.</summary>
        public static bool IsSupported(int pointer)
        {
            return pointer == Default || pointer == Null || pointer == Target || pointer == Tracer;
        }

        /// <summary>Resolves a supported pointer, or null when it points at nothing.</summary>
        public static Mobj Resolve(Mobj actor, int pointer)
        {
            switch (pointer)
            {
                case Default:
                    return actor;
                case Null:
                    return null;
                case Target:
                    return actor.Target;
                case Tracer:
                    return actor.Tracer;
                default:
                    return null;
            }
        }
    }
}
