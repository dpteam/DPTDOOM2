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
    /// <summary>
    /// Decoder for Boom's generalized linedef types.
    ///
    /// Boom needed more line types than Doom had numbers for, so it packed the
    /// type into the special field itself: the top bits choose the family, and
    /// the lower bits encode the trigger, the target height, the speed and a few
    /// flags. One number in a WAD therefore means "raise the floor to the lowest
    /// neighbouring ceiling at turbo speed when a monster crosses it".
    ///
    /// The bit layout is not guesswork: it is taken from the Boom source, which
    /// ships in `refs/prboom-plus/prboom2/src/p_spec.h`, and the field masks and
    /// shifts below carry the same names so they can be checked against it.
    ///
    /// This class only **decodes**. Turning a decoded type into a moving floor is
    /// the caller's job, which keeps the arithmetic testable on its own — and it
    /// is the arithmetic, not the movement, that a WAD's author cannot see and
    /// that is easy to get subtly wrong.
    /// </summary>
    public static class BoomGeneralized
    {
        // Family bases. A special at or above one of these, and below the next
        // one up, belongs to that family.
        public const int GenFloorBase = 0x6000;
        public const int GenCeilingBase = 0x4000;
        public const int GenDoorBase = 0x3c00;
        public const int GenLockedBase = 0x3800;
        public const int GenLiftBase = 0x3400;
        public const int GenStairsBase = 0x3000;
        public const int GenCrusherBase = 0x2f80;

        // Trigger type: the low three bits of every family.
        private const int TriggerType = 0x0007;
        private const int TriggerTypeShift = 0;

        // Floor fields.
        private const int FloorCrush = 0x1000;
        private const int FloorChange = 0x0c00;
        private const int FloorTarget = 0x0380;
        private const int FloorDirection = 0x0040;
        private const int FloorModel = 0x0020;
        private const int FloorSpeed = 0x0018;

        private const int FloorCrushShift = 12;
        private const int FloorChangeShift = 10;
        private const int FloorTargetShift = 7;
        private const int FloorDirectionShift = 6;
        private const int FloorModelShift = 5;
        private const int FloorSpeedShift = 3;

        // Ceiling fields (same layout as floors, different names in Boom).
        private const int CeilingCrush = 0x1000;
        private const int CeilingChange = 0x0c00;
        private const int CeilingTarget = 0x0380;
        private const int CeilingDirection = 0x0040;
        private const int CeilingModel = 0x0020;
        private const int CeilingSpeed = 0x0018;

        // Lift fields.
        private const int LiftTarget = 0x0300;
        private const int LiftDelay = 0x00c0;
        private const int LiftMonster = 0x0020;
        private const int LiftSpeed = 0x0018;

        private const int LiftTargetShift = 8;
        private const int LiftDelayShift = 6;
        private const int LiftSpeedShift = 3;

        // Stairs fields.
        private const int StairIgnore = 0x0200;
        private const int StairDirection = 0x0100;
        private const int StairStep = 0x00c0;
        private const int StairSpeed = 0x0018;

        private const int StairIgnoreShift = 9;
        private const int StairDirectionShift = 8;
        private const int StairStepShift = 6;
        private const int StairSpeedShift = 3;

        // Crusher fields.
        private const int CrusherSilent = 0x0040;
        private const int CrusherMonster = 0x0020;
        private const int CrusherSpeed = 0x0018;

        private const int CrusherSilentShift = 6;
        private const int CrusherSpeedShift = 3;

        // Door fields.
        private const int DoorDelay = 0x0300;
        private const int DoorMonster = 0x0080;
        private const int DoorKind = 0x0060;
        private const int DoorSpeed = 0x0018;

        private const int DoorDelayShift = 8;
        private const int DoorKindShift = 5;
        private const int DoorSpeedShift = 3;

        // Locked door fields.
        private const int LockedNKeys = 0x0200;
        private const int LockedKey = 0x01c0;
        private const int LockedKind = 0x0020;
        private const int LockedSpeed = 0x0018;

        private const int LockedKeyShift = 6;
        private const int LockedSpeedShift = 3;

        /// <summary>Which family a special belongs to.</summary>
        public enum Family
        {
            None = 0,
            Floor,
            Ceiling,
            Door,
            LockedDoor,
            Lift,
            Stairs,
            Crusher
        }

        /// <summary>How the line is activated.</summary>
        public enum Trigger
        {
            WalkOnce = 0,
            WalkMany = 1,
            SwitchOnce = 2,
            SwitchMany = 3,
            GunOnce = 4,
            GunMany = 5,
            PushOnce = 6,
            PushMany = 7
        }

        /// <summary>Motion speed, as a multiplier of the family's base speed.</summary>
        public enum Speed
        {
            Slow = 0,
            Normal = 1,
            Fast = 2,
            Turbo = 3
        }

        /// <summary>A decoded generalized line type.</summary>
        public struct Definition
        {
            public Family Family;
            public Trigger Trigger;
            public Speed Speed;

            /// <summary>Floor or ceiling target: the low three bits of the target field.</summary>
            public int Target;

            /// <summary>Floor or ceiling change: what happens to texture and special.</summary>
            public int Change;

            /// <summary>True when the texture and type are taken from a nearby sector by height.</summary>
            public bool NumericModel;

            /// <summary>True when the floor crushes things against the ceiling.</summary>
            public bool Crush;

            /// <summary>True when the motion goes up, false when it goes down.</summary>
            public bool Up;

            /// <summary>Lift: seconds to wait at each end, in tics.</summary>
            public int Delay;

            /// <summary>Lift or stairs: whether monsters can activate it.</summary>
            public bool MonstersCanTrigger;

            /// <summary>Stairs: whether the chain ignores the floor texture when looking for the next step.</summary>
            public bool StairIgnoresTexture;

            /// <summary>
            /// Stairs: step size in map units, one of 4, 8, 16 or 24.
            ///
            /// The field in the line is a two-bit selector, and Boom's table is 0→4, 1→8, 2→16,
            /// 3→24. It is stored here as the size rather than the selector because the size is
            /// what the builder needs, and because the previous version of this decoder mapped
            /// "selector 0" to eight and everything else to sixteen - which is not Boom's table
            /// and would have built the wrong staircase for two of the four values.
            /// </summary>
            public int StairStep;

            /// <summary>Door: what the door does after opening.</summary>
            public int DoorKind;

            /// <summary>Locked door: which key is needed.</summary>
            public int LockedKey;

            /// <summary>
            /// Locked door: whether skulls count as cards.
            ///
            /// Boom's macro is called `LockedNKeys`, which reads like a count and is not one:
            /// the bit means "a skull opens a card lock". Naming it after the macro would make
            /// every reader of this field reason about the wrong thing.
            /// </summary>
            public bool SkullsCountAsCards;

            /// <summary>Crusher: silent rather than noisy.</summary>
            public bool Silent;

            public bool IsGeneralized { get { return Family != Family.None; } }
        }

        /// <summary>
        /// True when a special is a Boom generalized type rather than a numbered
        /// Doom one. Boom starts at the crusher base and goes up.
        /// </summary>
        public static bool IsGeneralized(int special)
        {
            return special >= GenCrusherBase && special < 0x8000;
        }

        /// <summary>
        /// Decodes a special. Returns a definition with
        /// <see cref="Definition.IsGeneralized"/> false for anything that is not a
        /// generalized type, so a caller can pass every line through this.
        /// </summary>
        public static Definition Decode(int special)
        {
            var definition = new Definition();

            if (!IsGeneralized(special))
            {
                return definition;
            }

            // The families are ordered by base, and a special belongs to the
            // highest base it reaches. That ordering is why this is a chain of
            // comparisons and not a lookup: the ranges are not all the same size.
            if (special >= GenFloorBase)
            {
                definition.Family = Family.Floor;
            }
            else if (special >= GenCeilingBase)
            {
                definition.Family = Family.Ceiling;
            }
            else if (special >= GenDoorBase)
            {
                definition.Family = Family.Door;
            }
            else if (special >= GenLockedBase)
            {
                definition.Family = Family.LockedDoor;
            }
            else if (special >= GenLiftBase)
            {
                definition.Family = Family.Lift;
            }
            else if (special >= GenStairsBase)
            {
                definition.Family = Family.Stairs;
            }
            else
            {
                definition.Family = Family.Crusher;
            }

            var baseValue = BaseOf(definition.Family);
            var value = special - baseValue;

            definition.Trigger = (Trigger)((value & TriggerType) >> TriggerTypeShift);

            switch (definition.Family)
            {
                case Family.Floor:
                    definition.Crush = (value & FloorCrush) != 0;
                    definition.Change = (value & FloorChange) >> FloorChangeShift;
                    definition.Target = (value & FloorTarget) >> FloorTargetShift;
                    definition.Up = ((value & FloorDirection) >> FloorDirectionShift) != 0;
                    definition.NumericModel = ((value & FloorModel) >> FloorModelShift) != 0;
                    definition.Speed = (Speed)((value & FloorSpeed) >> FloorSpeedShift);
                    break;

                case Family.Ceiling:
                    definition.Crush = (value & CeilingCrush) != 0;
                    definition.Change = (value & CeilingChange) >> FloorChangeShift;
                    definition.Target = (value & CeilingTarget) >> FloorTargetShift;
                    definition.Up = ((value & CeilingDirection) >> FloorDirectionShift) != 0;
                    definition.NumericModel = ((value & CeilingModel) >> FloorModelShift) != 0;
                    definition.Speed = (Speed)((value & CeilingSpeed) >> FloorSpeedShift);
                    break;

                case Family.Lift:
                    definition.Target = (value & LiftTarget) >> LiftTargetShift;
                    definition.Delay = (value & LiftDelay) >> LiftDelayShift;
                    definition.MonstersCanTrigger = (value & LiftMonster) != 0;
                    definition.Speed = (Speed)((value & LiftSpeed) >> LiftSpeedShift);
                    break;

                case Family.Stairs:
                    definition.StairIgnoresTexture = (value & StairIgnore) != 0;
                    definition.Up = ((value & StairDirection) >> StairDirectionShift) != 0;

                    // Boom's table, from EV_DoGenStairs: 0, 1, 2, 3 mean 4, 8, 16, 24 units.
                    switch ((value & StairStep) >> StairStepShift)
                    {
                        case 0: definition.StairStep = 4; break;
                        case 1: definition.StairStep = 8; break;
                        case 2: definition.StairStep = 16; break;
                        default: definition.StairStep = 24; break;
                    }

                    definition.MonstersCanTrigger = (value & 0x0020) != 0;
                    definition.Speed = (Speed)((value & StairSpeed) >> StairSpeedShift);
                    break;

                case Family.Crusher:
                    definition.Silent = ((value & CrusherSilent) >> CrusherSilentShift) != 0;
                    definition.MonstersCanTrigger = (value & 0x0020) != 0;
                    definition.Speed = (Speed)((value & CrusherSpeed) >> CrusherSpeedShift);
                    break;

                case Family.Door:
                    definition.Delay = (value & DoorDelay) >> DoorDelayShift;
                    definition.MonstersCanTrigger = (value & DoorMonster) != 0;
                    definition.DoorKind = (value & DoorKind) >> DoorKindShift;
                    definition.Speed = (Speed)((value & DoorSpeed) >> DoorSpeedShift);
                    break;

                case Family.LockedDoor:
                    definition.SkullsCountAsCards = (value & LockedNKeys) != 0;
                    definition.LockedKey = (value & LockedKey) >> LockedKeyShift;
                    definition.DoorKind = ((value & LockedKind) != 0) ? 1 : 0;
                    definition.Speed = (Speed)((value & LockedSpeed) >> LockedSpeedShift);
                    break;
            }

            return definition;
        }

        /// <summary>The base value of a family, used to strip it before decoding.</summary>
        public static int BaseOf(Family family)
        {
            switch (family)
            {
                case Family.Floor: return GenFloorBase;
                case Family.Ceiling: return GenCeilingBase;
                case Family.Door: return GenDoorBase;
                case Family.LockedDoor: return GenLockedBase;
                case Family.Lift: return GenLiftBase;
                case Family.Stairs: return GenStairsBase;
                case Family.Crusher: return GenCrusherBase;
            }

            return 0;
        }

        /// <summary>
        /// Motion speed in fixed-point units per tic, for the family's base speed
        /// and the encoded multiplier. Boom's tables, not an invention: a
        /// generalized floor at "normal" moves at twice the vanilla floor speed.
        /// </summary>
        public static Fixed SpeedOf(Family family, Speed speed)
        {
            var unit = Fixed.One;
            var multiplier = 1;

            switch (family)
            {
                case Family.Lift:
                    // Boom's platform table is PLATSPEED times 2, 4, 8, 16.
                    multiplier = 2 << (int)speed;
                    break;

                case Family.Stairs:
                    // Stairs are slower than a plain floor: 1/4, 1/2, 2, 4.
                    switch (speed)
                    {
                        case Speed.Slow: return unit / 4;
                        case Speed.Normal: return unit / 2;
                        case Speed.Fast: return unit * 2;
                        default: return unit * 4;
                    }

                case Family.Door:
                case Family.LockedDoor:
                    // VDOORSPEED times 2, 4, 8, 16.
                    return Fixed.FromInt(2 << (int)speed);

                default:
                    multiplier = 1 << (int)speed;
                    break;
            }

            return unit * multiplier;
        }

        /// <summary>True when the trigger fires when something walks over the line.</summary>
        public static bool IsWalk(Trigger trigger)
        {
            return trigger == Trigger.WalkOnce || trigger == Trigger.WalkMany;
        }

        /// <summary>True when the trigger fires when the line is shot.</summary>
        public static bool IsGun(Trigger trigger)
        {
            return trigger == Trigger.GunOnce || trigger == Trigger.GunMany;
        }

        /// <summary>True when the trigger fires when the line is used or pushed.</summary>
        public static bool IsManual(Trigger trigger)
        {
            return trigger == Trigger.SwitchOnce || trigger == Trigger.SwitchMany ||
                   trigger == Trigger.PushOnce || trigger == Trigger.PushMany;
        }

        /// <summary>True when the trigger can fire more than once.</summary>
        public static bool IsRepeatable(Trigger trigger)
        {
            return trigger == Trigger.WalkMany || trigger == Trigger.SwitchMany ||
                   trigger == Trigger.GunMany || trigger == Trigger.PushMany;
        }
    }
}
