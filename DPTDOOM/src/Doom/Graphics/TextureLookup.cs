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
        private int textDefined;

        public TextureLookup(Wad wad) : this(wad, false)
        {
        }

        public TextureLookup(Wad wad, bool useDummy)
        {
            try
            {
                ConsoleUi.Begin("textures");

                InitLookup(wad);
                InitSwitchList();

                ConsoleUi.Ok(textures.Count + " textures" +
                             (pk3Replaced > 0 ? ", " + pk3Replaced + " from PK3" : "") +
                             (textDefined > 0 ? ", " + textDefined + " from TEXTURES" : ""));
            }
            catch (Exception e)
            {
                ConsoleUi.Failed("see below");
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

            // Last, so a text definition can use PNG patches from the PK3 and can override a
            // texture the same PK3 shipped as a single PNG.
            ApplyDefinitions(wad, patches);
        }

        /// <summary>
        /// Applies a TEXTURES lump: textures composed of patches, described in text.
        ///
        /// This is the same thing TEXTURE1 does in binary, and it lands in the same table, so
        /// the renderer, the switch list and the animation table all see a texture like any
        /// other. What it adds is what the binary form cannot do: a texture built from pieces
        /// that are not adjacent in the WAD, without a texture editor and without shipping a
        /// whole TEXTURE1 lump.
        ///
        /// Width and height may be left out of a definition; then they are the extent of its
        /// patches. That is not a convenience - a mod that writes only the patches should get
        /// exactly the picture it described, not a cropped one.
        /// </summary>
        private void ApplyDefinitions(Wad wad, Patch[] patches)
        {
            var lump = wad.GetLumpNumber("TEXTURES");

            if (lump == -1)
            {
                return;
            }

            var problems = new List<string>();

            var definitions = TextureDefinitions.Parse(
                System.Text.Encoding.UTF8.GetString(wad.ReadLump(lump)), "TEXTURES", problems);

            foreach (var problem in problems)
            {
                LoadReport.Add("bad texture definition", problem,
                    "that texture keeps the picture it had");
            }

            var byName = new Dictionary<string, Patch>(StringComparer.OrdinalIgnoreCase);

            foreach (var patch in patches)
            {
                if (patch != null && !byName.ContainsKey(patch.Name))
                {
                    byName.Add(patch.Name, patch);
                }
            }

            AddPk3Patches(wad, byName);

            var applied = 0;

            foreach (var definition in definitions)
            {
                var placed = new List<TexturePatch>();

                // The extent is accumulated over every patch and only then compared with what the
                // definition asked for. The first version guarded the accumulation with
                // `if (width < 0)`, which stopped it after the first patch - so a definition that
                // named only its patches got the size of the first one and the rest hung off the
                // right edge, invisible.
                var extentWidth = 0;
                var extentHeight = 0;

                foreach (var patchDefinition in definition.Patches)
                {
                    Patch patch;

                    if (!byName.TryGetValue(patchDefinition.Name, out patch))
                    {
                        LoadReport.Add(
                            "missing texture patch",
                            patchDefinition.Name + " in texture '" + definition.Name + "'",
                            "that patch is left out of the texture");

                        continue;
                    }

                    placed.Add(new TexturePatch(patchDefinition.X, patchDefinition.Y, patch));

                    extentWidth = Math.Max(extentWidth, patchDefinition.X + patch.Width);
                    extentHeight = Math.Max(extentHeight, patchDefinition.Y + patch.Height);
                }

                var width = definition.Width >= 0 ? definition.Width : extentWidth;
                var height = definition.Height >= 0 ? definition.Height : extentHeight;

                if (placed.Count == 0)
                {
                    // Every patch was missing, so the texture would be an empty rectangle. The
                    // map keeps whatever it had, which for a new name means the map's own
                    // "unknown texture" handling rather than a hole in a wall.
                    LoadReport.Add("bad texture definition",
                        definition.Name + " (no patch of it is in this WAD)",
                        "that texture keeps the picture it had");

                    continue;
                }

                if (width <= 0 || height <= 0)
                {
                    LoadReport.Add("bad texture definition",
                        definition.Name + " (" + width + "x" + height + ")",
                        "that texture keeps the picture it had");

                    continue;
                }

                var texture = new Texture(definition.Name, true, width, height, placed.ToArray());

                int number;

                if (nameToNumber.TryGetValue(definition.Name, out number))
                {
                    textures[number] = texture;
                }
                else
                {
                    number = textures.Count;
                    textures.Add(texture);
                    nameToNumber[definition.Name] = number;
                }

                nameToTexture[definition.Name] = texture;
                applied++;
            }

            if (applied > 0)
            {
                textDefined = applied;
            }
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
                    ConsoleUi.Detail("skipping " + graphic.Path + ": " + e.Message);
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

        /// <summary>
        /// Adds `patches/NAME.png` from a PK3 to the patch table.
        ///
        /// A TEXTURES definition names its pieces, and in a PK3 those pieces are usually PNG
        /// files rather than lumps in the WAD. Without this a definition that composes PK3
        /// artwork would find no patches at all and be dropped, which is the whole point of
        /// putting the definition in the PK3.
        /// </summary>
        private static void AddPk3Patches(Wad wad, Dictionary<string, Patch> byName)
        {
            if (wad.Graphics == null || wad.Graphics.Count == 0)
            {
                return;
            }

            byte[] playpal = null;

            foreach (var graphic in wad.Graphics)
            {
                var path = graphic.Path == null ? "" : graphic.Path.ToLowerInvariant();

                if (!path.StartsWith("patches/") || !path.EndsWith(".png"))
                {
                    continue;
                }

                var name = Pk3Graphics.LumpNameOf(graphic.Path);

                if (name == null || byName.ContainsKey(name))
                {
                    continue;
                }

                try
                {
                    if (playpal == null)
                    {
                        playpal = wad.ReadLump("PLAYPAL");
                    }

                    var image = PngImage.Decode(graphic.Data);
                    byName[name] = Pk3Graphics.ToPatch(image, playpal, name, false);
                }
                catch (Exception e)
                {
                    // A broken PNG must not stop the game: the patch is simply not there, and the
                    // definition that wanted it says so.
                    ConsoleUi.Detail("skipping patch " + graphic.Path + ": " + e.Message);
                }
            }
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
