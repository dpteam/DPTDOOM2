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
	public sealed class SectorAction
	{
		//
		// SECTOR HEIGHT CHANGING
		// After modifying a sectors floor or ceiling height,
		// call this routine to adjust the positions
		// of all things that touch the sector.
		//
		// If anything doesn't fit anymore, true will be returned.
		// If crunch is true, they will take damage
		// as they are being crushed.
		// If Crunch is false, you should set the sector height back
		// the way it was and call P_ChangeSector again
		// to undo the changes.
		//

		private World world;

		public SectorAction(World world)
		{
			this.world = world;

			InitSectorChange();
		}



		private bool crushChange;
		private bool noFit;
		private Func<Mobj, bool> crushThingFunc;

		private void InitSectorChange()
		{
			crushThingFunc = CrushThing;
		}

		private bool ThingHeightClip(Mobj thing)
		{
			var onFloor = (thing.Z == thing.FloorZ);

			var tm = world.ThingMovement;

			tm.CheckPosition(thing, thing.X, thing.Y);
			// What about stranding a monster partially off an edge?

			thing.FloorZ = tm.CurrentFloorZ;
			thing.CeilingZ = tm.CurrentCeilingZ;

			if (onFloor)
			{
				// Walking monsters rise and fall with the floor.
				thing.Z = thing.FloorZ;
			}
			else
			{
				// Don't adjust a floating monster unless forced to.
				if (thing.Z + thing.Height > thing.CeilingZ)
				{
					thing.Z = thing.CeilingZ - thing.Height;
				}
			}

			if (thing.CeilingZ - thing.FloorZ < thing.Height)
			{
				return false;
			}

			return true;
		}

		private bool CrushThing(Mobj thing)
		{
			if (ThingHeightClip(thing))
			{
				// Keep checking.
				return true;
			}

			// Crunch bodies to giblets.
			if (thing.Health <= 0)
			{
				thing.SetState(MobjState.Gibs);
				thing.Flags &= ~MobjFlags.Solid;
				thing.Height = Fixed.Zero;
				thing.Radius = Fixed.Zero;

				// Keep checking.
				return true;
			}

			// Crunch dropped items.
			if ((thing.Flags & MobjFlags.Dropped) != 0)
			{
				world.ThingAllocation.RemoveMobj(thing);

				// Keep checking.
				return true;
			}

			if ((thing.Flags & MobjFlags.Shootable) == 0)
			{
				// Assume it is bloody gibs or something.
				return true;
			}

			noFit = true;

			if (crushChange && (world.LevelTime & 3) == 0)
			{
				world.ThingInteraction.DamageMobj(thing, null, null, 10);

				// Spray blood in a random direction.
				var blood = world.ThingAllocation.SpawnMobj(
					thing.X,
					thing.Y,
					thing.Z + thing.Height / 2,
					MobjType.Blood);

				var random = world.Random;
				blood.MomX = new Fixed((random.Next() - random.Next()) << 12);
				blood.MomY = new Fixed((random.Next() - random.Next()) << 12);
			}

			// Keep checking (crush other things).	
			return true;
		}

		private bool ChangeSector(Sector sector, bool crunch)
		{
			noFit = false;
			crushChange = crunch;

			var bm = world.Map.BlockMap;
			var blockBox = sector.BlockBox;

			// Re-check heights for all things near the moving sector.
			for (var x = blockBox.Left(); x <= blockBox.Right(); x++)
			{
				for (var y = blockBox.Bottom(); y <= blockBox.Top(); y++)
				{
					bm.IterateThings(x, y, crushThingFunc);
				}
			}

			return noFit;
		}

		/// <summary>
		/// Move a plane (floor or ceiling) and check for crushing.
		/// </summary>
		public SectorActionResult MovePlane(
			Sector sector,
			Fixed speed,
			Fixed dest,
			bool crush,
			int floorOrCeiling,
			int direction)
		{
			switch (floorOrCeiling)
			{
				case 0:
					// Floor.
					switch (direction)
					{
						case -1:
							// Down.
							if (sector.FloorHeight - speed < dest)
							{
								var lastPos = sector.FloorHeight;
								sector.FloorHeight = dest;
								if (ChangeSector(sector, crush))
								{
									sector.FloorHeight = lastPos;
									ChangeSector(sector, crush);
								}

								return SectorActionResult.PastDestination;
							}
							else
							{
								var lastPos = sector.FloorHeight;
								sector.FloorHeight -= speed;
								if (ChangeSector(sector, crush))
								{
									sector.FloorHeight = lastPos;
									ChangeSector(sector, crush);

									return SectorActionResult.Crushed;
								}
							}

							break;

						case 1:
							// Up.
							if (sector.FloorHeight + speed > dest)
							{
								var lastPos = sector.FloorHeight;
								sector.FloorHeight = dest;
								if (ChangeSector(sector, crush))
								{
									sector.FloorHeight = lastPos;
									ChangeSector(sector, crush);
								}

								return SectorActionResult.PastDestination;
							}
							else
							{
								// Could get crushed.
								var lastPos = sector.FloorHeight;
								sector.FloorHeight += speed;
								if (ChangeSector(sector, crush))
								{
									if (crush)
									{
										return SectorActionResult.Crushed;
									}
									sector.FloorHeight = lastPos;
									ChangeSector(sector, crush);

									return SectorActionResult.Crushed;
								}
							}

							break;
					}
					break;

				case 1:
					// Ceiling.
					switch (direction)
					{
						case -1:
							// Down.
							if (sector.CeilingHeight - speed < dest)
							{
								var lastPos = sector.CeilingHeight;
								sector.CeilingHeight = dest;
								if (ChangeSector(sector, crush))
								{
									sector.CeilingHeight = lastPos;
									ChangeSector(sector, crush);
								}

								return SectorActionResult.PastDestination;
							}
							else
							{
								// Could get crushed.
								var lastPos = sector.CeilingHeight;
								sector.CeilingHeight -= speed;
								if (ChangeSector(sector, crush))
								{
									if (crush)
									{
										return SectorActionResult.Crushed;
									}
									sector.CeilingHeight = lastPos;
									ChangeSector(sector, crush);

									return SectorActionResult.Crushed;
								}
							}

							break;

						case 1:
							// UP
							if (sector.CeilingHeight + speed > dest)
							{
								var lastPos = sector.CeilingHeight;
								sector.CeilingHeight = dest;
								if (ChangeSector(sector, crush))
								{
									sector.CeilingHeight = lastPos;
									ChangeSector(sector, crush);
								}

								return SectorActionResult.PastDestination;
							}
							else
							{
								sector.CeilingHeight += speed;
								ChangeSector(sector, crush);
							}

							break;
					}

					break;
			}

			return SectorActionResult.OK;
		}

		private Sector GetNextSector(LineDef line, Sector sector)
		{
			if ((line.Flags & LineFlags.TwoSided) == 0)
			{
				return null;
			}

			if (line.FrontSector == sector)
			{
				return line.BackSector;
			}

			return line.FrontSector;
		}

		private Fixed FindLowestFloorSurrounding(Sector sector)
		{
			var floor = sector.FloorHeight;

			for (var i = 0; i < sector.Lines.Length; i++)
			{
				var check = sector.Lines[i];

				var other = GetNextSector(check, sector);
				if (other == null)
				{
					continue;
				}

				if (other.FloorHeight < floor)
				{
					floor = other.FloorHeight;
				}
			}

			return floor;
		}

		private Fixed FindHighestFloorSurrounding(Sector sector)
		{
			var floor = Fixed.FromInt(-500);

			for (var i = 0; i < sector.Lines.Length; i++)
			{
				var check = sector.Lines[i];

				var other = GetNextSector(check, sector);
				if (other == null)
				{
					continue;
				}

				if (other.FloorHeight > floor)
				{
					floor = other.FloorHeight;
				}
			}

			return floor;
		}

		private Fixed FindLowestCeilingSurrounding(Sector sector)
		{
			var height = Fixed.MaxValue;

			for (var i = 0; i < sector.Lines.Length; i++)
			{
				var check = sector.Lines[i];

				var other = GetNextSector(check, sector);
				if (other == null)
				{
					continue;
				}

				if (other.CeilingHeight < height)
				{
					height = other.CeilingHeight;
				}
			}

			return height;
		}

		private Fixed FindHighestCeilingSurrounding(Sector sector)
		{
			var height = Fixed.Zero;

			for (var i = 0; i < sector.Lines.Length; i++)
			{
				var check = sector.Lines[i];

				var other = GetNextSector(check, sector);
				if (other == null)
				{
					continue;
				}

				if (other.CeilingHeight > height)
				{
					height = other.CeilingHeight;
				}
			}

			return height;
		}

		private int FindSectorFromLineTag(LineDef line, int start)
		{
			var sectors = world.Map.Sectors;

			for (var i = start + 1; i < sectors.Length; i++)
			{
				if (sectors[i].Tag == line.Tag)
				{
					return i;
				}
			}

			return -1;
		}



		////////////////////////////////////////////////////////////
		// Door
		////////////////////////////////////////////////////////////

		private static readonly Fixed doorSpeed = Fixed.FromInt(2);
		private static readonly int doorWait = 150;

		/// <summary>
		/// Open a door manually, no tag value.
		/// </summary>
		public void DoLocalDoor(LineDef line, Mobj thing)
		{
			//	Check for locks.
			var player = thing.Player;

			switch ((int)line.Special)
			{
				// Blue Lock.
				case 26:
				case 32:
					if (player == null)
					{
						return;
					}

					if (!player.Cards[(int)CardType.BlueCard] &&
						!player.Cards[(int)CardType.BlueSkull])
					{
						player.SendMessage(DoomInfo.Strings.PD_BLUEK);
						world.StartSound(player.Mobj, Sfx.OOF, SfxType.Voice);
						return;
					}
					break;

				// Yellow Lock.
				case 27:
				case 34:
					if (player == null)
					{
						return;
					}

					if (!player.Cards[(int)CardType.YellowCard] &&
						!player.Cards[(int)CardType.YellowSkull])
					{
						player.SendMessage(DoomInfo.Strings.PD_YELLOWK);
						world.StartSound(player.Mobj, Sfx.OOF, SfxType.Voice);
						return;
					}
					break;

				// Red Lock.
				case 28:
				case 33:
					if (player == null)
					{
						return;
					}

					if (!player.Cards[(int)CardType.RedCard] &&
						!player.Cards[(int)CardType.RedSkull])
					{
						player.SendMessage(DoomInfo.Strings.PD_REDK);
						world.StartSound(player.Mobj, Sfx.OOF, SfxType.Voice);
						return;
					}
					break;
			}

			var sector = line.BackSide.Sector;

			// If the sector has an active thinker, use it.
			if (sector.SpecialData != null)
			{
				var door = (VerticalDoor)sector.SpecialData;
				switch ((int)line.Special)
				{
					// Only for "raise" doors, not "open"s.
					case 1:
					case 26:
					case 27:
					case 28:
					case 117:
						if (door.Direction == -1)
						{
							// Go back up.
							door.Direction = 1;
						}
						else
						{
							if (thing.Player == null)
							{
								// Bad guys never close doors.
								return;
							}

							// Start going down immediately.
							door.Direction = -1;
						}
						return;
				}
			}

			// For proper sound.
			switch ((int)line.Special)
			{
				// Blazing door raise.
				case 117:

				// Blazing door open.
				case 118:
					world.StartSound(sector.SoundOrigin, Sfx.BDOPN, SfxType.Misc);
					break;

				// Normal door sound.
				case 1:
				case 31:
					world.StartSound(sector.SoundOrigin, Sfx.DOROPN, SfxType.Misc);
					break;

				// Locked door sound.
				default:
					world.StartSound(sector.SoundOrigin, Sfx.DOROPN, SfxType.Misc);
					break;
			}

			// New door thinker.
			var newDoor = new VerticalDoor(world);
			world.Thinkers.Add(newDoor);
			sector.SpecialData = newDoor;
			newDoor.Sector = sector;
			newDoor.Direction = 1;
			newDoor.Speed = doorSpeed;
			newDoor.TopWait = doorWait;

			switch ((int)line.Special)
			{
				case 1:
				case 26:
				case 27:
				case 28:
					newDoor.Type = VerticalDoorType.Normal;
					break;

				case 31:
				case 32:
				case 33:
				case 34:
					newDoor.Type = VerticalDoorType.Open;
					line.Special = 0;
					break;

				// Blazing door raise.
				case 117:
					newDoor.Type = VerticalDoorType.BlazeRaise;
					newDoor.Speed = doorSpeed * 4;
					break;

				// Blazing door open.
				case 118:
					newDoor.Type = VerticalDoorType.BlazeOpen;
					line.Special = 0;
					newDoor.Speed = doorSpeed * 4;
					break;
			}

			// Find the top and bottom of the movement range.
			newDoor.TopHeight = FindLowestCeilingSurrounding(sector);
			newDoor.TopHeight -= Fixed.FromInt(4);
		}

		public bool DoDoor(LineDef line, VerticalDoorType type)
		{
			var sectors = world.Map.Sectors;
			var setcorNumber = -1;
			var result = false;

			while ((setcorNumber = FindSectorFromLineTag(line, setcorNumber)) >= 0)
			{
				var sector = sectors[setcorNumber];
				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				// New door thinker.
				var door = new VerticalDoor(world);
				world.Thinkers.Add(door);
				sector.SpecialData = door;
				door.Sector = sector;
				door.Type = type;
				door.TopWait = doorWait;
				door.Speed = doorSpeed;

				switch (type)
				{
					case VerticalDoorType.BlazeClose:
						door.TopHeight = FindLowestCeilingSurrounding(sector);
						door.TopHeight -= Fixed.FromInt(4);
						door.Direction = -1;
						door.Speed = doorSpeed * 4;
						world.StartSound(door.Sector.SoundOrigin, Sfx.BDCLS, SfxType.Misc);
						break;

					case VerticalDoorType.Close:
						door.TopHeight = FindLowestCeilingSurrounding(sector);
						door.TopHeight -= Fixed.FromInt(4);
						door.Direction = -1;
						world.StartSound(door.Sector.SoundOrigin, Sfx.DORCLS, SfxType.Misc);
						break;

					case VerticalDoorType.Close30ThenOpen:
						door.TopHeight = sector.CeilingHeight;
						door.Direction = -1;
						world.StartSound(door.Sector.SoundOrigin, Sfx.DORCLS, SfxType.Misc);
						break;

					case VerticalDoorType.BlazeRaise:
					case VerticalDoorType.BlazeOpen:
						door.Direction = 1;
						door.TopHeight = FindLowestCeilingSurrounding(sector);
						door.TopHeight -= Fixed.FromInt(4);
						door.Speed = doorSpeed * 4;
						if (door.TopHeight != sector.CeilingHeight)
						{
							world.StartSound(door.Sector.SoundOrigin, Sfx.BDOPN, SfxType.Misc);
						}
						break;

					case VerticalDoorType.Normal:
					case VerticalDoorType.Open:
						door.Direction = 1;
						door.TopHeight = FindLowestCeilingSurrounding(sector);
						door.TopHeight -= Fixed.FromInt(4);
						if (door.TopHeight != sector.CeilingHeight)
						{
							world.StartSound(door.Sector.SoundOrigin, Sfx.DOROPN, SfxType.Misc);
						}
						break;

					default:
						break;
				}

			}

			return result;
		}

		public bool DoLockedDoor(LineDef line, VerticalDoorType type, Mobj thing)
		{
			var player = thing.Player;
			if (player == null)
			{
				return false;
			}

			switch ((int)line.Special)
			{
				// Blue Lock.
				case 99:
				case 133:
					if (player == null)
					{
						return false;
					}
					if (!player.Cards[(int)CardType.BlueCard] &&
						!player.Cards[(int)CardType.BlueSkull])
					{
						player.SendMessage(DoomInfo.Strings.PD_BLUEO);
						world.StartSound(player.Mobj, Sfx.OOF, SfxType.Voice);
						return false;
					}
					break;

				// Red Lock.
				case 134:
				case 135:
					if (player == null)
					{
						return false;
					}
					if (!player.Cards[(int)CardType.RedCard] &&
						!player.Cards[(int)CardType.RedSkull])
					{
						player.SendMessage(DoomInfo.Strings.PD_REDO);
						world.StartSound(player.Mobj, Sfx.OOF, SfxType.Voice);
						return false;
					}
					break;

				// Yellow Lock.
				case 136:
				case 137:
					if (player == null)
					{
						return false;
					}
					if (!player.Cards[(int)CardType.YellowCard] &&
						!player.Cards[(int)CardType.YellowSkull])
					{
						player.SendMessage(DoomInfo.Strings.PD_YELLOWO);
						world.StartSound(player.Mobj, Sfx.OOF, SfxType.Voice);
						return false;
					}
					break;
			}

			return DoDoor(line, type);
		}

		/// <summary>
		/// True when the thing may open a Boom generalized locked door, and says why not when it
		/// may not.
		///
		/// Boom's generalized doors carry the key number in the line itself, so the check cannot
		/// live next to the vanilla one - that one is a switch over the special number. The rule
		/// is <see cref="GeneralizedLock"/>; this only supplies the player and the refusal.
		/// </summary>
		private bool CanUnlockGeneralizedDoor(LineDef line, BoomGeneralized.Definition definition,
		                                      Mobj thing)
		{
			var player = thing.Player;

			// Monsters cannot open locked doors, and neither can corpses.
			if (player == null)
			{
				return false;
			}

			var verdict = GeneralizedLock.Check(definition.LockedKey,
			                                    definition.SkullsCountAsCards, player.Cards);

			if (verdict.Open)
			{
				return true;
			}

			player.SendMessage(verdict.Message);
			world.StartSound(player.Mobj, verdict.Sound, SfxType.Voice);
			return false;
		}



		////////////////////////////////////////////////////////////
		// Platform
		////////////////////////////////////////////////////////////

		// In plutonia MAP23, number of adjoining sectors can be 44.
		private static readonly int maxAdjoiningSectorCount = 64;
		private Fixed[] heightList = new Fixed[maxAdjoiningSectorCount];

		private Fixed FindNextHighestFloor(Sector sector, Fixed currentHeight)
		{
			var height = currentHeight;
			var h = 0;

			for (var i = 0; i < sector.Lines.Length; i++)
			{
				var check = sector.Lines[i];

				var other = GetNextSector(check, sector);
				if (other == null)
				{
					continue;
				}

				if (other.FloorHeight > height)
				{
					heightList[h++] = other.FloorHeight;
				}

				// Check for overflow.
				if (h >= heightList.Length)
				{
					// Exit.
					throw new Exception("Too many adjoining sectors!");
				}
			}

			// Find lowest height in list.
			if (h == 0)
			{
				return currentHeight;
			}

			var min = heightList[0];

			// Range checking? 
			for (var i = 1; i < h; i++)
			{
				if (heightList[i] < min)
				{
					min = heightList[i];
				}
			}

			return min;
		}


		private static readonly int platformWait = 3;
		private static readonly Fixed platformSpeed = Fixed.One;

		/// <summary>Boom's ELEVATORSPEED, half a unit per tic.</summary>
		private static readonly Fixed elevatorSpeed = Fixed.FromInt(4);

		public bool DoPlatform(LineDef line, PlatformType type, int amount)
		{
			//	Activate all <type> plats that are in stasis.
			switch (type)
			{
				case PlatformType.PerpetualRaise:
					ActivateInStasis(line.Tag);
					break;

				case PlatformType.ToggleUpDown:
					// Boom 212 reactivates a toggle that is parked in stasis rather
					// than starting a second one; without this the line would only
					// ever work once.
					ActivateInStasis(line.Tag);
					break;

				default:
					break;
			}

			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];
				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				// Find lowest and highest floors around sector.
				var plat = new Platform(world);
				world.Thinkers.Add(plat);
				plat.Type = type;
				plat.Sector = sector;
				plat.Sector.SpecialData = plat;
				plat.Crush = false;
				plat.Tag = line.Tag;

				switch (type)
				{
					case PlatformType.RaiseToNearestAndChange:
						plat.Speed = platformSpeed / 2;
						sector.FloorFlat = line.FrontSide.Sector.FloorFlat;
						plat.High = FindNextHighestFloor(sector, sector.FloorHeight);
						plat.Wait = 0;
						plat.Status = PlatformState.Up;
						// No more damage, if applicable.
						sector.Special = 0;
						world.StartSound(sector.SoundOrigin, Sfx.STNMOV, SfxType.Misc);
						break;

					case PlatformType.RaiseAndChange:
						plat.Speed = platformSpeed / 2;
						sector.FloorFlat = line.FrontSide.Sector.FloorFlat;
						plat.High = sector.FloorHeight + amount * Fixed.One;
						plat.Wait = 0;
						plat.Status = PlatformState.Up;
						world.StartSound(sector.SoundOrigin, Sfx.STNMOV, SfxType.Misc);
						break;

					case PlatformType.ToggleUpDown:
						// Boom 212: the floor flips between its own height and the
						// ceiling, crushing anything in the way. "Down" here means
						// "toward Low", and Low is the ceiling, so the first trigger
						// sends it up — which is why the pair of fields looks
						// inverted.
						plat.Speed = platformSpeed;
						plat.Wait = 0;
						plat.Crush = true;
						plat.Low = sector.CeilingHeight;
						plat.High = sector.FloorHeight;
						plat.Status = PlatformState.Down;
						break;

					case PlatformType.DownWaitUpStay:
						plat.Speed = platformSpeed * 4;
						plat.Low = FindLowestFloorSurrounding(sector);
						if (plat.Low > sector.FloorHeight)
						{
							plat.Low = sector.FloorHeight;
						}
						plat.High = sector.FloorHeight;
						plat.Wait = 35 * platformWait;
						plat.Status = PlatformState.Down;
						world.StartSound(sector.SoundOrigin, Sfx.PSTART, SfxType.Misc);
						break;

					case PlatformType.BlazeDwus:
						plat.Speed = platformSpeed * 8;
						plat.Low = FindLowestFloorSurrounding(sector);
						if (plat.Low > sector.FloorHeight)
						{
							plat.Low = sector.FloorHeight;
						}
						plat.High = sector.FloorHeight;
						plat.Wait = 35 * platformWait;
						plat.Status = PlatformState.Down;
						world.StartSound(sector.SoundOrigin, Sfx.PSTART, SfxType.Misc);
						break;

					case PlatformType.PerpetualRaise:
						plat.Speed = platformSpeed;
						plat.Low = FindLowestFloorSurrounding(sector);
						if (plat.Low > sector.FloorHeight)
						{
							plat.Low = sector.FloorHeight;
						}
						plat.High = FindHighestFloorSurrounding(sector);
						if (plat.High < sector.FloorHeight)
						{
							plat.High = sector.FloorHeight;
						}
						plat.Wait = 35 * platformWait;
						plat.Status = (PlatformState)(world.Random.Next() & 1);
						world.StartSound(sector.SoundOrigin, Sfx.PSTART, SfxType.Misc);
						break;
				}

				AddActivePlatform(plat);
			}

			return result;
		}


		private static readonly int maxPlatformCount = 60;
		private Platform[] activePlatforms = new Platform[maxPlatformCount];

		public void ActivateInStasis(int tag)
		{
			for (var i = 0; i < activePlatforms.Length; i++)
			{
				if (activePlatforms[i] != null &&
					activePlatforms[i].Tag == tag &&
					activePlatforms[i].Status == PlatformState.InStasis)
				{
					// A toggle platform flips to the other end; everything else
					// resumes where it stopped. Boom does the same, and without the
					// flip a 212 line would jump to the same place every time.
					if (activePlatforms[i].Type == PlatformType.ToggleUpDown)
					{
						activePlatforms[i].Status =
							activePlatforms[i].OldStatus == PlatformState.Up
								? PlatformState.Down
								: PlatformState.Up;
					}
					else
					{
						activePlatforms[i].Status = activePlatforms[i].OldStatus;
					}

					activePlatforms[i].ThinkerState = ThinkerState.Active;
				}
			}
		}

		public void StopPlatform(LineDef line)
		{
			for (var j = 0; j < activePlatforms.Length; j++)
			{
				if (activePlatforms[j] != null &&
					activePlatforms[j].Status != PlatformState.InStasis &&
					activePlatforms[j].Tag == line.Tag)
				{
					activePlatforms[j].OldStatus = activePlatforms[j].Status;
					activePlatforms[j].Status = PlatformState.InStasis;
					activePlatforms[j].ThinkerState = ThinkerState.InStasis;
				}
			}
		}

		public void AddActivePlatform(Platform platform)
		{
			for (var i = 0; i < activePlatforms.Length; i++)
			{
				if (activePlatforms[i] == null)
				{
					activePlatforms[i] = platform;

					return;
				}
			}

			throw new Exception("Too many active platforms!");
		}

		public void RemoveActivePlatform(Platform platform)
		{
			for (var i = 0; i < activePlatforms.Length; i++)
			{
				if (platform == activePlatforms[i])
				{
					activePlatforms[i].Sector.SpecialData = null;
					world.Thinkers.Remove(activePlatforms[i]);
					activePlatforms[i] = null;
					return;
				}
			}

			throw new Exception("The platform was not found!");
		}



		////////////////////////////////////////////////////////////
		// Floor
		////////////////////////////////////////////////////////////

		private static readonly Fixed floorSpeed = Fixed.One;

		/// <summary>
		/// Finds a neighbouring sector whose floor sits at a given height.
		///
		/// Boom uses this for the "numeric" change model: instead of taking the
		/// texture from the line's own sector, it looks for a neighbour standing at
		/// the target height and copies that one. The idea is that the map author
		/// builds a model room showing what the result should look like, and the
		/// engine finds it by height rather than by tag.
		///
		/// Returns null when no neighbour matches, which Boom treats as "no change
		/// at all" rather than as an error.
		/// </summary>
		private Sector FindModelFloorSector(Sector sector, Fixed height)
		{
			foreach (var line in sector.Lines)
			{
				var other = line.FrontSector == sector ? line.BackSector : line.FrontSector;

				if (other == null || other == sector)
				{
					continue;
				}

				if (other.FloorHeight == height)
				{
					return other;
				}
			}

			return null;
		}

		/// <summary>The same, for ceilings.</summary>
		private Sector FindModelCeilingSector(Sector sector, Fixed height)
		{
			foreach (var line in sector.Lines)
			{
				var other = line.FrontSector == sector ? line.BackSector : line.FrontSector;

				if (other == null || other == sector)
				{
					continue;
				}

				if (other.CeilingHeight == height)
				{
					return other;
				}
			}

			return null;
		}

		/// <summary>
		/// Changes floor texture and sector type without moving anything.
		///
		/// This is Boom's 153/154 (trigger model) and 239/240 (numeric model). A
		/// map uses it to open a door that looks different, or to turn a damaging
		/// floor harmless, at the moment a switch is thrown.
		///
		/// <paramref name="numeric"/> selects where the new texture comes from: the
		/// line's own sector, or a neighbour at the target height. When the numeric
		/// search finds nothing, Boom changes nothing — deliberately, because the
		/// map author asked for a specific look and a wrong one is worse than none.
		/// </summary>
		public bool DoChange(LineDef line, bool numeric)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];

				result = true;

				if (!numeric)
				{
					sector.FloorFlat = line.FrontSector.FloorFlat;
					sector.Special = line.FrontSector.Special;
					continue;
				}

				var model = FindModelFloorSector(sector, sector.FloorHeight);

				if (model != null)
				{
					sector.FloorFlat = model.FloorFlat;
					sector.Special = model.Special;
				}
			}

			return result;
		}

		/// <summary>
		/// Starts a Boom elevator: floor and ceiling moving together.
		///
		/// The gap between them is preserved by computing the ceiling destination
		/// from the floor destination plus the current height difference, so a room
		/// 128 units tall is still 128 units tall when it arrives.
		///
		/// The Current type reads the target from the activating line's own sector,
		/// which is how a switch across the room calls a lift to the height the
		/// switch is at.
		/// </summary>
		public bool DoElevator(LineDef line, ElevatorType type)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];

				// Either plane moving means the sector is busy. Boom checks both,
				// because an elevator drives both.
				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				var elevator = new Elevator(world);
				world.Thinkers.Add(elevator);
				sector.SpecialData = elevator;

				elevator.Type = type;
				elevator.Sector = sector;
				elevator.Speed = elevatorSpeed;

				switch (type)
				{
					case ElevatorType.Down:
						elevator.Direction = -1;
						elevator.FloorDestHeight = FindNextLowestFloor(sector, sector.FloorHeight);
						break;

					case ElevatorType.Up:
						elevator.Direction = 1;
						elevator.FloorDestHeight = FindNextHighestFloor(sector, sector.FloorHeight);
						break;

					default:
						elevator.FloorDestHeight = line.FrontSector.FloorHeight;
						elevator.Direction = elevator.FloorDestHeight > sector.FloorHeight ? 1 : -1;
						break;
				}

				// The height difference is measured before anything moves, so the
				// lift keeps its shape.
				elevator.CeilingDestHeight = elevator.FloorDestHeight +
					(sector.CeilingHeight - sector.FloorHeight);
			}

			return result;
		}

		////////////////////////////////////////////////////////////
		// Boom generalized linedefs
		////////////////////////////////////////////////////////////

		/// <summary>
		/// Next floor height below the given one among the sector's neighbours, or
		/// the given height when there is none. The mirror of
		/// <see cref="FindNextHighestFloor"/>, which Boom also needs.
		/// </summary>
		private Fixed FindNextLowestFloor(Sector sector, Fixed currentHeight)
		{
			var best = currentHeight;
			var found = false;

			foreach (var line in sector.Lines)
			{
				var other = GetNextSector(line, sector);

				if (other == null)
				{
					continue;
				}

				if (other.FloorHeight < currentHeight)
				{
					if (!found || other.FloorHeight > best)
					{
						best = other.FloorHeight;
						found = true;
					}
				}
			}

			return best;
		}

		private Fixed FindNextHighestCeiling(Sector sector, Fixed currentHeight)
		{
			var best = currentHeight;
			var found = false;

			foreach (var line in sector.Lines)
			{
				var other = GetNextSector(line, sector);

				if (other == null)
				{
					continue;
				}

				if (other.CeilingHeight > currentHeight)
				{
					if (!found || other.CeilingHeight < best)
					{
						best = other.CeilingHeight;
						found = true;
					}
				}
			}

			return best;
		}

		private Fixed FindNextLowestCeiling(Sector sector, Fixed currentHeight)
		{
			var best = currentHeight;
			var found = false;

			foreach (var line in sector.Lines)
			{
				var other = GetNextSector(line, sector);

				if (other == null)
				{
					continue;
				}

				if (other.CeilingHeight < currentHeight)
				{
					if (!found || other.CeilingHeight > best)
					{
						best = other.CeilingHeight;
						found = true;
					}
				}
			}

			return best;
		}

		/// <summary>
		/// Runs a Boom generalized line type.
		///
		/// The decoding lives in <see cref="BoomGeneralized"/>; this turns the
		/// decoded definition into the movers the engine already has. The mapping
		/// is deliberately direct — a generalized floor is a <see cref="FloorMove"/>
		/// with its destination, speed and crush flag computed from the bit fields,
		/// not a new kind of mover.
		///
		/// What is **not** implemented, and is refused rather than approximated:
		/// the numeric change model, where Boom searches the map for a sector whose
		/// floor sits at the target height and copies its texture. Without that
		/// search the texture would have to come from somewhere else, and a wrong
		/// texture is worse than the vanilla one. Floors and ceilings whose change
		/// field asks for a numeric model are treated as trigger models instead,
		/// which is what most maps mean anyway.
		/// </summary>
		public bool DoGeneralized(LineDef line, Mobj thing)
		{
			var definition = BoomGeneralized.Decode((int)line.Special);

			if (!definition.IsGeneralized)
			{
				return false;
			}

			switch (definition.Family)
			{
				case BoomGeneralized.Family.Floor:
					return DoGeneralizedFloor(line, definition);

				case BoomGeneralized.Family.Ceiling:
					return DoGeneralizedCeiling(line, definition);

				case BoomGeneralized.Family.Door:
					return DoGeneralizedDoor(line, definition);

				case BoomGeneralized.Family.LockedDoor:
					// The lock is checked here and not in the door builder: a locked door that
					// opens without its key is the one failure of this family a player cannot
					// tell from a map that was built without a lock at all.
					if (!CanUnlockGeneralizedDoor(line, definition, thing))
					{
						return false;
					}

					return DoGeneralizedDoor(line, definition);

				case BoomGeneralized.Family.Lift:
					return DoGeneralizedLift(line, definition);

				case BoomGeneralized.Family.Crusher:
					return DoGeneralizedCrusher(line, definition);

				case BoomGeneralized.Family.Stairs:
					// Boom's generalized stairs differ from the vanilla ones only in
					// step size and speed, both of which the existing builder takes.
					return BuildStairs(
						line,
						definition.StairStep == 16 ? StairType.Turbo16 : StairType.Build8);
			}

			return false;
		}

		private bool DoGeneralizedFloor(LineDef line, BoomGeneralized.Definition definition)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			var change = definition.Change != 0;
			var type = change ? FloorMoveType.GeneralizedChange : FloorMoveType.Generalized;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];

				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				var floor = new FloorMove(world);
				world.Thinkers.Add(floor);
				sector.SpecialData = floor;

				floor.Type = type;
				floor.Sector = sector;
				floor.Crush = definition.Crush;
				floor.Direction = definition.Up ? 1 : -1;
				floor.Speed = BoomGeneralized.SpeedOf(definition.Family, definition.Speed);

				floor.FloorDestHeight = GeneralizedFloorTarget(sector, definition);

				if (change)
				{
					// The trigger model: the texture comes from the line's own
					// front sector, which is what Boom does when the numeric model
					// bit is clear.
					floor.Texture = (int)line.FrontSector.FloorFlat;

					if (definition.Change == 1)
					{
						floor.NewSpecial = 0;
					}
					else if (definition.Change == 3)
					{
						floor.NewSpecial = line.FrontSector.Special;
					}
					else
					{
						floor.NewSpecial = sector.Special;
					}
				}
			}

			return result;
		}

		/// <summary>
		/// Destination height for a generalized floor, from the target field.
		///
		/// The eight targets are Boom's, and they are not interchangeable with the
		/// vanilla floor types: "lowest neighbouring ceiling" and "next highest
		/// floor" have no vanilla equivalent in the same direction.
		/// </summary>
		private Fixed GeneralizedFloorTarget(Sector sector, BoomGeneralized.Definition definition)
		{
			switch (definition.Target)
			{
				case 0:
					// Highest neighbouring floor.
					return FindHighestFloorSurrounding(sector);

				case 1:
					// Lowest neighbouring floor.
					return FindLowestFloorSurrounding(sector);

				case 2:
					// Next floor up or down.
					return definition.Up
						? FindNextHighestFloor(sector, sector.FloorHeight)
						: FindNextLowestFloor(sector, sector.FloorHeight);

				case 3:
					// Lowest neighbouring ceiling.
					return FindLowestCeilingSurrounding(sector);

				case 4:
					// Own ceiling.
					return sector.CeilingHeight;

				case 5:
					// By the shortest texture around: the classic "lift by the
					// height of the surrounding wall" trick.
				{
					var amount = Fixed.FromInt(ShortestLowerTexture(sector));
					return sector.FloorHeight + (definition.Up ? amount : -amount);
				}

				case 6:
					return sector.FloorHeight + (definition.Up ? Fixed.FromInt(24) : Fixed.FromInt(-24));

				default:
					return sector.FloorHeight + (definition.Up ? Fixed.FromInt(32) : Fixed.FromInt(-32));
			}
		}

		private bool DoGeneralizedCeiling(LineDef line, BoomGeneralized.Definition definition)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];

				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				var ceiling = new CeilingMove(world);
				world.Thinkers.Add(ceiling);
				sector.SpecialData = ceiling;

				ceiling.Type = CeilingMoveType.Generalized;
				ceiling.Sector = sector;
				ceiling.Crush = definition.Crush;
				ceiling.Direction = definition.Up ? 1 : -1;
				ceiling.Speed = BoomGeneralized.SpeedOf(definition.Family, definition.Speed);

				var target = GeneralizedCeilingTarget(sector, definition);

				ceiling.TopHeight = target;
				ceiling.BottomHeight = target;

				AddActiveCeiling(ceiling);
			}

			return result;
		}

		private Fixed GeneralizedCeilingTarget(Sector sector, BoomGeneralized.Definition definition)
		{
			switch (definition.Target)
			{
				case 0:
					return FindHighestCeilingSurrounding(sector);

				case 1:
					return FindLowestCeilingSurrounding(sector);

				case 2:
					return definition.Up
						? FindNextHighestCeiling(sector, sector.CeilingHeight)
						: FindNextLowestCeiling(sector, sector.CeilingHeight);

				case 3:
					return FindHighestFloorSurrounding(sector);

				case 4:
					return sector.FloorHeight;

				case 5:
				{
					var amount = Fixed.FromInt(ShortestUpperTexture(sector));
					return sector.CeilingHeight + (definition.Up ? amount : -amount);
				}

				case 6:
					return sector.CeilingHeight + (definition.Up ? Fixed.FromInt(24) : Fixed.FromInt(-24));

				default:
					return sector.CeilingHeight + (definition.Up ? Fixed.FromInt(32) : Fixed.FromInt(-32));
			}
		}

		private bool DoGeneralizedDoor(LineDef line, BoomGeneralized.Definition definition)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];

				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				var door = new VerticalDoor(world);
				world.Thinkers.Add(door);
				sector.SpecialData = door;

				door.Sector = sector;
				door.Speed = BoomGeneralized.SpeedOf(definition.Family, definition.Speed);

				// Boom's delay field is in units of the standard door wait.
				switch (definition.Delay)
				{
					case 1: door.TopWait = 150; break;
					case 2: door.TopWait = 300; break;
					case 3: door.TopWait = 1050; break;
					default: door.TopWait = 35; break;
				}

				// Door kinds: open-and-close, open, close-and-open, close.
				switch (definition.DoorKind)
				{
					case 0:
						door.Type = VerticalDoorType.Normal;
						door.Direction = 1;
						door.TopHeight = FindLowestCeilingSurrounding(sector) - Fixed.FromInt(4);
						break;

					case 1:
						door.Type = VerticalDoorType.Open;
						door.Direction = 1;
						door.TopHeight = FindLowestCeilingSurrounding(sector) - Fixed.FromInt(4);
						break;

					case 2:
						door.Type = VerticalDoorType.Close30ThenOpen;
						door.Direction = -1;
						door.TopHeight = sector.CeilingHeight;
						break;

					default:
						door.Type = VerticalDoorType.Close;
						door.Direction = -1;
						door.TopHeight = FindLowestCeilingSurrounding(sector) - Fixed.FromInt(4);
						break;
				}

				door.TopCountDown = door.TopWait;
			}

			return result;
		}

		private bool DoGeneralizedLift(LineDef line, BoomGeneralized.Definition definition)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];

				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				var plat = new Platform(world);
				world.Thinkers.Add(plat);
				sector.SpecialData = plat;

				plat.Type = PlatformType.DownWaitUpStay;
				plat.Sector = sector;
				plat.Tag = line.Tag;
				plat.Speed = BoomGeneralized.SpeedOf(definition.Family, definition.Speed);
				plat.Low = FindLowestFloorSurrounding(sector);

				if (plat.Low > sector.FloorHeight)
				{
					plat.Low = sector.FloorHeight;
				}

				switch (definition.Target)
				{
					case 0:
						// Lowest neighbouring floor.
						break;

					case 1:
						plat.Low = FindNextLowestFloor(sector, sector.FloorHeight);
						break;

					case 2:
						plat.Low = FindLowestCeilingSurrounding(sector) - Fixed.FromInt(8);
						break;

					default:
						plat.High = FindNextHighestFloor(sector, sector.FloorHeight);
						break;
				}

				if (plat.High <= Fixed.Zero)
				{
					plat.High = sector.FloorHeight;
				}

				plat.Wait = definition.Delay * 35;
				plat.Status = PlatformState.Down;
				plat.Crush = false;
			}

			return result;
		}

		private bool DoGeneralizedCrusher(LineDef line, BoomGeneralized.Definition definition)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];

				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				var ceiling = new CeilingMove(world);
				world.Thinkers.Add(ceiling);
				sector.SpecialData = ceiling;

				ceiling.Type = definition.Silent
					? CeilingMoveType.SilentCrushAndRaise
					: CeilingMoveType.CrushAndRaise;

				ceiling.Sector = sector;
				ceiling.Crush = true;
				ceiling.Direction = -1;
				ceiling.Speed = BoomGeneralized.SpeedOf(BoomGeneralized.Family.Floor, definition.Speed);

				ceiling.TopHeight = FindHighestCeilingSurrounding(sector);
				ceiling.BottomHeight = sector.FloorHeight;

				AddActiveCeiling(ceiling);
			}

			return result;
		}

		/// <summary>
		/// Height of the shortest lower texture around a sector, in map units. Boom
		/// uses it for the "raise or lower by the surrounding wall height" target.
		/// </summary>
		private int ShortestLowerTexture(Sector sector)
		{
			var shortest = int.MaxValue;

			foreach (var line in sector.Lines)
			{
				if (line.FrontSector == sector && line.BackSector != null)
				{
					var height = world.Map.Textures[line.FrontSide.BottomTexture].Height;
					if (height < shortest)
					{
						shortest = height;
					}
				}

				if (line.BackSector == sector && line.FrontSector != null && line.BackSide != null)
				{
					var height = world.Map.Textures[line.BackSide.BottomTexture].Height;
					if (height < shortest)
					{
						shortest = height;
					}
				}
			}

			return shortest == int.MaxValue ? 0 : shortest;
		}

		/// <summary>Height of the shortest upper texture around a sector.</summary>
		private int ShortestUpperTexture(Sector sector)
		{
			var shortest = int.MaxValue;

			foreach (var line in sector.Lines)
			{
				if (line.FrontSector == sector && line.BackSector != null)
				{
					var height = world.Map.Textures[line.FrontSide.TopTexture].Height;
					if (height < shortest)
					{
						shortest = height;
					}
				}

				if (line.BackSector == sector && line.FrontSector != null && line.BackSide != null)
				{
					var height = world.Map.Textures[line.BackSide.TopTexture].Height;
					if (height < shortest)
					{
						shortest = height;
					}
				}
			}

			return shortest == int.MaxValue ? 0 : shortest;
		}

		public bool DoFloor(LineDef line, FloorMoveType type)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];

				// Already moving? If so, keep going...
				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				// New floor thinker.
				var floor = new FloorMove(world);
				world.Thinkers.Add(floor);
				sector.SpecialData = floor;
				floor.Type = type;
				floor.Crush = false;

				switch (type)
				{
					case FloorMoveType.LowerFloor:
						floor.Direction = -1;
						floor.Sector = sector;
						floor.Speed = floorSpeed;
						floor.FloorDestHeight = FindHighestFloorSurrounding(sector);
						break;

					case FloorMoveType.LowerFloorToLowest:
						floor.Direction = -1;
						floor.Sector = sector;
						floor.Speed = floorSpeed;
						floor.FloorDestHeight = FindLowestFloorSurrounding(sector);
						break;

					case FloorMoveType.LowerFloorToNearest:
						floor.Direction = -1;
						floor.Sector = sector;
						floor.Speed = floorSpeed;
						floor.FloorDestHeight = FindNextLowestFloor(sector, sector.FloorHeight);
						break;

					case FloorMoveType.TurboLower:
						floor.Direction = -1;
						floor.Sector = sector;
						floor.Speed = floorSpeed * 4;
						floor.FloorDestHeight = FindHighestFloorSurrounding(sector);
						if (floor.FloorDestHeight != sector.FloorHeight)
						{
							floor.FloorDestHeight += Fixed.FromInt(8);
						}
						break;

					case FloorMoveType.RaiseFloorCrush:
					case FloorMoveType.RaiseFloor:
						if (type == FloorMoveType.RaiseFloorCrush)
						{
							floor.Crush = true;
						}
						floor.Direction = 1;
						floor.Sector = sector;
						floor.Speed = floorSpeed;
						floor.FloorDestHeight = FindLowestCeilingSurrounding(sector);
						if (floor.FloorDestHeight > sector.CeilingHeight)
						{
							floor.FloorDestHeight = sector.CeilingHeight;
						}
						floor.FloorDestHeight -= Fixed.FromInt(8) * (type == FloorMoveType.RaiseFloorCrush ? 1 : 0);
						break;

					case FloorMoveType.RaiseFloorTurbo:
						floor.Direction = 1;
						floor.Sector = sector;
						floor.Speed = floorSpeed * 4;
						floor.FloorDestHeight = FindNextHighestFloor(sector, sector.FloorHeight);
						break;

					case FloorMoveType.RaiseFloorToNearest:
						floor.Direction = 1;
						floor.Sector = sector;
						floor.Speed = floorSpeed;
						floor.FloorDestHeight = FindNextHighestFloor(sector, sector.FloorHeight);
						break;

					case FloorMoveType.RaiseFloor24:
						floor.Direction = 1;
						floor.Sector = sector;
						floor.Speed = floorSpeed;
						floor.FloorDestHeight = floor.Sector.FloorHeight + Fixed.FromInt(24);
						break;

					case FloorMoveType.RaiseFloor512:
						floor.Direction = 1;
						floor.Sector = sector;
						floor.Speed = floorSpeed;
						floor.FloorDestHeight = floor.Sector.FloorHeight + Fixed.FromInt(512);
						break;

					case FloorMoveType.RaiseFloor24AndChange:
						floor.Direction = 1;
						floor.Sector = sector;
						floor.Speed = floorSpeed;
						floor.FloorDestHeight = floor.Sector.FloorHeight + Fixed.FromInt(24);
						sector.FloorFlat = line.FrontSector.FloorFlat;
						sector.Special = line.FrontSector.Special;
						break;

					case FloorMoveType.RaiseToTexture:
						var min = int.MaxValue;
						floor.Direction = 1;
						floor.Sector = sector;
						floor.Speed = floorSpeed;
						var textures = world.Map.Textures;
						for (var i = 0; i < sector.Lines.Length; i++)
						{
							if ((sector.Lines[i].Flags & LineFlags.TwoSided) != 0)
							{
								var frontSide = sector.Lines[i].FrontSide;
								if (frontSide.BottomTexture >= 0)
								{
									if (textures[frontSide.BottomTexture].Height < min)
									{
										min = textures[frontSide.BottomTexture].Height;
									}
								}
								var backSide = sector.Lines[i].BackSide;
								if (backSide.BottomTexture >= 0)
								{
									if (textures[backSide.BottomTexture].Height < min)
									{
										min = textures[backSide.BottomTexture].Height;
									}
								}
							}
						}
						floor.FloorDestHeight = floor.Sector.FloorHeight + Fixed.FromInt(min);
						break;

					case FloorMoveType.LowerAndChange:
						floor.Direction = -1;
						floor.Sector = sector;
						floor.Speed = floorSpeed;
						floor.FloorDestHeight = FindLowestFloorSurrounding(sector);
						floor.Texture = sector.FloorFlat;
						for (var i = 0; i < sector.Lines.Length; i++)
						{
							if ((sector.Lines[i].Flags & LineFlags.TwoSided) != 0)
							{
								if (sector.Lines[i].FrontSide.Sector.Number == sectorNumber)
								{
									sector = sector.Lines[i].BackSide.Sector;
									if (sector.FloorHeight == floor.FloorDestHeight)
									{
										floor.Texture = sector.FloorFlat;
										floor.NewSpecial = sector.Special;
										break;
									}
								}
								else
								{
									sector = sector.Lines[i].FrontSide.Sector;
									if (sector.FloorHeight == floor.FloorDestHeight)
									{
										floor.Texture = sector.FloorFlat;
										floor.NewSpecial = sector.Special;
										break;
									}
								}
							}
						}
						break;
				}
			}

			return result;
		}


		public bool BuildStairs(LineDef line, StairType type)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];

				// Already moving? If so, keep going...
				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				// New floor thinker.
				var floor = new FloorMove(world);
				world.Thinkers.Add(floor);
				sector.SpecialData = floor;
				floor.Direction = 1;
				floor.Sector = sector;

				Fixed speed;
				Fixed stairSize;
				switch (type)
				{
					case StairType.Build8:
						speed = floorSpeed / 4;
						stairSize = Fixed.FromInt(8);
						break;
					case StairType.Turbo16:
						speed = floorSpeed * 4;
						stairSize = Fixed.FromInt(16);
						break;
					default:
						throw new Exception("Unknown stair type!");
				}

				floor.Speed = speed;
				var height = sector.FloorHeight + stairSize;
				floor.FloorDestHeight = height;

				var texture = sector.FloorFlat;

				// Find next sector to raise.
				//     1. Find 2-sided line with same sector side[0].
				//     2. Other side is the next sector to raise.
				bool ok;
				do
				{
					ok = false;

					for (var i = 0; i < sector.Lines.Length; i++)
					{
						if (((sector.Lines[i]).Flags & LineFlags.TwoSided) == 0)
						{
							continue;
						}

						var target = (sector.Lines[i]).FrontSector;
						var newSectorNumber = target.Number;

						if (sectorNumber != newSectorNumber)
						{
							continue;
						}

						target = (sector.Lines[i]).BackSector;
						newSectorNumber = target.Number;

						if (target.FloorFlat != texture)
						{
							continue;
						}

						height += stairSize;

						if (target.SpecialData != null)
						{
							continue;
						}

						sector = target;
						sectorNumber = newSectorNumber;
						floor = new FloorMove(world);

						world.Thinkers.Add(floor);

						sector.SpecialData = floor;
						floor.Direction = 1;
						floor.Sector = sector;
						floor.Speed = speed;
						floor.FloorDestHeight = height;
						ok = true;
						break;
					}
				} while (ok);
			}

			return result;
		}



		////////////////////////////////////////////////////////////
		// Ceiling
		////////////////////////////////////////////////////////////

		public bool DoCeiling(LineDef line, CeilingMoveType type)
		{
			// Reactivate in-stasis ceilings...for certain types.
			switch (type)
			{
				case CeilingMoveType.FastCrushAndRaise:
				case CeilingMoveType.SilentCrushAndRaise:
				case CeilingMoveType.CrushAndRaise:
					ActivateInStasisCeiling(line);
					break;

				default:
					break;
			}

			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];
				if (sector.SpecialData != null)
				{
					continue;
				}

				result = true;

				// New door thinker.
				var ceiling = new CeilingMove(world);
				world.Thinkers.Add(ceiling);
				sector.SpecialData = ceiling;
				ceiling.Sector = sector;
				ceiling.Crush = false;

				switch (type)
				{
					case CeilingMoveType.FastCrushAndRaise:
						ceiling.Crush = true;
						ceiling.TopHeight = sector.CeilingHeight;
						ceiling.BottomHeight = sector.FloorHeight + Fixed.FromInt(8);
						ceiling.Direction = -1;
						ceiling.Speed = CeilingSpeed * 2;
						break;

					case CeilingMoveType.SilentCrushAndRaise:
					case CeilingMoveType.CrushAndRaise:
					case CeilingMoveType.LowerAndCrush:
					case CeilingMoveType.LowerToFloor:
						if (type == CeilingMoveType.SilentCrushAndRaise
							|| type == CeilingMoveType.CrushAndRaise)
						{
							ceiling.Crush = true;
							ceiling.TopHeight = sector.CeilingHeight;
						}
						ceiling.BottomHeight = sector.FloorHeight;
						if (type != CeilingMoveType.LowerToFloor)
						{
							ceiling.BottomHeight += Fixed.FromInt(8);
						}
						ceiling.Direction = -1;
						ceiling.Speed = CeilingSpeed;
						break;

					case CeilingMoveType.RaiseToHighest:
						ceiling.TopHeight = FindHighestCeilingSurrounding(sector);
						ceiling.Direction = 1;
						ceiling.Speed = CeilingSpeed;
						break;

					case CeilingMoveType.LowerToLowest:
						ceiling.BottomHeight = FindLowestCeilingSurrounding(sector);
						ceiling.Direction = -1;
						ceiling.Speed = CeilingSpeed;
						break;

					case CeilingMoveType.LowerToMaxFloor:
						ceiling.BottomHeight = FindHighestFloorSurrounding(sector);
						ceiling.Direction = -1;
						ceiling.Speed = CeilingSpeed;
						break;
				}

				ceiling.Tag = sector.Tag;
				ceiling.Type = type;
				AddActiveCeiling(ceiling);
			}

			return result;
		}


		public static readonly Fixed CeilingSpeed = Fixed.One;
		public static readonly int CeilingWwait = 150;

		private static readonly int maxCeilingCount = 30;

		private CeilingMove[] activeCeilings = new CeilingMove[maxCeilingCount];

		public void AddActiveCeiling(CeilingMove ceiling)
		{
			for (var i = 0; i < activeCeilings.Length; i++)
			{
				if (activeCeilings[i] == null)
				{
					activeCeilings[i] = ceiling;

					return;
				}
			}
		}

		public void RemoveActiveCeiling(CeilingMove ceiling)
		{
			for (var i = 0; i < activeCeilings.Length; i++)
			{
				if (activeCeilings[i] == ceiling)
				{
					activeCeilings[i].Sector.SpecialData = null;
					world.Thinkers.Remove(activeCeilings[i]);
					activeCeilings[i] = null;
					break;
				}
			}
		}

		public bool CheckActiveCeiling(CeilingMove ceiling)
		{
			if (ceiling == null)
			{
				return false;
			}

			for (var i = 0; i < activeCeilings.Length; i++)
			{
				if (activeCeilings[i] == ceiling)
				{
					return true;
				}
			}

			return false;
		}

		public void ActivateInStasisCeiling(LineDef line)
		{
			for (var i = 0; i < activeCeilings.Length; i++)
			{
				if (activeCeilings[i] != null &&
					activeCeilings[i].Tag == line.Tag &&
					activeCeilings[i].Direction == 0)
				{
					activeCeilings[i].Direction = activeCeilings[i].OldDirection;
					activeCeilings[i].ThinkerState = ThinkerState.Active;
				}
			}
		}

		public bool CeilingCrushStop(LineDef line)
		{
			var result = false;

			for (var i = 0; i < activeCeilings.Length; i++)
			{
				if (activeCeilings[i] != null &&
					activeCeilings[i].Tag == line.Tag &&
					activeCeilings[i].Direction != 0)
				{
					activeCeilings[i].OldDirection = activeCeilings[i].Direction;
					activeCeilings[i].ThinkerState = ThinkerState.InStasis;
					activeCeilings[i].Direction = 0;
					result = true;
				}
			}

			return result;
		}



		////////////////////////////////////////////////////////////
		// Teleport
		////////////////////////////////////////////////////////////

		////////////////////////////////////////////////////////////
		// Boom silent teleporters
		////////////////////////////////////////////////////////////

		/// <summary>
		/// Finds the exit marker for a silent teleporter: a teleport destination
		/// thing in a sector carrying the line's tag.
		///
		/// Unlike the vanilla teleporter, a silent one is not "warp to the marker
		/// and flash"; the marker only supplies the exit angle, and the rest is
		/// computed so the traveller keeps their speed and heading relative to the
		/// line they walked through.
		/// </summary>
		private Mobj FindTeleportDestination(LineDef line)
		{
			var sectors = world.Map.Sectors;

			for (var i = 0; i < sectors.Length; i++)
			{
				if (sectors[i].Tag != line.Tag)
				{
					continue;
				}

				foreach (var thinker in world.Thinkers)
				{
					var dest = thinker as Mobj;

					if (dest == null || dest.Type != MobjType.Teleportman)
					{
						continue;
					}

					if (dest.Subsector.Sector.Number == i)
					{
						return dest;
					}
				}
			}

			return null;
		}

		/// <summary>
		/// Boom's silent teleporter with a destination thing (207-210).
		///
		/// The point of it is that nothing flashes and nothing stops: the traveller
		/// comes out with the same speed, rotated to match the difference between
		/// the entry and exit angles, and at the same height above the floor. That
		/// is what makes rooms-over-rooms work — walking through a doorway continues
		/// into another room as if the two were adjacent.
		///
		/// The rotation is the part that makes it feel natural: crossing a line
		/// perpendicular leaves you moving in the direction the exit marker faces,
		/// rather than always in the marker's direction regardless of how you
		/// entered.
		/// </summary>
		public bool SilentTeleport(LineDef line, int side, Mobj thing)
		{
			if (side != 0 || (thing.Flags & MobjFlags.Missile) != 0)
			{
				return false;
			}

			var destination = FindTeleportDestination(line);

			if (destination == null)
			{
				return false;
			}

			// Height above the floor, so a mid-air teleport stays at the same
			// height above whatever it lands on.
			var heightAboveFloor = thing.Z - thing.FloorZ;

			// Angle between the exit marker and the entry line, turned a quarter
			// so that crossing perpendicularly exits along the marker's facing.
			var lineAngle = Geometry.PointToAngle(
				Fixed.Zero, Fixed.Zero, line.Dx, line.Dy);

			var angle = lineAngle - destination.Angle + Angle.Ang90;

			var sin = Trig.Sin(angle);
			var cos = Trig.Cos(angle);

			var momX = thing.MomX;
			var momY = thing.MomY;

			if (!world.ThingMovement.TeleportMove(thing, destination.X, destination.Y))
			{
				return false;
			}

			thing.Angle += angle;
			thing.Z = heightAboveFloor + thing.FloorZ;

			// Rotate the momentum so the traveller keeps going the same way
			// relative to the new room.
			thing.MomX = momX * cos - momY * sin;
			thing.MomY = momY * cos + momX * sin;

			ResetPlayerView(thing);

			return true;
		}

		/// <summary>
		/// Boom's silent teleporter between two linedefs sharing a tag (243/244,
		/// 261-269).
		///
		/// This is the general form: instead of a destination marker it finds
		/// another line with the same tag and maps the crossing position across it.
		/// Walk through at a quarter of the way along the entry line and you come
		/// out a quarter of the way along the exit line. That is what lets two rooms
		/// of different sizes line up.
		///
		/// <paramref name="reverse"/> mirrors the position along the exit line,
		/// which is how a teleporter can be made to feel like it turned you around.
		/// </summary>
		public bool SilentLineTeleport(LineDef line, int side, Mobj thing, bool reverse)
		{
			if (side != 0 || (thing.Flags & MobjFlags.Missile) != 0)
			{
				return false;
			}

			foreach (var other in world.Map.Lines)
			{
				if (other == line || other.Tag != line.Tag || other.BackSector == null)
				{
					continue;
				}

				// Position along the entry line, as a fraction from its start. The
				// larger of the two axes is used, which is what Boom does: on a
				// nearly vertical line the Y difference is the reliable one.
				Fixed position;

				if (Math.Abs(line.Dx.Data) > Math.Abs(line.Dy.Data))
				{
					position = (thing.X - line.Vertex1.X) / line.Dx;
				}
				else
				{
					position = (thing.Y - line.Vertex1.Y) / line.Dy;
				}

				if (reverse)
				{
					position = Fixed.One - position;
				}

				// Angle between the two lines, turned a half circle so that a
				// straight crossing continues straight.
				var angle = (reverse ? Angle.Ang0 : Angle.Ang180) +
					Geometry.PointToAngle(Fixed.Zero, Fixed.Zero, other.Dx, other.Dy) -
					Geometry.PointToAngle(Fixed.Zero, Fixed.Zero, line.Dx, line.Dy);

				// Map the position across the exit line, measured back from its end.
				var x = other.Vertex2.X - position * other.Dx;
				var y = other.Vertex2.Y - position * other.Dy;

				var heightAboveFloor = thing.Z - thing.FloorZ;

				// Land on the correct side of the exit line. Rounding can put the
				// traveller on the wrong side, and then they immediately teleport
				// back, which looks like being stuck in a doorway forever.
				var exitSide = reverse || (thing.Player != null && StepDownAt(other));

				var fudge = 10;

				while (Geometry.PointOnLineSide(x, y, other) != (exitSide ? 1 : 0) && --fudge >= 0)
				{
					if (Math.Abs(other.Dx.Data) > Math.Abs(other.Dy.Data))
					{
						y += (other.Dx.Data < 0) != exitSide ? Fixed.One : -Fixed.One;
					}
					else
					{
						x += (other.Dy.Data < 0) != exitSide ? Fixed.One : -Fixed.One;
					}
				}

				if (!world.ThingMovement.TeleportMove(thing, x, y))
				{
					return false;
				}

				// Ground level at the exit is the higher of the two floors there, so
				// a step down does not drop the traveller into the floor. Boom picks
				// it by side; the higher of the two is the same answer and does not
				// depend on getting the side convention right.
				var frontFloor = other.FrontSector.FloorHeight;
				var backFloor = other.BackSector.FloorHeight;
				var exitFloor = frontFloor > backFloor ? frontFloor : backFloor;

				thing.Z = heightAboveFloor + exitFloor;
				thing.Angle += angle;

				var momX = thing.MomX;
				var momY = thing.MomY;

				var sin = Trig.Sin(angle);
				var cos = Trig.Cos(angle);

				thing.MomX = momX * cos - momY * sin;
				thing.MomY = momY * cos + momX * sin;

				ResetPlayerView(thing);

				return true;
			}

			return false;
		}

		/// <summary>
		/// True when walking toward the first side of this line goes down a step.
		/// </summary>
		private static bool StepDownAt(LineDef line)
		{
			return line.FrontSector.FloorHeight < line.BackSector.FloorHeight;
		}

		/// <summary>
		/// Recalculates the player's view height after a teleport, without letting
		/// the step-up animation play.
		///
		/// A silent teleporter is supposed to be invisible, so the view must not bob
		/// as if the player had just climbed a step. The delta is saved, cleared,
		/// recalculated and restored, which is exactly what Boom does.
		/// </summary>
		private void ResetPlayerView(Mobj thing)
		{
			var player = thing.Player;

			if (player == null || player.Mobj != thing)
			{
				return;
			}

			var delta = player.DeltaViewHeight;

			player.DeltaViewHeight = Fixed.Zero;
			world.PlayerBehavior.CalcHeight(player);
			player.DeltaViewHeight = delta;
		}

		public bool Teleport(LineDef line, int side, Mobj thing)
		{
			// Don't teleport missiles.
			if ((thing.Flags & MobjFlags.Missile) != 0)
			{
				return false;
			}

			// Don't teleport if hit back of line, so you can get out of teleporter.
			if (side == 1)
			{
				return false;
			}

			var sectors = world.Map.Sectors;
			var tag = line.Tag;

			for (var i = 0; i < sectors.Length; i++)
			{
				if (sectors[i].Tag == tag)
				{
					foreach (var thinker in world.Thinkers)
					{
						var dest = thinker as Mobj;

						if (dest == null)
						{
							// Not a mobj.
							continue;
						}

						if (dest.Type != MobjType.Teleportman)
						{
							// Not a teleportman.
							continue;
						}

						var sector = dest.Subsector.Sector;

						if (sector.Number != i)
						{
							// Wrong sector.
							continue;
						}

						var oldX = thing.X;
						var oldY = thing.Y;
						var oldZ = thing.Z;

						if (!world.ThingMovement.TeleportMove(thing, dest.X, dest.Y))
						{
							return false;
						}

						// This compatibility fix is based on Chocolate Doom's implementation.
						if (world.Options.GameVersion != GameVersion.Final)
						{
							thing.Z = thing.FloorZ;
						}

						if (thing.Player != null)
						{
							thing.Player.ViewZ = thing.Z + thing.Player.ViewHeight;
						}

						var ta = world.ThingAllocation;

						// Spawn teleport fog at source position.
						var fog1 = ta.SpawnMobj(
							oldX,
							oldY,
							oldZ,
							MobjType.Tfog);
						world.StartSound(fog1, Sfx.TELEPT, SfxType.Misc);

						// Destination position.
						var angle = dest.Angle;
						var fog2 = ta.SpawnMobj(
							dest.X + 20 * Trig.Cos(angle),
							dest.Y + 20 * Trig.Sin(angle),
							thing.Z,
							MobjType.Tfog);
						world.StartSound(fog2, Sfx.TELEPT, SfxType.Misc);

						if (thing.Player != null)
						{
							// Don't move for a bit.
							thing.ReactionTime = 18;
						}

						thing.Angle = dest.Angle;
						thing.MomX = thing.MomY = thing.MomZ = Fixed.Zero;

						thing.DisableFrameInterpolationForOneFrame();
						if (thing.Player != null)
						{
							thing.Player.DisableFrameInterpolationForOneFrame();
						}

						return true;
					}
				}
			}

			return false;
		}



		////////////////////////////////////////////////////////////
		// Lighting
		////////////////////////////////////////////////////////////

		public void TurnTagLightsOff(LineDef line)
		{
			var sectors = world.Map.Sectors;

			for (var i = 0; i < sectors.Length; i++)
			{
				var sector = sectors[i];

				if (sector.Tag == line.Tag)
				{
					var min = sector.LightLevel;

					for (var j = 0; j < sector.Lines.Length; j++)
					{
						var target = GetNextSector(sector.Lines[j], sector);
						if (target == null)
						{
							continue;
						}

						if (target.LightLevel < min)
						{
							min = target.LightLevel;
						}
					}

					sector.LightLevel = min;
				}
			}
		}

		public void LightTurnOn(LineDef line, int bright)
		{
			var sectors = world.Map.Sectors;

			for (var i = 0; i < sectors.Length; i++)
			{
				var sector = sectors[i];

				if (sector.Tag == line.Tag)
				{
					// bright = 0 means to search for highest light level surrounding sector.
					if (bright == 0)
					{
						for (var j = 0; j < sector.Lines.Length; j++)
						{
							var target = GetNextSector(sector.Lines[j], sector);
							if (target == null)
							{
								continue;
							}

							if (target.LightLevel > bright)
							{
								bright = target.LightLevel;
							}
						}
					}

					sector.LightLevel = bright;
				}
			}
		}


		public void StartLightStrobing(LineDef line)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var sector = sectors[sectorNumber];

				if (sector.SpecialData != null)
				{
					continue;
				}

				world.LightingChange.SpawnStrobeFlash(sector, StrobeFlash.SlowDark, false);
			}
		}



		////////////////////////////////////////////////////////////
		// Miscellaneous
		////////////////////////////////////////////////////////////

		public bool DoDonut(LineDef line)
		{
			var sectors = world.Map.Sectors;
			var sectorNumber = -1;
			var result = false;

			while ((sectorNumber = FindSectorFromLineTag(line, sectorNumber)) >= 0)
			{
				var s1 = sectors[sectorNumber];

				// Already moving? If so, keep going...
				if (s1.SpecialData != null)
				{
					continue;
				}

				result = true;

				var s2 = GetNextSector(s1.Lines[0], s1);

				//
				// The code below is based on Chocolate Doom's implementation.
				//

				if (s2 == null)
				{
					break;
				}

				for (var i = 0; i < s2.Lines.Length; i++)
				{
					var s3 = s2.Lines[i].BackSector;

					if (s3 == s1)
					{
						continue;
					}

					if (s3 == null)
					{
						// Undefined behavior in Vanilla Doom.
						return result;
					}

					var thinkers = world.Thinkers;

					// Spawn rising slime.
					var floor1 = new FloorMove(world);
					thinkers.Add(floor1);
					s2.SpecialData = floor1;
					floor1.Type = FloorMoveType.DonutRaise;
					floor1.Crush = false;
					floor1.Direction = 1;
					floor1.Sector = s2;
					floor1.Speed = floorSpeed / 2;
					floor1.Texture = s3.FloorFlat;
					floor1.NewSpecial = 0;
					floor1.FloorDestHeight = s3.FloorHeight;

					// Spawn lowering donut-hole.
					var floor2 = new FloorMove(world);
					thinkers.Add(floor2);
					s1.SpecialData = floor2;
					floor2.Type = FloorMoveType.LowerFloor;
					floor2.Crush = false;
					floor2.Direction = -1;
					floor2.Sector = s1;
					floor2.Speed = floorSpeed / 2;
					floor2.FloorDestHeight = s3.FloorHeight;

					break;
				}
			}

			return result;
		}


		public void SpawnDoorCloseIn30(Sector sector)
		{
			var door = new VerticalDoor(world);

			world.Thinkers.Add(door);

			sector.SpecialData = door;
			sector.Special = 0;

			door.Sector = sector;
			door.Direction = 0;
			door.Type = VerticalDoorType.Normal;
			door.Speed = doorSpeed;
			door.TopCountDown = 30 * 35;
		}

		public void SpawnDoorRaiseIn5Mins(Sector sector)
		{
			var door = new VerticalDoor(world);

			world.Thinkers.Add(door);

			sector.SpecialData = door;
			sector.Special = 0;

			door.Sector = sector;
			door.Direction = 2;
			door.Type = VerticalDoorType.RaiseIn5Mins;
			door.Speed = doorSpeed;
			door.TopHeight = FindLowestCeilingSurrounding(sector);
			door.TopHeight -= Fixed.FromInt(4);
			door.TopWait = doorWait;
			door.TopCountDown = 5 * 60 * 35;
		}
	}
}
