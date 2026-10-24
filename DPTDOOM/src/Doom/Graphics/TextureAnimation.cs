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
using System.Runtime.ExceptionServices;

namespace DPTDOOM
{
    public sealed class TextureAnimation
    {
        private TextureAnimationInfo[] animations;

        public TextureAnimation(ITextureLookup textures, IFlatLookup flats)
        {
            try
            {
                ConsoleUi.Begin("animation");

                var list = new List<TextureAnimationInfo>();

                foreach (var animDef in DoomInfo.TextureAnimation)
                {
                    int picNum;
                    int basePic;
                    if (animDef.IsTexture)
                    {
                        if (textures.GetNumber(animDef.StartName) == -1)
                        {
                            continue;
                        }

                        picNum = textures.GetNumber(animDef.EndName);
                        basePic = textures.GetNumber(animDef.StartName);
                    }
                    else
                    {
                        if (flats.GetNumber(animDef.StartName) == -1)
                        {
                            continue;
                        }

                        picNum = flats.GetNumber(animDef.EndName);
                        basePic = flats.GetNumber(animDef.StartName);
                    }

                    var anim = new TextureAnimationInfo(
                        animDef.IsTexture,
                        picNum,
                        basePic,
                        picNum - basePic + 1,
                        animDef.Speed);

                    if (anim.NumPics < 2)
                    {
                        // A cycle that does not cycle: StartName and EndName are out of
                        // order, or one of them is missing from this WAD. Vanilla simply
                        // has no animation there, so the frame stays still, and the WAD
                        // keeps working. Throwing here meant one bad entry in a PWAD
                        // refused the whole game.
                        LoadReport.Add(
                            "bad animation cycle",
                            animDef.StartName + " to " + animDef.EndName,
                            "those textures do not animate");

                        continue;
                    }

                    list.Add(anim);
                }

                animations = list.ToArray();

                ConsoleUi.Ok(list.Count + " animated");
            }
            catch (Exception e)
            {
                ConsoleUi.Failed("see below");
                ExceptionDispatchInfo.Throw(e);
            }
        }

        public TextureAnimationInfo[] Animations => animations;
    }
}
