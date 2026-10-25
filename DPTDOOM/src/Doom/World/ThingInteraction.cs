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
	public sealed class ThingInteraction
	{
		private World world;

		public ThingInteraction(World world)
		{
			this.world = world;

			InitRadiusAttack();
		}


		/// <summary>
		/// Called when the target is killed.
		/// </summary>
		public void KillMobj(Mobj source, Mobj target)
		{
			target.Flags &= ~(MobjFlags.Shootable | MobjFlags.Float | MobjFlags.SkullFly);

			if (target.Type != MobjType.Skull)
			{
				target.Flags &= ~MobjFlags.NoGravity;
			}

			target.Flags |= MobjFlags.Corpse | MobjFlags.DropOff;
			target.Height = new Fixed(target.Height.Data >> 2);

			if (source != null && source.Player != null)
			{
				// Count for intermission.
				if ((target.Flags & MobjFlags.CountKill) != 0)
				{
					source.Player.KillCount++;
				}

				if (target.Player != null)
				{
					source.Player.Frags[target.Player.Number]++;
				}
			}
			else if (!world.Options.NetGame && (target.Flags & MobjFlags.CountKill) != 0)
			{
				// Count all monster deaths, even those caused by other monsters.
				world.Options.Players[0].KillCount++;
			}

			if (target.Player != null)
			{
				// Count environment kills against you.
				if (source == null)
				{
					target.Player.Frags[target.Player.Number]++;
				}

				target.Flags &= ~MobjFlags.Solid;
				target.Player.PlayerState = PlayerState.Dead;
				world.PlayerBehavior.DropWeapon(target.Player);

				var am = world.AutoMap;

				if (target.Player.Number == world.Options.ConsolePlayer && am.Visible)
				{
					// Don't die in auto map, switch view prior to dying.
					am.Close();
				}
			}

			if (target.Health < -target.Info.SpawnHealth && target.Info.XdeathState != 0)
			{
				target.SetState(target.Info.XdeathState);
			}
			else
			{
				target.SetState(target.Info.DeathState);
			}

			target.Tics -= world.Random.Next() & 3;
			if (target.Tics < 1)
			{
				target.Tics = 1;
			}

			// Drop stuff.
			// This determines the kind of object spawned during the death frame of a thing.
			MobjType item;
			switch (target.Type)
			{
				case MobjType.Wolfss:
				case MobjType.Possessed:
					item = MobjType.Clip;
					break;

				case MobjType.Shotguy:
					item = MobjType.Shotgun;
					break;

				case MobjType.Chainguy:
					item = MobjType.Chaingun;
					break;

				default:
					return;
			}

			var mo = world.ThingAllocation.SpawnMobj(target.X, target.Y, Mobj.OnFloorZ, item);

			// Special versions of items.
			mo.Flags |= MobjFlags.Dropped;
		}


		private static readonly int baseThreshold = 100;

		/// <summary>
		/// Damages both enemies and players.
		/// "inflictor" is the thing that caused the damage creature
		/// or missile, can be null (slime, etc).
		/// "source" is the thing to target after taking damage creature
		/// or null.
		/// Source and inflictor are the same for melee attacks.
		/// Source can be null for slime, barrel explosions and other
		/// environmental stuff.
		/// </summary>
		public void DamageMobj(Mobj target, Mobj inflictor, Mobj source, int damage)
		{
			if ((target.Flags & MobjFlags.Shootable) == 0)
			{
				// Shouldn't happen...
				return;
			}

			if (target.Health <= 0)
			{
				return;
			}

			if ((target.Flags & MobjFlags.SkullFly) != 0)
			{
				target.MomX = target.MomY = target.MomZ = Fixed.Zero;
			}

			var player = target.Player;
			if (player != null && world.Options.Skill == GameSkill.Baby)
			{
				// Take half damage in trainer mode.
				damage >>= 1;
			}

			// Some close combat weapons should not inflict thrust and
			// push the victim out of reach, thus kick away unless using the chainsaw.
			var notChainsawAttack =
				source == null ||
				source.Player == null ||
				source.Player.ReadyWeapon != WeaponType.Chainsaw;

			if (inflictor != null && (target.Flags & MobjFlags.NoClip) == 0 && notChainsawAttack)
			{
				var ang = Geometry.PointToAngle(
					inflictor.X,
					inflictor.Y,
					target.X,
					target.Y);

				var thrust = new Fixed(damage * (Fixed.FracUnit >> 3) * 100 / target.Info.Mass);

				// Make fall forwards sometimes.
				if (damage < 40 &&
					damage > target.Health &&
					target.Z - inflictor.Z > Fixed.FromInt(64) &&
					(world.Random.Next() & 1) != 0)
				{
					ang += Angle.Ang180;
					thrust *= 4;
				}

				target.MomX += thrust * Trig.Cos(ang);
				target.MomY += thrust * Trig.Sin(ang);
			}

			// Player specific.
			if (player != null)
			{
				// End of game hell hack.
				if (target.Subsector.Sector.Special == (SectorSpecial)11 && damage >= target.Health)
				{
					damage = target.Health - 1;
				}

				// Below certain threshold, ignore damage in GOD mode, or with INVUL power.
				if (damage < 1000 && ((player.Cheats & CheatFlags.GodMode) != 0 ||
					player.Powers[(int)PowerType.Invulnerability] > 0))
				{
					return;
				}

				int saved;

				if (player.ArmorType != 0)
				{
					if (player.ArmorType == 1)
					{
						saved = damage / 3;
					}
					else
					{
						saved = damage / 2;
					}

					if (player.ArmorPoints <= saved)
					{
						// Armor is used up.
						saved = player.ArmorPoints;
						player.ArmorType = 0;
					}

					player.ArmorPoints -= saved;
					damage -= saved;
				}

				// Mirror mobj health here for Dave.
				player.Health -= damage;
				if (player.Health < 0)
				{
					player.Health = 0;
				}

				player.Attacker = source;

				// Add damage after armor / invuln.
				player.DamageCount += damage;

				if (player.DamageCount > 100)
				{
					// Teleport stomp does 10k points...
					player.DamageCount = 100;
				}
			}

			// Do the damage.
			target.Health -= damage;
			if (target.Health <= 0)
			{
				if ((target.Flags & MobjFlags.Buddha) != 0)
				{
					// DECORATE's +BUDDHA: down to one point and no further. Distinct from
					// invulnerability, which refuses the damage outright - this one flinches,
					// shows the damage and simply will not die.
					target.Health = 1;
				}
				else
				{
					KillMobj(source, target);
					return;
				}
			}

			// The random draw happens first and unconditionally, before the flag is consulted:
			// skipping it for one actor would shift every later draw in the level, so a mod adding
			// +NOPAIN to one monster would change more than that monster.
			if ((world.Random.Next() < target.Info.PainChance) &&
				(target.Flags & MobjFlags.NoPain) == 0 &&
				(target.Flags & MobjFlags.SkullFly) == 0)
			{
				// Fight back!
				target.Flags |= MobjFlags.JustHit;

				target.SetState(target.Info.PainState);
			}

			// We're awake now...
			target.ReactionTime = 0;

			if ((target.Threshold == 0 || target.Type == MobjType.Vile) &&
				source != null &&
				source != target &&
				source.Type != MobjType.Vile &&
				(source.Flags & MobjFlags.NoTarget) == 0)
			{
				// If not intent on another player, chase after this one.
				//
				// The NoTarget check is ZDoom's OkayToSwitchTarget: "This actor not targetted
				// when it hurts something else". It is about retaliation only - the monster
				// above still woke up and still has its ReactionTime cleared, it just does not
				// take this actor as its chase target. NeverTarget is the flag that stops it
				// being a target at all.
				target.Target = source;
				target.Threshold = baseThreshold;
				if (target.State == DoomInfo.States[(int)target.Info.SpawnState] &&
					target.Info.SeeState != MobjState.Null)
				{
					target.SetState(target.Info.SeeState);
				}
			}
		}


		/// <summary>
		/// Called when the missile hits something (wall or thing).
		/// </summary>
		public void ExplodeMissile(Mobj thing)
		{
			thing.MomX = thing.MomY = thing.MomZ = Fixed.Zero;

			thing.SetState(DoomInfo.MobjInfos[(int)thing.Type].DeathState);

			thing.Tics -= world.Random.Next() & 3;

			if (thing.Tics < 1)
			{
				thing.Tics = 1;
			}

			thing.Flags &= ~MobjFlags.Missile;

			if (thing.Info.DeathSound != 0 ||
			    ActorSounds.Has((int)thing.Type, ActorSoundSlot.Death))
			{
				world.StartActorSound(thing, ActorSoundSlot.Death,
				    thing.Info.DeathSound, SfxType.Misc);
			}
		}


		private Mobj bombSource;
		private Mobj bombSpot;
		private int bombDamage;

		/// <summary>
		/// How far the blast reaches, which vanilla keeps equal to the damage.
		///
		/// A separate field rather than reusing <see cref="bombDamage"/>: the range check and the
		/// falloff are two different questions, and only the second one involves the damage.
		/// </summary>
		private int bombRadius;

		/// <summary>
		/// Whether the explosion is allowed to hurt the actor that caused it.
		///
		/// False by default, and that default is **not** the same as "always exempt the source".
		/// Vanilla Doom does not exempt the source at all - a rocket hurts the player who fired it
		/// at point blank, and the tests that compare the world hash depend on it. Only
		/// <see cref="exemptSource"/> turns the exemption on, and only DECORATE's A_Explode asks
		/// for it, because ZDoom's XF_HURTSOURCE is what removes the exemption there.
		///
		/// Keeping the two ideas apart is what stops a new flag from changing a vanilla rocket.
		/// </summary>
		private bool bombHurtSource;

		/// <summary>
		/// True only for an A_Explode that did not ask for XF_HURTSOURCE.
		/// </summary>
		private bool exemptSource;

		private Func<Mobj, bool> radiusAttackFunc;

		private void InitRadiusAttack()
		{
			radiusAttackFunc = DoRadiusAttack;
		}

		/// <summary>
		/// "bombSource" is the creature that caused the explosion at "bombSpot".
		/// </summary>
		private bool DoRadiusAttack(Mobj thing)
		{
			if ((thing.Flags & MobjFlags.Shootable) == 0)
			{
				return true;
			}

			// Boss spider and cyborg take no damage from concussion.
			if (thing.Type == MobjType.Cyborg || thing.Type == MobjType.Spider)
			{
				return true;
			}

			// The source is exempt **only** when this explosion says so, and vanilla never says so.
			// Vanilla Doom does not exempt the source of a radius attack: a rocket hurts the player
			// who fired it at point blank, and the world-hash tests depend on that. Exempting it by
			// default changed the world of every vanilla rocket - the failure looked like a test
			// problem and was a real regression.
			//
			// And the spot is not exempt either. Those are different actors for a barrel: its
			// Explode passes whoever it was aiming at as the source, which is what makes a barrel
			// chain-reaction work. Exempting the spot as well would make a barrel immune to itself.
			if (exemptSource && thing == bombSource)
			{
				return true;
			}

			var dx = Fixed.Abs(thing.X - bombSpot.X);
			var dy = Fixed.Abs(thing.Y - bombSpot.Y);

			var dist = dx > dy ? dx : dy;
			dist = new Fixed((dist - thing.Radius).Data >> Fixed.FracBits);

			if (dist < Fixed.Zero)
			{
				dist = Fixed.Zero;
			}

			if (dist.Data >= bombRadius)
			{
				// Out of range.
				return true;
			}

			if (world.VisibilityCheck.CheckSight(thing, bombSpot))
			{
				// Must be in direct path.
				DamageMobj(thing, bombSpot, bombSource, bombDamage - dist.Data);
			}

			return true;
		}

		/// <summary>
		/// Source is the creature that caused the explosion at spot.
		///
		/// This is the vanilla signature, where the damage and the radius are the same number and
		/// the source is always exempt. Those are facts a rocket depends on, so the overload below
		/// keeps this one as the call with the default parameters rather than changing its meaning.
		/// </summary>
		public void RadiusAttack(Mobj spot, Mobj source, int damage)
		{
			RadiusAttack(spot, source, damage, damage, false, false);
		}

		/// <summary>
		/// Radius attack with the damage and the radius as separate numbers, and a choice of
		/// whether the source is hurt.
		///
		/// Vanilla only ever calls this with radius == damage, hurtSource false and
		/// exemptSourceRequested false, so its world is unchanged. DECORATE's A_Explode needs the
		/// separation: A_Explode(128, 128) is a different explosion from A_Explode(128, 64), and
		/// collapsing them back into one number would mean a rocket-sized damage in a much bigger
		/// circle, or a small damage in a tiny one.
		///
		/// hurtSource and exemptSourceRequested are two different questions, and collapsing them is
		/// how a new flag would change a vanilla rocket. The first is what the explosion asks for;
		/// the second is whether this caller has ZDoom's rule at all.
		/// </summary>
		public void RadiusAttack(Mobj spot, Mobj source, int damage, int radius, bool hurtSource)
		{
			RadiusAttack(spot, source, damage, radius, hurtSource, true);
		}

		private void RadiusAttack(Mobj spot, Mobj source, int damage, int radius, bool hurtSource,
		                          bool exemptSourceRequested)
		{
			var bm = world.Map.BlockMap;

			var dist = Fixed.FromInt(radius + GameConst.MaxThingRadius.Data);

			var blockY1 = bm.GetBlockY(spot.Y - dist);
			var blockY2 = bm.GetBlockY(spot.Y + dist);
			var blockX1 = bm.GetBlockX(spot.X - dist);
			var blockX2 = bm.GetBlockX(spot.X + dist);

			bombSpot = spot;
			bombSource = source;
			bombDamage = damage;
			bombRadius = radius;
			bombHurtSource = hurtSource;

			// Vanilla calls this with hurtSource false and must not change at all: the source of a
			// vanilla radius attack is **not** exempt. DECORATE's XF_HURTSOURCE is what removes the
			// exemption, so only an explosion that did not ask for it gets one.
			exemptSource = !hurtSource && exemptSourceRequested;

			for (var by = blockY1; by <= blockY2; by++)
			{
				for (var bx = blockX1; bx <= blockX2; bx++)
				{
					bm.IterateThings(bx, by, radiusAttackFunc);
				}
			}
		}
	}
}
