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
        private const int MaxStuckTics = 35;

        /// <summary>How close the bot has to be to press an exit switch.</summary>
        private const int ExitReachDistance = 96;

        /// <summary>
        /// A sweeper leaves only after seeing this much of the map. Below one,
        /// because a few sectors are sealed behind keys or secrets and waiting
        /// for a hundred percent would mean never leaving.
        /// </summary>
        private const double SweeperCoverage = 0.85;

        /// <summary>
        /// ...and only for this long. A sweeper that gets itself stuck in a
        /// corner of some far room should still leave the level instead of
        /// clearing the same three rooms until the tic counter runs out.
        /// </summary>
        private const int SweeperTimeLimit = 35 * 90;

        /// <summary>Below this health a cautious bot backs off instead of advancing.</summary>
        private const int CautiousRetreatHealth = 70;

        /// <summary>How close a monster has to be to make a cautious bot retreat.</summary>
        private const int CautiousThreatRange = 256;

        /// <summary>
        /// How close a monster has to be to count as "the thing blocking the
        /// way" when the bot has stopped making progress.
        /// </summary>
        private const int BlockerRange = 160;

        private readonly World world;
        private readonly Player player;
        private readonly NavGraph nav;
        private readonly int[] path;
        private readonly bool[] visitedSector;

        // Sectors the bot failed to route to. Kept apart from visitedSector so a
        // failed plan cannot be mistaken for progress.
        private readonly bool[] triedSector;

        private readonly BotBrain brain;
        private readonly BotStyle style;

        private int pathLength;
        private int pathIndex;
        private int noProgressTics;
        private float bestDistance = float.MaxValue;

        private int detourCount;

        // A target that will not die is a target worth ignoring for a while: a
        // hunter can otherwise stand in front of a window shooting at something
        // it can never reach, for the rest of the level.
        private Mobj engagedTarget;
        private int engagedTics;
        private Mobj ignoredTarget;
        private int ignoreUntil;

        /// <summary>Tics before an unkillable target is dropped for a while.</summary>
        private const int GiveUpTics = 175;

        // The engine treats Use as an edge: holding it counts as one press.
        // Pressing every tic therefore gave exactly one attempt, usually while
        // the bot was still walking up to the switch, and then nothing forever.
        // A short pulse keeps giving it new chances.
        private int useTic;

        private bool WantUse
        {
            get { return useTic % 4 == 0; }
        }

        private bool headingToExit;
        private int exitNode;

        // Target of the current leg, fixed until the bot enters the next sector.
        private float legTargetX;
        private float legTargetY;
        private bool legIsDoor;

        // The remembered route from the brain is tried once per run.
        private bool hintUsed;

        // Direct steering used when the graph offers no route at all.
        private bool wanderActive;
        private float wanderX;
        private float wanderY;
        private int wanderTics;

        // Where the bot has to stand to end the level, and the sector that
        // contains the exit line.
        private float exitX;
        private float exitY;
        private int exitSector = -1;

        // Route that led to the exit, kept so a successful run can be handed to
        // the brain as a route worth repeating.
        private int[] routeToExit = new int[0];
        private int routeToExitLength;
        private bool routeCaptured;

        /// <summary>
        /// Tic on which this bot pressed the exit, or -1 while it has not. A run
        /// only counts as the bot's own if this is set: otherwise the level was
        /// ended by somebody else, usually the human player, and the bot's time
        /// says nothing about the bot.
        /// </summary>
        public int FinishedTics { get; private set; }

        public DoomBot(World world, Player player) : this(world, player, null, null, BotStyle.Sweeper)
        {
        }

        /// <summary>
        /// Bots in the same game share one navigation graph: it depends only on
        /// the map, and rebuilding it per bot would waste memory and load time
        /// for no benefit. They also share one brain, so what one bot learns on
        /// a map the others know too.
        /// </summary>
        public DoomBot(World world, Player player, NavGraph sharedNav)
            : this(world, player, sharedNav, null, BotStyle.Sweeper)
        {
        }

        public DoomBot(World world, Player player, NavGraph sharedNav, BotBrain sharedBrain, BotStyle style)
        {
            this.world = world;
            this.player = player;
            this.style = style;

            brain = sharedBrain;
            nav = sharedNav ?? new NavGraph(world.Map);
            path = new int[nav.NodeCount];
            visitedSector = new bool[world.Map.Sectors.Length];
            triedSector = new bool[world.Map.Sectors.Length];

            FinishedTics = -1;

            FindExit();
            PlanExplore();
        }

        public BotStyle Style { get { return style; } }

        /// <summary>Health of the bot's own player, for reporting a run.</summary>
        public int Health { get { return player.Health; } }

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

            useTic++;

            SelectWeapon();

            // A cautious bot gives ground when it is hurt instead of trading
            // blows. It keeps facing the threat while backing away, so it can
            // still shoot: turning tail would only get it shot in the back.
            //
            // This has to come **before** shooting. Shooting returns as soon as
            // it has a target, so a retreat check placed after it never ran
            // while a monster was visible — which is exactly when a cautious bot
            // needs it. The style used to stand and duel until it died.
            if (style == BotStyle.Cautious && player.Health < CautiousRetreatHealth)
            {
                var threat = NearestThreat(mobj, CautiousThreatRange);

                if (threat != null && HasRoomToRetreat(mobj, threat))
                {
                    var toThreat = Geometry.PointToAngle(mobj.X, mobj.Y, threat.X, threat.Y);
                    var facing = (toThreat - mobj.Angle).Data >> 16;

                    cmd.AngleTurn = (short)MathEx.Clamp(facing, -0x1000, 0x1000);
                    cmd.ForwardMove = -25;
                    cmd.SideMove = 0;
                    cmd.Buttons |= TicCmdButtons.Attack;

                    return;
                }
            }

            if (Shoot(cmd, mobj))
            {
                return;
            }

            if (wanderActive)
            {
                // No route exists from here. Walk at the goal for a moment and
                // let the ordinary steering slide along whatever it meets; often
                // one waypoint is enough for a plan to exist again.
                Steer(cmd, mobj, wanderX, wanderY);

                if (++wanderTics > 70)
                {
                    wanderActive = false;
                    wanderTics = 0;
                }

                return;
            }

            // Exit: walk up to the switch and press it.
            //
            // Finding the exit sector is not enough. The exit is a switch line,
            // and a switch has to be used from the front while standing in front
            // of it. The bot used to reach the exit room, see no more nodes to
            // visit and just stand there.
            if (headingToExit && exitSector >= 0 && sector == exitSector &&
                PressExit(cmd, mobj))
            {
                return;
            }

            // A route assumes the bot is in the sector of the node it is heading
            // for, or of the one it just left. Anything else means the world
            // moved it somewhere the plan did not expect — a drop, a lift, a
            // teleport — and continuing to walk at a waypoint two rooms away is
            // how the bot used to spend the rest of the level in a corner.
            if (pathLength > 0 && pathIndex < pathLength)
            {
                var targetSector = nav.GetSector(path[pathIndex]);
                var previousSector = pathIndex > 0 ? nav.GetSector(path[pathIndex - 1]) : targetSector;

                if (sector != targetSector && sector != previousSector)
                {
                    AbandonPath();
                }
            }

            // Advance along the path. A nav node is a point, so the honest test
            // is reaching that point, not entering some sector.
            while (pathLength > 0 && pathIndex < pathLength)
            {
                var nx = nav.GetX(path[pathIndex]) - mobj.X.ToFloat();
                var ny = nav.GetY(path[pathIndex]) - mobj.Y.ToFloat();

                if (Math.Abs(nx) + Math.Abs(ny) > ReachDistance)
                {
                    break;
                }

                pathIndex++;
                noProgressTics = 0;
                bestDistance = float.MaxValue;
            }

            if (pathLength == 0 || pathIndex >= pathLength)
            {
                PlanExplore();

                if (pathLength == 0 || pathIndex >= pathLength)
                {
                    // No route at all: the bot is on a node the graph cannot
                    // leave, which in practice means it slid off a ledge into a
                    // pit, or ended up in a corner with no plan. Walking at the
                    // nearest point it can actually see beats standing still,
                    // and often the next node is enough to find a route again.
                    if (!WanderTowards(exitX, exitY))
                    {
                        return;
                    }
                }
            }

            legTargetX = nav.GetX(path[pathIndex]);
            legTargetY = nav.GetY(path[pathIndex]);
            legIsDoor = nav.IsDoor(path[pathIndex]);

            // A waypoint can be reachable in the graph and still be hidden
            // behind the corner of a concave sector. Walking straight at it then
            // means grinding along the wall forever, which is exactly what the
            // bot used to do in the L-shaped corridor before the exit.
            //
            // When that happens, aim at the nearer point of the same sector that
            // is visible from here and lies in the right direction. The path is
            // untouched: this is only the point to walk at for this tic.
            var hereX = mobj.X.ToFloat();
            var hereY = mobj.Y.ToFloat();

            if (nav.GetSector(path[pathIndex]) == sector &&
                !nav.LineClear(hereX, hereY, legTargetX, legTargetY, sector))
            {
                var step = nav.VisibleNodeTowards(sector, hereX, hereY, legTargetX, legTargetY);

                if (step >= 0)
                {
                    legTargetX = nav.GetX(step);
                    legTargetY = nav.GetY(step);
                    legIsDoor = nav.IsDoor(step);
                }
            }

            var dx = legTargetX - mobj.X.ToFloat();
            var dy = legTargetY - mobj.Y.ToFloat();
            var distance = Math.Abs(dx) + Math.Abs(dy);

            if (legIsDoor)
            {
                // Doors open on Use from the side you face. Press it every tic
                // while walking in: one press can be wasted on the frame before
                // the engine registers contact, and a door still rising blocks
                // movement.
                if (WantUse)
                {
                    cmd.Buttons |= TicCmdButtons.Use;
                }
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

            Steer(cmd, mobj, legTargetX, legTargetY);

            // Something is in the way and the bot is not moving. If it is a
            // monster, shoot it: standing against a demon and pushing is how the
            // bot lost most of its health while making no progress. Shoot()
            // above only fires at targets already in front of the bot, and a
            // monster that wandered into its path from the side never qualifies,
            // so the bot pushed at it for the rest of the level.
            if (noProgressTics > 8 && noProgressTics <= MaxStuckTics)
            {
                var blocker = NearestVisibleThreat(mobj, BlockerRange);

                if (blocker != null)
                {
                    var toBlocker = Geometry.PointToAngle(mobj.X, mobj.Y, blocker.X, blocker.Y);
                    var facing = (toBlocker - mobj.Angle).Data >> 16;

                    cmd.AngleTurn = (short)MathEx.Clamp(facing, -0x1000, 0x1000);
                    cmd.ForwardMove = 0;
                    cmd.SideMove = 0;

                    if (Math.Abs(facing) < 0x0800)
                    {
                        cmd.Buttons |= TicCmdButtons.Attack;
                    }

                    return;
                }
            }

            if (noProgressTics > MaxStuckTics)
            {
                // Blocked on this waypoint for over a second. Rather than
                // reversing and strafing — which is what turned a briefly stuck
                // bot into one tracing circles around the same corner — make the
                // waypoint expensive and plan again. The new route goes around
                // it if there is any way around, and still through it if there
                // is not.
                nav.Penalize(path[pathIndex]);

                noProgressTics = 0;
                bestDistance = float.MaxValue;

                if (++detourCount > 8)
                {
                    detourCount = 0;
                    AbandonPath();
                }

                pathLength = 0;
            }
        }

        /// <summary>
        /// Uses the exit switch if the bot is standing in front of it. Returns
        /// true when the bot stopped to press it.
        /// </summary>
        private bool PressExit(TicCmd cmd, Mobj mobj)
        {
            var dx = exitX - mobj.X.ToFloat();
            var dy = exitY - mobj.Y.ToFloat();

            if (Math.Abs(dx) + Math.Abs(dy) > ExitReachDistance)
            {
                return false;
            }

            Steer(cmd, mobj, exitX, exitY);

            // Press every tic while in reach. A switch only works from the front
            // and only once the engine has registered the player as touching it,
            // and one wasted press is cheaper than a bot that stands at an open
            // exit doing nothing.
            if (WantUse)
            {
                cmd.Buttons |= TicCmdButtons.Use;
            }

            if (FinishedTics < 0)
            {
                FinishedTics = world.LevelTime;
            }

            return true;
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
            var relative = (angle - mobj.Angle).Data >> 16;

            var limit = noProgressTics > 6 ? 0x1000 : 0x0800;

            if (relative > limit)
            {
                cmd.AngleTurn = (short)limit;
            }
            else if (relative < -limit)
            {
                cmd.AngleTurn = (short)-limit;
            }
            else
            {
                cmd.AngleTurn = (short)relative;
            }

            // Move toward the target regardless of where the bot is looking.
            //
            // The earlier version only walked when the heading was within about
            // 34 degrees of the target, and otherwise stood still and turned.
            // Waypoints are close together, so the heading changed constantly
            // and the bot spent most of the level pivoting on the spot — which
            // is exactly what "the bots spin in circles" looks like from
            // outside.
            //
            // A player does not do that: they hold forward and strafe. Splitting
            // the direction into forward and sideways components gives the same
            // motion without stopping, and the turn still happens in parallel so
            // the bot ends up facing where it walks.
            //
            // The engine thrusts forward along the body angle and sideways along
            // angle - 90 (see PlayerBehavior), so:
            //   forward = cos(relative),  side = -sin(relative)
            // with the side axis flipped because a positive SideMove is right and
            // a right-hand target has a negative relative angle.
            var radians = relative * (Math.PI * 2.0 / 65536.0);

            var forward = (int)Math.Round(Math.Cos(radians) * 50.0);
            var side = (int)Math.Round(-Math.Sin(radians) * 40.0);

            // Target well behind: turn and strafe, do not walk backwards. Moving
            // in reverse is legal but reads as a bug, and it is slower than
            // turning. Past a quarter turn the forward component is dropped and
            // the side component does the work while the heading comes round.
            if (Math.Abs(relative) > 0x4000)
            {
                forward = 0;
            }

            cmd.ForwardMove = (sbyte)MathEx.Clamp(forward, -50, 50);
            cmd.SideMove = (sbyte)MathEx.Clamp(side, -40, 40);

            // Blocked for a while: keep pressing forward and use. Doors need
            // contact to open, and a doorway is the usual reason for being
            // blocked. Reversing and strafing here, as the first version did,
            // is what turned a bot that was briefly stuck into one that traced
            // circles around the same corner for the rest of the level.
            if (noProgressTics > 8)
            {
                if (WantUse)
                {
                    cmd.Buttons |= TicCmdButtons.Use;
                }

                cmd.ForwardMove = 30;
                cmd.SideMove = 0;
            }
        }

        ////////////////////////////////////////////////////////////
        // Planning
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Picks the next route: the remembered one, the exit, or unexplored
        /// ground — depending on the style.
        /// </summary>
        private void PlanExplore()
        {
            pathLength = 0;
            pathIndex = 0;
            headingToExit = false;

            var mobj = player.Mobj;
            if (mobj == null)
            {
                return;
            }

            var start = nav.NodeAt(mobj.X, mobj.Y);
            if (start < 0)
            {
                return;
            }

            // First order of business in a run: replay the route that worked
            // last time. This is the "map of previous runs" the bot keeps, and
            // it is what makes a second attempt on the same map quicker than
            // the first.
            if (!hintUsed)
            {
                hintUsed = true;

                if (AdoptHint())
                {
                    return;
                }
            }

            // A sweeper clears the level before leaving; everyone else heads for
            // the exit straight away. That difference is the whole point of
            // having styles: they cost different amounts of time and risk, and
            // the brain finds out which trade the map rewards.
            var exploreFirst = style == BotStyle.Sweeper &&
                               Coverage < SweeperCoverage &&
                               world.LevelTime < SweeperTimeLimit;

            if (!exploreFirst && exitNode >= 0)
            {
                var length = nav.FindPath(start, exitNode, path);
                if (length > 0)
                {
                    pathLength = length;
                    headingToExit = true;
                    CaptureRoute();
                    return;
                }
            }

            // Nearest unvisited sector, by straight line first, then verified
            // with a real search. Straight-line order is only a guess; the path
            // is the truth. A handful of attempts is enough to find something
            // reachable without turning this into a quadratic search.
            for (var attempt = 0; attempt < 24; attempt++)
            {
                var target = FindNearestUnvisitedNode(start);
                if (target < 0)
                {
                    break;
                }

                var length = nav.FindPath(start, target, path);
                if (length > 0)
                {
                    pathLength = length;
                    return;
                }

                // Unreachable: remember not to pick it again. This is a separate
                // flag from "visited" on purpose. Marking it visited used to
                // destroy the bot's map knowledge: after a few failed plans it
                // believed it had seen every sector, stopped exploring, and then
                // had nothing left to do at all.
                triedSector[nav.GetSector(target)] = true;
            }

            // Everything has been seen, so a sweeper may now leave.
            if (exitNode >= 0)
            {
                var length = nav.FindPath(start, exitNode, path);
                if (length > 0)
                {
                    pathLength = length;
                    headingToExit = true;
                    CaptureRoute();
                }
            }
        }

        /// <summary>
        /// Last resort when the graph has no route from where the bot stands:
        /// walk at something it can see, or straight at the goal for a while.
        /// </summary>
        private bool WanderTowards(float goalX, float goalY)
        {
            var mobj = player.Mobj;
            if (mobj == null)
            {
                return false;
            }

            var hereX = mobj.X.ToFloat();
            var hereY = mobj.Y.ToFloat();
            var sector = mobj.Subsector.Sector.Number;

            var step = nav.VisibleNodeTowards(sector, hereX, hereY, goalX, goalY);

            if (step >= 0)
            {
                var dx = nav.GetX(step) - hereX;
                var dy = nav.GetY(step) - hereY;

                if (Math.Abs(dx) + Math.Abs(dy) > ReachDistance)
                {
                    path[0] = step;
                    pathLength = 1;
                    pathIndex = 0;
                    headingToExit = false;
                    return true;
                }
            }

            // Nothing worth walking to inside this sector. Aim straight at the
            // goal for a second or so and let the ordinary steering slide along
            // whatever it bumps into.
            wanderActive = true;
            wanderX = goalX;
            wanderY = goalY;
            wanderTics = 0;

            return true;
        }

        /// <summary>
        /// Copies the route that worked from the brain, if there is one.
        ///
        /// The node indices are only valid for the same map: the mesh is built
        /// from the map data in a fixed order, so the same map always produces
        /// the same numbering, and the brain file is keyed by a fingerprint of
        /// the map. Anything out of range means the file belongs elsewhere, and
        /// the hint is dropped rather than trusted.
        /// </summary>
        private bool AdoptHint()
        {
            if (brain == null)
            {
                return false;
            }

            var hint = brain.BestRoute;

            if (hint.Length < 2)
            {
                return false;
            }

            for (var i = 0; i < hint.Length; i++)
            {
                if (hint[i] < 0 || hint[i] >= nav.NodeCount)
                {
                    return false;
                }
            }

            // The route has to start where the bot is standing now. A start
            // point that has moved means the route belongs to a different
            // arrangement of the map, and following it would walk the bot
            // across the level to reach its first waypoint.
            var mobj = player.Mobj;

            if (mobj != null)
            {
                var dx = nav.GetX(hint[0]) - mobj.X.ToFloat();
                var dy = nav.GetY(hint[0]) - mobj.Y.ToFloat();

                if (Math.Abs(dx) + Math.Abs(dy) > 512F)
                {
                    return false;
                }
            }

            var length = Math.Min(hint.Length, path.Length);
            Array.Copy(hint, path, length);

            pathLength = length;
            pathIndex = 0;
            headingToExit = true;

            return true;
        }

        /// <summary>
        /// Keeps the first plan that headed for the exit.
        ///
        /// Only the first one: a plan made after the bot got stuck somewhere
        /// halfway across the map starts halfway across the map, and replaying
        /// that from the spawn would walk the bot backwards. The first plan
        /// starts where the bot starts, which is what makes it replayable.
        /// </summary>
        private void CaptureRoute()
        {
            if (routeCaptured || pathLength <= 0)
            {
                return;
            }

            routeCaptured = true;

            if (routeToExit.Length < pathLength)
            {
                routeToExit = new int[pathLength];
            }

            Array.Copy(path, routeToExit, pathLength);
            routeToExitLength = pathLength;
        }

        private int FindNearestUnvisitedNode(int start)
        {
            var best = -1;
            var bestDistance = double.MaxValue;

            for (var node = 0; node < nav.NodeCount; node++)
            {
                var sector = nav.GetSector(node);
                if (visitedSector[sector] || triedSector[sector])
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
        /// Finds the level exit and works out where the bot has to stand to use
        /// it: in front of the exit line, inside the sector the line belongs to.
        ///
        /// The node alone is not enough. A node is a waypoint somewhere in the
        /// exit sector, and a switch has to be pressed from touching distance;
        /// a bot that stops at the waypoint can be ten metres from the switch
        /// and press Use at a wall forever.
        /// </summary>
        private void FindExit()
        {
            exitNode = -1;

            for (var s = 0; s < world.Map.Sectors.Length; s++)
            {
                var sector = world.Map.Sectors[s];

                foreach (var line in sector.Lines)
                {
                    if (!IsExit(line))
                    {
                        continue;
                    }

                    var midX = (line.Vertex1.X.ToFloat() + line.Vertex2.X.ToFloat()) * 0.5F;
                    var midY = (line.Vertex1.Y.ToFloat() + line.Vertex2.Y.ToFloat()) * 0.5F;

                    exitX = midX;
                    exitY = midY;
                    exitSector = s;

                    // Step back from the wall into the room, so the target is a
                    // place a 32-unit-wide body can actually stand.
                    float inwardX;
                    float inwardY;

                    if (InwardPoint(sector, midX, midY, out inwardX, out inwardY))
                    {
                        exitX = inwardX;
                        exitY = inwardY;
                    }

                    exitNode = NearestNode(exitX, exitY, s);

                    return;
                }
            }
        }

        /// <summary>Point a little way inside a sector, from a position on its edge.</summary>
        private bool InwardPoint(Sector sector, float x, float y, out float resultX, out float resultY)
        {
            resultX = 0;
            resultY = 0;

            var centreX = 0.0F;
            var centreY = 0.0F;
            var count = 0;

            foreach (var line in sector.Lines)
            {
                centreX += (line.Vertex1.X.ToFloat() + line.Vertex2.X.ToFloat()) * 0.5F;
                centreY += (line.Vertex1.Y.ToFloat() + line.Vertex2.Y.ToFloat()) * 0.5F;
                count++;
            }

            if (count == 0)
            {
                return false;
            }

            centreX /= count;
            centreY /= count;

            var dx = centreX - x;
            var dy = centreY - y;
            var length = (float)Math.Sqrt(dx * dx + dy * dy);

            if (length < 0.001F)
            {
                return false;
            }

            // Forty units clear of the wall: outside the player's radius, so
            // walking there does not rub along the wall, and close enough that
            // the switch is still within reach.
            const float Inset = 40F;

            var px = x + dx / length * Inset;
            var py = y + dy / length * Inset;

            var subsector = Geometry.PointInSubsector(
                Fixed.FromDouble(px), Fixed.FromDouble(py), world.Map);

            if (subsector.Sector.Number != sector.Number)
            {
                return false;
            }

            resultX = px;
            resultY = py;

            return true;
        }

        /// <summary>Closest navigation node to a position, preferring one sector.</summary>
        private int NearestNode(float x, float y, int sector)
        {
            var best = -1;
            var bestDistance = float.MaxValue;

            for (var node = 0; node < nav.NodeCount; node++)
            {
                if (sector >= 0 && nav.GetSector(node) != sector)
                {
                    continue;
                }

                var dx = nav.GetX(node) - x;
                var dy = nav.GetY(node) - y;
                var distance = dx * dx + dy * dy;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = node;
                }
            }

            return best;
        }

        ////////////////////////////////////////////////////////////
        // Combat
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// How far away this style is willing to engage.
        ///
        /// A hunter shoots at anything it can see; a cautious bot only takes
        /// fights it cannot walk around. That is the difference between the two
        /// styles that both go straight for the exit.
        /// </summary>
        private int EngagementRange
        {
            get
            {
                switch (style)
                {
                    case BotStyle.Rush:
                        return 384;

                    case BotStyle.Cautious:
                        return 320;

                    case BotStyle.Sweeper:
                        return 512;

                    default:
                        return ShootRange;
                }
            }
        }

        /// <summary>Whether there is clear space behind the bot to step back into.</summary>
        private bool HasRoomToRetreat(Mobj mobj, Mobj threat)
        {
            var ax = mobj.X.ToFloat() - threat.X.ToFloat();
            var ay = mobj.Y.ToFloat() - threat.Y.ToFloat();
            var length = (float)Math.Sqrt(ax * ax + ay * ay);

            if (length < 1F)
            {
                return false;
            }

            var hereX = mobj.X.ToFloat();
            var hereY = mobj.Y.ToFloat();
            var sector = mobj.Subsector.Sector.Number;

            return nav.LineClear(
                hereX, hereY,
                hereX + ax / length * 96F,
                hereY + ay / length * 96F,
                sector);
        }

        /// <summary>
        /// Closest living monster within range that the bot can actually see.
        ///
        /// Visibility matters here: without it the bot stops to shoot at a
        /// monster behind a wall, cannot hit it, and stands there until
        /// something else kills it.
        /// </summary>
        private Mobj NearestVisibleThreat(Mobj self, int range)
        {
            var best = (Mobj)null;
            var bestDistance = range;

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;

                if (mobj == null || mobj == self || mobj.Health <= 0)
                {
                    continue;
                }

                if ((mobj.Flags & MobjFlags.Shootable) == 0)
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

                if (!world.VisibilityCheck.CheckSight(self, mobj))
                {
                    continue;
                }

                bestDistance = distance;
                best = mobj;
            }

            return best;
        }

        /// <summary>Closest living monster within range, visible or not.</summary>
        private Mobj NearestThreat(Mobj self, int range)
        {
            var best = (Mobj)null;
            var bestDistance = range;

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;

                if (mobj == null || mobj == self || mobj.Health <= 0)
                {
                    continue;
                }

                if ((mobj.Flags & MobjFlags.Shootable) == 0)
                {
                    continue;
                }

                var dx = mobj.X - self.X;
                var dy = mobj.Y - self.Y;
                var distance = (int)(Math.Abs(dx.ToFloat()) + Math.Abs(dy.ToFloat()));

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = mobj;
                }
            }

            return best;
        }

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
            var bestDistance = EngagementRange;

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

                if (mobj == ignoredTarget && world.LevelTime < ignoreUntil)
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
                engagedTarget = null;
                engagedTics = 0;
                return false;
            }

            // Track how long the same target has been engaged. If it survives
            // this long it is probably out of reach, and standing there aiming
            // at it forever is not a plan.
            if (ReferenceEquals(best, engagedTarget))
            {
                engagedTics++;
            }
            else
            {
                engagedTarget = best;
                engagedTics = 0;
            }

            if (engagedTics > GiveUpTics)
            {
                ignoredTarget = best;
                ignoreUntil = world.LevelTime + 3 * 35;
                engagedTarget = null;
                engagedTics = 0;
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
        public int PathIndex { get { return pathIndex; } }

        /// <summary>Route node at an index, or -1. For diagnostics and tests.</summary>
        public int NodeAt(int index)
        {
            return index >= 0 && index < pathLength ? path[index] : -1;
        }
        public bool HeadingToExit { get { return headingToExit; } }
        public NavGraph Nav { get { return nav; } }
        public int ExitNode { get { return exitNode; } }
        public int ExitSector { get { return exitSector; } }

        /// <summary>Route to the exit that this run took, for the brain to keep.</summary>
        public int[] RouteToExit { get { return routeToExit; } }
        public int RouteToExitLength { get { return routeToExitLength; } }

        /// <summary>True when this bot ended the level itself.</summary>
        public bool Finished { get { return FinishedTics >= 0; } }

        /// <summary>One line describing what the bot is doing, for diagnostics.</summary>
        public string Status
        {
            get
            {
                if (pathLength == 0)
                {
                    return "idle";
                }

                if (pathIndex + 1 >= pathLength)
                {
                    return "arrived";
                }

                return "leg " + path[pathIndex] + "->" + path[pathIndex + 1] +
                       " target " + legTargetX.ToString("0") + "," + legTargetY.ToString("0") +
                       (legIsDoor ? " (door)" : "") +
                       (headingToExit ? " (exit)" : "") +
                       " stuck " + noProgressTics;
            }
        }

        public float TargetX { get { return legTargetX; } }
        public float TargetY { get { return legTargetY; } }
        public int StuckTics { get { return noProgressTics; } }
    }
}
