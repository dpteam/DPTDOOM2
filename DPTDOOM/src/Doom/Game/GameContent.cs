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
using DPTDOOM.Scripting;

namespace DPTDOOM
{
    public sealed class GameContent : IDisposable
    {
        private Wad wad;
        private Palette palette;
        private ColorMap colorMap;
        private ITextureLookup textures;
        private IFlatLookup flats;
        private ISpriteLookup sprites;
        private TextureAnimation animation;
        private ScriptLibrary scripts;

        private GameContent()
        {
        }

        public GameContent(CommandLineArgs args)
        {
            wad = new Wad(ConfigUtilities.GetWadPaths(args));

            // Collected across the whole load and printed once at the end: during the
            // load the player is watching a progress list, and forty "skipping x" lines
            // in the middle of it is noise.
            LoadReport.Clear();

            DeHackEd.Initialize(args, wad);

            // Scripts after DeHackEd, deliberately: a script may read what a patch wrote,
            // and the reverse is not true. A mod that ships both gets the newer tool able
            // to see the older one's result, which is what the author expects.
            InitializeScripts(args);

            // A blank line between the groups, so a startup log of a dozen lines can be
            // skimmed instead of read. Purely visual - the timestamps already say what
            // took how long.
            ConsoleUi.Section("graphics");

            palette = new Palette(wad);
            colorMap = new ColorMap(wad);
            textures = new TextureLookup(wad);
            flats = new FlatLookup(wad);
            sprites = new SpriteLookup(wad);
            animation = new TextureAnimation(textures, flats);

            LoadReport.Print();
        }

        public static GameContent CreateDummy(params string[] wadPaths)
        {
            var gc = new GameContent();

            gc.wad = new Wad(wadPaths);
            gc.palette = new Palette(gc.wad);
            gc.colorMap = new ColorMap(gc.wad);
            gc.textures = new DummyTextureLookup(gc.wad);
            gc.flats = new DummyFlatLookup(gc.wad);
            gc.sprites = new DummySpriteLookup(gc.wad);
            gc.animation = new TextureAnimation(gc.textures, gc.flats);

            return gc;
        }

        /// <summary>
        /// Loads actor scripts: the files on the command line, and the <c>SCRIPTS</c>
        /// lump inside the WAD or any PWAD.
        ///
        /// The lump is what makes a script ship with its mod. A PWAD that describes an
        /// actor has to be able to do that without the player also being told to pass a
        /// flag - a mod that needs instructions to work is a mod that half of the people
        /// who download it will run wrong.
        ///
        /// A broken script costs its actor's behaviour and nothing else: the level still
        /// loads, the actors keep what they had, and the load report names the file and
        /// the line. The same choice the loader makes for a broken sprite.
        /// </summary>
        private void InitializeScripts(CommandLineArgs args)
        {
            var paths = args.script.Present ? args.script.Value : new string[0];
            var lump = wad.GetLumpNumber("SCRIPTS");

            if (paths.Length == 0 && lump == -1)
            {
                return;
            }

            ConsoleUi.Begin("scripts");

            var library = new ScriptLibrary();

            foreach (var path in paths)
            {
                try
                {
                    library.Add(System.IO.File.ReadAllText(path), System.IO.Path.GetFileName(path),
                        DoomInfo.MobjInfos);
                }
                catch (Exception e)
                {
                    library.Problems.Add(path + ": " + e.Message);
                }
            }

            if (lump != -1)
            {
                // Named after the lump rather than the file: the player never knows which
                // PWAD it came from, and the load report is read by the player.
                library.Add(System.Text.Encoding.UTF8.GetString(wad.ReadLump(lump)),
                    "SCRIPTS lump", DoomInfo.MobjInfos);
            }

            library.ApplyTo(DoomInfo.MobjInfos);

            scripts = library;

            foreach (var problem in library.Problems)
            {
                LoadReport.Add("script problem", problem, "that script is skipped, the actor keeps its old behaviour");
            }

            if (library.Problems.Count > 0)
            {
                ConsoleUi.Failed(library.Problems.Count + " problem(s)");
            }
            else
            {
                ConsoleUi.Ok(library.Patches.Count + " actor(s) described");
            }
        }

        public void Dispose()
        {
            if (wad != null)
            {
                wad.Dispose();
                wad = null;
            }
        }

        public Wad Wad => wad;
        public Palette Palette => palette;
        public ColorMap ColorMap => colorMap;
        public ITextureLookup Textures => textures;
        public IFlatLookup Flats => flats;
        public ISpriteLookup Sprites => sprites;
        public TextureAnimation Animation => animation;

        /// <summary>
        /// The actor scripts that were loaded, or null when none were.
        ///
        /// Exposed rather than kept private because the load report is not the only place
        /// somebody needs to ask what a mod described: a console command or an About
        /// screen listing the active patches is the obvious next ask, and a private field
        /// would mean changing this class again for it.
        /// </summary>
        public ScriptLibrary Scripts => scripts;
    }
}
