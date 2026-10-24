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

namespace DPTDOOM.Scripting
{
    /// <summary>One call from a script into the engine.</summary>
    public sealed class ScriptCall
    {
        public string Name;
        public ScriptValue[] Arguments;
        public int Line;

        public int Count
        {
            get { return Arguments.Length; }
        }

        public ScriptValue At(int index)
        {
            return Arguments[index];
        }

        public int AsInt(int index)
        {
            return Arguments[index].AsInt();
        }

        public Fixed AsFixed(int index)
        {
            return Arguments[index].AsFixed();
        }

        public bool AsBool(int index)
        {
            return Arguments[index].AsBool();
        }

        public string AsText(int index)
        {
            return Arguments[index].AsText();
        }

        /// <summary>
        /// Reports a problem with the arguments, or with the world.
        ///
        /// A host function throwing a plain exception would surface as an unexplained
        /// crash somewhere in the simulation. Going through here attaches the script line,
        /// which is the difference between "index out of range" and "line 12: no such
        /// actor 'ImpX'".
        /// </summary>
        public ScriptException Fail(string message)
        {
            return new ScriptException(message, Line);
        }
    }

    /// <summary>An engine function a script may call.</summary>
    public delegate ScriptValue ScriptFunction(ScriptCall call);

    /// <summary>
    /// What a script is allowed to do: exactly the functions registered here, and
    /// nothing else.
    ///
    /// This is the sandbox, and it is a sandbox by construction rather than by policy.
    /// The language has no way to name a type, no way to reach a namespace, no
    /// reflection and no dynamic dispatch: a script can call a registered function and
    /// do arithmetic. There is no list of forbidden calls to keep in sync with the BCL,
    /// because there is no path to the BCL at all. That is why the language was written
    /// instead of embedding an existing one - every embeddable C# needs the runtime, and
    /// the runtime is what a downloaded PWAD must never have.
    ///
    /// Determinism follows from the same design. Nothing in here reads a clock or a
    /// random number unless the embedder registers it, and the embedder here registers
    /// the engine's own RNG - the one that is part of the world state and travels through
    /// the tic stream. A script therefore produces the same result on every machine in a
    /// lockstep session, which is the whole requirement.
    /// </summary>
    public sealed class ScriptHost
    {
        private sealed class Entry
        {
            public string Name;
            public int MinArguments;
            public int MaxArguments;
            public ScriptFunction Body;
        }

        private readonly Dictionary<string, Entry> functions = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly List<string> names = new List<string>();

        /// <summary>
        /// How many operations one top-level call may perform.
        ///
        /// The language has <c>while</c>, so a script can loop for ever, and a loop inside
        /// the simulation means the game stops with the window still painted - the worst
        /// kind of hang, because it looks like a freeze rather than a bug in a mod. The
        /// budget turns it into a reported error after a moment instead.
        ///
        /// Large enough that no honest script comes close: the actor scripts this is
        /// being built for run a few dozen operations per call.
        /// </summary>
        public int StepBudget = 100000;

        /// <summary>How deep script-to-script calls may nest.</summary>
        public int CallDepth = 32;

        public void Register(string name, int arguments, ScriptFunction body)
        {
            Register(name, arguments, arguments, body);
        }

        public void Register(string name, int minArguments, int maxArguments, ScriptFunction body)
        {
            if (functions.ContainsKey(name))
            {
                // Registering the same name twice means two parts of the embedder
                // disagree about what it does, and whichever ran second would win by
                // accident. Refused loudly at startup instead.
                throw new ScriptException("'" + name + "' is registered twice");
            }

            var entry = new Entry();
            entry.Name = name;
            entry.MinArguments = minArguments;
            entry.MaxArguments = maxArguments;
            entry.Body = body;

            functions.Add(name, entry);
            names.Add(name);
        }

        public bool Contains(string name)
        {
            return functions.ContainsKey(name);
        }

        /// <summary>Registered names, in registration order, for help output.</summary>
        public string[] Names
        {
            get { return names.ToArray(); }
        }

        internal ScriptValue Invoke(ScriptCall call)
        {
            Entry entry;

            if (!functions.TryGetValue(call.Name, out entry))
            {
                throw call.Fail("'" + call.Name + "' is not a function");
            }

            if (call.Count < entry.MinArguments || call.Count > entry.MaxArguments)
            {
                throw call.Fail(Describe(entry) + " takes " + Arity(entry) + ", got " + call.Count);
            }

            return entry.Body(call);
        }

        private static string Describe(Entry entry)
        {
            return "'" + entry.Name + "'";
        }

        private static string Arity(Entry entry)
        {
            if (entry.MinArguments == entry.MaxArguments)
            {
                return entry.MinArguments == 1 ? "1 argument" : entry.MinArguments + " arguments";
            }

            return entry.MinArguments + " to " + entry.MaxArguments + " arguments";
        }
    }
}
