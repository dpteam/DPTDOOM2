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
using System.Collections.Generic;

namespace DPTDOOM
{
    public sealed class ThingAllocation
    {
        private World world;

        public ThingAllocation(World world)
        {
            this.world = world;

            InitSpawnMapThing();
            InitMultiPlayerRespawn();
            InitRespawnSpecials();
        }



        ////////////////////////////////////////////////////////////
        // Spawn functions for level start
        ////////////////////////////////////////////////////////////

        private MapThing[] playerStarts;
        private List<MapThing> deathmatchStarts;

        private void InitSpawnMapThing()
        {
            playerStarts = new MapThing[Player.MaxPlayerCount];
            deathmatchStarts = new List<MapThing>();
        }

        /// <summary>
        /// Spawn a mobj at the mapthing.
        /// </summary>
        public void SpawnMapThing(MapThing mt)
        {
            // Count deathmatch start positions.
            if (mt.Type == 11)
            {
                if (deathmatchStarts.Count < 10)
                {
                    deathmatchStarts.Add(mt);
                }

                return;
            }

            // Check for players specially.
            if (mt.Type <= 4)
            {
                var playerNumber = mt.Type - 1;

                // This check is neccesary in Plutonia MAP12,
                // which contains an unknown thing with type 0.
                if (playerNumber < 0)
                {
                    return;
                }

                // Save spots for respawning in network games.
                playerStarts[playerNumber] = mt;

                if (world.Options.Deathmatch == 0)
                {
                    SpawnPlayer(mt);
                }

                return;
            }

            if (mt.Type == 11 || mt.Type <= 4)
            {
                return;
            }

            // Check for apropriate skill level.
            if (!world.Options.NetGame && ((int)mt.Flags & 16) != 0)
            {
                return;
            }

            int bit;
            if (world.Options.Skill == GameSkill.Baby)
            {
                bit = 1;
            }
            else if (world.Options.Skill == GameSkill.Nightmare)
            {
                bit = 4;
            }
            else
            {
                bit = 1 << ((int)world.Options.Skill - 1);
            }

            if (((int)mt.Flags & bit) == 0)
            {
                return;
            }

            // Find which type to spawn.
            int i;
            for (i = 0; i < DoomInfo.MobjInfos.Length; i++)
            {
                if (mt.Type == DoomInfo.MobjInfos[i].DoomEdNum)
                {
                    break;
                }
            }

            if (i == DoomInfo.MobjInfos.Length)
            {
                // A thing type this port does not know. Very common in a PWAD built
                // for a source port with more things than Doom has - a DECORATE actor,
                // a ZDoom-only type - and throwing here meant the whole level refused
                // to load over one object. Skipping it is what every port does: the
                // room is still playable, and the report says what was left out.
                LoadReport.Add(
                    "unknown thing type",
                    "type " + mt.Type + " at " + mt.X.ToIntFloor() + "," + mt.Y.ToIntFloor(),
                    "the object is not placed");

                return;
            }

            // Don't spawn keycards and players in deathmatch.
            if (world.Options.Deathmatch != 0 &&
                (DoomInfo.MobjInfos[i].Flags & MobjFlags.NotDeathmatch) != 0)
            {
                return;
            }

            // Don't spawn any monsters if -nomonsters.
            if (world.Options.NoMonsters &&
                    (i == (int)MobjType.Skull ||
                    (DoomInfo.MobjInfos[i].Flags & MobjFlags.CountKill) != 0))
            {
                return;
            }

            // Spawn it.
            Fixed x = mt.X;
            Fixed y = mt.Y;
            Fixed z;
            if ((DoomInfo.MobjInfos[i].Flags & MobjFlags.SpawnCeiling) != 0)
            {
                z = Mobj.OnCeilingZ;
            }
            else
            {
                z = Mobj.OnFloorZ;
            }

            var mobj = SpawnMobj(x, y, z, (MobjType)i);

            mobj.SpawnPoint = mt;

            if (mobj.Tics > 0)
            {
                mobj.Tics = 1 + (world.Random.Next() % mobj.Tics);
            }

            if ((mobj.Flags & MobjFlags.CountKill) != 0)
            {
                world.TotalKills++;
            }

            if ((mobj.Flags & MobjFlags.CountItem) != 0)
            {
                world.TotalItems++;
            }

            mobj.Angle = mt.Angle;

            if ((mt.Flags & ThingFlags.Ambush) != 0)
            {
                mobj.Flags |= MobjFlags.Ambush;
            }
        }

