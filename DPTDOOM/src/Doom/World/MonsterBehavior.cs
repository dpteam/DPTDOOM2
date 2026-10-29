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
    public sealed class MonsterBehavior
    {
        private World world;

        public MonsterBehavior(World world)
        {
            this.world = world;

            InitVile();
            InitBossDeath();
            InitBrain();
        }



        ////////////////////////////////////////////////////////////
        // Sleeping monster
        ////////////////////////////////////////////////////////////

        private bool LookForPlayers(Mobj actor, bool allAround)
        {
            return LookForPlayers(actor, allAround, null);
        }

        private bool LookForPlayers(Mobj actor, bool allAround, LookExParams p)
        {
            var players = world.Options.Players;

            var count = 0;
            var stop = (actor.LastLook - 1) & 3;

            for (; ; actor.LastLook = (actor.LastLook + 1) & 3)
            {
                if (!players[actor.LastLook].InGame)
                {
                    continue;
                }

                if (count++ == 2 || actor.LastLook == stop)
                {
                    // Done looking.
                    return false;
                }

                var player = players[actor.LastLook];

                if (player.Health <= 0)
                {
                    // Player is dead.
                    continue;
                }

                if ((player.Mobj.Flags & MobjFlags.NeverTarget) != 0)
                {
                    // DECORATE's +NEVERTARGET: this actor is not picked as a target at all.
                    // Distinct from Ambush, which is about the monster doing the looking and only
                    // stops it noticing a sound, and from +NOTARGET, which is only about a monster
                    // refusing to switch its target to whoever hurt it. Checked after the
                    // dead-player skip and before the sight check, so the number of sight checks
                    // does not change for anybody who does not use the flag.
                    continue;
                }

                // The distance window of A_LookEx is checked before the sight test, which is what
                // ZDoom does and also the cheap order: a target outside the window is not worth
                // tracing a line for. A_Look has no window, and this block costs it nothing.
                var distance = Fixed.Zero;

                if (p != null)
                {
                    distance = Geometry.AproxDistance(
                        player.Mobj.X - actor.X,
                        player.Mobj.Y - actor.Y);

                    if (p.MaxSeeDist.Data != 0 && distance > p.MaxSeeDist)
                    {
                        continue;
                    }

                    if (p.MinSeeDist.Data != 0 && distance < p.MinSeeDist)
                    {
                        continue;
                    }
                }

                if (!world.VisibilityCheck.CheckSight(actor, player.Mobj))
                {
                    // Out of sight.
                    continue;
                }

                if (!allAround)
                {
                    if (p == null)
                    {
                        var angle = Geometry.PointToAngle(
                            actor.X, actor.Y,
                            player.Mobj.X, player.Mobj.Y) - actor.Angle;

                        if (angle > Angle.Ang90 && angle < Angle.Ang270)
                        {
                            var dist = Geometry.AproxDistance(
                                player.Mobj.X - actor.X,
                                player.Mobj.Y - actor.Y);

                            // If real close, react anyway.
                            if (dist > WeaponBehavior.MeleeRange)
                            {
                                // Behind back.
                                continue;
                            }
                        }
                    }
                    else if (p.Fov.Data != 0)
                    {
                        // A_LookEx's own field of view, in the angle it is given in. ZDoom reads
                        // "outside the cone" as more than half the field away, and keeps the
                        // vanilla rule that something right in front of its face is noticed
                        // whatever the cone says.
                        var away = Angle.Abs(Geometry.PointToAngle(
                            actor.X, actor.Y,
                            player.Mobj.X, player.Mobj.Y) - actor.Angle);

                        if (away > p.Fov / 2u)
                        {
                            if (p.MinSeeDist.Data != 0 ||
                                distance > WeaponBehavior.MeleeRange + actor.Radius)
                            {
                                continue;
                            }
                        }
                    }
                }

                actor.Target = player.Mobj;

                return true;
            }
        }


        public void Look(Mobj actor)
        {
            LookEx(actor, null);
        }

        /// <summary>
        /// A_AlertMonsters: propagate a noise through the same sector graph as a gunshot.
        ///
        /// A player is always the thing monsters should hear about. A monster instead tells them
        /// about its player target, unless AMF_TARGETEMITTER or AMF_TARGETNONPLAYER says otherwise.
        /// There is deliberately no fallback to the monster itself: with no suitable target the
        /// original action is silent, and treating it as a gunshot would wake the wrong monsters.
        /// </summary>
        public void AlertMonsters(Mobj actor, int flags)
        {
            Mobj target = null;
            var emitter = actor;

            if (actor.Player != null || (flags & AlertMonsterFlags.TargetEmitter) != 0)
            {
                target = actor;
            }
            else if (actor.Target != null &&
                     (actor.Target.Player != null ||
                      (flags & AlertMonsterFlags.TargetNonPlayer) != 0))
            {
                target = actor.Target;
            }

            if ((flags & AlertMonsterFlags.EmitFromTarget) != 0)
            {
                emitter = target;
            }

            if (target != null && emitter != null)
            {
                world.WeaponBehavior.NoiseAlert(target, emitter);
            }
        }

        /// <summary>
        /// A_Look, with the knobs DECORATE's A_LookEx adds.
        ///
        /// The null case is plain A_Look, and it has to stay **bit for bit** the same: it runs on
        /// every vanilla monster every tic, so its draws, its sight checks and its state changes go
        /// into the world hash. Every difference below is behind a flag or a parameter that A_Look
        /// never sets.
        /// </summary>
        public void LookEx(Mobj actor, LookExParams p)
        {
            var flags = p == null ? 0 : p.Flags;

            // Any shot will wake up.
            actor.Threshold = 0;

            // LOF_NOSOUNDCHECK: ignore the noise that woke the sector, and look only.
            if ((flags & LookExFlags.NoSoundCheck) == 0)
            {
                var target = actor.Subsector.Sector.SoundTarget;

                if (target != null &&
                    (target.Flags & MobjFlags.Shootable) != 0 &&
                    (target.Flags & MobjFlags.NeverTarget) == 0 &&
                    !TooFarToHear(actor, target, p))
                {
                    actor.Target = target;

                    if ((actor.Flags & MobjFlags.Ambush) != 0)
                    {
                        // Ambush means "check for yourself": the noise alone is not enough.
                        if (WithinSeeRange(actor, target, p) &&
                            world.VisibilityCheck.CheckSight(actor, target))
                        {
                            goto seeYou;
                        }
                    }
                    else
                    {
                        goto seeYou;
                    }
                }
            }

            // LOF_NOSIGHTCHECK: a monster that only reacts to noise never opens its eyes.
            if ((flags & LookExFlags.NoSightCheck) != 0)
            {
                return;
            }

            if (!LookForPlayers(actor, false, p))
            {
                return;
            }

            // Go into chase state.
            seeYou:
            if ((flags & LookExFlags.NoSeeSound) == 0 &&
                (actor.Info.SeeSound != 0 || ActorSounds.Has((int)actor.Type, ActorSoundSlot.See)))
            {
                int sound;

                switch (actor.Info.SeeSound)
                {
                    case Sfx.POSIT1:
                    case Sfx.POSIT2:
                    case Sfx.POSIT3:
                        sound = (int)Sfx.POSIT1 + world.Random.Next() % 3;
                        break;

                    case Sfx.BGSIT1:
                    case Sfx.BGSIT2:
                        sound = (int)Sfx.BGSIT1 + world.Random.Next() % 2;
                        break;

                    default:
                        sound = (int)actor.Info.SeeSound;
                        break;
                }

                // The override is applied here, after the variant draw, so a mod's sound cannot
                // change how many numbers the world generator uses.
                if ((flags & LookExFlags.FullVolSeeSound) != 0 ||
                    actor.Type == MobjType.Spider ||
                    actor.Type == MobjType.Cyborg)
                {
                    // Full volume for boss monsters, and for LOF_FULLVOLSEESOUND.
                    world.StartActorSound(actor, ActorSoundSlot.See, (Sfx)sound, SfxType.Diffuse);
                }
                else
                {
                    world.StartActorSound(actor, ActorSoundSlot.See, (Sfx)sound, SfxType.Voice);
                }
            }

            // LOF_NOJUMP: the target is taken, but the animation is left to the mod's own frames.
            if ((flags & LookExFlags.NoJump) == 0)
            {
                actor.SetState(p != null && p.SeeState != MobjState.Null
                    ? p.SeeState
                    : actor.Info.SeeState);
            }
        }

        /// <summary>
        /// Is the noise too far away to answer? A_LookEx's maxheardist, which A_Look does not have.
        /// </summary>
        private static bool TooFarToHear(Mobj actor, Mobj target, LookExParams p)
        {
            if (p == null || p.MaxHearDist.Data == 0)
            {
                return false;
            }

            return Geometry.AproxDistance(
                target.X - actor.X,
                target.Y - actor.Y) > p.MaxHearDist;
        }

        /// <summary>
        /// Is the noise's source inside the distance window A_LookEx asked for?
        ///
        /// Only an ambush monster asks this, and only because it is the one case where the engine
        /// has to decide about a target it was told about rather than one it saw.
        /// </summary>
        private static bool WithinSeeRange(Mobj actor, Mobj target, LookExParams p)
        {
            if (p == null)
            {
                return true;
            }

            var distance = Geometry.AproxDistance(
                target.X - actor.X,
                target.Y - actor.Y);

            if (p.MaxSeeDist.Data != 0 && distance > p.MaxSeeDist)
            {
                return false;
            }

            if (p.MinSeeDist.Data != 0 && distance < p.MinSeeDist)
            {
                return false;
            }

            return true;
        }



        ////////////////////////////////////////////////////////////
        // Monster AI
        ////////////////////////////////////////////////////////////

        private static readonly Fixed[] xSpeed =
        {
            new Fixed(Fixed.FracUnit),
            new Fixed(47000),
            new Fixed(0),
            new Fixed(-47000),
            new Fixed(-Fixed.FracUnit),
            new Fixed(-47000),
            new Fixed(0),
            new Fixed(47000)
        };

        private static readonly Fixed[] ySpeed =
        {
            new Fixed(0),
            new Fixed(47000),
            new Fixed(Fixed.FracUnit),
            new Fixed(47000),
            new Fixed(0),
            new Fixed(-47000),
            new Fixed(-Fixed.FracUnit),
            new Fixed(-47000)
        };

        private bool Move(Mobj actor)
        {
            if (actor.MoveDir == Direction.None)
            {
                return false;
            }

            if ((int)actor.MoveDir >= 8)
            {
                throw new Exception("Weird actor->movedir!");
            }

            var tryX = actor.X + actor.Info.Speed * xSpeed[(int)actor.MoveDir];
            var tryY = actor.Y + actor.Info.Speed * ySpeed[(int)actor.MoveDir];

            var tm = world.ThingMovement;

            var tryOk = tm.TryMove(actor, tryX, tryY);

            if (!tryOk)
            {
                // Open any specials.
                if ((actor.Flags & MobjFlags.Float) != 0 && tm.FloatOk)
                {
                    // Must adjust height.
                    if (actor.Z < tm.CurrentFloorZ)
                    {
                        actor.Z += ThingMovement.FloatSpeed;
                    }
                    else
                    {
                        actor.Z -= ThingMovement.FloatSpeed;
                    }

                    actor.Flags |= MobjFlags.InFloat;

                    return true;
                }

                if (tm.crossedSpecialCount == 0)
                {
                    return false;
                }

                actor.MoveDir = Direction.None;
                var good = false;
                while (tm.crossedSpecialCount-- > 0)
                {
                    var line = tm.crossedSpecials[tm.crossedSpecialCount];
                    // If the special is not a door that can be opened,
                    // return false.
                    if (world.MapInteraction.UseSpecialLine(actor, line, 0))
                    {
                        good = true;
                    }
                }
                return good;
            }
            else
            {
                actor.Flags &= ~MobjFlags.InFloat;
            }

            if ((actor.Flags & MobjFlags.Float) == 0)
            {
                actor.Z = actor.FloorZ;
            }

            return true;
        }


        private bool TryWalk(Mobj actor)
        {
            if (!Move(actor))
            {
                return false;
            }

            actor.MoveCount = world.Random.Next() & 15;

            return true;
        }


        private static readonly Direction[] opposite =
        {
            Direction.west,
            Direction.Southwest,
            Direction.South,
            Direction.Southeast,
            Direction.East,
            Direction.Northeast,
            Direction.North,
            Direction.Northwest,
            Direction.None
        };

        private static readonly Direction[] diags =
        {
            Direction.Northwest,
            Direction.Northeast,
            Direction.Southwest,
            Direction.Southeast
        };

        private readonly Direction[] choices = new Direction[3];

        private void NewChaseDir(Mobj actor)
        {
            if (actor.Target == null)
            {
                throw new Exception("Called with no target.");
            }

            var oldDir = actor.MoveDir;
            var turnAround = opposite[(int)oldDir];

            var deltaX = actor.Target.X - actor.X;
            var deltaY = actor.Target.Y - actor.Y;

            if (deltaX > Fixed.FromInt(10))
            {
                choices[1] = Direction.East;
            }
            else if (deltaX < Fixed.FromInt(-10))
            {
                choices[1] = Direction.west;
            }
            else
            {
                choices[1] = Direction.None;
            }

            if (deltaY < Fixed.FromInt(-10))
            {
                choices[2] = Direction.South;
            }
            else if (deltaY > Fixed.FromInt(10))
            {
                choices[2] = Direction.North;
            }
            else
            {
                choices[2] = Direction.None;
            }

            // Try direct route.
            if (choices[1] != Direction.None && choices[2] != Direction.None)
            {
                var a = (deltaY < Fixed.Zero) ? 1 : 0;
                var b = (deltaX > Fixed.Zero) ? 1 : 0;
                actor.MoveDir = diags[(a << 1) + b];

                if (actor.MoveDir != turnAround && TryWalk(actor))
                {
                    return;
                }
            }

            // Try other directions.
            if (world.Random.Next() > 200 || Fixed.Abs(deltaY) > Fixed.Abs(deltaX))
            {
                var temp = choices[1];
                choices[1] = choices[2];
                choices[2] = temp;
            }

            if (choices[1] == turnAround)
            {
                choices[1] = Direction.None;
            }

            if (choices[2] == turnAround)
            {
                choices[2] = Direction.None;
            }

            if (choices[1] != Direction.None)
            {
                actor.MoveDir = choices[1];

                if (TryWalk(actor))
                {
                    // Either moved forward or attacked.
                    return;
                }
            }

            if (choices[2] != Direction.None)
            {
                actor.MoveDir = choices[2];

                if (TryWalk(actor))
                {
                    return;
                }
            }

            // There is no direct path to the player, so pick another direction.
            if (oldDir != Direction.None)
            {
                actor.MoveDir = oldDir;

                if (TryWalk(actor))
                {
                    return;
                }
            }

            // Randomly determine direction of search.
            if ((world.Random.Next() & 1) != 0)
            {
                for (var dir = (int)Direction.East; dir <= (int)Direction.Southeast; dir++)
                {
                    if ((Direction)dir != turnAround)
                    {
                        actor.MoveDir = (Direction)dir;

                        if (TryWalk(actor))
                        {
                            return;
                        }
                    }
                }
            }
            else
            {
                for (var dir = (int)Direction.Southeast; dir != ((int)Direction.East - 1); dir--)
                {
                    if ((Direction)dir != turnAround)
                    {
                        actor.MoveDir = (Direction)dir;

                        if (TryWalk(actor))
                        {
                            return;
                        }
                    }
                }
            }

            if (turnAround != Direction.None)
            {
                actor.MoveDir = turnAround;

                if (TryWalk(actor))
                {
                    return;
                }
            }

            // Can not move.
            actor.MoveDir = Direction.None;
        }


        /// <summary>
        /// Whether the actor's target is within melee reach, for A_JumpIfTargetInsideMeleeRange.
        ///
        /// The same test the melee attacks use, exposed rather than copied: a mod that branches on
        /// "can I punch it" must get the same answer as the punch, or the branch leads to an attack
        /// that misses.
        /// </summary>
        public bool IsInMeleeRange(Mobj actor)
        {
            return CheckMeleeRange(actor);
        }

        private bool CheckMeleeRange(Mobj actor)
        {
            if (actor.Target == null)
            {
                return false;
            }

            var target = actor.Target;

            var dist = Geometry.AproxDistance(target.X - actor.X, target.Y - actor.Y);

            if (dist >= WeaponBehavior.MeleeRange - Fixed.FromInt(20) + target.Info.Radius)
            {
                return false;
            }

            if (!world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                return false;
            }

            return true;
        }


        private bool CheckMissileRange(Mobj actor)
        {
            if (!world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                return false;
            }

            if ((actor.Flags & MobjFlags.JustHit) != 0)
            {
                // The target just hit the enemy, so fight back!
                actor.Flags &= ~MobjFlags.JustHit;

                return true;
            }

            if (actor.ReactionTime > 0)
            {
                // Do not attack yet
                return false;
            }

            // OPTIMIZE:
            //     Get this from a global checksight.
            var dist = Geometry.AproxDistance(
                actor.X - actor.Target.X,
                actor.Y - actor.Target.Y) - Fixed.FromInt(64);

            if (actor.Info.MeleeState == 0)
            {
                // No melee attack, so fire more.
                dist -= Fixed.FromInt(128);
            }

            var attackDist = dist.Data >> 16;

            if (actor.Type == MobjType.Vile)
            {
                if (attackDist > 14 * 64)
                {
                    // Too far away.
                    return false;
                }
            }

            if (actor.Type == MobjType.Undead)
            {
                if (attackDist < 196)
                {
                    // Close for fist attack.
                    return false;
                }

                attackDist >>= 1;
            }


            if (actor.Type == MobjType.Cyborg ||
                actor.Type == MobjType.Spider ||
                actor.Type == MobjType.Skull)
            {
                attackDist >>= 1;
            }

            if (attackDist > 200)
            {
                attackDist = 200;
            }

            if (actor.Type == MobjType.Cyborg && attackDist > 160)
            {
                attackDist = 160;
            }

            if (world.Random.Next() < attackDist)
            {
                return false;
            }

            return true;
        }


        public void Chase(Mobj actor)
        {
            Chase(actor, 0, -1, -1);
        }

        /// <summary>
        /// A_Chase with its arguments: flags, and states to use instead of the actor's own.
        ///
        /// With no flags and -1 for both states this is plain A_Chase, and it has to stay **bit
        /// for bit** the same: every monster in the IWAD runs it every tic, so its random draws,
        /// its sight checks and its state changes go into the world hash.
        ///
        /// -1 means "the actor's own state" because MobjState.Null is 0 and therefore a legal
        /// value; using it as the sentinel would make "no melee attack" indistinguishable from
        /// "the actor's melee state", which are opposite instructions.
        /// </summary>
        public void Chase(Mobj actor, int flags, int meleeState, int missileState)
        {
            if (actor.ReactionTime > 0)
            {
                actor.ReactionTime--;
            }

            // Modify target threshold.
            if (actor.Threshold > 0)
            {
                if (actor.Target == null || actor.Target.Health <= 0)
                {
                    actor.Threshold = 0;
                }
                else
                {
                    actor.Threshold--;
                }
            }

            // CHF_NIGHTMAREFAST: the generic speed-up a mod asks for by name, in nightmare or fast
            // mode. Separate from the vanilla one, which halves only the sergeant's frames and
            // happens where the state is set.
            if ((flags & ChaseFlags.NightmareFast) != 0 &&
                (world.Options.Skill == GameSkill.Nightmare || world.Options.FastMonsters) &&
                actor.Tics > 3)
            {
                actor.Tics -= actor.Tics / 2;

                if (actor.Tics < 3)
                {
                    actor.Tics = 3;
                }
            }

            // Turn towards movement direction if not there yet.
            if ((flags & ChaseFlags.NoDirectionTurn) == 0 && (int)actor.MoveDir < 8)
            {
                actor.Angle = new Angle((int)actor.Angle.Data & (7 << 29));

                var delta = (int)(actor.Angle - new Angle((int)actor.MoveDir << 29)).Data;

                if (delta > 0)
                {
                    actor.Angle -= new Angle(Angle.Ang90.Data / 2);
                }
                else if (delta < 0)
                {
                    actor.Angle += new Angle(Angle.Ang90.Data / 2);
                }
            }

            var allAround = (flags & ChaseFlags.DontLookAllAround) == 0;

            if (actor.Target == null || (actor.Target.Flags & MobjFlags.Shootable) == 0)
            {
                // Look for a new target.
                if (LookForPlayers(actor, allAround))
                {
                    // Got a new target.
                    return;
                }

                actor.SetState(actor.Info.SpawnState);

                return;
            }

            // Do not attack twice in a row.
            if ((actor.Flags & MobjFlags.JustAttacked) != 0)
            {
                actor.Flags &= ~MobjFlags.JustAttacked;

                if (world.Options.Skill != GameSkill.Nightmare &&
                    !world.Options.FastMonsters &&
                    (flags & ChaseFlags.DontMove) == 0 &&
                    (flags & ChaseFlags.NoPostAttackTurn) == 0 &&
                    (flags & ChaseFlags.StopIfBlocked) == 0)
                {
                    NewChaseDir(actor);
                }

                return;
            }

            var melee = meleeState >= 0 ? meleeState : (int)actor.Info.MeleeState;
            var missile = missileState >= 0 ? missileState : (int)actor.Info.MissileState;

            // Check for melee attack.
            if (melee != 0 && CheckMeleeRange(actor))
            {
                if (actor.Info.AttackSound != 0 ||
                    ActorSounds.Has((int)actor.Type, ActorSoundSlot.Attack))
                {
                    world.StartActorSound(actor, ActorSoundSlot.Attack,
                        actor.Info.AttackSound, SfxType.Weapon);
                }

                actor.SetStateNumber(melee);

                return;
            }

            // Check for missile attack.
            if (missile != 0)
            {
                if (world.Options.Skill < GameSkill.Nightmare &&
                    !world.Options.FastMonsters &&
                    actor.MoveCount != 0)
                {
                    goto noMissile;
                }

                if (!CheckMissileRange(actor))
                {
                    goto noMissile;
                }

                actor.SetStateNumber(missile);
                actor.Flags |= MobjFlags.JustAttacked;

                return;
            }

            noMissile:
            // Possibly choose another target.
            if (world.Options.NetGame &&
                actor.Threshold == 0 &&
                !world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                if (LookForPlayers(actor, allAround))
                {
                    // Got a new target.
                    return;
                }
            }

            // Chase towards player.
            //
            // The two flag guards sit inside the conditions rather than around the block, so that
            // with no flags the short-circuit order - and therefore whether Move is called at all -
            // is exactly what it was.
            if ((flags & ChaseFlags.DontMove) != 0)
            {
                // A turret still counts its move count down, which is what keeps its attack rhythm
                // the same as a monster that walks.
                if (actor.MoveCount > 0)
                {
                    actor.MoveCount--;
                }
            }
            else if ((--actor.MoveCount < 0 && (flags & ChaseFlags.NoRandomTurn) == 0) ||
                     (!Move(actor) && (flags & ChaseFlags.StopIfBlocked) == 0))
            {
                NewChaseDir(actor);
            }

            // Make active sound.
            //
            // The order of the conditions is not cosmetic: `ActiveSound != 0` is evaluated
            // first, so for every vanilla actor exactly as many world-random numbers are drawn as
            // before, whether or not a mod named a sound. The flag comes before the draw because
            // it asks for the sound not to happen at all - drawing a number to throw it away would
            // be worse than not drawing it.
            if ((flags & ChaseFlags.NoPlayActive) == 0 &&
                (actor.Info.ActiveSound != 0 ||
                 ActorSounds.Has((int)actor.Type, ActorSoundSlot.Active)) &&
                world.Random.Next() < 3)
            {
                world.StartActorSound(actor, ActorSoundSlot.Active,
                    actor.Info.ActiveSound, SfxType.Voice);
            }
        }



        ////////////////////////////////////////////////////////////
        // Monster death
        ////////////////////////////////////////////////////////////

        public void Pain(Mobj actor)
        {
            if ((actor.Flags & MobjFlags.NoPain) != 0)
            {
                // DECORATE's +NOPAIN. The pain state is never entered from damage, so this
                // action normally cannot run - but a mod's own chain may reach it directly, and a
                // silent monster that still cries out is not what the flag means.
                return;
            }

            if (actor.Info.PainSound != 0 ||
                ActorSounds.Has((int)actor.Type, ActorSoundSlot.Pain))
            {
                world.StartActorSound(actor, ActorSoundSlot.Pain,
                    actor.Info.PainSound, SfxType.Voice);
            }
        }

        public void Scream(Mobj actor)
        {
            int sound;

            switch (actor.Info.DeathSound)
            {
                case 0:
                    // No death sound of the actor's own. A mod may still have named one, and then
                    // there is something to play - so this is not a plain return.
                    if (!ActorSounds.Has((int)actor.Type, ActorSoundSlot.Death))
                    {
                        return;
                    }

                    sound = 0;
                    break;

                case Sfx.PODTH1:
                case Sfx.PODTH2:
                case Sfx.PODTH3:
                    sound = (int)Sfx.PODTH1 + world.Random.Next() % 3;
                    break;

                case Sfx.BGDTH1:
                case Sfx.BGDTH2:
                    sound = (int)Sfx.BGDTH1 + world.Random.Next() % 2;
                    break;

                default:
                    sound = (int)actor.Info.DeathSound;
                    break;
            }

            // After the variant draw, for the same reason as the see sound: a mod's sound must not
            // move the world generator on by a different number of steps.
            if (actor.Type == MobjType.Spider || actor.Type == MobjType.Cyborg)
            {
                // Full volume.
                world.StartActorSound(actor, ActorSoundSlot.Death, (Sfx)sound, SfxType.Diffuse);
            }
            else
            {
                world.StartActorSound(actor, ActorSoundSlot.Death, (Sfx)sound, SfxType.Voice);
            }
        }

        public void XScream(Mobj actor)
        {
            world.StartSound(actor, Sfx.SLOP, SfxType.Voice);
        }

        public void Fall(Mobj actor)
        {
            // Actor is on ground, it can be walked over.
            actor.Flags &= ~MobjFlags.Solid;
        }



        ////////////////////////////////////////////////////////////
        // Monster attack
        ////////////////////////////////////////////////////////////

        public void FaceTarget(Mobj actor)
        {
            FaceTarget(actor, Angle.Ang0, Angle.Ang0);
        }

        /// <summary>
        /// A_FaceTarget with its two useful arguments: a limit on how far the actor may turn, and
        /// an offset added to the resulting angle.
        ///
        /// ZDoom's own A_FaceTarget signature is (max_turn, max_pitch, ang_offset, ...), so a mod
        /// writing A_FaceTarget(90) is asking for a gradual turn of at most 90 degrees per call,
        /// not for an offset of 90 degrees. Reading that argument as an offset would make a
        /// turreting monster snap round instantly, which is the opposite of what it asked for.
        ///
        /// With both zero this is plain A_FaceTarget, and it has to stay **bit for bit** the same:
        /// every vanilla monster that attacks calls it, so its random draws and its angle go into
        /// the world hash.
        /// </summary>
        public void FaceTarget(Mobj actor, Angle maxTurn, Angle offset)
        {
            FaceTarget(actor, actor.Target, maxTurn, offset);
        }

        /// <summary>
        /// Turns toward a given actor, which is A_FaceTracer when that actor is the tracer.
        ///
        /// The target is a parameter rather than always <see cref="Mobj.Target"/> so that a mod can
        /// face the thing it is following without first retargeting itself - which would change
        /// what it attacks on the next tic.
        /// </summary>
        public void FaceTarget(Mobj actor, Mobj target, Angle maxTurn, Angle offset)
        {
            if (target == null)
            {
                return;
            }

            actor.Flags &= ~MobjFlags.Ambush;

            var toTarget = Geometry.PointToAngle(
                actor.X, actor.Y,
                target.X, target.Y);

            if (maxTurn.Data == 0)
            {
                actor.Angle = toTarget + offset;
            }
            else
            {
                // The signed shortest difference, which is what an angle subtraction gives: the
                // data wraps at a full turn, so reading it as an int is already the short way
                // round and is in the range -180 to 180 degrees.
                var delta = (int)(toTarget - actor.Angle).Data;

                var magnitude = delta < 0 ? -(long)delta : delta;

                if (magnitude > maxTurn.Data)
                {
                    // Turn as far as allowed, in the direction the target lies. The offset is
                    // added to the step, which is what ZDoom does and not an accident: it keeps
                    // an offsetted turret turning at the same rate as an offsetless one.
                    if (delta < 0)
                    {
                        actor.Angle -= maxTurn + offset;
                    }
                    else
                    {
                        actor.Angle += maxTurn + offset;
                    }
                }
                else
                {
                    actor.Angle = toTarget + offset;
                }
            }

            var random = world.Random;

            if ((target.Flags & MobjFlags.Shadow) != 0)
            {
                actor.Angle += new Angle((random.Next() - random.Next()) << 21);
            }
        }

        /// <summary>
        /// A_MonsterRefire: keep shooting unless the target is gone, dead or out of sight.
        ///
        /// The chance is a *keep going* chance, and it is drawn **before** the sight test - that order
        /// is what the original does, and reversing it would make a monster with a dead target fire
        /// one more shot instead of leaving. Facing happens first, always, so a monster that leaves
        /// for its label is still looking at where its target was.
        ///
        /// The draw comes from the world generator. GZDoom keeps a separate stream for this action,
        /// which this engine does not have; the difference is invisible inside one session and the
        /// same on every end of it, which is what lockstep needs.
        /// </summary>
        public void MonsterRefire(Mobj actor, int chance, int jumpState)
        {
            FaceTarget(actor);

            if (world.Random.Next() < chance)
            {
                return;
            }

            var target = actor.Target;

            if (target == null || target.Health <= 0 ||
                !world.VisibilityCheck.CheckSight(actor, target))
            {
                actor.JumpToState(jumpState);
            }
        }

        /// <summary>
        /// A_Explode: a blast around this actor, with its own damage and radius.
        ///
        /// A separate name from <see cref="Explode(Mobj)"/> on purpose, because they are not the
        /// same thing: Explode is the barrel's own action and passes the barrel's **target** as the
        /// source of the blast, which is what makes a barrel chain-reaction and infighting work.
        /// A_Explode is the actor blowing up where it stands, with itself as the source. Reusing
        /// one name for both would mean reading "Explode" and getting the wrong one.
        ///
        /// ZDoom's signature is A_Explode(damage, distance, flags, alert, fulldamagedistance, ...)
        /// and distance defaults to damage. Two things that are not obvious and are the reason this
        /// exists at all:
        ///
        ///  - damage and radius are **different numbers**. Vanilla ties them together because it
        ///    only ever explodes rockets, and a mod that writes A_Explode(128, 64) wants full
        ///    damage in a small circle, not half damage in a big one.
        ///  - ZDoom's default flags value is XF_HURTSOURCE, the opposite of vanilla: a barrel or a
        ///    grenade is meant to hurt whoever caused it. A mod that writes A_Explode() gets the
        ///    barrel behaviour.
        ///
        /// The damage is applied by the same routine a rocket uses, so pain, death and infighting
        /// are the ordinary ones rather than a second set of rules.
        /// </summary>
        public void RadiusExplode(Mobj actor, int damage, int radius, bool hurtSource)
        {
            if (radius <= 0)
            {
                // A zero or negative distance is ZDoom's way of saying "the same as the damage",
                // not "explode nowhere". A mod that writes A_Explode(128) means an explosion.
                radius = damage;
            }

            world.ThingInteraction.RadiusAttack(actor, actor, damage, radius, hurtSource);
        }

        /// <summary>
        /// A_ClearTarget: forget the target.
        ///
        /// ZDoom clears three fields - target, lastheard and lastenemy. This engine keeps no
        /// per-actor memory of either: a noise lives on the sector (<see cref="Sector.SoundTarget"/>),
        /// so there is nothing else here to forget, and writing code for fields that do not exist
        /// would only look thorough.
        /// </summary>
        public void ClearTarget(Mobj actor)
        {
            actor.Target = null;
        }

        /// <summary>
        /// A_Recoil: throw the actor backwards along its own facing.
        ///
        /// Thrust, not teleport: it is a velocity added to whatever the actor already had, which is
        /// what makes a shotgun push and a rocket jump work. Not divided by mass either - ZDoom
        /// does not, and a mod that tuned the number expects the number it wrote.
        /// </summary>
        public void Recoil(Mobj actor, Fixed distance)
        {
            actor.MomX -= distance * Trig.Cos(actor.Angle);
            actor.MomY -= distance * Trig.Sin(actor.Angle);
        }

        /// <summary>
        /// A_Stop: zero all velocity. Presentation-only idle handling in ZDoom is not part of the
        /// simulation here, so there is no second player-state path to update.
        /// </summary>
        public void Stop(Mobj actor)
        {
            actor.MomX = Fixed.Zero;
            actor.MomY = Fixed.Zero;
            actor.MomZ = Fixed.Zero;
        }

        /// <summary>
        /// A_NoBlocking: make a corpse stop blocking movement.
        ///
        /// DropItem metadata has no counterpart in this engine, and DECORATE already reports that
        /// property when it appears. There is deliberately no invented drop list here: the action's
        /// visible, ordinary behaviour is the SOLID flag being cleared.
        /// </summary>
        public void NoBlocking(Mobj actor)
        {
            actor.Flags &= ~MobjFlags.Solid;
        }

        /// <summary>
        /// A_SetHealth: set health, but never below one. ZDoom clamps to one rather than killing
        /// the actor, so a mod's A_SetHealth(0) is not an alternative A_Die.
        /// </summary>
        public void SetHealth(Mobj actor, int health)
        {
            var newHealth = health <= 0 ? 1 : health;

            actor.Health = newHealth;

            if (actor.Player != null)
            {
                actor.Player.Health = newHealth;

                if (actor.Player.Mobj != null)
                {
                    actor.Player.Mobj.Health = newHealth;
                }
            }
        }

        /// <summary>A_SetAngle: replace the actor's angle.</summary>
        public void SetAngle(Mobj actor, Angle angle)
        {
            actor.Angle = angle;
        }

        /// <summary>
        /// A_ChangeVelocity: add or replace velocity, optionally rotating x/y by the actor's angle.
        /// CVF_RELATIVE=1, CVF_REPLACE=2. The rotation follows ZDoom's formula, not the different
        /// left/right convention used by A_SpawnItemEx.
        /// </summary>
        public void ChangeVelocity(Mobj actor, Fixed x, Fixed y, Fixed z, int flags)
        {
            var velocityX = x;
            var velocityY = y;

            if ((flags & 1) != 0)
            {
                var sin = Trig.Sin(actor.Angle);
                var cos = Trig.Cos(actor.Angle);

                velocityX = x * cos - y * sin;
                velocityY = x * sin + y * cos;
            }

            if ((flags & 2) != 0)
            {
                actor.MomX = velocityX;
                actor.MomY = velocityY;
                actor.MomZ = z;
            }
            else
            {
                actor.MomX += velocityX;
                actor.MomY += velocityY;
                actor.MomZ += z;
            }
        }

        /// <summary>A_ScaleVelocity: multiply all three velocity components by one factor.</summary>
        public void ScaleVelocity(Mobj actor, Fixed scale)
        {
            actor.MomX *= scale;
            actor.MomY *= scale;
            actor.MomZ *= scale;
        }

        /// <summary>
        /// A_StartSound: play a sound by name with a CHAN_* slot and attenuation.
        ///
        /// The mixer uses its existing priority channels rather than pretending to implement
        /// ZDoom's full sound policies. CHAN_WEAPON/VOICE/ITEM/BODY map onto the corresponding
        /// Doom channels; CHAN_AUTO and the auxiliary slots use Misc. ATTN_NONE means diffuse,
        /// which is the mixer's full-volume path. Volume is a fixed-point factor from 0 to 1,
        /// converted to the mixer's 0..100 scale, so randomised volumes still work.
        /// </summary>
        public void StartSound(Mobj actor, string soundName, int channel, int flags,
                               Fixed volume, Fixed attenuation)
        {
            if (string.IsNullOrEmpty(soundName))
            {
                return;
            }

            var type = SfxType.Misc;

            switch (channel)
            {
                case 1: // CHAN_WEAPON
                    type = SfxType.Weapon;
                    break;

                case 2: // CHAN_VOICE
                    type = SfxType.Voice;
                    break;

                case 0: // CHAN_AUTO
                case 3: // CHAN_ITEM
                case 4: // CHAN_BODY
                default:
                    type = SfxType.Misc;
                    break;
            }

            if (attenuation.Data == 0)
            {
                type = SfxType.Diffuse;
            }

            var percent = (int)(((long)volume.Data * 100 + Fixed.FracUnit / 2) >> Fixed.FracBits);

            if (percent < 0)
            {
                percent = 0;
            }
            else if (percent > 100)
            {
                percent = 100;
            }

            world.StartSound(actor, soundName, type, percent);
        }


        /// <summary>
        /// A_CustomPunch: a melee hitscan with a custom damage amount, puff and range.
        ///
        /// The random multiplier is 1..8, as in ZDoom. When the caller passes norandom=true it is
        /// skipped entirely, including its draw; that is the form ademo1.wad uses, and skipping the
        /// draw is important because the multiplier belongs to the world simulation.
        ///
        /// Ammo, life stealing and dagger/pull-in behaviour are not silently approximated: their
        /// flags are reported by the converter. NoTurn and NoRandomPuffZ are implemented.
        /// </summary>
        public void CustomPunch(Mobj actor, int damage, bool noRandom, int flags,
                                MobjType puffType, Fixed range, string hitSound, string missSound)
        {
            if (actor.Player == null)
            {
                // ZDoom's A_CustomPunch is a player action, not a monster attack. A mod that puts
                // it on a monster gets no phantom melee hit; the converter still accepts the call,
                // because actor types are resolved before the world exists.
                return;
            }

            var random = world.Random;

            if (!noRandom)
            {
                damage *= random.Next() % 8 + 1;
            }

            if (range.Data == 0)
            {
                range = WeaponBehavior.MeleeRange;
            }

            var angle = actor.Angle +
                        new Angle((random.Next() - random.Next()) << 18);

            var slope = world.Hitscan.AimLineAttack(actor, angle, range);
            world.Hitscan.LineAttack(actor, angle, range, slope, damage, puffType,
                (flags & CustomPunchFlags.NoRandomPuffZ) != 0);

            if (world.Hitscan.LineTarget != null)
            {
                if (!string.IsNullOrEmpty(hitSound))
                {
                    world.StartSound(actor, hitSound, SfxType.Weapon, 100);
                }

                if ((flags & CustomPunchFlags.NoTurn) == 0)
                {
                    actor.Angle = Geometry.PointToAngle(
                        actor.X, actor.Y,
                        world.Hitscan.LineTarget.X, world.Hitscan.LineTarget.Y);
                }
            }
            else if (!string.IsNullOrEmpty(missSound))
            {
                world.StartSound(actor, missSound, SfxType.Weapon, 100);
            }
        }

        /// <summary>
        /// A_CustomBulletAttack: a burst of hitscan bullets from any actor, aimed at a reference
        /// actor or along its own facing.
        ///
        /// The reference is what the actor turns to and aims at, and its default is
        /// AAPTR_TARGET. Without a reference and without CBAF_AIMFACING the original does nothing
        /// at all - it does not fall back to firing straight ahead - so neither does this.
        /// </summary>
        public void CustomBulletAttack(Mobj actor, Fixed spreadXY, Fixed spreadZ, int numBullets,
                                       int damage, MobjType puffType, Fixed range, int flags,
                                       Mobj reference)
        {
            var aimFacing = (flags & CustomBulletAttackFlags.AimFacing) != 0;

            if (reference == null && !aimFacing)
            {
                return;
            }

            if (range.Data == 0)
            {
                // Zero means "the default range", not "no range at all". A bullet with a range of
                // zero travels nowhere, which looks exactly like an action that does nothing.
                range = WeaponBehavior.MissileRange;
            }

            if (!aimFacing)
            {
                // ZDoom's A_Face(reference): a full turn, not a limited one.
                FaceTarget(actor, reference, Angle.Ang0, Angle.Ang0);
            }

            var angle = actor.Angle;
            var slope = Fixed.Zero;

            if ((flags & CustomBulletAttackFlags.NoPitch) == 0)
            {
                slope = world.Hitscan.AimLineAttack(actor, angle, WeaponBehavior.MissileRange);
            }

            PlayAttackSound(actor);

            BulletVolley(actor, angle, slope, spreadXY, spreadZ, numBullets, damage, puffType,
                range, (flags & CustomBulletAttackFlags.ExplicitAngle) != 0,
                (flags & CustomBulletAttackFlags.NoRandom) != 0,
                (flags & CustomBulletAttackFlags.NoRandomPuffZ) != 0);
        }

        /// <summary>
        /// Fires the bullets, once the caller has decided the angle, the slope and the policy.
        ///
        /// Shared by A_CustomBulletAttack and A_FireBullets because the volley itself is identical;
        /// only the aiming and the ammo differ. The flags arrive as booleans rather than as a number
        /// on purpose: the two actions number their flags differently (NORANDOMPUFFZ is 16 for one
        /// and 32 for the other), and passing the raw value across would apply the wrong bit.
        ///
        /// The random draws are the world generator's and the order is the original's: per bullet
        /// the angle spread, then the slope spread, then the damage multiplier. Lockstep needs that
        /// order as much as it needs the values.
        /// </summary>
        public void BulletVolley(Mobj actor, Angle angle, Fixed slope,
                                 Fixed spreadXY, Fixed spreadZ, int numBullets, int damage,
                                 MobjType puffType, Fixed range, bool explicitAngle,
                                 bool noRandom, bool noRandomPuffZ)
        {
            var random = world.Random;

            if (numBullets <= 0)
            {
                numBullets = 1;
            }

            for (var i = 0; i < numBullets; i++)
            {
                var bulletAngle = angle;
                var bulletSlope = slope;

                if (explicitAngle)
                {
                    bulletAngle += AngleFromDegrees(spreadXY);
                    bulletSlope += spreadZ;
                }
                else
                {
                    // ZDoom's Random2 is a bell curve in -255..255 made of two draws, so a bullet
                    // lands near the middle far more often than at the edge. Two draws, not one:
                    // the shape is part of the feel and of the world hash.
                    bulletAngle += AngleFromDegrees(spreadXY, random.Next() - random.Next());
                    bulletSlope += new Fixed((int)((long)spreadZ.Data * (random.Next() - random.Next()) / 255));
                }

                var bulletDamage = damage;

                if (!noRandom)
                {
                    bulletDamage *= random.Next() % 3 + 1;
                }

                world.Hitscan.LineAttack(actor, bulletAngle, range, bulletSlope, bulletDamage,
                    puffType, noRandomPuffZ);
            }
        }

        /// <summary>
        /// A spread in degrees, scaled by a value in -255..255 the way ZDoom does it.
        ///
        /// Integer arithmetic only: the result goes into the world hash, so it must not depend on
        /// how a platform rounds a double.
        /// </summary>
        private static Angle AngleFromDegrees(Fixed degrees, int scale = 255)
        {
            const long fullTurn = 0x100000000L;

            var numerator = (long)degrees.Data * fullTurn * scale;
            var denominator = 360L * Fixed.FracUnit * 255L;

            if (numerator >= 0)
            {
                numerator += denominator / 2;
            }
            else
            {
                numerator -= denominator / 2;
            }

            return new Angle((uint)(numerator / denominator));
        }

        private void PlayAttackSound(Mobj actor)
        {
            if (actor.Info.AttackSound != 0 ||
                ActorSounds.Has((int)actor.Type, ActorSoundSlot.Attack))
            {
                world.StartActorSound(actor, ActorSoundSlot.Attack,
                    actor.Info.AttackSound, SfxType.Weapon);
            }
        }

        /// <summary>
        /// A_CustomMeleeAttack: face the target and, if it is in melee range, damage it.
        ///
        /// Kept separate from the vanilla TroopAttack/SargAttack routines because the damage is a
        /// parameter of the frame, not a dice formula owned by the monster type. The range test is
        /// shared with those routines, so a mod's melee reach is the same as a vanilla monster's.
        /// Sounds are optional and named through SNDINFO, exactly like A_PlaySound.
        /// </summary>
        public void CustomMeleeAttack(Mobj actor, int damage, string hitSound, string missSound)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (CheckMeleeRange(actor))
            {
                if (!string.IsNullOrEmpty(hitSound))
                {
                    world.StartSound(actor, hitSound, SfxType.Weapon, 100);
                }

                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);
            }
            else if (!string.IsNullOrEmpty(missSound))
            {
                world.StartSound(actor, missSound, SfxType.Weapon, 100);
            }
        }


        public void PosAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            var angle = actor.Angle;
            var slope = world.Hitscan.AimLineAttack(actor, angle, WeaponBehavior.MissileRange);

            world.StartSound(actor, Sfx.PISTOL, SfxType.Weapon);

            var random = world.Random;
            angle += new Angle((random.Next() - random.Next()) << 20);
            var damage = ((random.Next() % 5) + 1) * 3;

            world.Hitscan.LineAttack(actor, angle, WeaponBehavior.MissileRange, slope, damage);
        }


        public void SPosAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            world.StartSound(actor, Sfx.SHOTGN, SfxType.Weapon);

            FaceTarget(actor);

            var center = actor.Angle;
            var slope = world.Hitscan.AimLineAttack(actor, center, WeaponBehavior.MissileRange);

            var random = world.Random;

            for (var i = 0; i < 3; i++)
            {
                var angle = center + new Angle((random.Next() - random.Next()) << 20);
                var damage = ((random.Next() % 5) + 1) * 3;

                world.Hitscan.LineAttack(actor, angle, WeaponBehavior.MissileRange, slope, damage);
            }
        }


        public void CPosAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            world.StartSound(actor, Sfx.SHOTGN, SfxType.Weapon);

            FaceTarget(actor);

            var center = actor.Angle;
            var slope = world.Hitscan.AimLineAttack(actor, center, WeaponBehavior.MissileRange);

            var random = world.Random;
            var angle = center + new Angle((random.Next() - random.Next()) << 20);
            var damage = ((random.Next() % 5) + 1) * 3;

            world.Hitscan.LineAttack(actor, angle, WeaponBehavior.MissileRange, slope, damage);
        }


        public void CPosRefire(Mobj actor)
        {
            // Keep firing unless target got out of sight.
            FaceTarget(actor);

            if (world.Random.Next() < 40)
            {
                return;
            }

            if (actor.Target == null ||
                actor.Target.Health <= 0 ||
                !world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                actor.SetState(actor.Info.SeeState);
            }
        }


        public void TroopAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (CheckMeleeRange(actor))
            {
                world.StartSound(actor, Sfx.CLAW, SfxType.Weapon);

                var damage = (world.Random.Next() % 8 + 1) * 3;
                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);

                return;
            }

            // Launch a missile.
            world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Troopshot);
        }


        public void SargAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (CheckMeleeRange(actor))
            {
                var damage = ((world.Random.Next() % 10) + 1) * 4;
                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);
            }
        }


        public void HeadAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (CheckMeleeRange(actor))
            {
                var damage = (world.Random.Next() % 6 + 1) * 10;
                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);

                return;
            }

            // Launch a missile.
            world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Headshot);
        }


        public void BruisAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            if (CheckMeleeRange(actor))
            {
                world.StartSound(actor, Sfx.CLAW, SfxType.Weapon);

                var damage = (world.Random.Next() % 8 + 1) * 10;
                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);

                return;
            }

            // Launch a missile.
            world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Bruisershot);
        }


        private static readonly Fixed skullSpeed = Fixed.FromInt(20);

        public void SkullAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            var dest = actor.Target;

            actor.Flags |= MobjFlags.SkullFly;

            world.StartActorSound(actor, ActorSoundSlot.Attack, actor.Info.AttackSound,
                SfxType.Voice);

            FaceTarget(actor);

            var angle = actor.Angle;
            actor.MomX = skullSpeed * Trig.Cos(angle);
            actor.MomY = skullSpeed * Trig.Sin(angle);

            var dist = Geometry.AproxDistance(dest.X - actor.X, dest.Y - actor.Y);

            var num = (dest.Z + (dest.Height >> 1) - actor.Z).Data;
            var den = dist.Data / skullSpeed.Data;
            if (den < 1)
            {
                den = 1;
            }

            actor.MomZ = new Fixed(num / den);
        }


        public void FatRaise(Mobj actor)
        {
            FaceTarget(actor);

            world.StartSound(actor, Sfx.MANATK, SfxType.Voice);
        }


        private static readonly Angle fatSpread = Angle.Ang90 / 8;

        public void FatAttack1(Mobj actor)
        {
            FaceTarget(actor);

            var ta = world.ThingAllocation;

            // Change direction to...
            actor.Angle += fatSpread;
            var target = world.SubstNullMobj(actor.Target);
            ta.SpawnMissile(actor, target, MobjType.Fatshot);

            var missile = ta.SpawnMissile(actor, target, MobjType.Fatshot);
            missile.Angle += fatSpread;
            var angle = missile.Angle;
            missile.MomX = new Fixed(missile.Info.Speed) * Trig.Cos(angle);
            missile.MomY = new Fixed(missile.Info.Speed) * Trig.Sin(angle);
        }

        public void FatAttack2(Mobj actor)
        {
            FaceTarget(actor);

            var ta = world.ThingAllocation;

            // Now here choose opposite deviation.
            actor.Angle -= fatSpread;
            var target = world.SubstNullMobj(actor.Target);
            ta.SpawnMissile(actor, target, MobjType.Fatshot);

            var missile = ta.SpawnMissile(actor, target, MobjType.Fatshot);
            missile.Angle -= fatSpread * 2;
            var angle = missile.Angle;
            missile.MomX = new Fixed(missile.Info.Speed) * Trig.Cos(angle);
            missile.MomY = new Fixed(missile.Info.Speed) * Trig.Sin(angle);
        }

        public void FatAttack3(Mobj actor)
        {
            FaceTarget(actor);

            var ta = world.ThingAllocation;

            var target = world.SubstNullMobj(actor.Target);

            var missile1 = ta.SpawnMissile(actor, target, MobjType.Fatshot);
            missile1.Angle -= fatSpread / 2;
            var angle1 = missile1.Angle;
            missile1.MomX = new Fixed(missile1.Info.Speed) * Trig.Cos(angle1);
            missile1.MomY = new Fixed(missile1.Info.Speed) * Trig.Sin(angle1);

            var missile2 = ta.SpawnMissile(actor, target, MobjType.Fatshot);
            missile2.Angle += fatSpread / 2;
            var angle2 = missile2.Angle;
            missile2.MomX = new Fixed(missile2.Info.Speed) * Trig.Cos(angle2);
            missile2.MomY = new Fixed(missile2.Info.Speed) * Trig.Sin(angle2);
        }


        public void BspiAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            // Launch a missile.
            world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Arachplaz);
        }


        public void SpidRefire(Mobj actor)
        {
            // Keep firing unless target got out of sight.
            FaceTarget(actor);

            if (world.Random.Next() < 10)
            {
                return;
            }

            if (actor.Target == null ||
                actor.Target.Health <= 0 ||
                !world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                actor.SetState(actor.Info.SeeState);
            }
        }


        public void CyberAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Rocket);
        }



        ////////////////////////////////////////////////////////////
        // Miscellaneous
        ////////////////////////////////////////////////////////////

        public void Explode(Mobj actor)
        {
            world.ThingInteraction.RadiusAttack(actor, actor.Target, 128);
        }


        public void Metal(Mobj actor)
        {
            world.StartSound(actor, Sfx.METAL, SfxType.Footstep);

            Chase(actor);
        }


        public void BabyMetal(Mobj actor)
        {
            world.StartSound(actor, Sfx.BSPWLK, SfxType.Footstep);

            Chase(actor);
        }


        public void Hoof(Mobj actor)
        {
            world.StartSound(actor, Sfx.HOOF, SfxType.Footstep);

            Chase(actor);
        }



        ////////////////////////////////////////////////////////////
        // Arch vile
        ////////////////////////////////////////////////////////////

        private Func<Mobj, bool> vileCheckFunc;
        private Mobj vileTargetCorpse;
        private Fixed vileTryX;
        private Fixed vileTryY;

        private void InitVile()
        {
            vileCheckFunc = VileCheck;
        }


        private bool VileCheck(Mobj thing)
        {
            if ((thing.Flags & MobjFlags.Corpse) == 0)
            {
                // Not a monster.
                return true;
            }

            if (thing.Tics != -1)
            {
                // Not lying still yet.
                return true;
            }

            if (thing.Info.RaiseState == MobjState.Null)
            {
                // Monster doesn't have a raise state.
                return true;
            }

            var maxDist = thing.Info.Radius + DoomInfo.MobjInfos[(int)MobjType.Vile].Radius;

            if (Fixed.Abs(thing.X - vileTryX) > maxDist ||
                Fixed.Abs(thing.Y - vileTryY) > maxDist)
            {
                // Not actually touching.
                return true;
            }

            vileTargetCorpse = thing;
            vileTargetCorpse.MomX = vileTargetCorpse.MomY = Fixed.Zero;
            vileTargetCorpse.Height <<= 2;

            var check = world.ThingMovement.CheckPosition(
                vileTargetCorpse,
                vileTargetCorpse.X,
                vileTargetCorpse.Y);

            vileTargetCorpse.Height >>= 2;

            if (!check)
            {
                // Doesn't fit here.
                return true;
            }

            // Got one, so stop checking.
            return false;
        }


        public void VileChase(Mobj actor)
        {
            if (actor.MoveDir != Direction.None)
            {
                // Check for corpses to raise.
                vileTryX = actor.X + actor.Info.Speed * xSpeed[(int)actor.MoveDir];
                vileTryY = actor.Y + actor.Info.Speed * ySpeed[(int)actor.MoveDir];

                var bm = world.Map.BlockMap;

                var maxRadius = GameConst.MaxThingRadius * 2;
                var blockX1 = bm.GetBlockX(vileTryX - maxRadius);
                var blockX2 = bm.GetBlockX(vileTryX + maxRadius);
                var blockY1 = bm.GetBlockY(vileTryY - maxRadius);
                var blockY2 = bm.GetBlockY(vileTryY + maxRadius);

                for (var bx = blockX1; bx <= blockX2; bx++)
                {
                    for (var by = blockY1; by <= blockY2; by++)
                    {
                        // Call VileCheck to check whether object is a corpse that canbe raised.
                        if (!bm.IterateThings(bx, by, vileCheckFunc))
                        {
                            // Got one!
                            var temp = actor.Target;
                            actor.Target = vileTargetCorpse;
                            FaceTarget(actor);
                            actor.Target = temp;
                            actor.SetState(MobjState.VileHeal1);

                            world.StartSound(vileTargetCorpse, Sfx.SLOP, SfxType.Misc);

                            var info = vileTargetCorpse.Info;
                            vileTargetCorpse.SetState(info.RaiseState);
                            vileTargetCorpse.Height <<= 2;
                            vileTargetCorpse.Flags = info.Flags;
                            vileTargetCorpse.Health = info.SpawnHealth;
                            vileTargetCorpse.Target = null;

                            return;
                        }
                    }
                }
            }

            // Return to normal attack.
            Chase(actor);
        }


        public void VileStart(Mobj actor)
        {
            world.StartSound(actor, Sfx.VILATK, SfxType.Weapon);
        }


        public void StartFire(Mobj actor)
        {
            world.StartSound(actor, Sfx.FLAMST, SfxType.Weapon);

            Fire(actor);
        }


        public void FireCrackle(Mobj actor)
        {
            world.StartSound(actor, Sfx.FLAME, SfxType.Weapon);

            Fire(actor);
        }


        public void Fire(Mobj actor)
        {
            var dest = actor.Tracer;

            if (dest == null)
            {
                return;
            }

            var target = world.SubstNullMobj(actor.Target);

            // Don't move it if the vile lost sight.
            if (!world.VisibilityCheck.CheckSight(target, dest))
            {
                return;
            }

            world.ThingMovement.UnsetThingPosition(actor);

            var angle = dest.Angle;
            actor.X = dest.X + Fixed.FromInt(24) * Trig.Cos(angle);
            actor.Y = dest.Y + Fixed.FromInt(24) * Trig.Sin(angle);
            actor.Z = dest.Z;

            world.ThingMovement.SetThingPosition(actor);
        }


        public void VileTarget(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            var fog = world.ThingAllocation.SpawnMobj(
                actor.Target.X,
                actor.Target.X,
                actor.Target.Z,
                MobjType.Fire);

            actor.Tracer = fog;
            fog.Target = actor;
            fog.Tracer = actor.Target;
            Fire(fog);
        }


        public void VileAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (!world.VisibilityCheck.CheckSight(actor, actor.Target))
            {
                return;
            }

            world.StartSound(actor, Sfx.BAREXP, SfxType.Weapon);
            world.ThingInteraction.DamageMobj(actor.Target, actor, actor, 20);
            actor.Target.MomZ = Fixed.FromInt(1000) / actor.Target.Info.Mass;

            var fire = actor.Tracer;
            if (fire == null)
            {
                return;
            }

            var angle = actor.Angle;

            // Move the fire between the vile and the player.
            fire.X = actor.Target.X - Fixed.FromInt(24) * Trig.Cos(angle);
            fire.Y = actor.Target.Y - Fixed.FromInt(24) * Trig.Sin(angle);
            world.ThingInteraction.RadiusAttack(fire, actor, 70);
        }



        ////////////////////////////////////////////////////////////
        // Revenant
        ////////////////////////////////////////////////////////////

        public void SkelMissile(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            // Missile spawns higher.
            actor.Z += Fixed.FromInt(16);

            var missile = world.ThingAllocation.SpawnMissile(actor, actor.Target, MobjType.Tracer);

            // Back to normal.
            actor.Z -= Fixed.FromInt(16);

            missile.X += missile.MomX;
            missile.Y += missile.MomY;
            missile.Tracer = actor.Target;
        }


        private static Angle traceAngle = new Angle(0xc000000);

        public void Tracer(Mobj actor)
        {
            if ((world.GameTic & 3) != 0)
            {
                return;
            }

            // Spawn a puff of smoke behind the rocket.
            world.Hitscan.SpawnPuff(actor.X, actor.Y, actor.Z);

            var smoke = world.ThingAllocation.SpawnMobj(
                actor.X - actor.MomX,
                actor.Y - actor.MomY,
                actor.Z,
                MobjType.Smoke);

            smoke.MomZ = Fixed.One;
            smoke.Tics -= world.Random.Next() & 3;
            if (smoke.Tics < 1)
            {
                smoke.Tics = 1;
            }

            // Adjust direction.
            var dest = actor.Tracer;

            if (dest == null || dest.Health <= 0)
            {
                return;
            }

            // Change angle.
            var exact = Geometry.PointToAngle(
                actor.X, actor.Y,
                dest.X, dest.Y);

            if (exact != actor.Angle)
            {
                if (exact - actor.Angle > Angle.Ang180)
                {
                    actor.Angle -= traceAngle;
                    if (exact - actor.Angle < Angle.Ang180)
                    {
                        actor.Angle = exact;
                    }
                }
                else
                {
                    actor.Angle += traceAngle;
                    if (exact - actor.Angle > Angle.Ang180)
                    {
                        actor.Angle = exact;
                    }
                }
            }

            exact = actor.Angle;
            actor.MomX = new Fixed(actor.Info.Speed) * Trig.Cos(exact);
            actor.MomY = new Fixed(actor.Info.Speed) * Trig.Sin(exact);

            // Change slope.
            var dist = Geometry.AproxDistance(
                dest.X - actor.X,
                dest.Y - actor.Y);

            var num = (dest.Z + Fixed.FromInt(40) - actor.Z).Data;
            var den = dist.Data / actor.Info.Speed;
            if (den < 1)
            {
                den = 1;
            }

            var slope = new Fixed(num / den);

            if (slope < actor.MomZ)
            {
                actor.MomZ -= Fixed.One / 8;
            }
            else
            {
                actor.MomZ += Fixed.One / 8;
            }
        }


        public void SkelWhoosh(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            world.StartSound(actor, Sfx.SKESWG, SfxType.Weapon);
        }


        public void SkelFist(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            if (CheckMeleeRange(actor))
            {
                var damage = ((world.Random.Next() % 10) + 1) * 6;
                world.StartSound(actor, Sfx.SKEPCH, SfxType.Weapon);
                world.ThingInteraction.DamageMobj(actor.Target, actor, actor, damage);
            }
        }



        ////////////////////////////////////////////////////////////
        // Pain elemental
        ////////////////////////////////////////////////////////////

        private void PainShootSkull(Mobj actor, Angle angle)
        {
            // Count total number of skull currently on the level.
            var count = 0;

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;
                if (mobj != null && mobj.Type == MobjType.Skull)
                {
                    count++;
                }
            }

            // If there are allready 20 skulls on the level,
            // don't spit another one.
            if (count > 20)
            {
                return;
            }

            // Okay, there's playe for another one.

            var preStep = Fixed.FromInt(4) +
                3 * (actor.Info.Radius + DoomInfo.MobjInfos[(int)MobjType.Skull].Radius) / 2;

            var x = actor.X + preStep * Trig.Cos(angle);
            var y = actor.Y + preStep * Trig.Sin(angle);
            var z = actor.Z + Fixed.FromInt(8);

            var skull = world.ThingAllocation.SpawnMobj(x, y, z, MobjType.Skull);

            // Check for movements.
            if (!world.ThingMovement.TryMove(skull, skull.X, skull.Y))
            {
                // Kill it immediately.
                world.ThingInteraction.DamageMobj(skull, actor, actor, 10000);
                return;
            }

            skull.Target = actor.Target;

            SkullAttack(skull);
        }


        public void PainAttack(Mobj actor)
        {
            if (actor.Target == null)
            {
                return;
            }

            FaceTarget(actor);

            PainShootSkull(actor, actor.Angle);
        }


        public void PainDie(Mobj actor)
        {
            Fall(actor);

            PainShootSkull(actor, actor.Angle + Angle.Ang90);
            PainShootSkull(actor, actor.Angle + Angle.Ang180);
            PainShootSkull(actor, actor.Angle + Angle.Ang270);
        }



        ////////////////////////////////////////////////////////////
        // Boss death
        ////////////////////////////////////////////////////////////

        private LineDef junk;

        private void InitBossDeath()
        {
            var v = new Vertex(Fixed.Zero, Fixed.Zero);
            junk = new LineDef(v, v, 0, 0, 0, null, null);
        }


        public void BossDeath(Mobj actor)
        {
            var options = world.Options;
            if (options.GameMode == GameMode.Commercial)
            {
                if (options.Map != 7)
                {
                    return;
                }

                if ((actor.Type != MobjType.Fatso) && (actor.Type != MobjType.Baby))
                {
                    return;
                }
            }
            else
            {
                switch (options.Episode)
                {
                    case 1:
                        if (options.Map != 8)
                        {
                            return;
                        }

                        if (actor.Type != MobjType.Bruiser)
                        {
                            return;
                        }

                        break;

                    case 2:
                        if (options.Map != 8)
                        {
                            return;
                        }

                        if (actor.Type != MobjType.Cyborg)
                        {
                            return;
                        }

                        break;

                    case 3:
                        if (options.Map != 8)
                        {
                            return;
                        }

                        if (actor.Type != MobjType.Spider)
                        {
                            return;
                        }

                        break;

                    case 4:
                        switch (options.Map)
                        {
                            case 6:
                                if (actor.Type != MobjType.Cyborg)
                                {
                                    return;
                                }

                                break;

                            case 8:
                                if (actor.Type != MobjType.Spider)
                                {
                                    return;
                                }

                                break;

                            default:
                                return;
                        }
                        break;

                    default:
                        if (options.Map != 8)
                        {
                            return;
                        }

                        break;
                }
            }

            // Make sure there is a player alive for victory.
            var players = world.Options.Players;
            int i;
            for (i = 0; i < Player.MaxPlayerCount; i++)
            {
                if (players[i].InGame && players[i].Health > 0)
                {
                    break;
                }
            }

            if (i == Player.MaxPlayerCount)
            {
                // No one left alive, so do not end game.
                return;
            }

            // Scan the remaining thinkers to see if all bosses are dead.
            foreach (var thinker in world.Thinkers)
            {
                var mo2 = thinker as Mobj;
                if (mo2 == null)
                {
                    continue;
                }

                if (mo2 != actor && mo2.Type == actor.Type && mo2.Health > 0)
                {
                    // Other boss not dead.
                    return;
                }
            }

            // Victory!
            if (options.GameMode == GameMode.Commercial)
            {
                if (options.Map == 7)
                {
                    if (actor.Type == MobjType.Fatso)
                    {
                        junk.Tag = 666;
                        world.SectorAction.DoFloor(junk, FloorMoveType.LowerFloorToLowest);
                        return;
                    }

                    if (actor.Type == MobjType.Baby)
                    {
                        junk.Tag = 667;
                        world.SectorAction.DoFloor(junk, FloorMoveType.RaiseToTexture);
                        return;
                    }
                }
            }
            else
            {
                switch (options.Episode)
                {
                    case 1:
                        junk.Tag = 666;
                        world.SectorAction.DoFloor(junk, FloorMoveType.LowerFloorToLowest);
                        return;

                    case 4:
                        switch (options.Map)
                        {
                            case 6:
                                junk.Tag = 666;
                                world.SectorAction.DoDoor(junk, VerticalDoorType.BlazeOpen);
                                return;

                            case 8:
                                junk.Tag = 666;
                                world.SectorAction.DoFloor(junk, FloorMoveType.LowerFloorToLowest);
                                return;
                        }
                        break;
                }
            }

            world.ExitLevel();
        }


        public void KeenDie(Mobj actor)
        {
            Fall(actor);

            // scan the remaining thinkers
            // to see if all Keens are dead
            foreach (var thinker in world.Thinkers)
            {
                var mo2 = thinker as Mobj;
                if (mo2 == null)
                {
                    continue;
                }

                if (mo2 != actor && mo2.Type == actor.Type && mo2.Health > 0)
                {
                    // other Keen not dead
                    return;
                }
            }

            junk.Tag = 666;
            world.SectorAction.DoDoor(junk, VerticalDoorType.Open);
        }



        ////////////////////////////////////////////////////////////
        // Icon of sin
        ////////////////////////////////////////////////////////////

        private Mobj[] brainTargets;
        private int brainTargetCount;
        private int currentBrainTarget;
        private bool easy;

        private void InitBrain()
        {
            brainTargets = new Mobj[32];
            brainTargetCount = 0;
            currentBrainTarget = 0;
            easy = false;
        }


        public void BrainAwake(Mobj actor)
        {
            // Find all the target spots.
            brainTargetCount = 0;
            currentBrainTarget = 0;

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;
                if (mobj == null)
                {
                    // Not a mobj.
                    continue;
                }

                if (mobj.Type == MobjType.Bosstarget)
                {
                    brainTargets[brainTargetCount] = mobj;
                    brainTargetCount++;
                }
            }

            world.StartSound(actor, Sfx.BOSSIT, SfxType.Diffuse);
        }


        public void BrainPain(Mobj actor)
        {
            world.StartSound(actor, Sfx.BOSPN, SfxType.Diffuse);
        }


        public void BrainScream(Mobj actor)
        {
            var random = world.Random;

            for (var x = actor.X - Fixed.FromInt(196); x < actor.X + Fixed.FromInt(320); x += Fixed.FromInt(8))
            {
                var y = actor.Y - Fixed.FromInt(320);
                var z = new Fixed(128) + random.Next() * Fixed.FromInt(2);

                var explosion = world.ThingAllocation.SpawnMobj(x, y, z, MobjType.Rocket);
                explosion.MomZ = new Fixed(random.Next() * 512);
                explosion.SetState(MobjState.Brainexplode1);
                explosion.Tics -= random.Next() & 7;
                if (explosion.Tics < 1)
                {
                    explosion.Tics = 1;
                }
            }

            world.StartSound(actor, Sfx.BOSDTH, SfxType.Diffuse);
        }


        public void BrainExplode(Mobj actor)
        {
            var random = world.Random;

            var x = actor.X + new Fixed((random.Next() - random.Next()) * 2048);
            var y = actor.Y;
            var z = new Fixed(128) + random.Next() * Fixed.FromInt(2);

            var explosion = world.ThingAllocation.SpawnMobj(x, y, z, MobjType.Rocket);
            explosion.MomZ = new Fixed(random.Next() * 512);
            explosion.SetState(MobjState.Brainexplode1);
            explosion.Tics -= random.Next() & 7;
            if (explosion.Tics < 1)
            {
                explosion.Tics = 1;
            }
        }


        public void BrainDie(Mobj actor)
        {
            world.ExitLevel();
        }


        public void BrainSpit(Mobj actor)
        {
            easy = !easy;
            if (world.Options.Skill <= GameSkill.Easy && (!easy))
            {
                return;
            }

            // If the game is reconstructed from a savedata, brain targets might be cleared.
            // If so, re-initialize them to avoid crash.
            if (brainTargetCount == 0)
            {
                BrainAwake(actor);
            }

            // Shoot a cube at current target.
            var target = brainTargets[currentBrainTarget];
            currentBrainTarget = (currentBrainTarget + 1) % brainTargetCount;

            // Spawn brain missile.
            var missile = world.ThingAllocation.SpawnMissile(actor, target, MobjType.Spawnshot);
            missile.Target = target;
            missile.ReactionTime = ((target.Y - actor.Y).Data / missile.MomY.Data) / missile.State.Tics;

            world.StartSound(actor, Sfx.BOSPIT, SfxType.Diffuse);
        }


        public void SpawnSound(Mobj actor)
        {
            world.StartSound(actor, Sfx.BOSCUB, SfxType.Misc);
            SpawnFly(actor);
        }


        public void SpawnFly(Mobj actor)
        {
            if (--actor.ReactionTime > 0)
            {
                // Still flying.
                return;
            }

            var target = actor.Target;

            // If the game is reconstructed from a savedata, the target might be null.
            // If so, use own position to spawn the monster.
            if (target == null)
            {
                target = actor;
                actor.Z = actor.Subsector.Sector.FloorHeight;
            }

            var ta = world.ThingAllocation;

            // First spawn teleport fog.
            var fog = ta.SpawnMobj(target.X, target.Y, target.Z, MobjType.Spawnfire);
            world.StartSound(fog, Sfx.TELEPT, SfxType.Misc);

            // Randomly select monster to spawn.
            var r = world.Random.Next();

            // Probability distribution (kind of :), decreasing likelihood.
            MobjType type;
            if (r < 50)
            {
                type = MobjType.Troop;
            }
            else if (r < 90)
            {
                type = MobjType.Sergeant;
            }
            else if (r < 120)
            {
                type = MobjType.Shadows;
            }
            else if (r < 130)
            {
                type = MobjType.Pain;
            }
            else if (r < 160)
            {
                type = MobjType.Head;
            }
            else if (r < 162)
            {
                type = MobjType.Vile;
            }
            else if (r < 172)
            {
                type = MobjType.Undead;
            }
            else if (r < 192)
            {
                type = MobjType.Baby;
            }
            else if (r < 222)
            {
                type = MobjType.Fatso;
            }
            else if (r < 246)
            {
                type = MobjType.Knight;
            }
            else
            {
                type = MobjType.Bruiser;
            }

            var monster = ta.SpawnMobj(target.X, target.Y, target.Z, type);
            if (LookForPlayers(monster, true))
            {
                monster.SetState(monster.Info.SeeState);
            }

            // Telefrag anything in this spot.
            world.ThingMovement.TeleportMove(monster, monster.X, monster.Y);

            // Remove self (i.e., cube).
            world.ThingAllocation.RemoveMobj(actor);
        }
    }
}
