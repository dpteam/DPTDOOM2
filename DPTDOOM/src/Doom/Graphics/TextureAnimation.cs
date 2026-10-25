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

                // ANIMDEFS cycles, after the engine's own: a mod's explicit list replaces nothing and
                // adds to what animates. The pictures are resolved to indices here, once, because a
                // name lookup per frame per tic would be the one place this could get expensive.
                AddAnimatedDefinitions(textures, flats, list);

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

        /// <summary>
        /// Turns ANIMDEFS cycles into animation entries.
        ///
        /// A name that is not in this WAD is reported rather than dropped: the cycle is what the mod
        /// author wrote, and telling them which picture is missing is the difference between a
        /// five-second fix and a hunt. A cycle where nothing resolves is skipped entirely, because an
        /// animation with no frames would make the renderer index an empty array.
        /// </summary>
        private static void AddAnimatedDefinitions(ITextureLookup textures, IFlatLookup flats,
                                                   List<TextureAnimationInfo> list)
        {
            foreach (var definition in AnimatedTexture.Definitions)
            {
                var frames = new List<int>();
                var durations = new List<int>();

                for (var i = 0; i < definition.Frames.Count; i++)
                {
                    var name = definition.Frames[i];
                    var number = definition.IsTexture ? textures.GetNumber(name) : flats.GetNumber(name);

                    if (number < 0)
                    {
                        LoadReport.Add(
                            "missing animation frame",
                            name + " in the cycle at line " + definition.Line,
                            "that frame is skipped, so the cycle may look wrong");

                        continue;
                    }

                    frames.Add(number);
                    durations.Add(definition.Durations[i]);
                }

                if (frames.Count < 2)
                {
                    // Nothing left to animate. A cycle that lost one of two frames still animates; one
                    // that lost all of them would make the renderer index an empty array.
                    continue;
                }

                list.Add(new TextureAnimationInfo(definition.IsTexture, frames.ToArray(),
                                                 durations.ToArray()));
            }
        }
    }
}