        /// <summary>
        /// Called when a player is spawned on the level.
        /// Most of the player structure stays unchanged between levels.
        /// </summary>
        public void SpawnPlayer(MapThing mt)
        {
            var players = world.Options.Players;
            var playerNumber = mt.Type - 1;

            // Not playing?
            if (!players[playerNumber].InGame)
            {
                return;
            }

            var player = players[playerNumber];

            if (player.PlayerState == PlayerState.Reborn)
            {
                players[playerNumber].Reborn();
            }

            var x = mt.X;
            var y = mt.Y;
            var z = Mobj.OnFloorZ;
            var mobj = SpawnMobj(x, y, z, MobjType.Player);

            if (mt.Type - 1 == world.Options.ConsolePlayer)
            {
                world.StatusBar.Reset();
                world.Options.Sound.SetListener(mobj);
            }

            // Set color translations for player sprites.
            if (playerNumber >= 1)
            {
                mobj.Flags |= (MobjFlags)((mt.Type - 1) << (int)MobjFlags.TransShift);
            }

            mobj.Angle = mt.Angle;
            mobj.Player = player;
            mobj.Health = player.Health;

            player.Mobj = mobj;
            player.PlayerState = PlayerState.Live;
            player.Refire = 0;
            player.Message = null;
            player.MessageTime = 0;
            player.DamageCount = 0;
            player.BonusCount = 0;
            player.ExtraLight = 0;
            player.FixedColorMap = 0;
            player.ViewHeight = Player.NormalViewHeight;

            // Setup gun psprite.
            world.PlayerBehavior.SetupPlayerSprites(player);

            // Give all cards in death match mode.
            if (world.Options.Deathmatch != 0)
            {
                for (var i = 0; i < (int)CardType.Count; i++)
                {
                    player.Cards[i] = true;
                }
            }
        }

        public IList<MapThing> PlayerStarts => playerStarts;
        public IList<MapThing> DeathmatchStarts => deathmatchStarts;



        ////////////////////////////////////////////////////////////
        // Thing spawn functions for the middle of a game
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Spawn a mobj at the given position as the given type.
        /// </summary>
        public Mobj SpawnMobj(Fixed x, Fixed y, Fixed z, MobjType type)
        {
            var mobj = new Mobj(world);

            var info = DoomInfo.MobjInfos[(int)type];

            mobj.Type = type;
            mobj.Info = info;
            mobj.X = x;
            mobj.Y = y;
            mobj.Radius = info.Radius;
            mobj.Height = info.Height;
            mobj.Flags = info.Flags;
            mobj.Health = info.SpawnHealth;

            if (world.Options.Skill != GameSkill.Nightmare)
            {
                mobj.ReactionTime = info.ReactionTime;
            }

            mobj.LastLook = world.Random.Next() % Player.MaxPlayerCount;

            // Do not set the state with P_SetMobjState,
            // because action routines can not be called yet.
            var st = DoomInfo.States[(int)info.SpawnState];

            mobj.State = st;
            mobj.Tics = st.Tics;
            mobj.Sprite = st.Sprite;
            mobj.Frame = st.Frame;

            // Set subsector and/or block links.
            world.ThingMovement.SetThingPosition(mobj);

            mobj.FloorZ = mobj.Subsector.Sector.FloorHeight;
            mobj.CeilingZ = mobj.Subsector.Sector.CeilingHeight;

            if (z == Mobj.OnFloorZ)
            {
                mobj.Z = mobj.FloorZ;
            }
            else if (z == Mobj.OnCeilingZ)
            {
                mobj.Z = mobj.CeilingZ - mobj.Info.Height;
            }
            else
            {
                mobj.Z = z;
            }

            world.Thinkers.Add(mobj);

            return mobj;
        }

