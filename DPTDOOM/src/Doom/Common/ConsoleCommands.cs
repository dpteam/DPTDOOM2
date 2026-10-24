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

namespace DPTDOOM
{
    /// <summary>One invocation: the words after the command name, and the raw rest.</summary>
    public sealed class ConsoleCall
    {
        private readonly GameConsole console;

        public ConsoleCall(GameConsole console, string name, string[] args, string raw)
        {
            this.console = console;
            Name = name;
            Args = args;
            Raw = raw;
        }

        /// <summary>The command that was run, as typed.</summary>
        public string Name { get; private set; }

        /// <summary>Arguments, split on spaces. Empty when there are none.</summary>
        public string[] Args { get; private set; }

        /// <summary>
        /// Everything after the command name, unsplit.
        ///
        /// Exists for "echo", which is meant to repeat what was typed rather than
        /// its words - joining the split arguments back would collapse runs of
        /// spaces and is a needless way to be wrong.
        /// </summary>
        public string Raw { get; private set; }

        public void Write(string text)
        {
            console.Print(text);
        }

        public void Write(string text, ConsoleLineKind kind)
        {
            console.Print(text, kind);
        }

        public void Error(string text)
        {
            console.Print(text, ConsoleLineKind.Error);
        }

        public void Hint(string text)
        {
            console.Print(text, ConsoleLineKind.Hint);
        }

        /// <summary>
        /// Reads an on/off argument. Accepts the spellings people actually type,
        /// because a console that only takes "1" and "0" is a console that answers
        /// "expected on or off" to the word "on".
        /// </summary>
        public static bool TryParseToggle(string text, out bool value)
        {
            value = false;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            switch (text.Trim().ToLowerInvariant())
            {
                case "1":
                case "on":
                case "yes":
                case "true":
                    value = true;
                    return true;

                case "0":
                case "off":
                case "no":
                case "false":
                    value = false;
                    return true;
            }

            return false;
        }

        public static string ToggleText(bool value)
        {
            return value ? "on" : "off";
        }
    }

    /// <summary>
    /// The command table behind the console.
    ///
    /// A plain registry, deliberately: the console does not grow a command parser
    /// with expressions, pipes and variables, because none of that is needed to set
    /// a crosshair and all of it is another thing that can be wrong. A command is a
    /// name, a usage line, a help line and a handler.
    ///
    /// The table is filled by <see cref="Doom"/> rather than here, so this class has
    /// no idea what a video mode is and can be tested on its own.
    /// </summary>
    public sealed class ConsoleCommands
    {
        private sealed class Command
        {
            public string Name;
            public string Usage;
            public string Help;
            public Action<ConsoleCall> Handler;
        }

        private readonly Dictionary<string, Command> commands =
            new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Names in the order they were registered, which is the order "help" lists
        /// them in. Sorted output would be tidier and less useful: the built-ins are
        /// registered first and grouping them by purpose reads better than A-Z.
        /// </summary>
        private readonly List<string> order = new List<string>();

        public void Register(string name, string usage, string help, Action<ConsoleCall> handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null)
            {
                throw new ArgumentException("A console command needs a name and a handler.");
            }

            var command = new Command
            {
                Name = name,
                Usage = usage,
                Help = help,
                Handler = handler
            };

            if (!commands.ContainsKey(name))
            {
                order.Add(name);
            }

            commands[name] = command;
        }

        public bool Exists(string name)
        {
            return name != null && commands.ContainsKey(name);
        }

        public IList<string> Names
        {
            get { return order; }
        }

        /// <summary>
        /// Runs one line. Returns false when the line named no command, so the
        /// caller can tell "nothing happened" from "it ran".
        ///
        /// Errors are printed rather than thrown: a console is a place to try
        /// things, and an exception escaping into the game loop would turn a typo
        /// into a crash.
        /// </summary>
        public bool Execute(string line, GameConsole console)
        {
            if (string.IsNullOrEmpty(line) || line.Trim().Length == 0)
            {
                return false;
            }

            var trimmed = line.Trim();

            var space = trimmed.IndexOf(' ');
            var name = space < 0 ? trimmed : trimmed.Substring(0, space);
            var raw = space < 0 ? "" : trimmed.Substring(space + 1).Trim();

            Command command;
            if (!commands.TryGetValue(name, out command))
            {
                console.Print(
                    "Unknown command: " + name + ". Type \"help\" for a list.",
                    ConsoleLineKind.Error);
                return false;
            }

            var args = raw.Length == 0
                ? new string[0]
                : raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            try
            {
                command.Handler(new ConsoleCall(console, name, args, raw));
            }
            catch (Exception e)
            {
                // A broken command must not take the game with it.
                console.Print(name + " failed: " + e.Message, ConsoleLineKind.Error);
            }

            return true;
        }

        /// <summary>Help for everything, or for one command.</summary>
        public void ShowHelp(string name, GameConsole console)
        {
            if (!string.IsNullOrEmpty(name))
            {
                Command command;
                if (!commands.TryGetValue(name, out command))
                {
                    console.Print("Unknown command: " + name, ConsoleLineKind.Error);
                    return;
                }

                console.Print(command.Usage, ConsoleLineKind.Hint);
                console.Print("  " + command.Help);
                return;
            }

            console.Print("Commands. Type \"help <name>\" for one of them.", ConsoleLineKind.Hint);

            foreach (var key in order)
            {
                var command = commands[key];
                console.Print("  " + command.Name.PadRight(12) + command.Help, ConsoleLineKind.Hint);
            }
        }
    }
}
