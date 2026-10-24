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
    public sealed class SpriteLookup : ISpriteLookup
    {
        private SpriteDef[] spriteDefs;

        public SpriteLookup(Wad wad)
        {
            try
            {
                ConsoleUi.Begin("sprites");

                var temp = new Dictionary<string, List<SpriteInfo>>();
                for (var i = 0; i < (int)Sprite.Count; i++)
                {
                    temp.TryAdd(DoomInfo.SpriteNames[i], new List<SpriteInfo>());
                }

                var cache = new Dictionary<int, Patch>();

                foreach (var lump in EnumerateSprites(wad))
                {
                    var name = wad.LumpInfos[lump].Name.Substring(0, 4);

                    if (!temp.ContainsKey(name))
                    {
                        continue;
                    }

                    var index = lump;

                    RegisterSprite(
                        temp, wad.LumpInfos[lump].Name,
                        () => CachedRead(index, wad, cache),
                        false);
                }

                // Sprites from a PK3 come last, so they replace the IWAD's. That
                // is the point of loading a mod.
                var replaced = AddPk3Sprites(wad, temp);

                spriteDefs = new SpriteDef[(int)Sprite.Count];
                for (var i = 0; i < spriteDefs.Length; i++)
                {
                    var list = temp[DoomInfo.SpriteNames[i]];

                    var frames = new SpriteFrame[list.Count];
                    for (var j = 0; j < frames.Length; j++)
                    {
                        list[j].CheckCompletion();

                        var frame = new SpriteFrame(list[j].HasRotation(), list[j].Patches, list[j].Flip);
                        frames[j] = frame;
                    }

                    spriteDefs[i] = new SpriteDef(frames);
                }

                ConsoleUi.Ok(cache.Count + " sprites" +
                             (replaced > 0 ? ", " + replaced + " from PK3" : ""));
            }
            catch (Exception e)
            {
                ConsoleUi.Failed("see below");
                ExceptionDispatchInfo.Throw(e);
            }
        }

        /// <summary>
        /// Registers one sprite lump under its name.
        ///
        /// A lump name is four letters of sprite, a frame letter and a rotation
        /// digit, optionally followed by a second frame and rotation for the
        /// mirrored variant. Rotation 0 means "every rotation".
        ///
        /// Extracted from the loading loop so that a PNG from a PK3 goes through
        /// exactly the same rules as a lump from a WAD. Duplicating those rules
        /// would have been a reliable way to make the two behave differently.
        /// </summary>
        private static void RegisterSprite(
            Dictionary<string, List<SpriteInfo>> temp,
            string lumpName,
            Func<Patch> read,
            bool replaceExisting)
        {
            if (lumpName.Length < 6)
            {
                return;
            }

            var list = temp[lumpName.Substring(0, 4)];

            RegisterFrame(list, lumpName[4], lumpName[5], false, read, replaceExisting);

            if (lumpName.Length == 8)
            {
                // The second pair is the mirrored rotation, which Doom draws by
                // flipping the picture horizontally.
                RegisterFrame(list, lumpName[6], lumpName[7], true, read, replaceExisting);
            }
        }

        private static void RegisterFrame(
            List<SpriteInfo> list, char frameChar, char rotationChar, bool flip,
            Func<Patch> read, bool replaceExisting)
        {
            var frame = frameChar - 'A';
            var rotation = rotationChar - '0';

            if (frame < 0 || frame > 28 || rotation < 0 || rotation > 8)
            {
                return;
            }

            while (list.Count < frame + 1)
            {
                list.Add(new SpriteInfo());
            }

            if (rotation == 0)
            {
                for (var i = 0; i < 8; i++)
                {
                    if (replaceExisting || list[frame].Patches[i] == null)
                    {
                        list[frame].Patches[i] = read();
                        list[frame].Flip[i] = flip;
                    }
                }
            }
            else if (replaceExisting || list[frame].Patches[rotation - 1] == null)
            {
                list[frame].Patches[rotation - 1] = read();
                list[frame].Flip[rotation - 1] = flip;
            }
        }

        /// <summary>
        /// Loads sprite pictures from a PK3.
        ///
        /// PK3 sprites are PNG files under <c>sprites/</c>, named either
        /// <c>POSSA1.png</c> or <c>POSS A1.png</c>. They are decoded and converted
        /// to patches with the anchor in the middle of the bottom edge, which is
        /// how a sprite standing on the floor has to be positioned.
        /// </summary>
        private static int AddPk3Sprites(Wad wad, Dictionary<string, List<SpriteInfo>> temp)
        {
            if (wad.Graphics == null || wad.Graphics.Count == 0)
            {
                return 0;
            }

            var playpal = wad.ReadLump("PLAYPAL");
            var replaced = 0;

            foreach (var graphic in wad.Graphics)
            {
                var name = Pk3Graphics.ParseSpriteName(graphic.Path);

                if (name == null || !temp.ContainsKey(name.Substring(0, 4)))
                {
                    continue;
                }

                var source = graphic;
                Patch decoded = null;
                var failed = false;

                Func<Patch> read = () =>
                {
                    if (decoded != null || failed)
                    {
                        return decoded;
                    }

                    try
                    {
                        var image = PngImage.Decode(source.Data);
                        decoded = Pk3Graphics.ToPatch(image, playpal, name, true);
                    }
                    catch (Exception e)
                    {
                        // A broken PNG must not stop the game: the IWAD sprite
                        // stays in place.
                        ConsoleUi.Detail("skipping " + source.Path + ": " + e.Message);
                        failed = true;
                    }

                    return decoded;
                };

                RegisterSprite(temp, name, read, true);

                if (decoded != null)
                {
                    replaced++;
                }
            }

            return replaced;
        }

        private static IEnumerable<int> EnumerateSprites(Wad wad)
        {
            var spriteSection = false;

            for (var lump = wad.LumpInfos.Count - 1; lump >= 0; lump--)
            {
                var name = wad.LumpInfos[lump].Name;

                if (name.StartsWith("S"))
                {
                    if (name.EndsWith("_END"))
                    {
                        spriteSection = true;
                        continue;
                    }
                    else if (name.EndsWith("_START"))
                    {
                        spriteSection = false;
                        continue;
                    }
                }

                if (spriteSection)
                {
                    if (wad.LumpInfos[lump].Size > 0)
                    {
                        yield return lump;
                    }
                }
            }
        }

        private static Patch CachedRead(int lump, Wad wad, Dictionary<int, Patch> cache)
        {
            if (!cache.ContainsKey(lump))
            {
                var name = wad.LumpInfos[lump].Name;
                cache.Add(lump, Patch.FromData(name, wad.ReadLump(lump)));
            }

            return cache[lump];
        }

        public SpriteDef this[Sprite sprite]
        {
            get
            {
                return spriteDefs[(int)sprite];
            }
        }



        private class SpriteInfo
        {
            public Patch[] Patches;
            public bool[] Flip;

            public SpriteInfo()
            {
                Patches = new Patch[8];
                Flip = new bool[8];
            }

            public void CheckCompletion()
            {
                for (var i = 0; i < Patches.Length; i++)
                {
                    if (Patches[i] == null)
                    {
                        throw new Exception("Missing sprite!");
                    }
                }
            }

            public bool HasRotation()
            {
                for (var i = 1; i < Patches.Length; i++)
                {
                    if (Patches[i] != Patches[0])
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