        /// <summary>
        /// Remove the mobj from the level.
        /// </summary>
        public void RemoveMobj(Mobj mobj)
        {
            var tm = world.ThingMovement;

            if ((mobj.Flags & MobjFlags.Special) != 0 &&
                (mobj.Flags & MobjFlags.Dropped) == 0 &&
                (mobj.Type != MobjType.Inv) &&
                (mobj.Type != MobjType.Ins))
            {
                itemRespawnQue[itemQueHead] = mobj.SpawnPoint;
                itemRespawnTime[itemQueHead] = world.LevelTime;
                itemQueHead = (itemQueHead + 1) & (itemQueSize - 1);

                // Lose one off the end?
                if (itemQueHead == itemQueTail)
                {
                    itemQueTail = (itemQueTail + 1) & (itemQueSize - 1);
                }
            }

            // Unlink from sector and block lists.
            tm.UnsetThingPosition(mobj);

            // Stop any playing sound.
            world.StopSound(mobj);

            // Free block.
            world.Thinkers.Remove(mobj);
        }

        /// <summary>
        /// Spawns one object with A_SpawnItemEx's position, velocity, angle and pointer flags.
        ///
        /// The relative-coordinate formulas follow ZDoom, including its deliberately inverted
        /// left/right convention: positive y is right in relative mode, which is the opposite of
        /// absolute mode. That sign is easy to reverse while "tidying up" the arithmetic.
        ///
        /// All expressions and the fail-chance draw have already happened before this call. The
        /// fail chance is evaluated first by the caller, so an unsuccessful spawn consumes no
        /// other random values and evaluates no other expression.
        ///
        /// Returns the spawned object, or null when a monster was blocked. There is no master
        /// pointer in this engine, so flags that need one are reported by the converter rather than
        /// being approximated here.
        /// </summary>
        public Mobj SpawnItemEx(Mobj source, SpawnItemExParams parameters)
        {
            var flags = parameters.Flags;
            var angle = parameters.Angle;

            if ((flags & SpawnItemExFlags.AbsoluteAngle) == 0)
            {
                angle += source.Angle;
            }

            var sin = Trig.Sin(angle);
            var cos = Trig.Cos(angle);

            Fixed x;
            Fixed y;

            if ((flags & SpawnItemExFlags.AbsolutePosition) != 0)
            {
                x = source.X + parameters.XOffset;
                y = source.Y + parameters.YOffset;
            }
            else
            {
                x = source.X + parameters.XOffset * cos + parameters.YOffset * sin;
                y = source.Y + parameters.XOffset * sin - parameters.YOffset * cos;
            }

            var velocityX = parameters.XVelocity;
            var velocityY = parameters.YVelocity;
            var velocityZ = parameters.ZVelocity;

            if ((flags & SpawnItemExFlags.AbsoluteVelocity) == 0)
            {
                var rotatedX = velocityX * cos + velocityY * sin;
                var rotatedY = velocityX * sin - velocityY * cos;

                velocityX = rotatedX;
                velocityY = rotatedY;
            }

            var spawned = SpawnMobj(x, y, source.Z + parameters.ZOffset, parameters.Type);

            var originator = source;

            if ((flags & SpawnItemExFlags.Originator) == 0)
            {
                // A projectile's owner is the first non-projectile above it, not necessarily its
                // immediate target. The guard makes a malformed pointer chain harmless instead of
                // turning a spawn into an infinite walk.
                var hops = 0;

                while (originator != null &&
                       (originator.Flags & MobjFlags.Missile) != 0 &&
                       originator.Target != null &&
                       originator.Target != originator &&
                       hops++ < 32)
                {
                    originator = originator.Target;
                }
            }

            var isMonster = IsMonsterForSpawn(spawned);

            if (isMonster && (flags & SpawnItemExFlags.NoCheckPosition) == 0 &&
                !world.ThingMovement.CheckPosition(spawned, x, y))
            {
                // The monster is blocked, so it does not exist at all. Removing it through the
                // normal path keeps the sector links and the item-respawn bookkeeping intact.
                RemoveMobj(spawned);
                return null;
            }

            if ((flags & SpawnItemExFlags.TransferPointers) != 0)
            {
                spawned.Target = source.Target;
                spawned.Tracer = source.Tracer;
            }
            else if (!isMonster)
            {
                spawned.Target = originator;
            }

            if ((flags & SpawnItemExFlags.NoPointers) != 0)
            {
                spawned.Target = null;
                spawned.Tracer = null;
            }

            if ((flags & SpawnItemExFlags.SetTarget) != 0)
            {
                spawned.Target = originator;
            }

            if ((flags & SpawnItemExFlags.SetTracer) != 0)
            {
                spawned.Tracer = originator;
            }

            if ((flags & SpawnItemExFlags.TransferTranslation) != 0)
            {
                spawned.Flags = (spawned.Flags & ~MobjFlags.Translation) |
                                (source.Flags & MobjFlags.Translation);
            }

            if ((flags & SpawnItemExFlags.TransferAmbushFlag) != 0)
            {
                if ((source.Flags & MobjFlags.Ambush) != 0)
                {
                    spawned.Flags |= MobjFlags.Ambush;
                }
                else
                {
                    spawned.Flags &= ~MobjFlags.Ambush;
                }
            }

            if ((flags & SpawnItemExFlags.TransferSpriteFrame) != 0)
            {
                spawned.Sprite = source.Sprite;
                spawned.Frame = source.Frame;
            }

            if ((flags & SpawnItemExFlags.MultiplySpeed) != 0)
            {
                velocityX *= spawned.Info.Speed;
                velocityY *= spawned.Info.Speed;
                velocityZ *= spawned.Info.Speed;
            }

            spawned.MomX = velocityX;
            spawned.MomY = velocityY;
            spawned.MomZ = velocityZ;
            spawned.Angle = angle;

            if ((flags & SpawnItemExFlags.IsTarget) != 0)
            {
                source.Target = spawned;
            }

            if ((flags & SpawnItemExFlags.IsTracer) != 0)
            {
                source.Tracer = spawned;
            }

            return spawned;
        }

