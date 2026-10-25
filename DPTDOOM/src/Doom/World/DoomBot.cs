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

        /// <summary>
        /// Closest stepping stone worth aiming at. Below this the bearing swings
        /// wildly for every unit of movement and the bot turns in place instead
        /// of walking.
        /// </summary>
        private const float MinStepDistance = 64F;

        /// <summary>
        /// A waypoint this close counts as reached. The engine moves a body in
        /// steps of several units, so a tighter test can never be satisfied.
        /// </summary>
        private const float ArrivedDistance = 24F;

        /// <summary>
        /// Distance at which the bot starts slowing down. Must be comfortably
        /// larger than the turning radius at full speed, or it orbits instead of
        /// arriving.
        /// </summary>
        private const float ApproachDistance = 160F;

        /// <summary>How close the bot has to be to press an exit switch.</summary>
        private const int ExitReachDistance = 96;

        /// <summary>
        /// A sweeper leaves only after seeing this much of the map. Below one,
        /// because a few sectors are sealed behind keys or secrets and waiting
        /// for a hundred percent would mean never leaving.
        /// </summary>
        private const double SweeperCoverage = 0.6;

        /// <summary>
        /// ...and only for this long. A sweeper that gets itself stuck in a
        /// corner of some far room should still leave the level instead of
        /// clearing the same three rooms until the tic counter runs out.
        /// </summary>
        private const int SweeperTimeLimit = 35 * 30;

        /// <summary>Below this health a cautious bot backs off instead of advancing.</summary>
        private const int CautiousRetreatHealth = 70;

        /// <summary>
        /// Below this health every other style backs off too.
        ///
        /// Measured on MAP01: at skill 3 the bots finish with 0 deaths, at
        /// Ultra-Violence none of the four finishes and Rush dies ten times. The
        /// difference is not the route - it is that a style which never gives
        /// ground trades blows with everything it meets, and on UV that trade is
        /// lost. Backing off while still firing is the cheapest way to survive a
        /// fight the bot picked by walking past.
        ///
        /// The threshold is lower than the cautious one on purpose: a Rush bot
        /// that breaks off at 70 would spend the level retreating, which is the
        /// opposite of what the style is for. At 40 it only does so when the fight
        /// has actually gone wrong.
        /// </summary>
        private const int RetreatHealth = 40;

        /// <summary>How close a monster has to be to make a cautious bot retreat.</summary>
        private const int CautiousThreatRange = 256;

        /// <summary>How close a monster has to be to make any style retreat.</summary>
        private const int RetreatThreatRange = 192;

        /// <summary>
        /// How close a monster has to be to count as "the thing blocking the
        /// way" when the bot has stopped making progress.
        /// </summary>
        private const int BlockerRange = 160;

        private readonly World world;
        private readonly Player player;
        private readonly NavGraph nav;
        private readonly int[] path;

        /// <summary>
        /// Scratch buffer for the "can I still get out" check, kept apart from
        /// <see cref="path"/> so the check does not destroy the route being built.
        /// </summary>
        private readonly int[] escapePath;
        private readonly bool[] visitedSector;

        // Sectors the bot failed to route to. Kept apart from visitedSector so a
        // failed plan cannot be mistaken for progress. Cleared whenever the bot
        // moves to another sector, because reachability is a property of where it
        // stands, not of the level.
        private readonly bool[] triedSector;

        private int lastSector = -1;

        /// <summary>
        /// Whether the exit can no longer be reached from where the bot is standing.
        ///
        /// The graph is directional, so parts of a map are one-way: a ledge can be dropped
        /// off and not climbed. A bot that ends up past such a ledge is not stuck in any
        /// sense the stuck counter notices - it moves, it explores, and it will never finish
        /// the level. MAP01 has exactly this around sector 43.
        /// </summary>
        private bool trapped;

        /// <summary>The last position from which the exit was still reachable.</summary>
        private float retreatX;
        private float retreatY;
        private bool retreatValid;

        private readonly BotBrain brain;
        private readonly BotStyle style;
        private readonly BotTemperament temperament;
        private readonly BotDanger danger;

        private int pathLength;
        private int pathIndex;
        private int noProgressTics;
        private float bestDistance = float.MaxValue;

        private int detourCount;

        // Stepping stone around the corner of a concave sector. Held until it is
        // reached or the bot leaves the sector; recomputing it every tic made the
        // bot chase a target that slid across the room.
        private int stoneNode = -1;
        private bool stoneValid;
        private int blockerTics;

        // Combat: which monster the bot is circling, and which way it is going
        // round it. The direction is held for a while and then reversed, so the
        // bot does not orbit one way into a wall.
        private Mobj combatTarget;
        private int strafeDirection = 1;
        private int strafeTics;

        /// <summary>
        /// Tics of fighting allowed before the bot goes back to walking. Without a
        /// budget the bot duels every monster it can see and the level takes four
        /// times as long; with it, it fights what is in the way and moves on.
        /// </summary>
        private const int StrafeBudget = 3 * 35;

        /// <summary>
        /// Distance below which stopping to fight is worth it at all. Beyond this
        /// the bot shoots on the move, which is almost always better: standing
        /// still at four hundred units to trade shots with an imp is how a run
        /// goes from under a minute to nearly two.
        /// </summary>
        private const float CloseCombatRange = 220F;

        /// <summary>How close a door has to be for the door timer to run.</summary>
        private const float DoorGiveUpRange = 56F;

        /// <summary>
        /// Tics of pressing at a door before it is written off. Patience scales
        /// it: a patient bot waits longer before deciding the door is locked.
        /// </summary>
        private const int DoorGiveUpTics = 2 * 35;

        // Health as of the last tic, so damage can be noticed.


        // Tics before another loot detour is considered.
        private int lootCooldown;

        // Tics spent pushing at the current door.
        private int doorTics;

        /// <summary>How long the bot ignores loot after fetching some.</summary>
        private const int LootCooldown = 15 * 35;

        /// <summary>
        /// Map units of walking a bot will spend for one unit of item desire. A
        /// medikit at thirty health has desire 1.75, so it is worth about four
        /// hundred units of detour — most of a room.
        /// </summary>
        private const float ItemWorthPerDesire = 240F;


        // Watchdog for standing in one place: sector, anchor position, tics.
        private int pocketSector = -1;
        private float pocketX;
        private float pocketY;
        private int pocketTics;

        /// <summary>Tics in one spot before every way out is closed.</summary>
        private const int PocketLimit = 5 * 35;

        /// <summary>Why the last command was built the way it was, for tracing.</summary>
        private string decision = "";

        // A target that will not die is a target worth ignoring for a while: a
        // hunter can otherwise stand in front of a window shooting at something
        // it can never reach, for the rest of the level.
        private Mobj engagedTarget;
        private int engagedTics;
        private Mobj ignoredTarget;
        private int ignoreUntil;

        // Tics spent standing and shooting, and a window during which shooting
        // is suppressed so the bot walks instead of firing at nothing.
        private int shootTics;
        private int suppressShootingUntil;

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

        /// <summary>
        /// Whether pressing Use at this sector would help rather than hurt.
        ///
        /// A door with an active thinker is a door in motion, and Use on a moving door
        /// reverses it. That is what turned "open the door" into "slam it": pressing every
        /// fourth tic opened the door and immediately shut it again, over and over, which is
        /// the door spam reported in co-op. So the bot presses only where pressing starts
        /// something - at a door that is shut.
        ///
        /// The height test is the second half: a sector whose gap is already taller than
        /// the bot is a doorway it can walk through, door or not, and pressing there is
        /// wasted in the best case.
        /// </summary>
        private bool IsDoorPressable(int sectorNumber)
        {
            var sectors = world.Map.Sectors;

            if (sectorNumber < 0 || sectorNumber >= sectors.Length)
            {
                // No sector means no information; pressing is the old behaviour and is
                // the safer default for a bot that has to get through something.
                return true;
            }

            var sector = sectors[sectorNumber];

            if (sector.SpecialData != null)
            {
                // Any door with a thinker is a door in motion, and Use on a moving door
                // reverses it. Allowing the press while a door was closing was tried, on the
                // theory that reopening it is what a player would do; it is worse, because
                // the bot presses every fourth tic for as long as it is on that leg and the
                // door then never finishes a cycle. Measured: four bot tests went from
                // passing to not finishing MAP01 at all.
                return false;
            }

            return sector.CeilingHeight - sector.FloorHeight < Fixed.FromInt(56);
        }

        /// <summary>
        /// Whether a door that is already moving touches the sector the bot stands in.
        ///
        /// This is the guard for the second, separate way door spam happened: the "try Use
        /// because I am blocked" branch. It has no route, so it does not know what is in
        /// front of it, and it used to press on the theory that a doorway is the usual
        /// reason for being blocked - including at doors that were already opening, where
        /// Use reverses them shut.
        ///
        /// The check is deliberately narrow. Requiring a *shut* door instead was tried and
        /// is wrong: a blocked bot often stands one step short of the door line, in a sector
        /// that does not contain it, and Use reaches through the neighbouring sector. Asked
        /// for a shut door it saw none and refused to press - measured, the Hunter style
        /// stopped finishing MAP01. Refusing only while touching a moving door has neither
        /// problem.
        ///
        /// Both sides are examined because a door line is shared: the bot may be standing
        /// in either of the two sectors it joins.
        /// </summary>
        private bool IsTouchingBusyDoor(Mobj mobj)
        {
            var sector = mobj.Subsector == null ? null : mobj.Subsector.Sector;

            if (sector == null)
            {
                return false;
            }

            foreach (var line in sector.Lines)
            {
                if (!NavGraph.IsDoorSpecial((int)line.Special))
                {
                    continue;
                }

                var front = line.FrontSide == null ? null : line.FrontSide.Sector;
                var back = line.BackSide == null ? null : line.BackSide.Sector;

                if (front != null && front != sector && !IsDoorPressable(front.Number))
                {
                    return true;
                }

                if (back != null && back != sector && !IsDoorPressable(back.Number))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The door rule, exposed so a test can check it without a whole run.
        ///
        /// Internal rather than public: it is not part of what a bot offers the game, it
        /// is a decision the game needs to be able to verify. A run trace can show that a
        /// bot stopped at a door, but not why - and the "why" here is the entire bug that
        /// was fixed.
        /// </summary>
        internal bool WouldPressDoor(int sectorNumber)
        {
            return IsDoorPressable(sectorNumber);
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

        /// <summary>How long a wander target is held before it is reconsidered.</summary>
        private const int WanderHold = 70;

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
            : this(world, player, sharedNav, sharedBrain, style, BotTemperament.Neutral, null)
        {
        }

        public DoomBot(
            World world, Player player, NavGraph sharedNav, BotBrain sharedBrain, BotStyle style,
            BotTemperament temperament, BotDanger danger)
        {
            this.world = world;
            this.player = player;
            this.style = style;
            this.temperament = temperament ?? BotTemperament.Neutral;

            brain = sharedBrain;
            nav = sharedNav ?? new NavGraph(world.Map);
            path = new int[nav.NodeCount];
            escapePath = new int[nav.NodeCount];
            sectorExitCache = new sbyte[world.Map.Sectors.Length];
            visitedSector = new bool[world.Map.Sectors.Length];
            triedSector = new bool[world.Map.Sectors.Length];

            this.danger = danger ?? new BotDanger(world.Map.Sectors.Length);

            // Danger is a cost in the graph, not a separate check. Applying it
            // once at construction is enough: the heat only changes when this bot
            // takes damage, and it re-applies it then.
            ApplyDangerToGraph();

            FinishedTics = -1;

            FindExit();
            PlanExplore(-1);
        }

        /// <summary>
        /// Turns remembered heat into pathfinding cost.
        ///
        /// This is the whole point of <see cref="BotDanger"/>: the bot does not
        /// decide "avoid that room" as a special case, it simply finds the room
        /// expensive, and the existing search does the rest. One mechanism, no new
        /// code path, and it composes with the penalties for unreachable points.
        /// </summary>
        private void ApplyDangerToGraph()
        {
            for (var sector = 0; sector < danger.SectorCount; sector++)
            {
                var cost = danger.CostMultiplier(sector);

                if (cost > 1F)
                {
                    nav.SetSectorPenalty(sector, cost);
                }
            }
        }

        public BotStyle Style { get { return style; } }

        /// <summary>This bot's character, for the overlay and the trace.</summary>
        public BotTemperament Temperament { get { return temperament; } }

        /// <summary>What this bot has learned about where it gets hurt.</summary>
        public BotDanger Danger { get { return danger; } }

        /// <summary>Why the last command was built as it was, for tracing.</summary>
        public string Decision { get { return decision; } }

        /// <summary>Health of the bot's own player, for reporting a run.</summary>
        public int Health { get { return player.Health; } }


        /// <summary>Fills the command for one tic.</summary>
        public void BuildTicCmd(TicCmd cmd)
        {
            cmd.Clear();
            decision = "start";

            var mobj = player.Mobj;
            if (mobj == null)
            {
                decision = "no-body";
                return;
            }

            if (player.Health <= 0)
            {
                // Dead. Doom waits for the player to press Use before rebuilding
                // the body, so a bot that just gives up here leaves the game
                // standing at a corpse for the rest of the level — which is
                // exactly what a Cautious run did: health 0, nothing moving,
                // eight thousand tics of nothing.
                //
                // Pressed every tic rather than pulsed: the engine only looks at
                // the button while the death animation is over, and the bot has
                // nothing else to do meanwhile.
                cmd.Buttons |= TicCmdButtons.Use;

                decision = "dead-use";
                return;
            }

            var sector = mobj.Subsector.Sector.Number;
            visitedSector[sector] = true;

            // Moving to another sector invalidates what the bot learned about
            // which places are out of reach.
            //
            // "Tried and unreachable" is a property of where the bot was standing,
            // not of the level: from one side of a lift or a ledge a sector is
            // impossible, from the other it is a short walk. Marking a sector
            // tried *permanently* meant one bad spot early in a run quietly
            // removed half the map from the plan — including the exit — and the
            // bot then wandered for thousands of tics with nothing left to do.
            if (sector != lastSector)
            {
                lastSector = sector;
                Array.Clear(triedSector, 0, triedSector.Length);
                UpdateTrapped(mobj);
            }

            useTic++;

            // Remember where damage came from. The bot cannot see who shot it,
            // but it knows which room cost it health, and that is enough to make
            // the room expensive next time.
            if (danger.Observe(sector, player.Health, world.LevelTime))
            {
                ApplyDangerToGraph();
            }

            SelectWeapon();

            // A bot gives ground when it is hurt instead of trading blows. It
            // keeps facing the threat while backing away, so it can still shoot:
            // turning tail would only get it shot in the back.
            //
            // This has to come **before** shooting. Shooting returns as soon as
            // it has a target, so a retreat check placed after it never ran
            // while a monster was visible — which is exactly when a bot needs it.
            //
            // Every style does this now, not only Cautious. The cautious threshold
            // is high because retreating *is* that style; the others only break off
            // once the fight has gone wrong. See RetreatHealth.
            var retreatHealth = style == BotStyle.Cautious ? CautiousRetreatHealth : RetreatHealth;
            var retreatRange = style == BotStyle.Cautious ? CautiousThreatRange : RetreatThreatRange;

            if (player.Health < retreatHealth)
            {
                var threat = NearestThreat(mobj, retreatRange);

                if (threat != null && HasRoomToRetreat(mobj, threat))
                {
                    var toThreat = Geometry.PointToAngle(mobj.X, mobj.Y, threat.X, threat.Y);
                    var facing = TurnTo(mobj.Angle, toThreat);

                    cmd.AngleTurn = (short)MathEx.Clamp(facing, -0x1000, 0x1000);
                    cmd.ForwardMove = -25;
                    cmd.SideMove = 0;
                    cmd.Buttons |= TicCmdButtons.Attack;

                    return;
                }
            }

            // Where to look. A visible monster takes priority, but it does not
            // stop the bot from walking: the aim only changes the facing, and
            // movement is expressed relative to it below.
            var aim = Shoot(mobj);

            if (aim.HasValue && FacingWithin(mobj.Angle, aim.Value, 0x0400))
            {
                cmd.Buttons |= TicCmdButtons.Attack;
            }

            // Close combat: hold the range this bot likes and circle the target.
            //
            // Only when the monster is genuinely close, and only for a while.
            // Firing while walking is already handled above; this is the extra
            // step of stopping to fight properly, and it is expensive. The first
            // version did it for anything within twice the preferred range and
            // turned a 0:43 run into 3:50, because on this map almost everything
            // is within that distance of somewhere on the route.
            if (aim.HasValue && combatTarget != null)
            {
                var combatDistance = Math.Abs(combatTarget.X.ToFloat() - mobj.X.ToFloat()) +
                                     Math.Abs(combatTarget.Y.ToFloat() - mobj.Y.ToFloat());

                var reach = Math.Min(PreferredRange() * FightRangeFactor(), CloseCombatRange);

                if (combatDistance < reach && strafeTics < StrafeBudget)
                {
                    SelectWeaponFor(combatDistance);
                    StrafeAround(cmd, mobj, combatTarget, aim.Value);

                    decision = "strafe";
                    return;
                }
            }

            // Not fighting this tic: the circle is over, and the budget resets so
            // the next monster gets a fair share of it.
            strafeTics = 0;

            if (trapped && retreatValid)
            {
                // There is no route to the exit from here, and there was from a place the
                // bot has already been. Walk back to it.
                //
                // This is the only cure for walking into a one-way part of a map. Every
                // other guard in this file assumes the graph tells the truth about where
                // the bot can go; here the graph is right and the bot has already gone too
                // far. Banning passages and marking sectors tried were both tried and both
                // fail for the same reason - they cannot build an exit that does not exist.
                //
                // Measured on MAP01: a Sweeper with a fresh brain crossed a one-way ledge at
                // tic 481 and spent the remaining fifteen hundred tics of the run in a
                // pocket that reaches six of the map's fifty-nine sectors.
                Steer(cmd, mobj, retreatX, retreatY, aim);

                decision = "retreat";

                if (Math.Abs(retreatX - mobj.X.ToFloat()) + Math.Abs(retreatY - mobj.Y.ToFloat()) < 40F)
                {
                    // Standing on the remembered spot. The sector check that follows will
                    // say whether that was enough.
                    retreatValid = false;
                }

                return;
            }

            if (wanderActive)
            {
                // No route exists from here. Walk at the goal for a moment and
                // let the ordinary steering slide along whatever it meets; often
                // one waypoint is enough for a plan to exist again.
                Steer(cmd, mobj, wanderX, wanderY, aim);

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
                PressExit(cmd, mobj, aim))
            {
                return;
            }

            // Loot is chosen when a new destination is picked, inside
            // PlanExplore — not here. Doing it every tic reset the route
            // constantly: the bot would start walking to a medikit, notice the
            // same medikit again, and start over, which took a 0:43 run to 4:52.
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

            // Advance along the path, and aim past the near waypoints.
            //
            // The mesh is dense: points sit 30 to 50 units apart. A bot that
            // stops at each one cannot stop in time — Doom keeps the body's
            // momentum and applies friction — so it overshoots, finds the next
            // waypoint behind it, turns around, overshoots again. The trace shows
            // this clearly: waypoint at 384,1587, body carried to 422,1581, then
            // a full turn back towards 371,1536.
            //
            // So the bot looks ahead and walks at the farthest waypoint it can
            // reach in a straight line. Near waypoints are skipped, not visited.
            // This is the usual string-pulling on a path, and it is what makes
            // the movement look like a person walking rather than a metronome.
            if (pathLength > 0)
            {
                var lookX = mobj.X.ToFloat();
                var lookY = mobj.Y.ToFloat();

                var lookahead = pathIndex;

                for (var i = pathIndex; i < pathLength; i++)
                {
                    if (nav.GetSector(path[i]) != sector)
                    {
                        // Crossing into another sector means leaving the line of
                        // sight anyway; stop at the last point that was still in
                        // this one.
                        break;
                    }

                    if (!nav.LineClear(lookX, lookY, nav.GetX(path[i]), nav.GetY(path[i]), sector))
                    {
                        break;
                    }

                    lookahead = i;

                    // Do not reach so far ahead that a doorway gets skipped: a
                    // door has to be opened from its own side, so the point in
                    // front of it is kept.
                    if (nav.IsDoor(path[i]))
                    {
                        break;
                    }
                }

                if (lookahead > pathIndex)
                {
                    pathIndex = lookahead;
                    stoneValid = false;

                    // Note: noProgressTics is deliberately *not* reset here.
                    // Resetting it made the bot stand at a locked door forever:
                    // the lookahead kept re-selecting the same point, which
                    // looked like progress, so the stuck handler never fired and
                    // nothing was ever replanned.
                }
            }

            if (pathLength == 0 || pathIndex >= pathLength)
            {
                PlanExplore(LastNode());

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
            // is visible from here. The stepping stone is **held** until the bot
            // reaches it or leaves the sector, and that stickiness is the whole
            // point: recomputing it every tic means "the visible node closest to
            // the goal", which changes as the bot moves, so the target slides
            // across the room and the bot chases it in circles.
            var hereX = mobj.X.ToFloat();
            var hereY = mobj.Y.ToFloat();

            if (stoneValid &&
                (nav.GetSector(stoneNode) != sector || DistanceTo(stoneNode, mobj) < ArrivedDistance))
            {
                stoneValid = false;
            }

            if (nav.GetSector(path[pathIndex]) == sector &&
                !nav.LineClear(hereX, hereY, legTargetX, legTargetY, sector))
            {
                if (!stoneValid)
                {
                    stoneNode = nav.VisibleNodeTowards(
                        sector, hereX, hereY, legTargetX, legTargetY, MinStepDistance);

                    stoneValid = stoneNode >= 0;
                }

                if (stoneValid)
                {
                    legTargetX = nav.GetX(stoneNode);
                    legTargetY = nav.GetY(stoneNode);
                    legIsDoor = nav.IsDoor(stoneNode);
                }
            }
            else
            {
                stoneValid = false;
            }

            var dx = legTargetX - mobj.X.ToFloat();
            var dy = legTargetY - mobj.Y.ToFloat();
            var distance = Math.Abs(dx) + Math.Abs(dy);

            // End of the route: the last waypoint is reached.
            //
            // There used to be an extra "arrived" test here that advanced the
            // route whenever the bot was within 24 units of a waypoint. It was
            // harmful: a waypoint 20 units away then counted as reached, the
            // route moved on, the next plan handed the same waypoint back, and
            // the bot stood still forever, "arriving" every tic. The trace shows
            // it as a run of thousands of tics at one pair of coordinates with
            // stuck = 0 — which is why the stuck handler never fired.
            //
            // Skipping waypoints is the lookahead's job, and it does it by line
            // of sight rather than by distance, which is the honest test.
            if (pathIndex >= pathLength - 1 && distance < ReachDistance)
            {
                PlanExplore(path[pathIndex]);

                if (pathLength == 0 || pathIndex >= pathLength)
                {
                    return;
                }

                legTargetX = nav.GetX(path[pathIndex]);
                legTargetY = nav.GetY(path[pathIndex]);
                legIsDoor = nav.IsDoor(path[pathIndex]);

                dx = legTargetX - mobj.X.ToFloat();
                dy = legTargetY - mobj.Y.ToFloat();
                distance = Math.Abs(dx) + Math.Abs(dy);
            }

            if (legIsDoor)
            {
                // Press Use only while the door is genuinely shut.
                //
                // The old comment here said "press every tic while walking in", and it
                // was wrong in a way that only shows up in play: pressing Use on a door
                // that is already opening reverses it. UseSpecialLine flips a door's
                // direction from +1 to -1 for the "open" specials, so a bot that pressed
                // every fourth tic opened the door and then slammed it, over and over -
                // the door spam reported in co-op, and the reason a bot could stand at
                // one door for the rest of a level.
                //
                // "Shut" is readable from the sector: an active door thinker means the
                // door is opening, waiting or closing, and in all three cases pressing
                // either does nothing useful or undoes what is happening.
                if (WantUse && IsDoorPressable(nav.GetSector(path[pathIndex])))
                {
                    cmd.Buttons |= TicCmdButtons.Use;
                }

                // Doors need their own timer. The generic stuck counter watches
                // the distance to the waypoint, and at a door that distance
                // wobbles by a few units as the bot pushes, so it keeps resetting
                // and the door is never given up on. Measured: a bot pressed
                // against a closed door for the remaining thirteen thousand tics
                // of a run with "stuck" never above 20.
                if (distance < DoorGiveUpRange)
                {
                    doorTics++;

                    if (doorTics > DoorGiveUpTics)
                    {
                        doorTics = 0;

                        if (pathLength > 1)
                        {
                            nav.BanPassage(
                                path[Math.Max(0, pathIndex - 1)],
                                path[pathIndex]);
                        }

                        AbandonPath();
                    }
                }
                else
                {
                    doorTics = 0;
                }
            }
            else
            {
                doorTics = 0;
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

            Steer(cmd, mobj, legTargetX, legTargetY, aim);

            // Something is in the way and the bot is not moving. If it is a
            // monster, shoot it: standing against a demon and pushing is how the
            // bot lost most of its health while making no progress. Shoot()
            // above only fires at targets already in front of the bot, and a
            // monster that wandered into its path from the side never qualifies,
            // so the bot pushed at it for the rest of the level.
            if (noProgressTics > 8 && blockerTics <= GiveUpTics)
            {
                // Another player in the way. Co-op players all spawn on the same
                // spot and collide, so a bot that only pushes forward can stand
                // in a pile for the whole level. Step away from whoever is
                // closest and try again.
                var other = NearestPlayer(mobj, BlockerRange);

                if (other != null)
                {
                    // Face away from the other body, then walk. Not both at once:
                    // backing up while turning traces an arc, and in a spawn pile
                    // that arc is a small circle — the bot rotates around its
                    // neighbour instead of leaving. Same geometry as everywhere
                    // else in this file.
                    var toOther = Geometry.PointToAngle(mobj.X, mobj.Y, other.X, other.Y);
                    var away = TurnTo(mobj.Angle, toOther);

                    // Reversing the bearing: a half turn from the direction to the
                    // other player.
                    var awayTurn = away > 0 ? away - 0x8000 : away + 0x8000;

                    cmd.AngleTurn = (short)MathEx.Clamp(awayTurn, -0x1000, 0x1000);

                    if (Math.Abs(awayTurn) > 0x2000)
                    {
                        cmd.ForwardMove = 0;
                    }
                    else
                    {
                        cmd.ForwardMove = 30;
                    }

                    cmd.SideMove = 0;

                    return;
                }

                var blocker = NearestVisibleThreat(mobj, BlockerRange);

                if (blocker != null)
                {
                    blockerTics++;

                    var toBlocker = Geometry.PointToAngle(mobj.X, mobj.Y, blocker.X, blocker.Y);
                    var facing = TurnTo(mobj.Angle, toBlocker);

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
            else if (blockerTics > GiveUpTics)
            {
                // Stood here shooting at something for two seconds without
                // progress. Either it is out of reach or it cannot die; either
                // way, standing still is not a plan. Give up on this waypoint and
                // let the search route around it.
                blockerTics = 0;
                noProgressTics = 0;
                bestDistance = float.MaxValue;

                nav.Penalize(path[pathIndex]);
            }

            if (noProgressTics > MaxStuckTics)
            {
                // Blocked on this waypoint for over a second.
                //
                // If the waypoint is close, the bot is standing at it and simply
                // cannot touch the exact point: a doorway node can sit nearer the
                // wall than a body's radius, and no amount of pushing closes the
                // last few units. Treat it as reached and move on. The trace
                // shows this as a bot pinned to one pair of coordinates for
                // thousands of tics with "stuck" climbing, which is exactly the
                // grinding the eye reads as circling.
                if (pathLength > 0 && pathIndex < pathLength &&
                    DistanceTo(path[pathIndex], mobj) < ArrivedDistance + 12F)
                {
                    // Standing on it. Step over it and keep the route: clearing
                    // the path here would replan to the same unreachable point.
                    pathIndex++;
                    stoneValid = false;
                }
                else
                {
                    if (legIsDoor && pathLength > 1)
                    {
                        // A door that has not opened by now is locked or already
                        // spent: close the passage for the rest of the level.
                        nav.BanPassage(
                            path[Math.Max(0, pathIndex - 1)],
                            path[pathIndex]);
                    }
                    else
                    {
                        // Penalising the node alone is not enough on its own: when the
                        // unreachable node *is* the destination, extra cost does not make
                        // it stop being the destination, so the next plan can hand it back
                        // and the bot orbits it thirty-five tics at a time. Remembering that
                        // the sector was tried is what stops that; the flag is cleared
                        // whenever the bot changes sector, so a spot that fails from here is
                        // not written off from everywhere - the lesson of the earlier bug
                        // where permanent "unreachable" removed half the map, exit included.
                        //
                        // Closing the passage instead was tried and is worse: a waypoint can
                        // be unreachable for a moment (a lift, a monster, a corner) and the
                        // ban then lasts the whole level. Measured, it cost the Hunter style
                        // three times its time and eight deaths on MAP01.
                        nav.Penalize(path[pathIndex]);
                        triedSector[nav.GetSector(path[pathIndex])] = true;
                    }

                    pathLength = 0;
                }

                noProgressTics = 0;
                bestDistance = float.MaxValue;

                if (++detourCount > 8)
                {
                    detourCount = 0;
                    AbandonPath();
                }
            }
        }

        /// <summary>
        /// Uses the exit switch if the bot is standing in front of it. Returns
        /// true when the bot stopped to press it.
        /// </summary>
        private bool PressExit(TicCmd cmd, Mobj mobj, Angle? aim)
        {
            var dx = exitX - mobj.X.ToFloat();
            var dy = exitY - mobj.Y.ToFloat();

            if (Math.Abs(dx) + Math.Abs(dy) > ExitReachDistance)
            {
                return false;
            }

            // At the exit the switch is the only thing that matters, so the bot
            // looks at it rather than at a monster.
            Steer(cmd, mobj, exitX, exitY, null);

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

        /// <summary>
        /// Last-resort watchdog: if the bot has stayed in one sector without
        /// moving for several seconds, every passage out of its current waypoint
        /// is closed and the route is dropped.
        ///
        /// This exists because a graph can be wrong in a way no amount of
        /// replanning fixes: a pocket that looks connected and is not. The trace
        /// showed a bot standing in the same two square metres for four thousand
        /// tics while the route kept handing back the same doorway. Closing the
        /// passages forces the search somewhere else; if there really is nowhere
        /// else, the bot explores or wanders, but it does not stand still.
        /// </summary>
        private void CheckPocket(int sector, Mobj mobj)
        {
            var x = mobj.X.ToFloat();
            var y = mobj.Y.ToFloat();

            if (sector != pocketSector ||
                Math.Abs(x - pocketX) + Math.Abs(y - pocketY) > 48F)
            {
                pocketSector = sector;
                pocketX = x;
                pocketY = y;
                pocketTics = 0;
                return;
            }

            if (++pocketTics < PocketLimit)
            {
                return;
            }

            pocketTics = 0;

            if (pathLength > 0 && pathIndex < pathLength)
            {
                var node = path[pathIndex];
                nav.BanPassage(node, path[pathIndex > 0 ? pathIndex - 1 : pathIndex]);
            }

            AbandonPath();
        }
        private float DistanceTo(int node, Mobj mobj)
        {
            var dx = nav.GetX(node) - mobj.X.ToFloat();
            var dy = nav.GetY(node) - mobj.Y.ToFloat();

            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private int LastNode()
        {
            return pathLength > 0 ? path[pathLength - 1] : -1;
        }

        private void AbandonPath()
        {
            if (pathLength > 0 && !headingToExit)
            {
                visitedSector[nav.GetSector(path[pathLength - 1])] = true;
            }

            pathLength = 0;
            pathIndex = 0;
            stoneValid = false;
        }

        ////////////////////////////////////////////////////////////
        // Angles
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Signed turn from one heading to another, in TicCmd units (1/65536 of
        /// a turn): negative means turn clockwise, positive counter-clockwise.
        ///
        /// This must go through an <c>int</c>. <see cref="Angle.Data"/> is
        /// <c>uint</c>, and angle subtraction wraps in unsigned arithmetic, so
        /// <c>(to - from).Data &gt;&gt; 16</c> is a **logical** shift: a target
        /// slightly to the right of the bot comes out as about 65500 instead of
        /// about -300. The bot then clamps to the turn limit and rotates away
        /// from the target — the same direction every tic, forever.
        ///
        /// That is the "constant angle" that was reported: not a frozen angle,
        /// but a heading rotating at full rate that never arrives, because every
        /// small correction was read as a large one in the wrong direction.
        /// </summary>
        private static int TurnTo(Angle from, Angle to)
        {
            return (int)(to - from).Data >> 16;
        }

        /// <summary>
        /// Whether one heading is within the given tolerance of another, in
        /// TicCmd units. Uses the same signed difference, so it does not suffer
        /// the wrap above.
        /// </summary>
        private static bool FacingWithin(Angle from, Angle to, int tolerance)
        {
            return Math.Abs(TurnTo(from, to)) <= tolerance;
        }

        ////////////////////////////////////////////////////////////
        // Steering
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Walks toward a point, optionally while looking somewhere else.
        ///
        /// <paramref name="aimAngle"/> is where the bot wants to face — at a
        /// monster, usually. Movement is then expressed relative to that facing,
        /// so the bot strafes toward its waypoint with its eyes on the enemy,
        /// exactly as a person does. Without this the bot either walked or
        /// fought, never both, and spent most of the level standing still.
        /// </summary>
        private void Steer(TicCmd cmd, Mobj mobj, float targetX, float targetY, Angle? aimAngle)
        {
            var toTarget = Geometry.PointToAngle(
                mobj.X, mobj.Y,
                Fixed.FromDouble(targetX), Fixed.FromDouble(targetY));

            var facing = aimAngle.HasValue ? aimAngle.Value : toTarget;

            // Angle counts 1/2^32 of a turn; TicCmd.AngleTurn counts 1/65536.
            var turn = TurnTo(mobj.Angle, facing);

            var limit = noProgressTics > 6 ? 0x1000 : 0x0800;

            if (turn > limit)
            {
                cmd.AngleTurn = (short)limit;
            }
            else if (turn < -limit)
            {
                cmd.AngleTurn = (short)-limit;
            }
            else
            {
                cmd.AngleTurn = (short)turn;
            }

            // The way we want to move, expressed in the frame we are facing.
            //
            // The engine thrusts forward along the body angle and sideways along
            // angle - 90 (see PlayerBehavior), so a direction that lies
            // <c>relative</c> counter-clockwise from the facing is:
            //   forward = cos(relative), side = -sin(relative)
            // The side axis is flipped because a positive SideMove is to the
            // right, and a target on the right has a negative relative angle.
            var relative = TurnTo(facing, toTarget);
            var radians = relative * (Math.PI * 2.0 / 65536.0);

            // Slow down on approach. A body that moves while it turns follows an
            // arc, and the radius of that arc is speed divided by turn rate. At
            // full walking speed and the normal turn limit the radius is about
            // twenty units — the same size as the distance to a waypoint — so
            // the bot circles the point instead of reaching it.
            var dx = targetX - mobj.X.ToFloat();
            var dy = targetY - mobj.Y.ToFloat();
            var distance = (float)Math.Sqrt(dx * dx + dy * dy);

            var speed = 50.0;

            if (distance < ApproachDistance)
            {
                speed = 50.0 * (distance / ApproachDistance);

                if (speed < 14.0)
                {
                    speed = 14.0;
                }
            }

            var forward = (int)Math.Round(Math.Cos(radians) * speed);
            var side = (int)Math.Round(-Math.Sin(radians) * speed * 0.8);

            // Moving backwards is not a thing this bot does. If the waypoint is
            // behind, it turns; the strafe component carries it sideways in the
            // meantime, and that is enough.
            if (Math.Abs(relative) > 0x4000)
            {
                forward = 0;
            }

            cmd.ForwardMove = (sbyte)MathEx.Clamp(forward, -50, 50);
            cmd.SideMove = (sbyte)MathEx.Clamp(side, -40, 40);

            // Blocked for a while: keep pressing forward and use. Doors need
            // contact to open, and a doorway is the usual reason for being blocked.
            // Reversing and strafing here, as the first version did, is what turned a
            // bot that was briefly stuck into one that traced circles around the same
            // corner for the rest of the level.
            //
            // The pulse stays at every fourth tic. Slowing it down was tried twice - once a
            // second, then once every eight tics - and both made bots stop finishing MAP01,
            // because a sparse pulse misses the window in which a door becomes pressable.
            // What does change is that the press is skipped at a door that is already
            // moving, where the engine flips it shut; that is the reported spam, and the
            // check deliberately says nothing about anywhere else.
            if (noProgressTics > 8)
            {
                if (WantUse && !IsTouchingBusyDoor(mobj))
                {
                    cmd.Buttons |= TicCmdButtons.Use;
                }

                // Straight ahead, no side component: a sideways slide while
                // turning is an arc, and an arc is a circle.
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
        ///
        /// <paramref name="continueFrom"/> is the node the bot has just reached,
        /// if any. Planning from there rather than from the nearest node is what
        /// stops the ping-pong: two mesh points can stand 28 units apart, and
        /// "nearest node" then names the one just left, so the route immediately
        /// pointed backwards and the bot turned around and walked back. Over and
        /// over, which is a circle with a 14-unit radius and a heading that never
        /// settles.
        /// </summary>
        /// <summary>
        /// Whether a route to this node leaves the bot able to reach the exit.
        ///
        /// The navigation graph is directional on purpose - its edges are filtered by step
        /// height and clearance, because a ledge can be dropped off and not climbed - and a
        /// directional graph can contain one-way pockets. A bot that walks into one is not
        /// stuck in any sense the stuck handler recognises: it keeps moving, it keeps
        /// exploring, and it will never finish the level.
        ///
        /// Measured on MAP01: the sector 43 area reaches six sectors of fifty-nine and the
        /// exit is not one of them. That is exactly where a Sweeper with a fresh brain
        /// spent the rest of its run - fifteen hundred tics of walking around one room.
        ///
        /// So a detour is taken only if the exit is still reachable from where it ends.
        /// The first half of the check is skipped when the bot cannot reach the exit from
        /// where it already stands: being trapped is not a reason to refuse every move, it
        /// is the moment any move is worth trying.
        /// </summary>
        /// <summary>
        /// Recomputes whether the exit is still reachable, and remembers the last place
        /// where it was.
        ///
        /// Called on entering a new sector rather than every tic: a search per tic would
        /// cost more than the rendering it competes with, while a sector is exactly the
        /// granularity at which the answer changes - the bot cannot cross a one-way edge
        /// without changing sector.
        /// </summary>
        private void UpdateTrapped(Mobj mobj)
        {
            if (exitSector < 0)
            {
                return;
            }

            var node = nav.NodeAt(mobj.X, mobj.Y);

            if (node < 0)
            {
                return;
            }

            if (nav.FindPathToSector(node, exitSector, escapePath) > 0)
            {
                trapped = false;
                retreatX = mobj.X.ToFloat();
                retreatY = mobj.Y.ToFloat();
                retreatValid = true;
                return;
            }

            trapped = true;
        }

        /// <summary>
        /// Whether every sector along a route can still reach the exit.
        ///
        /// This is the check that actually keeps the bot out of a pocket, and it took a wrong
        /// version to see why. Checking only the *destination* is not enough: the graph is
        /// directed, so a route to somewhere harmless can pass **through** a one-way drop and
        /// leave the bot stranded at the far end while the destination itself was fine.
        /// Measured on MAP01: a Sweeper heading for a point on the far side went through the
        /// sector 43 area, and from there only six of the map's fifty-nine sectors are
        /// reachable - the exit is not one of them.
        ///
        /// Answering per sector, cached, because a route is hundreds of nodes long and the
        /// same handful of sectors repeats.
        /// </summary>
        internal bool RouteStaysEscapable(int[] route, int length)
        {
            if (exitSector < 0)
            {
                return true;
            }

            for (var i = 0; i < length; i++)
            {
                if (!SectorCanReachExit(nav.GetSector(route[i])))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether a sector reaches the exit, cached per sector.
        ///
        /// The cache is valid for the life of the bot: the graph does not change, and the exit
        /// does not move. Penalties and banned passages do change, which is why the answer is
        /// computed with a private buffer and the plain search rather than through anything
        /// that remembers state.
        /// </summary>
        private bool SectorCanReachExit(int sector)
        {
            if (sector < 0 || sector >= sectorExitCache.Length)
            {
                return true;
            }

            if (sectorExitCache[sector] != 0)
            {
                return sectorExitCache[sector] > 0;
            }

            var first = nav.FirstNodeOfSector(sector);
            var reaches = false;

            if (first >= 0)
            {
                reaches = nav.FindPathToSector(first, exitSector, escapePath) > 0;
            }

            sectorExitCache[sector] = reaches ? (sbyte)1 : (sbyte)-1;
            return reaches;
        }

        /// <summary>
        /// Per-sector answer to "can the exit be reached from here": 0 unknown, 1 yes, -1 no.
        ///
        /// A byte array rather than a dictionary: there is one entry per sector of the map and
        /// the index is the sector number.
        /// </summary>
        private readonly sbyte[] sectorExitCache;

        internal bool CanStillLeave(int node, int fromNode)
        {
            if (exitSector < 0)
            {
                return true;
            }

            // Whether the bot is *currently* trapped is a question about its sector, not
            // about the nearest navigation node. The nearest node can belong to the sector
            // on the other side of the line the bot is standing on, and answering from it
            // is how a bot inside a one-way pocket decided it was not trapped: the node it
            // asked about was in the room it had just dropped out of.
            if (IsTrappedNow())
            {
                return true;
            }

            return nav.FindPathToSector(node, exitSector, escapePath) > 0;
        }

        /// <summary>
        /// Whether the exit can be reached from the sector the bot is standing in.
        ///
        /// Answered by trying the sector's own nodes rather than the nearest one: a sector can
        /// hold several clusters of points that do not see each other, and the nearest node is
        /// not always the connected one - the same lesson the recovery path in
        /// <see cref="PlanExplore"/> already learned.
        ///
        /// Cached per sector because this runs while planning, which happens on every arrival,
        /// and the answer cannot change without the bot changing sector.
        /// </summary>
        private bool IsTrappedNow()
        {
            var mobj = player.Mobj;

            if (mobj == null || exitSector < 0)
            {
                return false;
            }

            var sector = mobj.Subsector.Sector.Number;

            if (sector == trappedCheckedSector)
            {
                return trappedResult;
            }

            trappedCheckedSector = sector;
            trappedResult = !SectorReachesExit(sector);

            return trappedResult;
        }

        /// <summary>
        /// Whether any node of this sector reaches the exit.
        ///
        /// Any, not all: one connected cluster is enough to get out, and requiring every node
        /// would call a large room a trap because of one point wedged behind a pillar.
        /// </summary>
        private bool SectorReachesExit(int sector)
        {
            var first = nav.FirstNodeOfSector(sector);

            if (first >= 0 && nav.FindPathToSector(first, exitSector, escapePath) > 0)
            {
                return true;
            }

            var node = nav.NodeAt(player.Mobj.X, player.Mobj.Y);

            return node >= 0 && nav.FindPathToSector(node, exitSector, escapePath) > 0;
        }

        /// <summary>The sector the trapped answer was computed for, or -1.</summary>
        private int trappedCheckedSector = -1;

        private bool trappedResult;

        private void PlanExplore(int continueFrom)
        {
            pathLength = 0;
            pathIndex = 0;
            headingToExit = false;

            var mobj = player.Mobj;
            if (mobj == null)
            {
                return;
            }

            var start = continueFrom;

            if (start < 0 || start >= nav.NodeCount)
            {
                start = nav.NodeAt(mobj.X, mobj.Y);
            }
            else
            {
                // The node we just reached is only a valid starting point while
                // the bot is still standing on it. If it has drifted away, the
                // nearest node is the honest answer.
                var dx = nav.GetX(start) - mobj.X.ToFloat();
                var dy = nav.GetY(start) - mobj.Y.ToFloat();

                if (Math.Abs(dx) + Math.Abs(dy) > 96F)
                {
                    start = nav.NodeAt(mobj.X, mobj.Y);
                }
            }

            if (start < 0)
            {
                return;
            }

            // Loot worth a detour.
            //
            // Chosen here, once per destination, rather than every tic. Every-tic
            // selection reset the route constantly and turned a 0:43 run into
            // 4:52 — the bot kept noticing the same medikit and starting over.
            //
            // The cooldown is the other half: after one loot trip the bot has to
            // do something else for a while, so a room full of clips does not
            // become an infinite shopping trip.
            if (lootCooldown > 0)
            {
                lootCooldown--;
            }
            else if (GrabWorthIt(mobj))
            {
                var pickup = FindWantedItem(mobj, nav.GetSector(start));

                if (pickup.HasValue)
                {
                    var lootLength = nav.FindPath(start, pickup.Value.Node, path);

                    // Worth is expressed in the same unit as a path length: how
                    // many map units the bot is willing to walk for this thing.
                    // A medikit at thirty health is worth several hundred; a clip
                    // when nothing is running low is worth nothing at all.
                    if (lootLength > 1 && pickup.Value.Worth > lootLength &&
                        CanStillLeave(pickup.Value.Node, start))
                    {
                        pathLength = lootLength;
                        pathIndex = 0;
                        headingToExit = false;
                        stoneValid = false;

                        lootCooldown = LootCooldown;

                        decision = "loot";
                        return;
                    }
                }
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

            if (!exploreFirst && exitSector >= 0)
            {
                // By sector, not by node: see FindPathToSector. The nearest node
                // to the switch is often in a cluster that nothing connects to,
                // while the exit room itself is fine.
                var length = nav.FindPathToSector(start, exitSector, path);
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

                if (length > 0 && CanStillLeave(target, start) &&
                    RouteStaysEscapable(path, length))
                {
                    pathLength = length;
                    return;
                }

                // Unreachable: remember not to pick it again. This is a separate
                // flag from "visited" on purpose. Marking it visited used to
                // destroy the bot's map knowledge: after a few failed plans it
                // believed it had seen every sector, stopped exploring, and then
                // had nothing left to do at all.
                //
                // A detour that cannot be left counts as unreachable for the same
                // reason: from here it is a place the bot must not go, and the route
                // to it will be offered again on the next plan otherwise.
                triedSector[nav.GetSector(target)] = true;
            }

            // Everything has been seen, so a sweeper may now leave.
            if (exitSector >= 0)
            {
                var length = nav.FindPathToSector(start, exitSector, path);
                if (length > 0)
                {
                    pathLength = length;
                    headingToExit = true;
                    CaptureRoute();
                    return;
                }
            }

            // Still nothing. The node the bot is standing on may sit in an
            // isolated cluster of its own sector — a sector can hold several
            // groups of points that are not mutually visible, and the nearest one
            // is not always the connected one. Try the other nodes of the sector
            // before giving up: from any of them the route may exist.
            //
            // This used to try only the exit and only through exitNode, so when
            // the exit node was detached the whole recovery did nothing. Now it
            // retries both things that actually matter: unexplored sectors, and
            // the exit sector.
            var sector = nav.GetSector(start);

            for (var node = 0; node < nav.NodeCount; node++)
            {
                if (node == start || nav.GetSector(node) != sector)
                {
                    continue;
                }

                var target = FindNearestUnvisitedNode(node);

                if (target >= 0)
                {
                    var explored = nav.FindPath(node, target, path);

                    if (explored > 0 && CanStillLeave(target, node) &&
                        RouteStaysEscapable(path, explored))
                    {
                        pathLength = explored;
                        pathIndex = 0;
                        headingToExit = false;
                        return;
                    }
                }

                if (exitSector < 0)
                {
                    continue;
                }

                var length = nav.FindPathToSector(node, exitSector, path);
                if (length > 0)
                {
                    pathLength = length;
                    pathIndex = 0;
                    headingToExit = true;
                    CaptureRoute();
                    return;
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

            // Commit to one wander target instead of picking a new one every
            // tic.
            //
            // "The visible node closest to the goal" changes as the bot walks, so
            // re-choosing each tic made it turn towards a different point every
            // few tics and pace around a room without leaving it — 48 of these
            // windows in a four-minute co-op run. Choosing once and holding it
            // until it is reached or the timer runs out turns wandering into
            // walking.
            if (wanderActive && wanderTics < WanderHold)
            {
                return true;
            }

            var step = nav.VisibleNodeTowards(sector, hereX, hereY, goalX, goalY, MinStepDistance);

            if (step >= 0)
            {
                var dx = nav.GetX(step) - hereX;
                var dy = nav.GetY(step) - hereY;

                if (Math.Abs(dx) + Math.Abs(dy) > ReachDistance)
                {
                    wanderActive = true;
                    wanderX = nav.GetX(step);
                    wanderY = nav.GetY(step);
                    wanderTics = 0;

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

                // Not the node the bot is standing on, and not one so close that
                // reaching it proves nothing.
                //
                // The nearest "unvisited" point can be the bot's own node: a body
                // near a doorway belongs to one sector while its nearest waypoint
                // belongs to the other, so the sector can be unvisited while the
                // node is right under the bot. The route then has length one and
                // goes nowhere — the bot "arrives" immediately, plans the same
                // thing again, and oscillates between two points on opposite
                // sides of the room. That is the small loop the traces kept
                // showing, and it is why a run could sit in the first room
                // forever.
                if (node == start)
                {
                    continue;
                }

                var dx = nav.GetX(node) - nav.GetX(start);
                var dy = nav.GetY(node) - nav.GetY(start);
                var distance = dx * dx + dy * dy;

                if (distance < ArrivedDistance * ArrivedDistance)
                {
                    continue;
                }

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

        /// <summary>Closest other player within range, or null.</summary>
        private Mobj NearestPlayer(Mobj self, int range)
        {
            var best = (Mobj)null;
            var bestDistance = range;

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;

                // Only real players. Anything else that is not shootable is an
                // item, and backing away from a health pack would be a strange
                // way to spend a level — which is exactly what the first version
                // of this did.
                if (mobj == null || mobj == self || mobj.Health <= 0 || mobj.Player == null)
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
        /// How far past the preferred range this style is willing to stop and
        /// fight. Beyond this it keeps walking and shoots on the move.
        /// </summary>
        private float FightRangeFactor()
        {
            // Zero: the bot shoots on the move and does not stop to duel.
            //
            // Close-quarters circling was implemented and measured, and it is a
            // genuine loss on this map. Every version of it — any style, any
            // range, any budget — cost more time than it saved, because firing
            // while walking already works and the bot cannot shoot and hold a
            // ring at the same time without giving up the route. It stays in the
            // code, off, because it is the right idea for an arena and the wrong
            // one for a level with a way through it.
            return 0F;
        }

        /// <summary>
        /// Combat movement: keep the preferred distance and circle the target.
        ///
        /// This is the one place where "circling" is the right answer. A player
        /// fighting an imp does not stand still and does not charge: they hold a
        /// distance and strafe, so the monster's aim has to keep correcting. The
        /// same geometry that made the bot useless when it orbited a waypoint
        /// makes it competent when it orbits an enemy — the difference is whether
        /// the circle has a purpose.
        ///
        /// The preferred distance comes from temperament: an aggressive bot wants
        /// to be close, a cautious one wants the far edge of its weapon range.
        /// </summary>
        private void StrafeAround(TicCmd cmd, Mobj mobj, Mobj target, Angle aim)
        {
            var dx = target.X.ToFloat() - mobj.X.ToFloat();
            var dy = target.Y.ToFloat() - mobj.Y.ToFloat();
            var distance = (float)Math.Sqrt(dx * dx + dy * dy);

            var preferred = PreferredRange();

            // Forward/back to hold the distance: a proportional term with a dead
            // band, so the bot does not jitter in and out by a unit.
            var error = distance - preferred;
            var forward = 0;

            if (error > 24F)
            {
                forward = 40;
            }
            else if (error < -24F)
            {
                forward = -40;
            }

            // Sideways is the circle. The direction is held for a while and then
            // reversed, so the bot does not commit to orbiting one way into a
            // wall: a real player switches when the wall comes up.
            strafeTics++;

            if (strafeTics > 35 + (int)(temperament.Patience * 35))
            {
                strafeTics = 0;
                strafeDirection = -strafeDirection;
            }

            cmd.AngleTurn = (short)MathEx.Clamp(TurnTo(mobj.Angle, aim), -0x1000, 0x1000);
            cmd.ForwardMove = (sbyte)forward;
            cmd.SideMove = (sbyte)(strafeDirection * 40);

            if (FacingWithin(mobj.Angle, aim, 0x0600))
            {
                cmd.Buttons |= TicCmdButtons.Attack;
            }
        }

        /// <summary>
        /// The distance this bot wants to fight at, in map units.
        ///
        /// Aggression pulls it in, caution pushes it out, and the weapon sets the
        /// limits: a shotgun is useless at 700 units and a rocket launcher is
        /// suicide at 60.
        /// </summary>
        private float PreferredRange()
        {
            var weapon = player.ReadyWeapon;
            var min = 96F;
            var max = 512F;

            switch (weapon)
            {
                case WeaponType.Fist:
                case WeaponType.Chainsaw:
                    min = 40F;
                    max = 64F;
                    break;

                case WeaponType.Shotgun:
                case WeaponType.SuperShotgun:
                    min = 96F;
                    max = 320F;
                    break;

                case WeaponType.Missile:
                    // Never close: the blast hurts the bot too.
                    min = 200F;
                    max = 640F;
                    break;
            }

            var t = 0.5F - (temperament.Aggression - 0.5F) * 0.8F;

            if (t < 0F) t = 0F;
            else if (t > 1F) t = 1F;

            return min + (max - min) * t;
        }

        /// <summary>
        /// Picks a weapon for the distance to the target.
        ///
        /// The old rule was a fixed preference order, which meant a bot with a
        /// rocket launcher fired it at a demon two metres away and killed itself.
        /// This one drops weapons that are wrong for the range and then takes the
        /// best of what is left.
        /// </summary>
        private void SelectWeaponFor(float distance)
        {
            var order = new[]
            {
                WeaponType.Bfg,
                WeaponType.Plasma,
                WeaponType.SuperShotgun,
                WeaponType.Chaingun,
                WeaponType.Shotgun,
                WeaponType.Missile,
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

                if (!GoodAtRange(weapon, distance))
                {
                    continue;
                }

                var info = DoomInfo.WeaponInfos[(int)weapon];

                if (info.Ammo != AmmoType.NoAmmo && player.Ammo[(int)info.Ammo] <= 0)
                {
                    continue;
                }

                if (player.ReadyWeapon != weapon)
                {
                    player.PendingWeapon = weapon;
                }

                return;
            }
        }

        private static bool GoodAtRange(WeaponType weapon, float distance)
        {
            switch (weapon)
            {
                case WeaponType.Missile:
                    return distance > 180F;

                case WeaponType.Shotgun:
                case WeaponType.SuperShotgun:
                    return distance < 480F;

                case WeaponType.Bfg:
                    return distance < 900F;

                case WeaponType.Fist:
                case WeaponType.Chainsaw:
                    return distance < 96F;
            }

            return true;
        }

        /// <summary>
        /// Finds the pickup this bot most wants, if any is worth the walk.
        ///
        /// Score is desire times greed over distance, which is the simplest thing
        /// that behaves: a medikit next to the bot at low health wins, a clip on
        /// the far side of the level never does. Keys ignore the distance, because
        /// without them the level cannot be finished at all.
        /// </summary>
        private BotPickup? FindWantedItem(Mobj mobj, int sector)
        {
            BotPickup? best = null;
            var bestScore = 0F;

            var hereX = mobj.X.ToFloat();
            var hereY = mobj.Y.ToFloat();

            foreach (var thinker in world.Thinkers)
            {
                var thing = thinker as Mobj;

                if (thing == null || thing.Health <= 0)
                {
                    continue;
                }

                // Pickups are touchable but not shootable. That is a data-driven
                // test that works for any PWAD, and it excludes monsters,
                // decorations and projectiles in one go.
                if ((thing.Flags & MobjFlags.Special) == 0 ||
                    (thing.Flags & MobjFlags.Shootable) != 0)
                {
                    continue;
                }

                var kind = BotItemValue.Classify(thing);

                if (kind == BotItemValue.Kind.Nothing)
                {
                    continue;
                }

                var desire = BotItemValue.Desire(kind, player);

                if (desire <= 0F)
                {
                    continue;
                }

                var dx = thing.X.ToFloat() - hereX;
                var dy = thing.Y.ToFloat() - hereY;
                var distance = (float)Math.Sqrt(dx * dx + dy * dy);

                if (distance < 1F)
                {
                    continue;
                }

                // Worth is measured in map units of walking, which makes the
                // comparison against a path length honest instead of a fudge
                // factor. Greed stretches it: a greedy bot will walk twice as far
                // for the same thing.
                var worth = desire * ItemWorthPerDesire * (0.5F + temperament.Greed);

                if (kind == BotItemValue.Kind.Key)
                {
                    // A key is worth any walk: without it the level cannot end.
                    worth = float.MaxValue / 4F;
                }

                if (worth <= bestScore)
                {
                    continue;
                }

                var node = nav.NodeAt(thing.X, thing.Y);

                if (node < 0)
                {
                    continue;
                }

                bestScore = worth;
                best = new BotPickup { Node = node, Worth = worth };
            }

            return best;
        }

        /// <summary>
        /// Whether the bot should interrupt its run for loot at all. A rush bot
        /// only detours when it is in trouble; a curious one always will.
        /// </summary>
        private bool GrabWorthIt(Mobj mobj)
        {
            // Only when it matters. Loot that is merely nice to have costs more
            // than it is worth: an early version that grabbed whenever the style
            // allowed it turned a 0:43 run into 4:52, because the bot kept
            // planning trips to things it did not need.
            if (player.Health < 45 || player.ArmorPoints < 30)
            {
                return true;
            }

            return BotItemValue.AmmoDesire(player) > 0.7F;
        }

        /// <summary>A pickup the bot might go for.</summary>
        private struct BotPickup
        {
            public int Node;
            public float Worth;
        }

        /// <summary>
        /// Closest living monster within range, visible or not.</summary>
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
        /// Picks something to shoot at and returns the bearing to it, or null.
        ///
        /// The caller faces that bearing and moves toward its waypoint at the
        /// same time, and presses Attack once the aim is close enough. Shooting
        /// used to return a bool and stop all movement, which is why the bot
        /// stood in one spot for most of a level: a monster anywhere in sight
        /// took priority over going anywhere.
        /// </summary>
        private Angle? Shoot(Mobj self)
        {
            var weapon = player.ReadyWeapon;

            if (weapon == WeaponType.Fist || weapon == WeaponType.Chainsaw)
            {
                return MeleeTarget(self);
            }

            var info = DoomInfo.WeaponInfos[(int)weapon];
            if (info.Ammo != AmmoType.NoAmmo && player.Ammo[(int)info.Ammo] <= 0)
            {
                // Out of ammo: walk instead of standing there clicking.
                return null;
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
                shootTics = 0;
                combatTarget = null;
                return null;
            }

            combatTarget = best;

            // Track how long this bot has been shooting at all.
            //
            // Counting tics per target is not enough: if two monsters are both
            // in sight, the chosen one can alternate between them and the
            // per-target counter resets every tic. The bot then fires at
            // something it can never kill for the rest of the level — the trace
            // showed decision "shoot" with zero movement, for hundreds of tics.
            //
            // The counter is on the bot, so swapping targets cannot reset it.
            if (world.LevelTime < suppressShootingUntil)
            {
                return null;
            }

            if (++shootTics > GiveUpTics)
            {
                suppressShootingUntil = world.LevelTime + 3 * 35;
                shootTics = 0;
                engagedTics = 0;
                engagedTarget = null;
                return null;
            }

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
                return null;
            }

            return Geometry.PointToAngle(self.X, self.Y, best.X, best.Y);
        }

        /// <summary>Bearing to a monster close enough to punch, or null.</summary>
        private Angle? MeleeTarget(Mobj self)
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

                return Geometry.PointToAngle(self.X, self.Y, mobj.X, mobj.Y);
            }

            return null;
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

        /// <summary>
        /// True when the bot is standing at the exit.
        ///
        /// A walkover exit (W1) ends the level without any button being pressed,
        /// so "did this bot finish" cannot rely on the Use button alone. If the
        /// level ended while the bot was alive at the exit, it finished.
        /// </summary>
        public bool AtExit
        {
            get
            {
                var mobj = player.Mobj;

                if (mobj == null || exitSector < 0 || player.Health <= 0)
                {
                    return false;
                }

                if (mobj.Subsector.Sector.Number != exitSector)
                {
                    return false;
                }

                var dx = exitX - mobj.X.ToFloat();
                var dy = exitY - mobj.Y.ToFloat();

                return Math.Abs(dx) + Math.Abs(dy) <= ExitReachDistance * 2;
            }
        }

        /// <summary>Tics elapsed in the level, for reporting a run.</summary>
        public int LevelTime { get { return world.LevelTime; } }

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
