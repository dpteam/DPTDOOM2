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

namespace DPTDOOM
{
    public static partial class DoomInfo
    {
        /// <summary>
        /// How many actors a mod may add in one run.
        ///
        /// Matched to the reserve in <see cref="MobjType"/>, and fixed for the same reason:
        /// the actor table is indexed by type from the world, the save format and the
        /// renderer, so it cannot grow while a game is running.
        /// </summary>
        public const int CustomActorReserve = 32;

        private static MobjInfo[] mobjInfos;
        private static int customActorsUsed;

        /// <summary>
        /// The whole actor table: vanilla numbering first, then the reserve for mods.
        ///
        /// Index <c>i</c> is <c>MobjType</c> value <c>i</c>. Vanilla actors keep the numbers
        /// they have always had, so a save file or a demo recorded before this existed still
        /// loads: the tail is new, nothing moved.
        /// </summary>
        public static MobjInfo[] MobjInfos
        {
            get
            {
                if (mobjInfos != null)
                {
                    return mobjInfos;
                }

                var table = new MobjInfo[VanillaMobjInfos.Length + CustomActorReserve];

                Array.Copy(VanillaMobjInfos, table, VanillaMobjInfos.Length);

                for (var i = VanillaMobjInfos.Length; i < table.Length; i++)
                {
                    // A slot that exists but is inert: no doomednum, no health, no states.
                    // Nothing spawns it unless a mod defines it, and a map that names the
                    // number before any mod did gets the "unknown thing type" report it
                    // always got, because the doomednum is -1 and cannot match.
                    table[i] = new MobjInfo(
                        -1, MobjState.Null, 0, MobjState.Null, Sfx.NONE, 0, Sfx.NONE,
                        MobjState.Null, 0, Sfx.NONE, MobjState.Null, MobjState.Null,
                        MobjState.Null, MobjState.Null, Sfx.NONE, 0, Fixed.Zero, Fixed.Zero,
                        0, 0, Sfx.NONE, 0, MobjState.Null);
                }

                mobjInfos = table;
                return mobjInfos;
            }
        }

        /// <summary>
        /// The first <see cref="MobjType"/> value that belongs to mods.
        ///
        /// A property, not a field: static field initialisers run in an order the compiler
        /// chooses across partial files, and a field here can be read before
        /// <c>VanillaMobjInfos</c> is built.
        /// </summary>
        public static int CustomActorFirst
        {
            get { return VanillaMobjInfos.Length; }
        }

        /// <summary>
        /// Finds a free slot and fills it from a template, returning its type number, or -1
        /// when the reserve is exhausted.
        ///
        /// The new actor is a **copy** of its parent, not a blank: inheritance is the whole
        /// point of "ACTOR Child : Parent", and an actor that inherited nothing would have no
        /// states and disappear on its first tic. The copy is shallow on purpose - every
        /// field of <see cref="MobjInfo"/> is a value or an enum, so there is nothing to share
        /// accidentally.
        /// </summary>
        public static int AddActor(int doomEdNum, int parentType)
        {
            if (doomEdNum < 0)
            {
                return -1;
            }

            var table = MobjInfos;

            // A doomednum already in the table is a replacement, not a new actor. Answered
            // with that type rather than refused: a mod that redefines an existing number
            // means "change this one", and that is the common case.
            for (var i = 0; i < table.Length; i++)
            {
                if (table[i].DoomEdNum == doomEdNum)
                {
                    return i;
                }
            }

            if (customActorsUsed >= CustomActorReserve)
            {
                return -1;
            }

            var slot = CustomActorFirst + customActorsUsed;

            if (slot >= table.Length)
            {
                return -1;
            }

            var source = parentType >= 0 && parentType < table.Length
                ? table[parentType]
                : table[0];

            var from = source;

            table[slot] = new MobjInfo(
                doomEdNum, from.SpawnState, from.SpawnHealth, from.SeeState,
                from.SeeSound, from.ReactionTime, from.AttackSound, from.PainState,
                from.PainChance, from.PainSound, from.MeleeState, from.MissileState,
                from.DeathState, from.XdeathState, from.DeathSound, from.Speed,
                from.Radius, from.Height, from.Mass, from.Damage, from.ActiveSound,
                from.Flags, from.RaiseState);

            customActorsUsed++;
            return slot;
        }

        /// <summary>
        /// The type number of an actor by doomednum, or -1.
        ///
        /// This is the same search <see cref="ThingAllocation"/> does when it places a map
        /// object, exposed so the script layer can ask "does this number already exist"
        /// without duplicating the rule.
        /// </summary>
        public static int TypeOf(int doomEdNum)
        {
            var table = MobjInfos;

            for (var i = 0; i < table.Length; i++)
            {
                if (table[i].DoomEdNum == doomEdNum)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