        /// <summary>
        /// The spawn-time monster test. Items with a see state are not monsters merely because
        /// they have one: a monster has health, is shootable, and is not a missile. This is the
        /// only place that needs the distinction, so it does not add a field to MobjInfo.
        /// </summary>
        private static bool IsMonsterForSpawn(Mobj mobj)
        {
            return (mobj.Flags & MobjFlags.Missile) == 0 &&
                   (mobj.Flags & MobjFlags.Shootable) != 0 &&
                   mobj.Info.SpawnHealth > 0 &&
                   mobj.Info.SeeState != MobjState.Null;
        }

        /// <summary>
        /// Spawns a projectile with DECORATE's side offset, height and angle adjustment.
        ///
        /// The side offset always uses the source's own facing. The angle argument is an offset
        /// from the direction to the target (or from the source's facing without one), except in
        /// absolute-angle mode, where it replaces that direction. Keeping those two bases separate
        /// is what makes CMF_ABSOLUTEANGLE different from an ordinary angled shot.
        /// </summary>
        public Mobj SpawnCustomProjectile(Mobj source, MobjType type, Fixed sideOffset,
                                           Fixed height, Angle angle, bool absoluteAngle,
                                           Mobj target, bool transferTranslation)
        {
            var side = source.Angle - Angle.Ang90;
            var x = source.X + sideOffset * Trig.Cos(side);
            var y = source.Y + sideOffset * Trig.Sin(side);

            var missile = SpawnMobj(x, y, source.Z + height, type);

            if (missile.Info.SeeSound != 0 ||
                ActorSounds.Has((int)missile.Type, ActorSoundSlot.See))
            {
                world.StartActorSound(missile, ActorSoundSlot.See,
                    missile.Info.SeeSound, SfxType.Misc);
            }

            missile.Target = source;

            var travelAngle = target != null
                ? Geometry.PointToAngle(x, y, target.X, target.Y)
                : source.Angle;

            travelAngle = absoluteAngle ? angle : travelAngle + angle;

            var speed = GetMissileSpeed(type);

            missile.Angle = travelAngle;
            missile.MomX = new Fixed(speed) * Trig.Cos(travelAngle);
            missile.MomY = new Fixed(speed) * Trig.Sin(travelAngle);

            if (target != null)
            {
                var dist = Geometry.AproxDistance(target.X - x, target.Y - y);
                var num = (target.Z - (source.Z + height)).Data;
                var den = (dist / speed).Data;

                if (den >= 1)
                {
                    missile.MomZ = new Fixed((int)((long)num * speed / den));
                }
            }

            if (transferTranslation)
            {
                missile.Flags = (missile.Flags & ~MobjFlags.Translation) |
                                (source.Flags & MobjFlags.Translation);
            }

            return missile;
        }

