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

namespace DPTDOOM.Scripting
{
    /// <summary>
    /// One actor a script has described, and the script that describes it.
    /// </summary>
    public sealed class ActorPatch
    {
        public int DoomEdNum;
        public string Function;
        public string Source;
        public int Line;
    }

    /// <summary>
    /// The scripts a game has loaded, and the results of applying them.
    ///
    /// One library per load. Applying a patch twice would double every effect that reads
    /// the value it writes - <c>Health(Health() * 2)</c> is the obvious one - so applying
    /// happens once and a second attempt is refused rather than silently applied.
    ///
    /// Errors do not stop the load. A broken script means the actor keeps the behaviour
    /// it already had, the player still gets a level, and the load report names the file
    /// and the line. That is the same choice the content loader makes for a broken
    /// sprite or a bad animation cycle: one mistake in one mod must not cost the game.
    /// </summary>
    public sealed class ScriptLibrary
    {
        private sealed class Compiled
        {
            public ScriptUnit Unit;
            public string Source;
        }

        private readonly List<Compiled> compiled = new List<Compiled>();
        private readonly List<ActorPatch> patches = new List<ActorPatch>();
        private readonly List<string> problems = new List<string>();

        private bool applied;

        /// <summary>Actors described by the loaded scripts.</summary>
        public IList<ActorPatch> Patches
        {
            get { return patches; }
        }

        /// <summary>
        /// What went wrong, one line per problem. Empty when everything loaded.
        /// </summary>
        public IList<string> Problems
        {
            get { return problems; }
        }

        /// <summary>
        /// Reads one script and adds it to the library.
        ///
        /// A script that fails to parse is recorded as a problem and skipped: the others
        /// still load, which is what makes a collection of PWADs usable when one of them
        /// has a typo.
        /// </summary>
        public void Add(string text, string source, MobjInfo[] table)
        {
            ScriptUnit unit;

            try
            {
                unit = ScriptParser.Parse(text);
            }
            catch (ScriptException e)
            {
                problems.Add(source + ": " + e.Message);
                return;
            }

            var named = 0;

            foreach (var function in unit.Functions)
            {
                int doomEdNum;

                if (!TryParseActorName(function.Name, out doomEdNum))
                {
                    // A function that is not an actor definition is a helper, and helpers
                    // are legitimate: they are how a script shares arithmetic between two
                    // actors. Not reported.
                    continue;
                }

                named++;

                int type;

                if (Find(table, doomEdNum, out type) == null)
                {
                    problems.Add(source + " line " + function.Line + ": no actor with DoomEdNum " +
                                 doomEdNum + " (from '" + function.Name + "')");
                    continue;
                }

                var patch = new ActorPatch();
                patch.DoomEdNum = doomEdNum;
                patch.Function = function.Name;
                patch.Source = source;
                patch.Line = function.Line;
                patches.Add(patch);
            }

            if (named == 0)
            {
                // Only when no actor-looking function was written at all. A script whose
                // only actor name is wrong already has a message naming the mistake, and
                // adding "no actors found" on top of it would report one cause twice.
                problems.Add(source + ": no actor definitions found; name them Thing_<DoomEdNum>");
            }

            var entry = new Compiled();
            entry.Unit = unit;
            entry.Source = source;
            compiled.Add(entry);
        }

        /// <summary>
        /// Runs every definition against the engine's actor table.
        ///
        /// Called once, after the actor table is complete and before any level is built.
        /// Later than DeHackEd on purpose: a script may read what a DeHackEd patch wrote,
        /// and the reverse is not true - which is the order a modder expects, since a
        /// script is the newer and more precise way to say the same thing.
        /// </summary>
        public void ApplyTo(MobjInfo[] table)
        {
            if (applied)
            {
                // Refused rather than ignored: a second call would double every
                // read-then-write, and a silent doubling is far worse than a message.
                problems.Add("the scripts were already applied; applying again would double their effects");
                return;
            }

            applied = true;

            foreach (var entry in compiled)
            {
                foreach (var function in entry.Unit.Functions)
                {
                    int doomEdNum;

                    if (!TryParseActorName(function.Name, out doomEdNum))
                    {
                        continue;
                    }

                    int type;
                    var info = Find(table, doomEdNum, out type);

                    if (info == null)
                    {
                        continue;
                    }

                    var host = new ScriptHost();
                    var api = new ScriptActorApi(info, type);
                    api.Register(host);

                    try
                    {
                        new ScriptInterpreter(entry.Unit, host).Call(function.Name);

                        // The links between a mod's own frames can only be made now: a chain
                        // names its successor before that successor exists.
                        if (api.HasCustomStates)
                        {
                            api.Finish(problems, entry.Source);
                        }
                    }
                    catch (ScriptException e)
                    {
                        problems.Add(entry.Source + ": " + e.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="name"/> describes an actor, and which one.
        ///
        /// <c>Thing_3004</c> is the form. DoomEdNum is used rather than a friendly name
        /// because it is the one identifier that already exists everywhere: in the map
        /// format, in every editor, and in the actor table. ZDoom's names ("Zombieman",
        /// "Imp") are an invention of that port, and inventing a second naming scheme
        /// here would mean every modder has to learn which of two names applies.
        /// </summary>
        public static bool TryParseActorName(string name, out int doomEdNum)
        {
            doomEdNum = 0;

            const string prefix = "Thing_";

            if (name == null || !name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var number = name.Substring(prefix.Length);

            return number.Length > 0 &&
                   int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out doomEdNum);
        }

        /// <summary>
        /// The actor a `fn Thing_&lt;number&gt;()` names, and its index in the table.
        ///
        /// The index is what a mod's sound override is keyed by, so it is returned from the same
        /// search rather than looked up again: the table has one entry per type, and a second scan
        /// that disagreed with the first would be a bug waiting to happen.
        /// </summary>
        private static MobjInfo Find(MobjInfo[] table, int doomEdNum, out int type)
        {
            type = -1;

            for (var index = 0; index < table.Length; index++)
            {
                if (table[index].DoomEdNum == doomEdNum)
                {
                    type = index;
                    return table[index];
                }
            }

            return null;
        }
    }
}
