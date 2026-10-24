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
    public class Specials
    {
        private static readonly int maxButtonCount = 32;
        private static readonly int buttonTime = 35;

        private World world;

        private bool levelTimer;
        private int levelTimeCount;

        private Button[] buttonList;

        private int[] textureTranslation;
        private int[] flatTranslation;

        /// <summary>
        /// Boom texture scrollers, built once per level from line specials.
        ///
        /// The model is PrBoom's: a scroller is a velocity applied every tic to one
        /// target, where the velocity comes from the line's own direction vector
        /// shifted by <see cref="ScrollShift"/>. Two things are deliberately NOT
        /// copied from PrBoom because nothing in a Boom map depends on them - the
        /// displacement (245-249) and accelerative (214-218) variants - and one is
        /// copied even though it is easy to skip: 252/253 move *things* on the
        /// floor, which is simulation, not decoration.
        /// </summary>
        private Scroller[] scrollers;

        /// <summary>Boom wind/current pushers, built once per level.</summary>
        private Pusher[] pushers;

        private sealed class Pusher
        {
            public Sector Sector;
            public bool IsCurrent;
            public int Dx;
            public int Dy;
        }

        /// <summary>
        /// How far the line vector is shifted right to become a scroll speed.
        ///
        /// PrBoom's SCROLL_SHIFT. It is not a tunable: a Boom map's scrolling water
        /// is authored against this exact divisor, so a different value scrolls
        /// everything at the wrong speed.
        /// </summary>
        private const int ScrollShift = 5;

        /// <summary>
        /// Scale from a scrolling floor to the push it gives the things standing on
        /// it, 3/32 in PrBoom. It makes a carried object move at the same speed as
        /// the texture under it.
        /// </summary>
        private const double CarryFactor = 0.09375;

        private enum ScrollKind
        {
            Side,
            Floor,
            Ceiling,
            Carry
        }

        private sealed class Scroller
        {
            public ScrollKind Kind;
            public SideDef Side;
            public Sector Sector;
            public Fixed Dx;
            public Fixed Dy;
        }

        public Specials(World world)
        {
            this.world = world;

            levelTimer = false;

            buttonList = new Button[maxButtonCount];
            for (var i = 0; i < buttonList.Length; i++)
            {
                buttonList[i] = new Button();
            }

            textureTranslation = new int[world.Map.Textures.Count];
            for (var i = 0; i < textureTranslation.Length; i++)
            {
                textureTranslation[i] = i;
            }

            flatTranslation = new int[world.Map.Flats.Count];
            for (var i = 0; i < flatTranslation.Length; i++)
            {
                flatTranslation[i] = i;
            }
        }

        /// <summary>
        /// After the map has been loaded, scan for specials that spawn thinkers.
        /// </summary>
        public void SpawnSpecials(int levelTimeCount)
        {
            levelTimer = true;
            this.levelTimeCount = levelTimeCount;
            SpawnSpecials();
        }

        /// <summary>
        /// After the map has been loaded, scan for specials that spawn thinkers.
        /// </summary>
        public void SpawnSpecials()
        {
            // Init special sectors.
            var lc = world.LightingChange;
            var sa = world.SectorAction;
            foreach (var sector in world.Map.Sectors)
            {
                if (sector.Special == 0)
                {
                    continue;
                }

                switch (SectorSpecials.EffectOf(sector))
                {
                    case 1:
                        // Flickering lights.
                        lc.SpawnLightFlash(sector);
                        break;

                    case 2:
                        // Strobe fast.
                        lc.SpawnStrobeFlash(sector, StrobeFlash.FastDark, false);
                        break;

                    case 3:
                        // Strobe slow.
                        lc.SpawnStrobeFlash(sector, StrobeFlash.SlowDark, false);
                        break;

                    case 4:
                        // Strobe fast / death slime.
                        lc.SpawnStrobeFlash(sector, StrobeFlash.FastDark, false);
                        sector.Special = (SectorSpecial)4;
                        break;

                    case 8:
                        // Glowing light.
                        lc.SpawnGlowingLight(sector);
                        break;
                    case 9:
                        // Secret sector.
                        world.TotalSecrets++;
                        break;

                    case 10:
                        // Door close in 30 seconds.
                        sa.SpawnDoorCloseIn30(sector);
                        break;

                    case 12:
                        // Sync strobe slow.
                        lc.SpawnStrobeFlash(sector, StrobeFlash.SlowDark, true);
                        break;

                    case 13:
                        // Sync strobe fast.
                        lc.SpawnStrobeFlash(sector, StrobeFlash.FastDark, true);
                        break;

                    case 14:
                        // Door raise in 5 minutes.
                        sa.SpawnDoorRaiseIn5Mins(sector);
                        break;

                    case 17:
                        lc.SpawnFireFlicker(sector);
                        break;
                }
            }

            BuildScrollers();
            BuildPushers();
        }

        /// <summary>
        /// Collects the Boom pushers of this level.
        ///
        /// 224 is a wind and 225 a current: a linedef tagged to sectors makes those
        /// sectors push whatever stands in them, in the direction of the line.
        /// 226 (a point source that pushes things within a radius) is deliberately
        /// absent - it needs the MT_PUSH and MT_PULL things, which Doom II does not
        /// contain and this port has no sprites or states for. Adding the special
        /// without the thing it depends on would look implemented and do nothing.
        ///
        /// Which sectors a pusher acts on is read from the sector itself, not from
        /// the line: Boom sets the pusher flag (0x200) on the sector and the line
        /// only supplies direction and speed. A map that forgot the flag gets no
        /// wind, which is what PrBoom does too.
        /// </summary>
        internal void BuildPushers()
        {
            var list = new List<Pusher>();

            foreach (var line in world.Map.Lines)
            {
                var special = (int)line.Special;

                if (special != 224 && special != 225)
                {
                    continue;
                }

                var dx = line.Dx.ToIntFloor();
                var dy = line.Dy.ToIntFloor();

                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                foreach (var sector in world.Map.Sectors)
                {
                    if (sector.Tag != line.Tag || !SectorSpecials.IsPusher(sector))
                    {
                        continue;
                    }

                    var pusher = new Pusher();
                    pusher.Sector = sector;
                    pusher.IsCurrent = special == 225;
                    pusher.Dx = dx;
                    pusher.Dy = dy;
                    list.Add(pusher);
                }
            }

            pushers = list.ToArray();
        }

        /// <summary>
        /// Applies every pusher for one tic.
        ///
        /// The three-case rule is PrBoom's and is not an aesthetic choice: a wind
        /// pushes harder on something standing up, a current only moves what is on
        /// the ground, and neither moves anything that noclips or floats. Getting it
        /// wrong makes a wind tunnel either useless or a launcher.
        ///
        /// Only players are affected, as in Boom without MBF extensions. Monsters
        /// being blown around is an MBF feature, and this port does not claim it.
        /// </summary>
        private void UpdatePushers()
        {
            if (pushers == null)
            {
                return;
            }

            foreach (var pusher in pushers)
            {
                // The sector's flag can be cleared at runtime (a Boom line that
                // changes the floor type), and then the pusher must stop too.
                if (!SectorSpecials.IsPusher(pusher.Sector))
                {
                    continue;
                }

                var sector = pusher.Sector;

                for (var thing = sector.ThingList; thing != null; thing = thing.SectorNext)
                {
                    if (thing.Player == null)
                    {
                        continue;
                    }

                    if ((thing.Flags & (MobjFlags.NoGravity | MobjFlags.NoClip)) != 0)
                    {
                        continue;
                    }

                    var airborne = thing.Z > sector.FloorHeight;

                    // Wind: full force in the air, half on the ground. Current: no
                    // force in the air, full on the ground.
                    Fixed scale;

                    if (pusher.IsCurrent)
                    {
                        if (airborne)
                        {
                            continue;
                        }

                        scale = Fixed.One;
                    }
                    else
                    {
                        scale = airborne ? Fixed.One : Fixed.One / 2;
                    }

                    // PUSH_FACTOR is 7 in PrBoom, so the momentum added per tic is
                    // the speed divided by 128 - the force a map is authored
                    // against, not a number to tune.
                    var pushX = Fixed.FromInt(pusher.Dx) / PushFactor;
                    var pushY = Fixed.FromInt(pusher.Dy) / PushFactor;

                    thing.MomX += pushX * scale;
                    thing.MomY += pushY * scale;
                }
            }
        }

        /// <summary>PrBoom's PUSH_FACTOR: momentum per tic is the speed over this.</summary>
        private const int PushFactor = 128;

        /// <summary>
        /// Collects the Boom scrollers of this level.
        ///
        /// PrBoom derives every velocity from the line vector shifted right by
        /// <see cref="ScrollShift"/>, so a scroller's speed and direction are read
        /// off the geometry of the line that carries the special. Reproducing that
        /// rule instead of inventing a speed per special is what makes a scrolling
        /// lava fall look right: the author drew the line in the direction they
        /// wanted the texture to move.
        ///
        /// Internal rather than private because a test has to rebuild the set: the
        /// stock maps carry almost no Boom scrollers, so the only way to exercise
        /// the model is to give a line the special and ask for a rebuild.
        /// </summary>
        internal void BuildScrollers()
        {
            var list = new List<Scroller>();

            foreach (var line in world.Map.Lines)
            {
                var special = (int)line.Special;

                if (special != 48 && special != 85 && (special < 250 || special > 255))
                {
                    continue;
                }

                // Direction and speed, in map units per tic.
                var dx = line.Dx >> ScrollShift;
                var dy = line.Dy >> ScrollShift;

                switch (special)
                {
                    case 48:
                        // Scroll the first side right.
                        AddScroller(list, ScrollKind.Side, line.FrontSide, Fixed.One, Fixed.Zero);
                        break;

                    case 85:
                        // Scroll the first side left.
                        AddScroller(list, ScrollKind.Side, line.FrontSide, -Fixed.One, Fixed.Zero);
                        break;

                    case 255:
                        // Constant speed taken from the sidedef's own offsets.
                        AddScroller(list, ScrollKind.Side, line.FrontSide,
                            -line.FrontSide.TextureOffset, line.FrontSide.RowOffset);
                        break;

                    case 250:
                        // Ceiling flats of every sector carrying this tag.
                        AddSectorScrollers(list, ScrollKind.Ceiling, line, -dx, dy);
                        break;

                    case 251:
                    case 253:
                        AddSectorScrollers(list, ScrollKind.Floor, line, -dx, dy);

                        if (special == 251)
                        {
                            break;
                        }

                        // 253 also carries things standing on the floor. This is
                        // simulation, not decoration: a Boom map that moves the
                        // player across the room is built on it.
                        AddSectorScrollers(list, ScrollKind.Carry, line,
                            Fixed.FromDouble(dx.ToDouble() * CarryFactor),
                            Fixed.FromDouble(dy.ToDouble() * CarryFactor));
                        break;

                    case 252:
                        AddSectorScrollers(list, ScrollKind.Carry, line,
                            Fixed.FromDouble(dx.ToDouble() * CarryFactor),
                            Fixed.FromDouble(dy.ToDouble() * CarryFactor));
                        break;

                    case 254:
                        // Walls of every line carrying this tag, or its own when
                        // the tag is zero.
                        if (line.Tag == 0)
                        {
                            AddWallScroller(list, line, -dx, dy);
                        }
                        else
                        {
                            foreach (var other in world.Map.Lines)
                            {
                                if (other != line && other.Tag == line.Tag)
                                {
                                    AddWallScroller(list, other, -dx, dy);
                                }
                            }
                        }

                        break;
                }
            }

            scrollers = list.ToArray();
        }

        private static void AddScroller(
            List<Scroller> list, ScrollKind kind, SideDef side, Fixed dx, Fixed dy)
        {
            if (side == null || (dx == Fixed.Zero && dy == Fixed.Zero))
            {
                return;
            }

            var scroller = new Scroller();
            scroller.Kind = kind;
            scroller.Side = side;
            scroller.Dx = dx;
            scroller.Dy = dy;
            list.Add(scroller);
        }

        private void AddSectorScrollers(
            List<Scroller> list, ScrollKind kind, LineDef line, Fixed dx, Fixed dy)
        {
            if (dx == Fixed.Zero && dy == Fixed.Zero)
            {
                return;
            }

            var tag = line.Tag;

            foreach (var sector in world.Map.Sectors)
            {
                if (sector.Tag != tag)
                {
                    continue;
                }

                var scroller = new Scroller();
                scroller.Kind = kind;
                scroller.Sector = sector;
                scroller.Dx = dx;
                scroller.Dy = dy;
                list.Add(scroller);
            }
        }

        private static void AddWallScroller(
            List<Scroller> list, LineDef line, Fixed dx, Fixed dy)
        {
            // The perpendicular compensation PrBoom applies: a wall scroller has to
            // follow the line it belongs to, so the velocity is rotated into the
            // wall's own frame rather than applied as a raw map direction.
            var length = line.Dx.ToDouble() * line.Dx.ToDouble() +
                         line.Dy.ToDouble() * line.Dy.ToDouble();

            Fixed x;
            Fixed y;

            if (length < 1.0)
            {
                x = dx;
                y = dy;
            }
            else
            {
                var d = Math.Sqrt(length);
                var dxd = dx.ToDouble();
                var dyd = dy.ToDouble();
                var ldx = line.Dx.ToDouble();
                var ldy = line.Dy.ToDouble();

                x = Fixed.FromDouble((dyd * -ldy - dxd * ldx) / d);
                y = Fixed.FromDouble((dyd * ldx - dxd * ldy) / d);
            }

            if (line.FrontSide != null)
            {
                AddScroller(list, ScrollKind.Side, line.FrontSide, x, y);
            }

            if (line.BackSide != null)
            {
                AddScroller(list, ScrollKind.Side, line.BackSide, x, y);
            }
        }

        /// <summary>
        /// Applies every scroller for one tic.
        ///
        /// Runs inside the tic, not per frame: a scroller is part of the world's
        /// update, and 252/253 move things, so a frame-rate-dependent version would
        /// make a lockstep session disagree with itself.
        /// </summary>
        private void UpdateScrollers()
        {
            if (scrollers == null)
            {
                return;
            }

            foreach (var scroller in scrollers)
            {
                switch (scroller.Kind)
                {
                    case ScrollKind.Side:
                        scroller.Side.TextureOffset += scroller.Dx;
                        scroller.Side.RowOffset += scroller.Dy;
                        break;

                    case ScrollKind.Floor:
                        scroller.Sector.FloorFlatOffsetX += scroller.Dx;
                        scroller.Sector.FloorFlatOffsetY += scroller.Dy;
                        break;

                    case ScrollKind.Ceiling:
                        scroller.Sector.CeilingFlatOffsetX += scroller.Dx;
                        scroller.Sector.CeilingFlatOffsetY += scroller.Dy;
                        break;

                    case ScrollKind.Carry:
                        CarryThings(scroller.Sector, scroller.Dx, scroller.Dy);
                        break;
                }
            }
        }

        /// <summary>
        /// Pushes everything standing on a scrolling floor.
        ///
        /// The conditions are PrBoom's: skip anything that noclips or floats, skip
        /// anything above the floor. A carried object gains momentum, not position,
        /// so it slides and slows down exactly as if it had been pushed - which is
        /// what a Boom map's moving platform is relying on.
        /// </summary>
        private static void CarryThings(Sector sector, Fixed dx, Fixed dy)
        {
            for (var thing = sector.ThingList; thing != null; thing = thing.SectorNext)
            {
                if ((thing.Flags & MobjFlags.NoClip) != 0)
                {
                    continue;
                }

                if ((thing.Flags & MobjFlags.NoGravity) != 0 && thing.Z > sector.FloorHeight)
                {
                    continue;
                }

                if (thing.Z > sector.FloorHeight)
                {
                    continue;
                }

                thing.MomX += dx;
                thing.MomY += dy;
            }
        }

        public void ChangeSwitchTexture(LineDef line, bool useAgain)
        {
            if (!useAgain)
            {
                line.Special = 0;
            }

            var frontSide = line.FrontSide;
            var topTexture = frontSide.TopTexture;
            var middleTexture = frontSide.MiddleTexture;
            var bottomTexture = frontSide.BottomTexture;

            var sound = Sfx.SWTCHN;

            // Exit switch?
            if ((int)line.Special == 11)
            {
                sound = Sfx.SWTCHX;
            }

            var switchList = world.Map.Textures.SwitchList;

            for (var i = 0; i < switchList.Length; i++)
            {
                if (switchList[i] == topTexture)
                {
                    world.StartSound(line.SoundOrigin, sound, SfxType.Misc);
                    frontSide.TopTexture = switchList[i ^ 1];

                    if (useAgain)
                    {
                        StartButton(line, ButtonPosition.Top, switchList[i], buttonTime);
                    }

                    return;
                }
                else
                {
                    if (switchList[i] == middleTexture)
                    {
                        world.StartSound(line.SoundOrigin, sound, SfxType.Misc);
                        frontSide.MiddleTexture = switchList[i ^ 1];

                        if (useAgain)
                        {
                            StartButton(line, ButtonPosition.Middle, switchList[i], buttonTime);
                        }

                        return;
                    }
                    else
                    {
                        if (switchList[i] == bottomTexture)
                        {
                            world.StartSound(line.SoundOrigin, sound, SfxType.Misc);
                            frontSide.BottomTexture = switchList[i ^ 1];

                            if (useAgain)
                            {
                                StartButton(line, ButtonPosition.Bottom, switchList[i], buttonTime);
                            }

                            return;
                        }
                    }
                }
            }
        }

        private void StartButton(LineDef line, ButtonPosition w, int texture, int time)
        {
            // See if button is already pressed.
            for (var i = 0; i < maxButtonCount; i++)
            {
                if (buttonList[i].Timer != 0 && buttonList[i].Line == line)
                {
                    return;
                }
            }

            for (var i = 0; i < maxButtonCount; i++)
            {
                if (buttonList[i].Timer == 0)
                {
                    buttonList[i].Line = line;
                    buttonList[i].Position = w;
                    buttonList[i].Texture = texture;
                    buttonList[i].Timer = time;
                    buttonList[i].SoundOrigin = line.SoundOrigin;
                    return;
                }
            }

            throw new Exception("No button slots left!");
        }

        /// <summary>
        /// Animate planes, scroll walls, etc.
        /// </summary>
        public void Update()
        {
            // Level timer.
            if (levelTimer)
            {
                levelTimeCount--;
                if (levelTimeCount == 0)
                {
                    world.ExitLevel();
                }
            }

            // Animate flats and textures globally.
            var animations = world.Map.Animation.Animations;
            for (var k = 0; k < animations.Length; k++)
            {
                var anim = animations[k];
                for (var i = anim.BasePic; i < anim.BasePic + anim.NumPics; i++)
                {
                    var pic = anim.BasePic + ((world.LevelTime / anim.Speed + i) % anim.NumPics);
                    if (anim.IsTexture)
                    {
                        textureTranslation[i] = pic;
                    }
                    else
                    {
                        flatTranslation[i] = pic;
                    }
                }
            }

            // Animate line specials.
            UpdateScrollers();
            UpdatePushers();

            // Do buttons.
            for (var i = 0; i < maxButtonCount; i++)
            {
                if (buttonList[i].Timer > 0)
                {
                    buttonList[i].Timer--;

                    if (buttonList[i].Timer == 0)
                    {
                        switch (buttonList[i].Position)
                        {
                            case ButtonPosition.Top:
                                buttonList[i].Line.FrontSide.TopTexture = buttonList[i].Texture;
                                break;

                            case ButtonPosition.Middle:
                                buttonList[i].Line.FrontSide.MiddleTexture = buttonList[i].Texture;
                                break;

                            case ButtonPosition.Bottom:
                                buttonList[i].Line.FrontSide.BottomTexture = buttonList[i].Texture;
                                break;
                        }

                        world.StartSound(buttonList[i].SoundOrigin, Sfx.SWTCHN, SfxType.Misc, 50);
                        buttonList[i].Clear();
                    }
                }
            }
        }

        public int[] TextureTranslation => textureTranslation;
        public int[] FlatTranslation => flatTranslation;
    }
}