        /// <summary>
        /// A_FireProjectile: the player-only variant with Doom's ordinary horizontal auto-aim.
        /// FPF_AIMATANGLE moves the initial auto-aim direction; without it, the angle is added
        /// after auto-aim. FPF_NOAUTOAIM skips that search without changing the angle rule.
        /// </summary>
        public Mobj FireCustomProjectile(Mobj source, MobjType type, Fixed sideOffset,
                                          Fixed height, Angle angle, bool aimAtAngle,
                                          bool transferTranslation, bool noAutoAim)
        {
            if (source.Player == null)
            {
                return null;
            }

            var initialAngle = aimAtAngle ? source.Angle + angle : source.Angle;
            Mobj target = null;

            if (!noAutoAim)
            {
                var hs = world.Hitscan;
                var range = Fixed.FromInt(16 * 64);
                var searchAngle = initialAngle;

                hs.AimLineAttack(source, searchAngle, range);

                if (hs.LineTarget == null)
                {
                    searchAngle += new Angle(1 << 26);
                    hs.AimLineAttack(source, searchAngle, range);

                    if (hs.LineTarget == null)
                    {
                        searchAngle -= new Angle(2 << 26);
                        hs.AimLineAttack(source, searchAngle, range);
                    }
                }

                target = hs.LineTarget;
            }

            if (aimAtAngle && target == null)
            {
                // There is no auto-aim result to supply the base direction, so the already
                // adjusted initial angle is the complete direction.
                return SpawnCustomProjectile(source, type, sideOffset, height,
                    initialAngle, true, null, transferTranslation);
            }

            return SpawnCustomProjectile(source, type, sideOffset, height,
                aimAtAngle ? Angle.Ang0 : angle, false, target, transferTranslation);
        }

        /// <summary>
        /// Fires a missile of any type, from any height, at a target or along the actor's own
        /// facing.
        ///
        /// A generalization of <see cref="SpawnMissile"/> for mods: DECORATE's A_CustomMissile names
        /// the projectile, gives it a height, and works from a monster - where vanilla only ever
        /// fires the missile a monster's own state table names. Reusing this method's private helpers
        /// keeps the fast-monster speed rule in one place.
        ///
        /// A null destination means "straight ahead": a mod that spawns a projectile from a state
        /// with no target still expects it to travel, not to be dropped.
        /// </summary>
        public Mobj SpawnScriptMissile(Mobj source, Mobj destination, MobjType type, Fixed height)
        {
            var missile = SpawnMobj(source.X, source.Y, source.Z + height, type);

            if (missile.Info.SeeSound != 0 ||
                ActorSounds.Has((int)missile.Type, ActorSoundSlot.See))
            {
                world.StartActorSound(missile, ActorSoundSlot.See,
                    missile.Info.SeeSound, SfxType.Misc);
            }

            missile.Target = source;

            var angle = destination != null
                ? Geometry.PointToAngle(source.X, source.Y, destination.X, destination.Y)
                : source.Angle;

            var speed = GetMissileSpeed(missile.Type);

            missile.Angle = angle;
            missile.MomX = new Fixed(speed) * Trig.Cos(angle);
            missile.MomY = new Fixed(speed) * Trig.Sin(angle);

            if (destination != null)
            {
                // Aim vertically at the target, which vanilla does and a mod expects: a projectile
                // that only travels level would sail over anything lower than its launcher.
                var dist = Geometry.AproxDistance(
                    destination.X - source.X,
                    destination.Y - source.Y);

                var num = (destination.Z - source.Z).Data;
                var den = (dist / speed).Data;

                if (den >= 1)
                {
                    missile.MomZ = new Fixed((int)((long)num * speed / den));
                }
            }

            return missile;
        }

