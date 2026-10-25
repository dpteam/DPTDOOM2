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

namespace DPTDOOM
{
    public class MobjStateDef
    {
        private int number;
        private Sprite sprite;
        private int frame;
        private int tics;
        private Action<World, Player, PlayerSpriteDef> playerAction;
        private Action<World, Mobj> mobjAction;
        private MobjState next;
        private int misc1;
        private int misc2;

        /// <summary>
        /// State to jump to instead of <see cref="Next"/>, or -1 for none.
        ///
        /// A property of the state rather than an action, because that is what DECORATE's A_Jump
        /// means: the frame's action is "leave this frame for that one", and the leaving is the
        /// transition this class already describes. Making it an action would need the action
        /// signature to carry arguments, which would touch all fifty of the engine's own.
        /// </summary>
        private int jumpTarget = -1;

        /// <summary>How likely the jump is, as one chance in 256.</summary>
        private int jumpChance;

        /// <summary>
        /// A question that decides the jump instead of the chance, or null.
        ///
        /// DECORATE's A_JumpIf: the frame leaves for its target when the question is true, with no
        /// roll. A delegate rather than the script type itself, for the same reason the actions are
        /// delegates: the world layer does not have to know that a scripting language exists, and
        /// the state table stays as cheap to copy as before.
        /// </summary>
        private Func<World, Mobj, bool> jumpCondition;

        /// <summary>A question that decides the jump instead of the chance, or null.</summary>
        public Func<World, Mobj, bool> JumpCondition
        {
            get => jumpCondition;
            set => jumpCondition = value;
        }

        public MobjStateDef(
            int number,
            Sprite sprite,
            int frame,
            int tics,
            Action<World, Player, PlayerSpriteDef> playerAction,
            Action<World, Mobj> mobjAction,
            MobjState next,
            int misc1,
            int misc2)
        {
            this.number = number;
            this.sprite = sprite;
            this.frame = frame;
            this.tics = tics;
            this.playerAction = playerAction;
            this.mobjAction = mobjAction;
            this.next = next;
            this.misc1 = misc1;
            this.misc2 = misc2;
        }

        public int Number
        {
            get => number;
            set => number = value;
        }

        public Sprite Sprite
        {
            get => sprite;
            set => sprite = value;
        }

        public int Frame
        {
            get => frame;
            set => frame = value;
        }

        public int Tics
        {
            get => tics;
            set => tics = value;
        }

        public Action<World, Player, PlayerSpriteDef> PlayerAction
        {
            get => playerAction;
            set => playerAction = value;
        }

        public Action<World, Mobj> MobjAction
        {
            get => mobjAction;
            set => mobjAction = value;
        }

        public MobjState Next
        {
            get => next;
            set => next = value;
        }

        public int Misc1
        {
            get => misc1;
            set => misc1 = value;
        }

        public int Misc2
        {
            get => misc2;
            set => misc2 = value;
        }

        /// <summary>State to jump to instead of <see cref="Next"/>, or -1 for none.</summary>
        public int JumpTarget
        {
            get => jumpTarget;
            set => jumpTarget = value;
        }

        /// <summary>How likely the jump is, as one chance in 256.</summary>
        public int JumpChance
        {
            get => jumpChance;
            set => jumpChance = value;
        }
    }
}
