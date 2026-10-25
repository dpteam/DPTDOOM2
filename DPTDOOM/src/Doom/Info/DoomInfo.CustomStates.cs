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
using System.Collections.Generic;

namespace DPTDOOM
{
    public static partial class DoomInfo
    {
        /// <summary>
        /// How many state slots are reserved for actors described by mods.
        ///
        /// Fixed rather than grown on demand, and that is the honest choice: the state table
        /// is read through <c>DoomInfo.States[(int)state]</c> from a dozen places including
        /// the save loader, so it cannot be resized while a game is running. A thousand
        /// slots is about as many states as vanilla Doom has, which is more than a PWAD
        /// describes; when it runs out, the mod is told rather than the game broken.
        /// </summary>
        public const int CustomStateReserve = 1024;

        /// <summary>
        /// The first state number that belongs to mods, which is also the end of vanilla.
        ///
        /// A property, not a field: static field initialisers run in an order the compiler
        /// chooses across partial files, and the first version read <c>VanillaStates</c>
        /// before it was built. Reading it lazily costs one comparison per call and cannot be
        /// wrong.
        /// </summary>
        public static int CustomStateFirst
        {
            get { return VanillaStates.Length; }
        }

        /// <summary>
        /// How many custom slots have been handed out so far.
        ///
        /// Shared across every script applied in this run: the table is static, so two mods
        /// defining states must not both receive slot zero.
        /// </summary>
        private static int customStatesUsed;

        private static MobjStateDef[] states;

        /// <summary>
        /// The whole state table: vanilla numbering first, then the reserve for mods.
        ///
        /// A property rather than a field only because the reserve has to be appended to a
        /// copy. Index <c>i</c> is still state number <c>i</c>, vanilla states keep the
        /// numbers they have always had, and a save file written before this existed still
        /// loads - the tail is new, nothing moved.
        /// </summary>
        public static MobjStateDef[] States
        {
            get
            {
                if (states != null)
                {
                    return states;
                }

                var table = new MobjStateDef[VanillaStates.Length + CustomStateReserve];

                Array.Copy(VanillaStates, table, VanillaStates.Length);

                for (var i = VanillaStates.Length; i < table.Length; i++)
                {
                    // Null-like: not a placeholder that does something surprising but one
                    // that removes the mobj, exactly as state 0 does. A script that names a
                    // state it never defined then loses the actor rather than the game.
                    table[i] = new MobjStateDef(i, Sprite.TROO, 0, -1, null, null,
                                                MobjState.Null, 0, 0);
                }

                states = table;
                return states;
            }
        }

        /// <summary>
        /// Reserves one custom state slot and returns its number, or -1 when the reserve is
        /// exhausted.
        /// </summary>
        public static int AllocateState()
        {
            // Touching States first so the table exists and the count is comparable.
            var table = States;

            if (customStatesUsed >= CustomStateReserve)
            {
                return -1;
            }

            var number = CustomStateFirst + customStatesUsed;
            customStatesUsed++;
            return number;
        }

        /// <summary>
        /// Puts a caller's description into a reserved slot.
        ///
        /// Kept here rather than in the script layer because the table is the engine's, and
        /// because everything that writes to it must go through one place that keeps
        /// <c>Number</c> equal to the index - the save loader reads the number back and uses
        /// it as an index.
        /// </summary>
        public static bool DefineState(int number, Sprite sprite, int frame, int tics,
                                       Action<World, Mobj> action, MobjState next)
        {
            var table = States;

            if (number < CustomStateFirst || number >= table.Length)
            {
                return false;
            }

            var def = table[number];
            def.Number = number;
            def.Sprite = sprite;
            def.Frame = frame;
            def.Tics = tics;
            def.MobjAction = action;
            def.PlayerAction = null;
            def.Next = next;
            return true;
        }

        /// <summary>
        /// Sets the successor of a state that is already defined.
        ///
        /// Separate from <see cref="DefineState"/> because a chain is a cycle: the frames are
        /// created first, each pointing at nothing, and only then are their successors known.
        /// </summary>
        public static bool SetStateNext(int number, MobjState next)
        {
            var table = States;

            if (number < CustomStateFirst || number >= table.Length)
            {
                return false;
            }

            table[number].Next = next;
            return true;
        }

        /// <summary>
        /// Gives a state a jump: leave for <paramref name="target"/> on a chance out of 256.
        ///
        /// Separate from the successor for the same reason the successor is separate: the target is
        /// a name that may not exist yet when the frame is created, so the link is made once every
        /// chain has been read.
        /// </summary>
        public static bool SetStateJump(int number, int target, int chance)
        {
            var table = States;

            if (number < CustomStateFirst || number >= table.Length)
            {
                return false;
            }

            table[number].JumpTarget = target;
            table[number].JumpChance = chance < 0 ? 0 : (chance > 256 ? 256 : chance);
            return true;
        }

        /// <summary>
        /// Action names by the spelling mods use, with the <c>A_</c> prefix.
        ///
        /// Built by reflection over <see cref="MobjActions"/> because every method there is
        /// exactly one action and the mapping is the method's own name: writing fifty-two
        /// entries by hand would be fifty-two chances to mistype one, and the reflection
        /// version cannot fall out of step when an action is added.
        ///
        /// Nothing outside this class can be reached this way: the type is private, the
        /// methods are found by their signature, and a mod supplies only a name to look up.
        /// </summary>
        private static Dictionary<string, Action<World, Mobj>> actionNames;

        public static bool TryGetAction(string name, out Action<World, Mobj> action)
        {
            action = null;

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var table = ActionNames();

            // Both spellings: a script may write "A_Look" as DECORATE does, or "Look" as the
            // engine's method is named. Accepting both costs nothing and saves a modder from
            // a rule that exists only in this project.
            if (table.TryGetValue(name, out action))
            {
                return true;
            }

            return name.StartsWith("A_", StringComparison.OrdinalIgnoreCase) &&
                   table.TryGetValue(name.Substring(2), out action);
        }

        /// <summary>The names of every action this engine has, for a report.</summary>
        public static IList<string> ActionNameList()
        {
            var names = new List<string>(ActionNames().Count);

            // Spelled with the A_ prefix, because that is what a mod writes and what a
            // message listing them is for: the reader is looking for the name they typed.
            foreach (var pair in ActionNames())
            {
                names.Add("A_" + pair.Key);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static Dictionary<string, Action<World, Mobj>> ActionNames()
        {
            if (actionNames != null)
            {
                return actionNames;
            }

            var table = new Dictionary<string, Action<World, Mobj>>(StringComparer.OrdinalIgnoreCase);

            foreach (var method in typeof(MobjActions).GetMethods())
            {
                if (method.ReturnType != typeof(void))
                {
                    continue;
                }

                var parameters = method.GetParameters();

                if (parameters.Length != 2 ||
                    parameters[0].ParameterType != typeof(World) ||
                    parameters[1].ParameterType != typeof(Mobj))
                {
                    continue;
                }

                var action = (Action<World, Mobj>)Delegate.CreateDelegate(
                    typeof(Action<World, Mobj>), ma, method, false);

                if (action != null)
                {
                    table[method.Name] = action;
                }
            }

            actionNames = table;
            return actionNames;
        }
    }
}