        /// <summary>
        /// Throws a grenade for DECORATE's A_ThrowGrenade.
        ///
        /// This is deliberately not the auto-aiming projectile path: xyvel replaces the actor's
        /// normal speed when nonzero, zvel is independent, and the thrower contributes half its
        /// own horizontal momentum. There is no actor pitch in this simulation, so the throw is
        /// level; the converter reports that limitation instead of borrowing the player's view.
        /// </summary>
        public Mobj ThrowCustomGrenade(Mobj source, MobjType type, Fixed height,
                                        Fixed xyVelocity, Fixed zVelocity)
        {
            var grenade = SpawnMobj(source.X, source.Y,
                source.Z + height + Fixed.FromInt(35), type);

            if (grenade.Info.SeeSound != 0 ||
                ActorSounds.Has((int)grenade.Type, ActorSoundSlot.See))
            {
                world.StartActorSound(grenade, ActorSoundSlot.See,
                    grenade.Info.SeeSound, SfxType.Misc);
            }

            // GZDoom's 360/256-degree steps are exactly 1<<24 in Doom's binary angle format.
            // Use the world RNG: a presentation or system random source would desync lockstep.
            var spread = world.Random.Next() % 8 - 4;
            var angle = source.Angle + new Angle(spread << 24);
            var speed = xyVelocity.Data != 0
                ? xyVelocity
                : new Fixed(grenade.Info.Speed);

            grenade.Angle = angle;
            grenade.MomX = speed * Trig.Cos(angle) + (source.MomX >> 1);
            grenade.MomY = speed * Trig.Sin(angle) + (source.MomY >> 1);
            grenade.MomZ = zVelocity;
            grenade.Target = source;

            CheckMissileSpawn(grenade);
            return grenade;
        }

        /// <summary>
        /// Get the speed of the given missile type.
        /// Some missiles have different speeds according to the game setting.
        /// </summary>
        private int GetMissileSpeed(MobjType type)
        {
            if (world.Options.FastMonsters || world.Options.Skill == GameSkill.Nightmare)
            {
                switch (type)
                {
                    case MobjType.Bruisershot:
                    case MobjType.Headshot:
                    case MobjType.Troopshot:
                        return 20 * Fixed.FracUnit;
                    default:
                        return DoomInfo.MobjInfos[(int)type].Speed;
                }
            }
            else
            {
                return DoomInfo.MobjInfos[(int)type].Speed;
            }
        }

        /// <summary>
        /// Moves the missile forward a bit and possibly explodes it right there.
        /// </summary>
        private void CheckMissileSpawn(Mobj missile)
        {
            missile.Tics -= world.Random.Next() & 3;
            if (missile.Tics < 1)
            {
                missile.Tics = 1;
            }

            // Move a little forward so an angle can be computed if it immediately explodes.
            missile.X += (missile.MomX >> 1);
            missile.Y += (missile.MomY >> 1);
            missile.Z += (missile.MomZ >> 1);

            if (!world.ThingMovement.TryMove(missile, missile.X, missile.Y))
            {
                world.ThingInteraction.ExplodeMissile(missile);
            }
        }

        /// <summary>
        /// Shoot a missile from the source to the destination.
        /// For monsters.
        /// </summary>
        public Mobj SpawnMissile(Mobj source, Mobj dest, MobjType type)
        {
            var missile = SpawnMobj(
                source.X,
                source.Y,
                source.Z + Fixed.FromInt(32), type);

            if (missile.Info.SeeSound != 0 ||
                ActorSounds.Has((int)missile.Type, ActorSoundSlot.See))
            {
                world.StartActorSound(missile, ActorSoundSlot.See,
                    missile.Info.SeeSound, SfxType.Misc);
            }

            // Where it came from?
            missile.Target = source;

            var angle = Geometry.PointToAngle(
                source.X, source.Y,
                dest.X, dest.Y);

            // Fuzzy player.
            if ((dest.Flags & MobjFlags.Shadow) != 0)
            {
                var random = world.Random;
                angle += new Angle((random.Next() - random.Next()) << 20);
            }

            var speed = GetMissileSpeed(missile.Type);

            missile.Angle = angle;
            missile.MomX = new Fixed(speed) * Trig.Cos(angle);
            missile.MomY = new Fixed(speed) * Trig.Sin(angle);

            var dist = Geometry.AproxDistance(
                dest.X - source.X,
                dest.Y - source.Y);

            var num = (dest.Z - source.Z).Data;
            var den = (dist / speed).Data;
            if (den < 1)
            {
                den = 1;
            }

            missile.MomZ = new Fixed(num / den);

            CheckMissileSpawn(missile);

            return missile;
        }

