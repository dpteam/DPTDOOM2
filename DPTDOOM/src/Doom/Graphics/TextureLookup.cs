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
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace DPTDOOM
{
    public sealed class TextureLookup : ITextureLookup
    {
        private List<Texture> textures;
        private Dictionary<string, Texture> nameToTexture;
        private Dictionary<string, int> nameToNumber;

        private int[] switchList;
        private int pk3Replaced;

        public TextureLookup(Wad wad) : this(wad, false)
        {
        }

        public TextureLookup(Wad wad, bool useDummy)
        {
            try
            {
                Console.Write("Load textures: ");

                InitLookup(wad);
                InitSwitchList();

                Console.WriteLine("OK (" + textures.Count + " textures" +
                                  (pk3Replaced > 0 ? ", " + pk3Replaced + " from PK3" : "") + ")");
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed");
                ExceptionDispatchInfo.Throw(e);
            }
        }

        private void InitLookup(Wad wad)
        {
            textures = new List<Texture>();
            nameToTexture = new Dictionary<string, Texture>();
            nameToNumber = new Dictionary<string, int>();

            var patches = LoadPatches(wad);

            for (var n = 1; n <= 2; n++)
            {
                var lumpNumber = wad.GetLumpNumber("TEXTURE" + n);
                if (lumpNumber == -1)
                {
                    break;
                }

                var data = wad.ReadLump(lumpNumber);
                var count = BitConverter.ToInt32(data, 0);
                for (var i = 0; i < count; i++)
                {
                    var offset = BitConverter.ToInt32(data, 4 + 4 * i);
                    var texture = Texture.FromData(data, offset, patches);
                    nameToNumber.TryAdd(texture.Name, textures.Count);
                    textures.Add(texture);
                    nameToTexture.TryAdd(texture.Name, texture);
                }
            }

            AddPk3Textures(wad);
        }

        /// <summary>
        /// Replaces wall textures with PNG files from a PK3.
        ///
        /// A texture is a composition of patches, and a PNG is a finished picture,
        /// so a PNG-backed texture is a composition of exactly one patch covering
        /// the whole area. That keeps every existing code path — the renderer, the
        /// switch list, the animation table — working unchanged, because they all
        /// deal with a Texture, not with where it came from.
        ///
        /// Only the simple form is supported: <c>textures/NAME.png</c> replaces the
        /// texture called NAME. The ZDoom TEXTURES definition lump, which can
        /// compose new textures out of pieces, is a separate language and is not
        /// implemented.
        /// </summary>
        private void AddPk3Textures(Wad wad)
        {
            if (wad.Graphics == null || wad.Graphics.Count == 0)
            {
                return;
            }

            var playpal = wad.ReadLump("PLAYPAL");
            var replaced = 0;

            foreach (var graphic in wad.Graphics)
            {
                if (!Pk3Graphics.IsTexturePath(graphic.Path))
                {
                    continue;
                }

                var name = Pk3Graphics.LumpNameOf(graphic.Path);

                if (name == null)
                {
                    continue;
                }

                Texture texture;

                try
                {
                    var image = PngImage.Decode(graphic.Data);
                    var patch = Pk3Graphics.ToPatch(image, playpal, name, false);

                    texture = new Texture(
                        name, true, image.Width, image.Height,
                        new[] { new TexturePatch(0, 0, patch) });
                }
                catch (Exception e)
                {
                    // A broken PNG must not stop the game: the IWAD texture stays.
                    Console.WriteLine();
                    Console.WriteLine("  skipping " + graphic.Path + ": " + e.Message);
                    continue;
                }

                int number;

                if (nameToNumber.TryGetValue(name, out number))
                {
                    textures[number] = texture;
                }
                else
                {
                    number = textures.Count;
                    textures.Add(texture);
                    nameToNumber[name] = number;
                }

                nameToTexture[name] = texture;
                replaced++;
            }

            pk3Replaced = replaced;
        }

        private void InitSwitchList()
        {
            var list = new List<int>();
            foreach (var tuple in DoomInfo.SwitchNames)
            {
                var texNum1 = GetNumber(tuple.Item1);
                var texNum2 = GetNumber(tuple.Item2);
                if (texNum1 != -1 && texNum2 != -1)
                {
                    list.Add(texNum1);
                    list.Add(texNum2);
                }
            }
            switchList = list.ToArray();
        }

        public int GetNumber(string name)
        {
            if (name[0] == '-')
            {
                return 0;
            }

            int number;
            if (nameToNumber.TryGetValue(name, out number))
            {
                return number;
            }
            else
            {
                return -1;
            }
        }

        private static Patch[] LoadPatches(Wad wad)
        {
            var patchNames = LoadPatchNames(wad);
            var patches = new Patch[patchNames.Length];
            for (var i = 0; i < patches.Length; i++)
            {
                var name = patchNames[i];

                // This check is necessary to avoid crash in DOOM1.WAD.
                if (wad.GetLumpNumber(name) == -1)
                {
                    continue;
                }

                var data = wad.ReadLump(name);
                patches[i] = Patch.FromData(name, data);
            }
            return patches;
        }

        private static string[] LoadPatchNames(Wad wad)
        {
            var data = wad.ReadLump("PNAMES");
            var count = BitConverter.ToInt32(data, 0);
            var names = new string[count];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = DoomInterop.ToString(data, 4 + 8 * i, 8);
            }
            return names;
        }

        public IEnumerator<Texture> GetEnumerator()
        {
            return textures.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return textures.GetEnumerator();
        }

        public int Count => textures.Count;
        public Texture this[int num] => textures[num];
        public Texture this[string name] => nameToTexture[name];
        public int[] SwitchList => switchList;
    }
}
