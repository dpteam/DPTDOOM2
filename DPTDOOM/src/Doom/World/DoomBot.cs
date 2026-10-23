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
    /// A bot that can complete a level on its own.
    ///
    /// Navigation uses <see cref="NavGraph"/>: A* over the BSP subsectors, with
    /// two-sided linedefs as portals. That gives a real route instead of the
    /// "walk toward the nearest thing" heuristic the first version used, which
    /// spun in place because the nearest thing kept changing.
    ///
    /// Behaviour is a small state machine:
    ///
    ///   - shoot what is visible and in front, aiming first;
    ///   - walk the current path, pressing Use at doors;
    ///   - if the path is blocked for too long, replan;
    ///   - when the level's exit is reachable, go there instead of exploring.
    ///
    /// It produces ordinary TicCmds, so it cannot do anything a player could
    /// not, and it is deterministic, which keeps demo recording and the network
    /// lockstep tests honest.
    /// </summary>
    public sealed class DoomBot
    {
        private const int ShootRange = 1024;
        private const int ReachDistance = 32;
        private const int DoorReachDistance = 96;
        private const int MaxStuckTics = 35;

        private readonly World world;
        private readonly Player player;
        private readonly NavGraph nav;
        private readonly int[] path;
        private readonly bool[] visitedSector;

        private int pathLength;
        private int pathIndex;
        private int noProgressTics;
        private float bestDistance = float.MaxValue;
        private int turnTics;
        private int turnDirection = 1;
        private int detourCount;


        private bool headingToExit;
        private readonly int exitNode;

        public DoomBot(World world, Player player)
        {
            this.world = world;
            this.player = player;

            nav = new NavGraph(world.Map);
            path = new int[nav.NodeCount];
            visitedSector = new bool[world.Map.Sectors.Length];


            exitNode = FindExitNode();
            PlanExplore();
        }

        /// <summary>Fills the command for one tic.</summary>
        public void BuildTicCmd(TicCmd cmd)
        {
            cmd.Clear();

            var mobj = player.Mobj;
            if (mobj == null || player.Health <= 0)
            {
                return;
            }

            var sector = mobj.Subsector.Sector.Number;
            visitedSector[sector] = true;

            SelectWeapon();

            if (Shoot(cmd, mobj))
            {
                return;
            }

            // Keep the route in step with reality. A path is a list of sectors,
            // so the only honest way to advance is to actually enter the next
            // one. Advancing when merely close to the portal let the bot skip
            // through doorways it had not crossed, after which it aimed at a
            // point behind a wall and thrashed.
            if (pathLength > 0)
            {
                if (pathIndex + 1 < pathLength && nav.GetSector(path[pathIndex + 1]) == sector)
                {
                    pathIndex++;
                    noProgressTics = 0;
                    bestDistance = float.MaxValue;
                }
                else if (nav.GetSector(path[pathIndex]) != sector)
                {
                    // Somewhere the route did not expect: start over.
                    pathLength = 0;
                }
            }

            if (pathLength == 0 || pathIndex + 1 >= pathLength)
            {
                PlanExplore();
                if (pathLength == 0 || pathIndex + 1 >= pathLength)
                {
                    return;
                }
            }

            float targetX;
            float targetY;
            LineDef portal;

            if (!nav.FindPortal(
                path[pathIndex], path[pathIndex + 1],
                mobj.X.ToFloat(), mobj.Y.ToFloat(),
                out targetX, out targetY, out portal))
            {
                pathLength = 0;
                return;
            }

            var dx = targetX - mobj.X.ToFloat();
            var dy = targetY - mobj.Y.ToFloat();
            var distance = Math.Abs(dx) + Math.Abs(dy);

            if (portal != null && IsDoor(portal))
            {
                // Doors open on Use from the side you face. Press it every tic
                // while walking in: one press can be wasted on the frame before
                // the engine registers contact, and a door still rising blocks
                // movement.
                cmd.Buttons |= TicCmdButtons.Use;
            }

            // Progress is measured against the best distance achieved so far,
            // not against raw movement: a bot that oscillates still "moves", so
            // a movement test never fires and the bot grinds forever.
            if (distance < bestDistance - 4)
            {
                bestDistance = distance;
                noProgressTics = 0;
            }
            else
            {
                noProgressTics++;
            }

            Steer(cmd, mobj, targetX, targetY);

            if (noProgressTics > MaxStuckTics)
            {
                // Blocked for over a second on the way to a portal. Rather than
                // abandoning the whole route (which made the bot repath to the
                // same place and repeat), nudge it sideways to get around
                // whatever it is snagged on.
                noProgressTics = 0;
                bestDistance = float.MaxValue;

                if (++detourCount > 3)
                {
                    detourCount = 0;
                    AbandonPath();
                }
            }
        }

        private void AbandonPath()
        {
            if (pathLength > 0 && !headingToExit)
            {
                visitedSector[nav.GetSector(path[pathLength - 1])] = true;
            }

            pathLength = 0;
            pathIndex = 0;
        }

        ////////////////////////////////////////////////////////////
        // Steering
        ////////////////////////////////////////////////////////////

        private void Steer(TicCmd cmd, Mobj mobj, float targetX, float targetY)
        {
            var angle = Geometry.PointToAngle(
                mobj.X, mobj.Y,
                Fixed.FromDouble(targetX), Fixed.FromDouble(targetY));

            // Angle counts 1/2^32 of a turn; TicCmd.AngleTurn counts 1/65536.
            // Comparing the two without converting is what made the first
            // version of this bot rotate forever.
            var delta = (angle - mobj.Angle).Data >> 16;

            var limit = noProgressTics > 6 ? 0x1000 : 0x0800;

            if (delta > limit)
            {
                cmd.AngleTurn = (short)limit;
            }
            else if (delta < -limit)
            {
                cmd.AngleTurn = (short)-limit;
            }
            else
            {
                cmd.AngleTurn = (short)delta;
            }

            if (Math.Abs(delta) < 0x0800)
            {
                cmd.ForwardMove = 50;
            }

            if (noProgressTics > 6)
            {
                // Slide along the obstacle; alternating direction every tic
                // would only shake in place, so a direction is held for a while.
                cmd.SideMove = (sbyte)(turnDirection * 40);
                cmd.ForwardMove = 40;
                cmd.Buttons |= TicCmdButtons.Use;

                if (noProgressTics > MaxStuckTics - 8)
                {
                    turnDirection = -turnDirection;
                }
            }
        }

        ////////////////////////////////////////////////////////////
        // Planning
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Routes to the level exit when one is reachable, otherwise to the
        /// nearest sector that has not been entered yet.
        /// </summary>
        private void PlanExplore()
        {
            pathLength = 0;
            pathIndex = 0;
            headingToExit = false;

            var mobj = player.Mobj;
            var start = nav.NodeAt(mobj.X, mobj.Y);
            if (start < 0)
            {
                return;
            }

            if (exitNode >= 0)
            {
                var length = nav.FindPath(start, exitNode, path);
                if (length > 0)
                {
                    pathLength = length;
                    headingToExit = true;
                    return;
                }
            }

            // Nearest unvisited sector, by straight line first, then verified
            // with A*. Straight-line order is a heuristic; the path is the
            // truth. A handful of attempts is enough to find something
            // reachable without turning this into a quadratic search.
            for (var attempt = 0; attempt < 24; attempt++)
            {
                var target = FindNearestUnvisitedNode(start);
                if (target < 0)
                {
                    return;
                }

                var length = nav.FindPath(start, target, path);
                if (length > 0)
                {
                    pathLength = length;
                    return;
                }

                // Unreachable: mark the whole sector so it is not picked again.
                visitedSector[nav.GetSector(target)] = true;
            }
        }

        private int FindNearestUnvisitedNode(int start)
        {
            var best = -1;
            var bestDistance = double.MaxValue;

            for (var node = 0; node < nav.NodeCount; node++)
            {
                var sector = nav.GetSector(node);
                if (visitedSector[sector])
                {
                    continue;
                }

                var dx = nav.GetX(node) - nav.GetX(start);
                var dy = nav.GetY(node) - nav.GetY(start);
                var distance = dx * dx + dy * dy;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = node;
                }
            }

            return best;
        }

        /// <summary>
        /// Node whose sector contains a line that ends the level. Exit lines are
        /// switches and walkovers; either way the bot has to stand in front of
        /// one, so the sector is what matters.
        /// </summary>
        private int FindExitNode()
        {
            for (var node = 0; node < nav.NodeCount; node++)
            {
                var sector = world.Map.Sectors[nav.GetSector(node)];

                foreach (var line in sector.Lines)
                {
                    if (IsExit(line))
                    {
                        return node;
                    }
                }
            }

            return -1;
        }

        ////////////////////////////////////////////////////////////
        // Combat
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Switches to the best weapon that still has ammo.
        ///
        /// Without this the bot keeps an empty pistol selected, cannot kill the
        /// monster blocking the doorway, and stands there forever: a stuck bot
        /// that looks exactly like a hung program.
        /// </summary>
        private void SelectWeapon()
        {
            var current = player.ReadyWeapon;
            if (current == WeaponType.Fist || current == WeaponType.Chainsaw)
            {
                return;
            }

            var info = DoomInfo.WeaponInfos[(int)current];
            if (info.Ammo == AmmoType.NoAmmo || player.Ammo[(int)info.Ammo] > 0)
            {
                return;
            }

            // Preference order, best first.
            var order = new[]
            {
                WeaponType.Bfg,
                WeaponType.Plasma,
                WeaponType.Chaingun,
                WeaponType.SuperShotgun,
                WeaponType.Shotgun,
                WeaponType.Pistol,
                WeaponType.Chainsaw,
                WeaponType.Fist
            };

            foreach (var weapon in order)
            {
                if (!player.WeaponOwned[(int)weapon])
                {
                    continue;
                }

                var candidate = DoomInfo.WeaponInfos[(int)weapon];

                if (candidate.Ammo == AmmoType.NoAmmo ||
                    player.Ammo[(int)candidate.Ammo] > 0)
                {
                    player.PendingWeapon = weapon;
                    return;
                }
            }
        }

        /// <summary>
        /// Shoots the closest monster that is visible, in front, and affordable.
        /// </summary>
        private bool Shoot(TicCmd cmd, Mobj self)
        {
            var weapon = player.ReadyWeapon;

            if (weapon == WeaponType.Fist || weapon == WeaponType.Chainsaw)
            {
                return ShootMelee(cmd, self);
            }

            var info = DoomInfo.WeaponInfos[(int)weapon];
            if (info.Ammo != AmmoType.NoAmmo && player.Ammo[(int)info.Ammo] <= 0)
            {
                // Out of ammo: walk instead of standing there clicking.
                return false;
            }

            var best = (Mobj)null;
            var bestDistance = ShootRange;

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;
                if (mobj == null || mobj == self || mobj.Health <= 0)
                {
                    continue;
                }

                if ((mobj.Flags & MobjFlags.Shootable) == 0 ||
                    (mobj.Flags & MobjFlags.CountKill) == 0)
                {
                    continue;
                }

                var dx = mobj.X - self.X;
                var dy = mobj.Y - self.Y;
                var distance = (int)(Math.Abs(dx.ToFloat()) + Math.Abs(dy.ToFloat()));

                if (distance >= bestDistance)
                {
                    continue;
                }

                var angle = Geometry.PointToAngle(self.X, self.Y, mobj.X, mobj.Y);
                if (Math.Abs((angle - self.Angle).Data) > 0x10000000)
                {
                    continue;
                }

                if (!world.VisibilityCheck.CheckSight(self, mobj))
                {
                    continue;
                }

                best = mobj;
                bestDistance = distance;
            }

            if (best == null)
            {
                return false;
            }

            // Aim before firing: shooting straight ahead only works when the
            // target happens to be exactly on the current heading.
            var toTarget = Geometry.PointToAngle(self.X, self.Y, best.X, best.Y);
            var error = (toTarget - self.Angle).Data >> 16;

            if (Math.Abs(error) > 0x0400)
            {
                cmd.AngleTurn = (short)MathEx.Clamp(error, -0x1000, 0x1000);
                return true;
            }

            cmd.Buttons |= TicCmdButtons.Attack;
            return true;
        }

        private bool ShootMelee(TicCmd cmd, Mobj self)
        {
            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;
                if (mobj == null || mobj == self || mobj.Health <= 0)
                {
                    continue;
                }

                if ((mobj.Flags & MobjFlags.Shootable) == 0 ||
                    (mobj.Flags & MobjFlags.CountKill) == 0)
                {
                    continue;
                }

                var dx = mobj.X - self.X;
                var dy = mobj.Y - self.Y;

                if (Math.Abs(dx.ToFloat()) + Math.Abs(dy.ToFloat()) > 80)
                {
                    continue;
                }

                cmd.Buttons |= TicCmdButtons.Attack;
                return true;
            }

            return false;
        }

        ////////////////////////////////////////////////////////////
        // Level knowledge
        ////////////////////////////////////////////////////////////

        private static int Special(LineDef line)
        {
            return (int)line.Special;
        }

        /// <summary>Lines that raise a door when used or crossed.</summary>
        private static bool IsDoor(LineDef line)
        {
            switch (Special(line))
            {
                case 1:   // DR door, opens and closes
                case 26:  // DR door, blue key
                case 27:  // DR door, yellow key
                case 28:  // DR door, red key
                case 31:  // D1 door
                case 32:  // D1 door, blue key
                case 33:  // D1 door, yellow key
                case 34:  // D1 door, red key
                case 117: // DR door, fast
                case 118: // D1 door, fast
                    return true;
            }

            return false;
        }

        /// <summary>Lines that end the level.</summary>
        private static bool IsExit(LineDef line)
        {
            switch (Special(line))
            {
                case 11:  // S1 exit
                case 51:  // S1 secret exit
                case 52:  // W1 exit
                case 124: // W1 secret exit
                    return true;
            }

            return false;
        }

        ////////////////////////////////////////////////////////////
        // Diagnostics
        ////////////////////////////////////////////////////////////

        /// <summary>Fraction of sectors entered, 0..1.</summary>
        public double Coverage
        {
            get
            {
                var total = 0;
                var done = 0;

                for (var i = 0; i < visitedSector.Length; i++)
                {
                    total++;
                    if (visitedSector[i])
                    {
                        done++;
                    }
                }

                return total == 0 ? 0 : (double)done / total;
            }
        }

        public int VisitedSectors
        {
            get
            {
                var count = 0;
                for (var i = 0; i < visitedSector.Length; i++)
                {
                    if (visitedSector[i])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int TotalSectors { get { return visitedSector.Length; } }
        public int PathLength { get { return pathLength; } }
        public bool HeadingToExit { get { return headingToExit; } }
        public NavGraph Nav { get { return nav; } }
        public int ExitNode { get { return exitNode; } }

        /// <summary>One line describing what the bot is doing, for diagnostics.</summary>
        public string Status
        {
            get
            {
                if (pathLength == 0)
                {
                    return "idle";
                }

                if (pathIndex >= pathLength)
                {
                    return "arrived";
                }

                var sector = nav.GetSector(path[pathIndex]);

                if (pathIndex + 1 < pathLength)
                {
                    float x;
                    float y;
                    LineDef line;
                    nav.FindPortal(path[pathIndex], path[pathIndex + 1], 0, 0, out x, out y, out line);

                    var special = line == null ? 0 : (int)line.Special;
                    return "to node " + path[pathIndex + 1] + " sector " + sector +
                           " via special " + special + " at " + x.ToString("0") + "," + y.ToString("0") +
                           (headingToExit ? " (exit)" : "");
                }

                return "to node " + path[pathIndex] + " sector " + sector;
            }
        }
    }
}