        /// <summary>
        /// Shoot a missile from the source.
        /// For players.
        /// </summary>
        public void SpawnPlayerMissile(Mobj source, MobjType type)
        {
            var hs = world.Hitscan;

            // See which target is to be aimed at.
            var angle = source.Angle;
            var slope = hs.AimLineAttack(source, angle, Fixed.FromInt(16 * 64));

            if (hs.LineTarget == null)
            {
                angle += new Angle(1 << 26);
                slope = hs.AimLineAttack(source, angle, Fixed.FromInt(16 * 64));

                if (hs.LineTarget == null)
                {
                    angle -= new Angle(2 << 26);
                    slope = hs.AimLineAttack(source, angle, Fixed.FromInt(16 * 64));
                }

                if (hs.LineTarget == null)
                {
                    angle = source.Angle;
                    slope = Fixed.Zero;
                }
            }

            var x = source.X;
            var y = source.Y;
            var z = source.Z + Fixed.FromInt(32);

            var missile = SpawnMobj(x, y, z, type);

            if (missile.Info.SeeSound != 0 ||
                ActorSounds.Has((int)missile.Type, ActorSoundSlot.See))
            {
                world.StartActorSound(missile, ActorSoundSlot.See,
                    missile.Info.SeeSound, SfxType.Misc);
            }

            missile.Target = source;
            missile.Angle = angle;
            missile.MomX = new Fixed(missile.Info.Speed) * Trig.Cos(angle);
            missile.MomY = new Fixed(missile.Info.Speed) * Trig.Sin(angle);
            missile.MomZ = new Fixed(missile.Info.Speed) * slope;

            CheckMissileSpawn(missile);
        }



        ////////////////////////////////////////////////////////////
        // Multi-player related functions
        ////////////////////////////////////////////////////////////

        private static readonly int bodyQueSize = 32;
        private int bodyQueSlot;
        private Mobj[] bodyQue;

        private void InitMultiPlayerRespawn()
        {
            bodyQueSlot = 0;
            bodyQue = new Mobj[bodyQueSize];
        }

        /// <summary>
        /// Returns false if the player cannot be respawned at the given
        /// mapthing spot because something is occupying it.
        /// </summary>
        public bool CheckSpot(int playernum, MapThing mthing)
        {
            var players = world.Options.Players;

            if (players[playernum].Mobj == null)
            {
                // First spawn of level, before corpses.
                for (var i = 0; i < playernum; i++)
                {
                    if (players[i].Mobj.X == mthing.X && players[i].Mobj.Y == mthing.Y)
                    {
                        return false;
                    }
                }
                return true;
            }

            var x = mthing.X;
            var y = mthing.Y;

            if (!world.ThingMovement.CheckPosition(players[playernum].Mobj, x, y))
            {
                return false;
            }

            // Flush an old corpse if needed.
            if (bodyQueSlot >= bodyQueSize)
            {
                RemoveMobj(bodyQue[bodyQueSlot % bodyQueSize]);
            }
            bodyQue[bodyQueSlot % bodyQueSize] = players[playernum].Mobj;
            bodyQueSlot++;

            // Spawn a teleport fog.
            var subsector = Geometry.PointInSubsector(x, y, world.Map);

            var angle = (Angle.Ang45.Data >> Trig.AngleToFineShift) *
                ((int)Math.Round(mthing.Angle.ToDegree()) / 45);

            //
            // The code below to reproduce respawn fog bug in deathmath
            // is based on Chocolate Doom's implementation.
            //

            Fixed xa;
            Fixed ya;
            switch (angle)
            {
                case 4096: // -4096:
                    xa = Trig.Tan(2048); // finecosine[-4096]
                    ya = Trig.Tan(0);    // finesine[-4096]
                    break;
                case 5120: // -3072:
                    xa = Trig.Tan(3072); // finecosine[-3072]
                    ya = Trig.Tan(1024); // finesine[-3072]
                    break;
                case 6144: // -2048:
                    xa = Trig.Sin(0);    // finecosine[-2048]
                    ya = Trig.Tan(2048); // finesine[-2048]
                    break;
                case 7168: // -1024:
                    xa = Trig.Sin(1024); // finecosine[-1024]
                    ya = Trig.Tan(3072); // finesine[-1024]
                    break;
                case 0:
                case 1024:
                case 2048:
                case 3072:
                    xa = Trig.Cos((int)angle);
                    ya = Trig.Sin((int)angle);
                    break;
                default:
                    throw new Exception("Unexpected angle: " + angle);
            }

            var mo = SpawnMobj(
                x + 20 * xa, y + 20 * ya,
                subsector.Sector.FloorHeight,
                MobjType.Tfog);

            if (!world.FirstTicIsNotYetDone)
            {
                // Don't start sound on first frame.
                world.StartSound(mo, Sfx.TELEPT, SfxType.Misc);
            }

            return true;
        }

