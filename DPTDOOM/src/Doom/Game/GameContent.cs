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

            // The actor table as text, before anything else touches it.
            //
            // The order here is the whole design, and getting it backwards is silent:
            //
            //   1. unpack - writes the table as SharpScript, if the folder is not there yet;
            //   2. load    - applies that folder to the table, so an edit takes effect;
            //   3. DeHackEd - a patch applied after the text wins over it, which is what
            //                 somebody loading both expects;
            //   4. scripts - a mod's own script runs last, so it can read the patch's result.
            //
            // Applying the text after DeHackEd would set every field back to the value written
            // in the file and quietly undo the patch. That is why step 2 is here and not below.
            if (!args.nodata.Present)
            {
                UnpackActorReference(args);
                LoadActorReference();
            }

            DeHackEd.Initialize(args, wad);

            // Scripts after DeHackEd, deliberately: a script may read what a patch wrote,
            // and the reverse is not true. A mod that ships both gets the newer tool able
            // to see the older one's result, which is what the author expects.
            InitializeScripts(args);

            // Levels are described separately from actors, and read before any map exists.
            InitializeMapInfo(args);

            // Sounds by name, so a mod can give a vanilla sound its own lump.
            InitializeSoundInfo(args);

            // Which pictures animate. Parsed here, resolved against the texture tables below, and
            // handed to the animation object when it is built.
            InitializeAnimatedTextures(args);

            // What a numbered lock needs, says and plays. Before the world is built, because the
            // table is consulted the moment a player tries a door.
            InitializeLockDefs(args);

            // What a floor does to whatever stands on it. Parsed here; bound to the flats once
            // they are loaded, because a binding is by flat name.
            InitializeTerrain(args);

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

            // A terrain is bound to a flat by name, so the binding can only be resolved once the
            // WAD's flat list exists. Doing it here rather than at parse time is what lets a
            // missing flat be reported instead of silently applied to nothing.
            ResolveTerrains();

            LoadReport.Print();
        }

        private void ResolveTerrains()
        {
            if (!Terrain.Any)
            {
                return;
            }

            var problems = new System.Collections.Generic.List<string>();

            Terrain.Resolve(flats, problems);

            foreach (var problem in problems)
            {
                LoadReport.Add("terrain", problem, "that terrain is not applied");
            }
        }

        /// <summary>
        /// Writes the actor reference into the data directory, once, and keeps the archive the
        /// engine builds for itself up to date.
        ///
        /// The folder is never overwritten; the archive is a build artifact and is rewritten when
        /// the fingerprint differs, because nobody edits a PK3 by hand and a stale one would be
        /// invisible. The two exist for different reasons: the folder is what a person edits, the
        /// archive is what can be copied to another machine as one file.
        ///
        /// Never fails the load: a read-only data directory must not stop the game. Problems go to
        /// the load report, which is where a player already looks when something is missing.
        /// </summary>
        private void UnpackActorReference(CommandLineArgs args)
        {
            var dataDirectory = ConfigUtilities.GetDataDirectory();

            var written = ActorTemplateWriter.UnpackIfMissing(
                dataDirectory, DoomInfo.MobjInfos, actorReferenceProblems);

            // Written on every run rather than only on the first: the check is a fingerprint
            // comparison, and on the overwhelming majority of runs it writes nothing.
            var archiveProblems = new System.Collections.Generic.List<string>();

            var pk3Path = ActorTemplateWriter.Pk3Path(dataDirectory);

            var stale = !System.IO.File.Exists(pk3Path) ||
                        !string.Equals(ReadPk3Fingerprint(pk3Path),
                                       ActorTemplateWriter.CurrentFingerprint(DoomInfo.MobjInfos),
                                       StringComparison.Ordinal);

            if (stale)
            {
                ActorTemplateWriter.WritePk3(dataDirectory, DoomInfo.MobjInfos, archiveProblems);
            }

            if (written.Count == 0 && archiveProblems.Count == 0 && !stale)
            {
                return;
            }

            if (written.Count > 0 || archiveProblems.Count > 0)
            {
                ConsoleUi.Begin("actors");
            }

            foreach (var problem in actorReferenceProblems)
            {
                LoadReport.Add("actor reference", problem, "that file was not written");
            }

            foreach (var problem in archiveProblems)
            {
                LoadReport.Add("actor archive", problem, "it is read from the folder instead");
            }

            if (actorReferenceProblems.Count > 0 || archiveProblems.Count > 0)
            {
                ConsoleUi.Failed((actorReferenceProblems.Count + archiveProblems.Count) +
                                 " problem(s)");
            }
            else
            {
                // Said once, on the run that creates it, and never again - a line on every
                // start about a folder that has not changed would be noise.
                ConsoleUi.Ok(written.Count + " file(s) in " +
                             ActorTemplateWriter.FolderName + "/actors" +
                             (stale ? "; " + ActorTemplateWriter.Pk3Name + " refreshed" : ""));
            }
        }

        /// <summary>
        /// The fingerprint stored in the archive, or null.
        ///
        /// Read from the README the archive carries rather than kept in a marker of its own: the
        /// archive is supposed to be the same reference the folder holds, so the two cannot
        /// disagree about what build they came from.
        /// </summary>
        private static string ReadPk3Fingerprint(string path)
        {
            try
            {
                var archive = new ZipArchiveReader(path);

                foreach (var entry in archive.Entries)
                {
                    if (!entry.Name.EndsWith("README.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var text = System.Text.Encoding.UTF8.GetString(archive.ReadEntry(entry));

                    foreach (var line in text.Split('\n'))
                    {
                        var trimmed = line.Trim();

                        if (trimmed.StartsWith("Fingerprint:", StringComparison.Ordinal))
                        {
                            return trimmed.Substring("Fingerprint:".Length).Trim();
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable or half-written archive: treat as stale and rewrite it.
            }

            return null;
        }

        private readonly System.Collections.Generic.List<string> actorReferenceProblems =
            new System.Collections.Generic.List<string>();

        /// <summary>
        /// Applies the actor folder to the table, and says what happened.
        ///
        /// Two things are worth a line in the load report and nothing else is:
        ///
        ///  - a file that does not parse, or names a state that does not exist, costs that actor
        ///    and is reported with the file and the line;
        ///  - a folder written by a different build is reported as possibly stale. Not an error:
        ///    keeping an older reference is a legitimate choice, and deleting it regenerates.
        ///
        /// A folder that matches this build says nothing at all, which is the case on almost
        /// every run and is why the message has to be reserved for the two above.
        /// </summary>
        private void LoadActorReference()
        {
            var problems = new System.Collections.Generic.List<string>();

            var recorded = ActorTemplateWriter.TryReadFingerprint(ConfigUtilities.GetDataDirectory());

            // Read before the folder is applied, and that is not a detail: the fingerprint
            // describes the table this build was compiled with, and applying the folder changes
            // the table. Taking it afterwards would compare the folder's own values against
            // themselves, so every edited folder would claim to be stale.
            var current = ActorTemplateWriter.CurrentFingerprint(DoomInfo.MobjInfos);

            var described = ActorTemplateWriter.Load(
                ConfigUtilities.GetDataDirectory(), DoomInfo.MobjInfos, problems);

            foreach (var problem in problems)
            {
                LoadReport.Add("actor folder", problem,
                    "that actor keeps its built-in values");
            }

            if (described == 0)
            {
                return;
            }

            ConsoleUi.Begin("actors");

            if (recorded != null && recorded != current)
            {
                LoadReport.Add("actor folder",
                    "written by a different build (fingerprint " + recorded + ", this build " +
                    current + ")",
                    "the values there may be stale; delete " +
                    ActorTemplateWriter.FolderName + "/actors to regenerate");
            }

            if (problems.Count > 0)
            {
                ConsoleUi.Failed(problems.Count + " problem(s)");
            }
            else if (recorded != null && recorded != current)
            {
                ConsoleUi.Ok(described + " actor(s), from an older table");
            }
            else
            {
                ConsoleUi.Ok(described + " actor(s)");
            }
        }

        /// <summary>
        /// Reads MAPINFO: the <c>MAPINFO</c> lump from the WAD, and any files named with
        /// <c>-mapinfo</c>.
        ///
        /// A lump rather than a flag, for the same reason SCRIPTS and DECORATE are lumps: a mod
        /// that needs instructions to work is a mod half of the people who download it will run
        /// wrong. The flag exists for the case where somebody is editing a file and wants to try
        /// it without rebuilding a PWAD.
        /// </summary>
        private void InitializeMapInfo(CommandLineArgs args)
        {
            var paths = args.mapinfo.Present ? args.mapinfo.Value : new string[0];
            var lump = wad.GetLumpNumber("MAPINFO");

            if (paths.Length == 0 && lump == -1)
            {
                return;
            }

            ConsoleUi.Begin("mapinfo");

            var problems = new System.Collections.Generic.List<string>();

            MapInfo.Clear();

            foreach (var path in paths)
            {
                try
                {
                    MapInfo.Add(System.IO.File.ReadAllText(path),
                        System.IO.Path.GetFileName(path), problems);
                }
                catch (Exception e)
                {
                    problems.Add(path + ": " + e.Message);
                }
            }

            if (lump != -1)
            {
                MapInfo.Add(System.Text.Encoding.UTF8.GetString(wad.ReadLump(lump)),
                    "MAPINFO lump", problems);
            }

            foreach (var problem in problems)
            {
                LoadReport.Add("mapinfo", problem,
                    "that level keeps the engine's own name, sky and music");
            }

            if (problems.Count > 0)
            {
                ConsoleUi.Failed(problems.Count + " problem(s)");
            }
            else
            {
                ConsoleUi.Ok(MapInfo.Count + " map(s) described");
            }
        }

        /// <summary>
        /// Reads SNDINFO: the <c>SNDINFO</c> lump from the WAD, and any files named with
        /// <c>-sndinfo</c>.
        ///
        /// Read before the sound backend is constructed, because the backend loads every sample at
        /// startup: a mapping that arrives afterwards would apply to the next sound played and not to
        /// the ones already in memory.
        /// </summary>
        private void InitializeSoundInfo(CommandLineArgs args)
        {
            var paths = args.sndinfo.Present ? args.sndinfo.Value : new string[0];
            var lump = wad.GetLumpNumber("SNDINFO");

            if (paths.Length == 0 && lump == -1)
            {
                return;
            }

            ConsoleUi.Begin("sndinfo");

            var problems = new System.Collections.Generic.List<string>();

            SoundInfo.Clear();

            foreach (var path in paths)
            {
                try
                {
                    SoundInfo.Add(System.IO.File.ReadAllText(path),
                        System.IO.Path.GetFileName(path), problems);
                }
                catch (Exception e)
                {
                    problems.Add(path + ": " + e.Message);
                }
            }

            if (lump != -1)
            {
                SoundInfo.Add(System.Text.Encoding.UTF8.GetString(wad.ReadLump(lump)),
                    "SNDINFO lump", problems);
            }

            foreach (var problem in problems)
            {
                LoadReport.Add("sndinfo", problem,
                    "that sound keeps the lump it would have played anyway");
            }

            if (problems.Count > 0)
            {
                ConsoleUi.Failed(problems.Count + " problem(s)");
            }
            else
            {
                ConsoleUi.Ok(SoundInfo.Count + " sound(s) described");
            }
        }

        /// <summary>
        /// Reads ANIMDEFS: the <c>ANIMDEFS</c> lump from the WAD, and any files named with
        /// <c>-animdefs</c>.
        ///
        /// Parsed before the graphics are loaded, applied when the animation object is built: the
        /// names in the file are resolved to picture indices once, and a lookup per frame per tic
        /// would be the one place this could get expensive.
        /// </summary>
        private void InitializeAnimatedTextures(CommandLineArgs args)
        {
            var paths = args.animdefs.Present ? args.animdefs.Value : new string[0];
            var lump = wad.GetLumpNumber("ANIMDEFS");

            if (paths.Length == 0 && lump == -1)
            {
                return;
            }

            ConsoleUi.Begin("animdefs");

            var problems = new System.Collections.Generic.List<string>();

            AnimatedTexture.Clear();

            foreach (var path in paths)
            {
                try
                {
                    AnimatedTexture.Add(System.IO.File.ReadAllText(path),
                        System.IO.Path.GetFileName(path), problems);
                }
                catch (Exception e)
                {
                    problems.Add(path + ": " + e.Message);
                }
            }

            if (lump != -1)
            {
                AnimatedTexture.Add(System.Text.Encoding.UTF8.GetString(wad.ReadLump(lump)),
                    "ANIMDEFS lump", problems);
            }

            foreach (var problem in problems)
            {
                LoadReport.Add("animdefs", problem,
                    "that cycle does not animate");
            }

            if (problems.Count > 0)
            {
                ConsoleUi.Failed(problems.Count + " problem(s)");
            }
            else
            {
                ConsoleUi.Ok(AnimatedTexture.Count + " cycle(s) described");
            }
        }

        /// <summary>
        /// Reads LOCKDEFS: the <c>LOCKDEFS</c> lump from the WAD, and any files named with
        /// <c>-lockdefs</c>.
        ///
        /// A numbered lock is what a Boom generalized locked door carries in its linedef, and
        /// without a file the built-in Boom rules apply. The file overrides them, which is the
        /// only way to ask for something Boom cannot express - "a blue card or a red skull" -
        /// or to change the refusal line and its sound.
        /// </summary>
        private void InitializeLockDefs(CommandLineArgs args)
        {
            var paths = args.lockdefs.Present ? args.lockdefs.Value : new string[0];
            var lump = wad.GetLumpNumber("LOCKDEFS");

            if (paths.Length == 0 && lump == -1)
            {
                return;
            }

            ConsoleUi.Begin("lockdefs");

            var problems = new System.Collections.Generic.List<string>();

            LockDefs.Clear();

            foreach (var path in paths)
            {
                try
                {
                    LockDefs.Add(System.IO.File.ReadAllText(path),
                        System.IO.Path.GetFileName(path), problems);
                }
                catch (Exception e)
                {
                    problems.Add(path + ": " + e.Message);
                }
            }

            if (lump != -1)
            {
                LockDefs.Add(System.Text.Encoding.UTF8.GetString(wad.ReadLump(lump)),
                    "LOCKDEFS lump", problems);
            }

            foreach (var problem in problems)
            {
                LoadReport.Add("lockdefs", problem,
                    "that lock keeps the definition it had");
            }

            if (problems.Count > 0)
            {
                ConsoleUi.Failed(problems.Count + " problem(s)");
            }
            else
            {
                ConsoleUi.Ok(LockDefs.Count + " lock(s) defined");
            }
        }

        /// <summary>
        /// Reads TERRAIN: the <c>TERRAIN</c> lump from the WAD, and any files named with
        /// <c>-terrain</c>.
        ///
        /// A map's flats say what a floor looks like; this says what it is. The parse happens
        /// here, but the bindings are resolved later, in <see cref="ResolveTerrains"/>, because a
        /// terrain is attached to a flat by name and the names only exist once the WAD's flat
        /// list has been read.
        /// </summary>
        private void InitializeTerrain(CommandLineArgs args)
        {
            var paths = args.terrain.Present ? args.terrain.Value : new string[0];
            var lump = wad.GetLumpNumber("TERRAIN");

            if (paths.Length == 0 && lump == -1)
            {
                return;
            }

            ConsoleUi.Begin("terrain");

            var problems = new System.Collections.Generic.List<string>();

            Terrain.Clear();

            foreach (var path in paths)
            {
                try
                {
                    Terrain.Add(System.IO.File.ReadAllText(path),
                        System.IO.Path.GetFileName(path), problems);
                }
                catch (Exception e)
                {
                    problems.Add(path + ": " + e.Message);
                }
            }

            if (lump != -1)
            {
                Terrain.Add(System.Text.Encoding.UTF8.GetString(wad.ReadLump(lump)),
                    "TERRAIN lump", problems);
            }

            foreach (var problem in problems)
            {
                LoadReport.Add("terrain", problem, "that entry does nothing");
            }

            if (problems.Count > 0)
            {
                ConsoleUi.Failed(problems.Count + " problem(s)");
            }
            else
            {
                ConsoleUi.Ok(Terrain.Count + " terrain(s) defined");
            }
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
                if (decorateScript.Contains(ScriptSyntax.ActorPrefix))
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
