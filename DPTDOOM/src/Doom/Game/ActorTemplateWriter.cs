//
// Copyright (C) 2026 DartPower Team
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
using System.Globalization;
using System.Text;

namespace DPTDOOM
{
    /// <summary>One file of the unpacked actor reference.</summary>
    public sealed class ActorTemplateFile
    {
        public string Name;
        public string Content;
    }

    /// <summary>
    /// Writes the engine's built-in actor table out as SharpScript, on first run.
    ///
    /// This is the first step towards what ZDoom does with `zdoom.pk3`: the actors a game ships
    /// are text that a person can read and copy, not numbers buried in a constructor. The reason
    /// to have it is not tidiness - it is that "what is a shotgun guy's pain chance" currently
    /// has no answer that does not involve reading this project's source, and a modder who wants
    /// to start from a working actor has to guess at every field.
    ///
    /// ## What these files are, and what they are not
    ///
    /// They are **reference**: the built-in table, written out as the patch that reproduces it
    /// exactly. Applying one changes nothing, which is the property that makes them trustworthy -
    /// a reference that silently disagrees with the engine is worse than no reference.
    ///
    /// They are **not** the source of truth yet, and the header of every file says so. Scripts are
    /// applied after DeHackEd, so a file that sets every field to its vanilla value would quietly
    /// undo a DeHackEd patch loaded alongside it. Making text the source of truth means loading it
    /// *before* DeHackEd and changing the order of the whole pipeline, which is a separate piece
    /// of work with its own risks; until then an edited copy is used by passing it explicitly
    /// with `-script`, which is unambiguous about what the player asked for.
    ///
    /// Actors with no map number cannot be addressed at all: the script API names actors
    /// `Thing_&lt;DoomEdNum&gt;`, and nineteen of the engine's types are internal (the player, a
    /// tracer, a puff) and have none. They are left out and counted in the folder's README.
    /// </summary>
    public static class ActorTemplateWriter
    {
        /// <summary>The folder name inside the data directory.</summary>
        public const string FolderName = "dptdoom-data";

        /// <summary>
        /// Names of the files that hold actor definitions, in the order they are applied.
        ///
        /// Order does not matter between them - the three buckets do not overlap - but a fixed
        /// order makes the load reproducible, and a load that varies between machines is the one
        /// thing this project cannot afford.
        /// </summary>
        public static readonly string[] ActorFiles =
        {
            "monsters.dpts", "items.dpts", "decorations.dpts"
        };

        /// <summary>
        /// Builds the files without touching the disk.
        ///
        /// Separated from writing so the content can be tested: a test that compares the written
        /// files against the table would otherwise be testing the file system as much as the
        /// generator.
        /// </summary>
        public static IList<ActorTemplateFile> Build(MobjInfo[] table)
        {
            var monsters = new StringBuilder();
            var items = new StringBuilder();
            var decorations = new StringBuilder();

            var monsterCount = 0;
            var itemCount = 0;
            var decorationCount = 0;
            var skipped = 0;

            var header = Header();

            monsters.Append(header);
            items.Append(header);
            decorations.Append(header);

            foreach (var info in table)
            {
                if (info.DoomEdNum < 0)
                {
                    // Two different things look alike here, and telling them apart matters for
                    // the README's arithmetic: a vanilla actor with no map number (the player, a
                    // tracer, a puff) is a real type that simply cannot be named, while a slot in
                    // the mod reserve is not a type at all. Counting both as "types not written"
                    // made the README claim the engine has 169 actors, which is 32 more than it
                    // has.
                    if (!IsReserveSlot(info))
                    {
                        skipped++;
                    }

                    continue;
                }

                var target = monsters;

                if ((info.Flags & MobjFlags.CountKill) != 0)
                {
                    monsterCount++;
                }
                else if ((info.Flags & MobjFlags.PickUp) != 0 ||
                         (info.Flags & MobjFlags.CountItem) != 0)
                {
                    target = items;
                    itemCount++;
                }
                else
                {
                    target = decorations;
                    decorationCount++;
                }

                WriteActor(target, info);
            }

            var files = new List<ActorTemplateFile>();

            files.Add(File(ActorFiles[0], monsters));
            files.Add(File(ActorFiles[1], items));
            files.Add(File(ActorFiles[2], decorations));

            var readme = new ActorTemplateFile();
            readme.Name = "README.txt";

            // The count of types, not the length of the table: the table now includes the mod
            // reserve, and reporting that as "types in the engine" overstated it by 32.
            var total = monsterCount + itemCount + decorationCount + skipped;

            var fingerprint = FingerprintOf(files);

            readme.Content = Readme(total, monsterCount, itemCount, decorationCount, skipped,
                                    fingerprint);
            files.Add(readme);

            return files;
        }