        /// <summary>
        /// Spawns a player at one of the random death match spots.
        /// Called at level load and each death.
        /// </summary>
        public void DeathMatchSpawnPlayer(int playerNumber)
        {
            var selections = deathmatchStarts.Count;
            if (selections < 4)
            {
                throw new Exception("Only " + selections + " deathmatch spots, 4 required");
            }

            var random = world.Random;
            for (var j = 0; j < 20; j++)
            {
                var i = random.Next() % selections;
                if (CheckSpot(playerNumber, deathmatchStarts[i]))
                {
                    deathmatchStarts[i].Type = playerNumber + 1;
                    SpawnPlayer(deathmatchStarts[i]);
                    return;
                }
            }

            // No good spot, so the player will probably get stuck .
            SpawnPlayer(playerStarts[playerNumber]);
        }



        ////////////////////////////////////////////////////////////
        // Item respawn
        ////////////////////////////////////////////////////////////

        private static readonly int itemQueSize = 128;
        private MapThing[] itemRespawnQue;
        private int[] itemRespawnTime;
        private int itemQueHead;
        private int itemQueTail;

        private void InitRespawnSpecials()
        {
            itemRespawnQue = new MapThing[itemQueSize];
            itemRespawnTime = new int[itemQueSize];
            itemQueHead = 0;
            itemQueTail = 0;
        }

        /// <summary>
        /// Respawn items if the game mode is altdeath.
        /// </summary>
        public void RespawnSpecials()
        {
            // Only respawn items in deathmatch.
            if (world.Options.Deathmatch != 2)
            {
                return;
            }

            // Nothing left to respawn?
            if (itemQueHead == itemQueTail)
            {
                return;
            }

            // Wait at least 30 seconds.
            if (world.LevelTime - itemRespawnTime[itemQueTail] < 30 * 35)
            {
                return;
            }

            var mthing = itemRespawnQue[itemQueTail];

            var x = mthing.X;
            var y = mthing.Y;

            // Spawn a teleport fog at the new spot.
            var ss = Geometry.PointInSubsector(x, y, world.Map);
            var mo = SpawnMobj(x, y, ss.Sector.FloorHeight, MobjType.Ifog);
            world.StartSound(mo, Sfx.ITMBK, SfxType.Misc);

            int i;
            // Find which type to spawn.
            for (i = 0; i < DoomInfo.MobjInfos.Length; i++)
            {
                if (mthing.Type == DoomInfo.MobjInfos[i].DoomEdNum)
                {
                    break;
                }
            }

            // Spawn it.
            Fixed z;
            if ((DoomInfo.MobjInfos[i].Flags & MobjFlags.SpawnCeiling) != 0)
            {
                z = Mobj.OnCeilingZ;
            }
            else
            {
                z = Mobj.OnFloorZ;
            }

            mo = SpawnMobj(x, y, z, (MobjType)i);
            mo.SpawnPoint = mthing;
            mo.Angle = mthing.Angle;

            // Pull it from the que.
            itemQueTail = (itemQueTail + 1) & (itemQueSize - 1);
        }
    }
}
