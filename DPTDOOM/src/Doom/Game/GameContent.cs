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

        /// <summary>
        /// The SharpScript this run's DECORATE was converted into, or null when there was
        /// none.
        ///
        /// Kept so the <c>decorate</c> console command can print it. A modder whose actor
        /// came out wrong has exactly one question - "what did my DECORATE become" - and
        /// without the text the only way to answer it is to read this project's source.
        /// </summary>
        private string decorateScript;

        /// <summary>What the DECORATE conversion could not do, in DECORATE terms.</summary>
        private readonly System.Collections.Generic.List<string> decorateProblems =
            new System.Collections.Generic.List<string>();

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
            var decoratePaths = args.decorate.Present ? args.decorate.Value : new string[0];
            var lump = wad.GetLumpNumber("SCRIPTS");
            var decorateLump = wad.GetLumpNumber("DECORATE");

            if (paths.Length == 0 && lump == -1 &&
                decoratePaths.Length == 0 && decorateLump == -1)
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

            // DECORATE is converted, not applied: the result is SharpScript source and goes
            // through the same library, the same sandbox and the same single application
            // pass as a hand-written script. A second way of changing actors would need its
            // own copy of all three, and the two would drift.
            var converted = new System.Text.StringBuilder();

            foreach (var path in decoratePaths)
            {
                try
                {
                    converted.Append(DecorateConverter.Convert(
                        System.IO.File.ReadAllText(path),
                        DoomInfo.MobjInfos,
                        decorateProblems));
                }
                catch (Exception e)
                {
                    decorateProblems.Add(path + ": " + e.Message);
                }
            }

            if (decorateLump != -1)
            {
                converted.Append(DecorateConverter.Convert(
                    System.Text.Encoding.UTF8.GetString(wad.ReadLump(decorateLump)),
                    DoomInfo.MobjInfos,
                    decorateProblems));
            }

            if (converted.Length > 0)
            {
                decorateScript = converted.ToString();

                // Only when something actually came out of it. A DECORATE file whose every
                // actor was refused has its problems reported below, and handing the library
                // an empty script would add "no actor definitions found" on top of them.
                if (decorateScript.Contains("fn Thing_"))
                {
                    library.Add(decorateScript, "DECORATE", DoomInfo.MobjInfos);
                }
            }

            library.ApplyTo(DoomInfo.MobjInfos);

            scripts = library;

            foreach (var problem in decorateProblems)
            {
                // "Unchanged" is the true outcome for every case here - a States section the
                // engine cannot accept, a property with no counterpart, an actor that was
                // refused outright - and the line above says which of them it was and what to
                // do about it. A consequence invented per problem kind would be a second
                // place to keep in step with the messages.
                LoadReport.Add("decorate problem", problem,
                    "that part of the actor keeps the engine's values");
            }

            foreach (var problem in library.Problems)
            {
                LoadReport.Add("script problem", problem, "that script is skipped, the actor keeps its old behaviour");
            }

            // Two different outcomes, told apart on purpose. A script that fails to parse is
            // a failure: nothing it said took effect. A DECORATE file whose States section
            // could not be converted is not - the numbers did take effect, and the file is
            // usable; calling that "Failed" every time a mod ships States (which is every
            // time) would teach the player to ignore the word.
            if (library.Problems.Count > 0)
            {
                ConsoleUi.Failed((library.Problems.Count + decorateProblems.Count) + " problem(s)");
            }
            else
            {
                ConsoleUi.Ok(library.Patches.Count + " actor(s) described");

                if (decorateProblems.Count > 0)
                {
                    ConsoleUi.Detail(decorateProblems.Count +
                                     " thing(s) in DECORATE not converted, listed below");
                }
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

        /// <summary>
        /// The SharpScript that DECORATE was converted into, or null when there was none.
        ///
        /// Exposed because "what did my DECORATE become" is the only question a modder can
        /// usefully ask when an actor comes out wrong, and the answer has to be the text
        /// that actually ran, not a description of it.
        /// </summary>
        public string DecorateScript => decorateScript;

        /// <summary>What the DECORATE conversion could not do, in DECORATE terms.</summary>
        public System.Collections.Generic.IList<string> DecorateProblems => decorateProblems;
    }
}