        /// <summary>
        /// A short marker for "which build's table this folder was written from".
        ///
        /// FNV-1a rather than anything cryptographic, and deliberately: this is not a security
        /// hash, it answers one question - has the engine's actor table changed since these files
        /// were written - and a 32-bit answer is plenty for that. Calling SHA-256 here would
        /// suggest the value protects something.
        /// </summary>
        private static string FingerprintOf(IList<ActorTemplateFile> files)
        {
            var hash = 2166136261u;

            foreach (var file in files)
            {
                if (!file.Name.EndsWith(".dpts", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var c in file.Content)
                {
                    hash = unchecked((hash ^ c) * 16777619u);
                }
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Writes the files into <paramref name="dataDirectory"/>, but only if the folder is not
        /// there yet.
        ///
        /// Never overwrites: the whole point of unpacking is that a person may edit what came
        /// out, and a second run that replaced their edits would make the feature a trap.
        /// Returns the paths written, empty when the folder already existed.
        /// </summary>
        public static IList<string> UnpackIfMissing(string dataDirectory, MobjInfo[] table,
                                                    IList<string> problems)
        {
            var written = new List<string>();
            var root = System.IO.Path.Combine(dataDirectory, FolderName);
            var folder = System.IO.Path.Combine(root, "actors");

            if (System.IO.Directory.Exists(folder))
            {
                return written;
            }

            try
            {
                System.IO.Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                problems.Add("could not create " + folder + ": " + e.Message);
                return written;
            }

            foreach (var file in Build(table))
            {
                var path = System.IO.Path.Combine(folder, file.Name);

                try
                {
                    System.IO.File.WriteAllText(path, file.Content);
                    written.Add(path);
                }
                catch (Exception e)
                {
                    problems.Add("could not write " + path + ": " + e.Message);
                }
            }

            return written;
        }

        /// <summary>
        /// Reads the fingerprint recorded in the folder's README, or null when there is none.
        ///
        /// Absent is not a failure: a folder from before fingerprints existed, or one a person
        /// made by hand, has no marker and is still perfectly loadable. The caller decides what
        /// to say about it.
        /// </summary>
        public static string TryReadFingerprint(string dataDirectory)
        {
            var path = System.IO.Path.Combine(dataDirectory, FolderName, "actors", "README.txt");

            try
            {
                if (!System.IO.File.Exists(path))
                {
                    return null;
                }

                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    if (line.StartsWith("Fingerprint:", StringComparison.Ordinal))
                    {
                        return line.Substring("Fingerprint:".Length).Trim();
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable README: treat as unmarked rather than as an error, since the actor
                // files themselves may still be fine.
            }

            return null;
        }

        /// <summary>The fingerprint this build would write for its own table.</summary>
        public static string CurrentFingerprint(MobjInfo[] table)
        {
            return FingerprintOf(Build(table));
        }

        /// <summary>
        /// Applies the actor files from the folder, if it has any.
        ///
        /// Called **before** DeHackEd, and that order is the whole design: a script applied after
        /// a patch would set the fields back to the values written here and quietly undo the
        /// patch. Before it, the patch wins, which is what somebody loading both expects.
        ///
        /// A folder with no files is not an error - it is a machine that has never run the game
        /// with this feature, and the compiled table is already correct for it.
        ///
        /// Returns the number of actors described, so the caller can say something only when
        /// there is something to say.
        /// </summary>
        public static int Load(string dataDirectory, MobjInfo[] table, IList<string> problems)
        {
            var folder = System.IO.Path.Combine(dataDirectory, FolderName, "actors");

            if (!System.IO.Directory.Exists(folder))
            {
                return 0;
            }

            var library = new Scripting.ScriptLibrary();
            var any = false;

            foreach (var name in ActorFiles)
            {
                var path = System.IO.Path.Combine(folder, name);

                try
                {
                    if (!System.IO.File.Exists(path))
                    {
                        continue;
                    }

                    library.Add(System.IO.File.ReadAllText(path), name, table);
                    any = true;
                }
                catch (Exception e)
                {
                    problems.Add(name + ": " + e.Message);
                }
            }

            if (!any)
            {
                return 0;
            }

            library.ApplyTo(table);

            foreach (var problem in library.Problems)
            {
                problems.Add(problem);
            }

            return library.Patches.Count;
        }

        private static ActorTemplateFile File(string name, StringBuilder content)
        {
            var file = new ActorTemplateFile();
            file.Name = name;
            file.Content = content.ToString();
            return file;
        }

        /// <summary>
        /// Whether this entry is one of the empty slots reserved for mods rather than a type.
        ///
        /// Told apart by position, not by content: a reserve slot and a vanilla actor without a
        /// map number both have doomednum -1, and only the position says which is which.
        /// </summary>
        private static bool IsReserveSlot(MobjInfo info)
        {
            var table = DoomInfo.MobjInfos;

            for (var i = DoomInfo.CustomActorFirst; i < table.Length; i++)
            {
                if (ReferenceEquals(table[i], info))
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteActor(StringBuilder target, MobjInfo info)
        {
            target.Append("\n// ").Append(info.DoomEdNum).Append("\n");
            target.Append("fn Thing_").Append(info.DoomEdNum).Append("() {\n");

            Number(target, "Health", info.SpawnHealth);
            Number(target, "Speed", info.Speed);
            Number(target, "PainChance", info.PainChance);
            Number(target, "ReactionTime", info.ReactionTime);
            Number(target, "Mass", info.Mass);
            Number(target, "Damage", info.Damage);

            Size(target, "Radius", info.Radius);
            Size(target, "Height", info.Height);

            State(target, "SpawnState", info.SpawnState);
            State(target, "SeeState", info.SeeState);
            State(target, "PainState", info.PainState);
            State(target, "MeleeState", info.MeleeState);
            State(target, "MissileState", info.MissileState);
            State(target, "DeathState", info.DeathState);
            State(target, "XdeathState", info.XdeathState);
            State(target, "RaiseState", info.RaiseState);

            Sound(target, "SeeSound", info.SeeSound);
            Sound(target, "AttackSound", info.AttackSound);
            Sound(target, "PainSound", info.PainSound);
            Sound(target, "DeathSound", info.DeathSound);
            Sound(target, "ActiveSound", info.ActiveSound);

            Flags(target, info.Flags);

            target.Append("}\n");
        }

        private static void Number(StringBuilder target, string name, int value)
        {
            target.Append("    ").Append(name).Append('(')
                  .Append(value.ToString(CultureInfo.InvariantCulture)).Append(");\n");
        }

        /// <summary>
        /// A size, as a fixed-point number.
        ///
        /// Written with the fraction only when there is one: "Radius(20)" reads better than
        /// "Radius(20.0)", and every vanilla size is a whole number. The fraction is produced by
        /// integer arithmetic rather than by dividing doubles, because the value has to come back
        /// as the same Fixed - and 16.16 has exact decimal expansions that a double round-trip can
        /// miss by one.
        /// </summary>
        private static void Size(StringBuilder target, string name, Fixed value)
        {
            var whole = value.Data >> 16;
            var fraction = value.Data & 0xFFFF;

            target.Append("    ").Append(name).Append('(')
                  .Append(whole.ToString(CultureInfo.InvariantCulture));

            if (fraction != 0)
            {
                // Four decimal places is more than 16.16 can express: the smallest step is
                // 1/65536, about 0.000015, and the parser accepts up to four digits.
                var scaled = (fraction * 10000 + 32768) / 65536;

                target.Append('.')
                      .Append(scaled.ToString("0000", CultureInfo.InvariantCulture)
                              .TrimEnd('0'));
            }

            target.Append(");\n");
        }

        private static void State(StringBuilder target, string name, MobjState state)
        {
            target.Append("    ").Append(name).Append("(\"")
                  .Append(state.ToString()).Append("\");\n");
        }

        private static void Sound(StringBuilder target, string name, Sfx sound)
        {
            target.Append("    ").Append(name).Append("(\"")
                  .Append(sound.ToString()).Append("\");\n");
        }

        /// <summary>
        /// One line per flag that is set.
        ///
        /// Composite members are skipped, and that is not tidiness: <c>Translation</c> is a
        /// two-bit field wearing a flag's clothes, and <c>Flag("Translation", true)</c> is
        /// refused by the API because it is not a power of two. No vanilla actor uses it, so
        /// nothing is lost today - but a generator that emitted it would produce a file that
        /// fails to load for the one actor that ever does.
        /// </summary>
        private static void Flags(StringBuilder target, MobjFlags flags)
        {
            foreach (MobjFlags flag in Enum.GetValues(typeof(MobjFlags)))
            {
                var value = (int)flag;

                if (value == 0 || (value & (value - 1)) != 0)
                {
                    continue;
                }

                if ((flags & flag) == 0)
                {
                    continue;
                }

                target.Append("    Flag(\"").Append(flag.ToString()).Append("\", true);\n");
            }
        }

        private static string Header()
        {
            return
                "// Built-in actors, as SharpScript.\n" +
                "//\n" +
                "// This file IS loaded, and it is loaded BEFORE DeHackEd, so a .deh patch still\n" +
                "// wins over it. Edit a number here and the game changes; delete the folder and\n" +
                "// the engine's own values come back.\n" +
                "//\n" +
                "// Delete this file and its two neighbours to start from the built-in table again:\n" +
                "// the folder is regenerated on the next run and never overwritten otherwise, so\n" +
                "// your edits survive.\n";
        }

        private static string Readme(int total, int monsters, int items, int decorations,
                                     int skipped, string fingerprint)
        {
            var text = new StringBuilder();

            text.Append("dptdoom-data/actors\n");
            text.Append("===================\n\n");
            text.Append("The engine's actor table, written out as SharpScript.\n");
            text.Append("Generated on first run, never overwritten afterwards, so edits survive.\n");
            text.Append("Fingerprint: ").Append(fingerprint).Append("\n\n");

            text.Append("These files are LOADED\n");
            text.Append("----------------------\n\n");
            text.Append("They are applied before DeHackEd, which means:\n\n");
            text.Append("  - editing a number here changes the game, with no recompiling;\n");
            text.Append("  - a .deh patch loaded with -deh still wins, because it is applied after;\n");
            text.Append("  - deleting the folder brings the built-in values back on the next run.\n\n");
            text.Append("The fingerprint above is the build that wrote them. If it differs from\n");
            text.Append("the fingerprint this build would write, the engine says so at startup:\n");
            text.Append("the values here were taken from an older table and may be stale. Delete\n");
            text.Append("the folder to regenerate, or keep it - an old reference is not an error,\n");
            text.Append("it is a choice.\n\n");

            text.Append("Files\n");
            text.Append("-----\n\n");
            text.Append("  monsters.dpts    ").Append(monsters).Append(" actors\n");
            text.Append("  items.dpts       ").Append(items).Append(" actors\n");
            text.Append("  decorations.dpts ").Append(decorations).Append(" actors\n\n");
            text.Append("Total: ").Append(total).Append(" types in the engine, of which ")
                .Append(skipped).Append(" are not here:\n");
            text.Append("they have no map number (the player, tracers, puffs), and the script\n");
            text.Append("API names actors by map number. Their values stay in the engine.\n\n");

            text.Append("What an entry looks like\n");
            text.Append("-----------------------\n\n");
            text.Append("    fn Thing_3004() {\n");
            text.Append("        Health(20);\n");
            text.Append("        Speed(8);\n");
            text.Append("        SpawnState(\"PossStnd\");\n");
            text.Append("        Flag(\"Solid\", true);\n");
            text.Append("        ...\n");
            text.Append("    }\n\n");
            text.Append("The function name is the map number: 3004 is the zombieman. Change a\n");
            text.Append("number and that is what the actor gets. Add a function for a number that\n");
            text.Append("does not exist yet and the actor is created, copying the parent named in\n");
            text.Append("DECORATE - see docs/DECORATE.md.\n\n");

            text.Append("A broken entry costs that actor, not the file and not the game: the rest\n");
            text.Append("of the file is applied, the level loads, and the startup log names the\n");
            text.Append("file and the line.\n\n");

            text.Append("Language reference\n");
            text.Append("------------------\n\n");
            text.Append("  docs/SCRIPTS.md\n");

            return text.ToString();
        }
    }
}
