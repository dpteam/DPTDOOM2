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
using DPTDOOM.UserInput;

namespace DPTDOOM.Win32
{
    /// <summary>
    /// Translates Win32 keyboard/mouse state into Doom tic commands. The
    /// movement rules match the original Managed Doom input handling.
    /// </summary>
    internal sealed class Win32UserInput : IUserInput
    {
        private readonly Config config;
        private readonly Win32Window window;

        private readonly bool[] weaponKeys;
        private int turnHeld;

        public Win32UserInput(Config config, Win32Window window)
        {
            this.config = config;
            this.window = window;

            weaponKeys = new bool[7];
            turnHeld = 0;
        }

        public void BuildTicCmd(TicCmd cmd)
        {
            var keyForward = IsPressed(config.key_forward);
            var keyBackward = IsPressed(config.key_backward);
            var keyStrafeLeft = IsPressed(config.key_strafeleft);
            var keyStrafeRight = IsPressed(config.key_straferight);
            var keyTurnLeft = IsPressed(config.key_turnleft);
            var keyTurnRight = IsPressed(config.key_turnright);
            var keyFire = IsPressed(config.key_fire);
            var keyUse = IsPressed(config.key_use);
            var keyRun = IsPressed(config.key_run);
            var keyStrafe = IsPressed(config.key_strafe);

            weaponKeys[0] = window.IsKeyPressed(DoomKey.Num1);
            weaponKeys[1] = window.IsKeyPressed(DoomKey.Num2);
            weaponKeys[2] = window.IsKeyPressed(DoomKey.Num3);
            weaponKeys[3] = window.IsKeyPressed(DoomKey.Num4);
            weaponKeys[4] = window.IsKeyPressed(DoomKey.Num5);
            weaponKeys[5] = window.IsKeyPressed(DoomKey.Num6);
            weaponKeys[6] = window.IsKeyPressed(DoomKey.Num7);

            cmd.Clear();

            var strafe = keyStrafe;
            var speed = keyRun ? 1 : 0;
            var forward = 0;
            var side = 0;

            if (config.game_alwaysrun)
            {
                speed = 1 - speed;
            }

            if (keyTurnLeft || keyTurnRight)
            {
                turnHeld++;
            }
            else
            {
                turnHeld = 0;
            }

            int turnSpeed;
            if (turnHeld < PlayerBehavior.SlowTurnTics)
            {
                turnSpeed = 2;
            }
            else
            {
                turnSpeed = speed;
            }

            if (strafe)
            {
                if (keyTurnRight)
                {
                    side += PlayerBehavior.SideMove[speed];
                }

                if (keyTurnLeft)
                {
                    side -= PlayerBehavior.SideMove[speed];
                }
            }
            else
            {
                if (keyTurnRight)
                {
                    cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[turnSpeed];
                }

                if (keyTurnLeft)
                {
                    cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[turnSpeed];
                }
            }

            if (keyForward)
            {
                forward += PlayerBehavior.ForwardMove[speed];
            }

            if (keyBackward)
            {
                forward -= PlayerBehavior.ForwardMove[speed];
            }

            if (keyStrafeLeft)
            {
                side -= PlayerBehavior.SideMove[speed];
            }

            if (keyStrafeRight)
            {
                side += PlayerBehavior.SideMove[speed];
            }

            if (keyFire)
            {
                cmd.Buttons |= TicCmdButtons.Attack;
            }

            if (keyUse)
            {
                cmd.Buttons |= TicCmdButtons.Use;
            }

            for (var i = 0; i < weaponKeys.Length; i++)
            {
                if (weaponKeys[i])
                {
                    cmd.Buttons |= TicCmdButtons.Change;
                    cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift);
                    break;
                }
            }

            int mouseDeltaX;
            int mouseDeltaY;
            window.ConsumeMouseDelta(out mouseDeltaX, out mouseDeltaY);

            if (config.mouse_disableyaxis)
            {
                mouseDeltaY = 0;
            }

            var ms = 0.5F * config.mouse_sensitivity;
            var mx = (int)Math.Round(ms * mouseDeltaX);
            var my = (int)Math.Round(ms * -mouseDeltaY);
            forward += my;

            if (strafe)
            {
                side += mx * 2;
            }
            else
            {
                cmd.AngleTurn -= (short)(mx * 0x8);
            }

            if (forward > PlayerBehavior.MaxMove)
            {
                forward = PlayerBehavior.MaxMove;
            }
            else if (forward < -PlayerBehavior.MaxMove)
            {
                forward = -PlayerBehavior.MaxMove;
            }

            if (side > PlayerBehavior.MaxMove)
            {
                side = PlayerBehavior.MaxMove;
            }
            else if (side < -PlayerBehavior.MaxMove)
            {
                side = -PlayerBehavior.MaxMove;
            }

            cmd.ForwardMove += (sbyte)forward;
            cmd.SideMove += (sbyte)side;
        }

        private bool IsPressed(KeyBinding keyBinding)
        {
            foreach (var key in keyBinding.Keys)
            {
                if (window.IsKeyPressed(key))
                {
                    return true;
                }
            }

            foreach (var mouseButton in keyBinding.MouseButtons)
            {
                if (window.IsMouseButtonPressed(mouseButton))
                {
                    return true;
                }
            }

            return false;
        }

        public void Reset()
        {
            int dx;
            int dy;
            window.ConsumeMouseDelta(out dx, out dy);
        }

        public void GrabMouse()
        {
            window.GrabMouse();
        }

        public void ReleaseMouse()
        {
            window.ReleaseMouse();
        }

        public int MaxMouseSensitivity => 15;

        public int MouseSensitivity
        {
            get => config.mouse_sensitivity;
            set => config.mouse_sensitivity = value;
        }
    }
}
