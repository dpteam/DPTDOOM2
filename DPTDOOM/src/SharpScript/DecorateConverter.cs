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

namespace DPTDOOM.Scripting
{
    /// <summary>
    /// One actor block read out of a DECORATE file.
    ///
    /// Kept as data rather than converted on the fly, because the two halves fail
    /// differently and a modder needs to know which one: reading is a matter of syntax, and
    /// applying is a matter of whether this engine has the actor and the property.
    /// </summary>
    public sealed class DecorateActor
    {
        public string Name;
        public string ParentName;
        public string Replaces;

        /// <summary>The map number, or -1 when the file did not give one.</summary>
        public int DoomEdNum = -1;

        /// <summary>Line of the ACTOR keyword, for a report a person can act on.</summary>
        public int Line;

        /// <summary>Integer properties, already normalised to the names this engine uses.</summary>
        public readonly List<KeyValuePair<string, int>> Numbers = new List<KeyValuePair<string, int>>();

        /// <summary>Flag names with the value the file asked for.</summary>
        public readonly List<KeyValuePair<string, bool>> Flags = new List<KeyValuePair<string, bool>>();

        /// <summary>Properties that were read and cannot be applied, with their text.</summary>
        public readonly List<KeyValuePair<string, string>> Unsupported =
            new List<KeyValuePair<string, string>>();

        /// <summary>Whether the block carried a States section.</summary>
        public bool HasStates;
    }

    /// <summary>
    /// Reads DECORATE and writes SharpScript.
    ///
    /// The conversion is the honest way to support DECORATE in this project, and not a
    /// shortcut. SharpScript already has a parser, a sandbox and an application order that
    /// is tested against the actor table; a second path that applies changes directly would
    /// need its own copy of all three and would drift from the first. Emitting source also
    /// means a modder can read what their DECORATE became - <c>-decorate-inspect</c> prints
    /// it - and that is the only way to find out why an actor did not come out as expected.
    ///
    /// What it converts, and what it refuses:
    ///
    ///  - numbers: Health, Speed, Radius, Height, PainChance, Mass, Damage, ReactionTime;
    ///  - flags: <c>+FLAG</c> and <c>-FLAG</c>, plus the MONSTER and PROJECTILE bundles;
    ///  - nothing else. A States section is read, counted and reported, because states need
    ///    entries in the engine's state table that do not exist yet, and pretending
    ///    otherwise would produce an actor that looks converted and behaves like the
    ///    original - a much worse outcome than a line in the report.
    /// </summary>
    public static class DecorateConverter
    {
        /// <summary>
        /// DoomEdNum by DECORATE actor name, for the names this converter can resolve.
        ///
        /// Only the monsters, and deliberately so. A wrong number here does not fail: it
        /// silently rewrites a different actor, so a table guessed at from memory is worse
        /// than no table at all - a mod replacing a shotgun guy would end up editing a
        /// health bonus. Everything else is reported as an unknown name, which costs the
        /// modder one line to fix and costs nobody a mystery.
        /// </summary>
        private static readonly string[,] KnownActors =
        {
            { "zombieman", "3004" },
            { "shotgonguy", "9" },
            { "shotgunguy", "9" },
            { "chaingunguy", "65" },
            { "heavyweapondude", "65" },
            { "doomimp", "3001" },
            { "imp", "3001" },
            { "demon", "3002" },
            { "pinky", "3002" },
            { "spectre", "58" },
            { "cacodemon", "3005" },
            { "lostsoul", "3006" },
            { "baronofhell", "3003" },
            { "hellknight", "69" },
            { "revenant", "66" },
            { "mancubus", "67" },
            { "fatso", "67" },
            { "arachnotron", "68" },
            { "archvile", "64" },
            { "painelemental", "71" },
            { "wolfensteinss", "84" },
            { "ssguy", "84" },
            { "commanderkeen", "72" },
            { "bossbrain", "88" },
            { "bosseye", "89" },
            { "cyberdemon", "16" },
            { "spidermastermind", "7" },
            { "player", "1" }
        };

        /// <summary>
        /// The properties this converter knows how to map, by DECORATE spelling.
        ///
        /// The values are the function names in the actor API, which are not always the
        /// same word: DECORATE says "Health" and so does the API, but DECORATE's
        /// "ReactionTime" is the same thing the API calls by that name, while "Scale",
        /// "Alpha" and "RenderStyle" have no counterpart at all.
        /// </summary>
        private static readonly Dictionary<string, string> NumberProperties =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "health", "Health" },
            { "speed", "Speed" },
            { "radius", "Radius" },
            { "height", "Height" },
            { "painchance", "PainChance" },
            { "mass", "Mass" },
            { "damage", "Damage" },
            { "reactiontime", "ReactionTime" }
        };

        /// <summary>
        /// The DECORATE bundles, expanded to the flags this engine has.
        ///
        /// MONSTER and PROJECTILE are not flags but groups of them. The expansion is taken
        /// from vanilla Doom's own actors - a rocket is MF_NOBLOCKMAP|MF_MISSILE|MF_DROPOFF|
        /// MF_NOGRAVITY, an imp is MF_SHOOTABLE|MF_SOLID|MF_COUNTKILL - rather than invented,
        /// because the whole point of the flags is to match what the engine already does.
        /// </summary>
        private static readonly string[] MonsterFlags = { "Solid", "Shootable", "CountKill" };

        private static readonly string[] ProjectileFlags =
        {
            "NoBlockMap", "Missile", "DropOff", "NoGravity"
        };

        /// <summary>
        /// Reads DECORATE and returns SharpScript source. Problems are appended to
        /// <paramref name="problems"/> and are all in DECORATE terms: the file and the line
        /// the modder wrote, never a line of the generated text.
        /// </summary>
        public static string Convert(string text, MobjInfo[] table, IList<string> problems)
        {
            var actors = Parse(text, problems);
            var script = new StringBuilder();

            script.Append("// Generated from DECORATE. Edits are lost: change the DECORATE file.\n");

            foreach (var actor in actors)
            {
                var target = ResolveTarget(actor, table, problems);

                if (target == null)
                {
                    continue;
                }

                if (actor.HasStates)
                {
                    // Said once per actor, in the terms that matter: the numbers came
                    // across, the behaviour did not.
                    problems.Add(actor.Name + " line " + actor.Line +
                                 ": its States section is not converted, so the actor keeps the " +
                                 "engine's own states");
                }

                foreach (var property in actor.Unsupported)
                {
                    problems.Add(actor.Name + " line " + actor.Line + ": '" + property.Key +
                                 " " + property.Value + "' has no counterpart in this engine, so it " +
                                 "is left as it was");
                }

                if (actor.Numbers.Count == 0 && actor.Flags.Count == 0)
                {
                    // Nothing to say, so nothing is emitted. An empty patch would still be
                    // an entry in the report and would look like work.
                    continue;
                }

                script.Append("\nfn Thing_").Append(target.DoomEdNum).Append("() {\n");

                foreach (var pair in actor.Numbers)
                {
                    script.Append("    ").Append(pair.Key).Append('(')
                          .Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append(");\n");
                }

                foreach (var pair in actor.Flags)
                {
                    script.Append("    Flag(\"").Append(pair.Key).Append("\", ")
                          .Append(pair.Value ? "true" : "false").Append(");\n");
                }

                script.Append("}\n");
            }

            return script.ToString();
        }

        /// <summary>
        /// Finds the actor in the engine's table that a DECORATE block describes, or null
        /// with the reason.
        ///
        /// Three ways a block names its target, in the order DECORATE itself resolves them:
        /// the number written in the block, the name it replaces, and the parent it inherits
        /// from. New actors - a block with a number this engine does not have - are refused
        /// rather than created, because creating one needs a state table entry and a sprite
        /// name, and an actor with neither cannot be placed on a map.
        /// </summary>
        private static MobjInfo ResolveTarget(DecorateActor actor, MobjInfo[] table,
                                              IList<string> problems)
        {
            var number = actor.DoomEdNum;

            if (number < 0 && actor.Replaces != null)
            {
                if (!TryResolveName(actor.Replaces, out number))
                {
                    problems.Add(actor.Name + " line " + actor.Line + ": 'replaces " +
                                 actor.Replaces + "' is an actor name this converter does not " +
                                 "know, so the actor is not changed; write the DoomEdNum in the " +
                                 "block instead");
                    return null;
                }
            }

            if (number < 0 && actor.ParentName != null)
            {
                // Inheriting from a known actor and not replacing it means the derived actor
                // takes over the parent's map slot, which is what DECORATE means by a block
                // whose only identifier is ":" and a parent name.
                TryResolveName(actor.ParentName, out number);
            }

            if (number < 0)
            {
                problems.Add(actor.Name + " line " + actor.Line + ": the block names no DoomEdNum " +
                             "this converter can resolve, so nothing is changed");
                return null;
            }

            foreach (var info in table)
            {
                if (info.DoomEdNum == number)
                {
                    return info;
                }
            }

            problems.Add(actor.Name + " line " + actor.Line + ": this engine has no actor with " +
                         "DoomEdNum " + number.ToString(CultureInfo.InvariantCulture) +
                         "; new actors need their own states, so it is not created");
            return null;
        }

        private static bool TryResolveName(string name, out int number)
        {
            number = -1;

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (var i = 0; i < KnownActors.GetLength(0); i++)
            {
                if (!string.Equals(KnownActors[i, 0], name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return int.TryParse(KnownActors[i, 1], NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out number);
            }

            return false;
        }

        // ------------------------------------------------------------ reading

        /// <summary>
        /// Reads every actor block in the text.
        ///
        /// Written by hand rather than with a parser generator for the same reason the rest
        /// of this project is: a dependency is a thing to ship. The grammar that matters is
        /// four tokens - a word, a number, a quoted string and a brace - and everything else
        /// is a comment.
        /// </summary>
        public static IList<DecorateActor> Parse(string text, IList<string> problems)
        {
            var actors = new List<DecorateActor>();
            var reader = new Reader(text);
            var line = 1;

            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    break;
                }

                if (!string.Equals(token.Text, "ACTOR", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var startLine = token.Line;
                var actor = new DecorateActor();
                actor.Line = startLine;

                // Everything up to the opening brace describes which actor this is. DECORATE
                // allows these in either order and in any combination, so they are read as a
                // bag rather than as a sequence.
                while (true)
                {
                    var header = reader.Next(ref line);

                    if (header == null)
                    {
                        problems.Add("line " + startLine + ": the actor block is not closed");
                        return actors;
                    }

                    if (header.Text == "{")
                    {
                        break;
                    }

                    if (header.Text == ":")
                    {
                        var parent = reader.Next(ref line);

                        if (parent != null && parent.Text != "{")
                        {
                            actor.ParentName = parent.Text;
                        }

                        continue;
                    }

                    if (string.Equals(header.Text, "replaces", StringComparison.OrdinalIgnoreCase))
                    {
                        var replaced = reader.Next(ref line);

                        if (replaced != null && replaced.Text != "{")
                        {
                            actor.Replaces = replaced.Text;
                        }

                        continue;
                    }

                    if (header.IsNumber)
                    {
                        actor.DoomEdNum = header.Number;
                        continue;
                    }

                    if (actor.Name == null)
                    {
                        actor.Name = header.Text;
                    }
                }

                if (string.IsNullOrEmpty(actor.Name))
                {
                    problems.Add("line " + startLine + ": the actor has no name");
                }

                actor.Name = actor.Name ?? "actor at line " + startLine;

                ReadBody(reader, ref line, actor, problems);
                actors.Add(actor);
            }

            return actors;
        }

        private static void ReadBody(Reader reader, ref int line, DecorateActor actor,
                                     IList<string> problems)
        {
            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    problems.Add(actor.Name + " line " + actor.Line + ": the actor block is not closed");
                    return;
                }

                if (token.Text == "}")
                {
                    return;
                }

                if (string.Equals(token.Text, "States", StringComparison.OrdinalIgnoreCase))
                {
                    actor.HasStates = true;
                    SkipBlock(reader, ref line, actor, problems);
                    continue;
                }

                // +FLAG and -FLAG are a single token in the reader, because a lone "+" is
                // already the sign of a number and splitting the two would make the reader
                // ambiguous for no gain.
                if (token.Text.Length > 1 && (token.Text[0] == '+' || token.Text[0] == '-'))
                {
                    ReadFlag(actor, token.Text.Substring(1), token.Text[0] == '+');
                    continue;
                }

                if (string.Equals(token.Text, "MONSTER", StringComparison.OrdinalIgnoreCase))
                {
                    AddFlags(actor, MonsterFlags, true);
                    continue;
                }

                if (string.Equals(token.Text, "PROJECTILE", StringComparison.OrdinalIgnoreCase))
                {
                    AddFlags(actor, ProjectileFlags, true);
                    continue;
                }

                var value = reader.Next(ref line);

                if (value == null)
                {
                    return;
                }

                if (value.Text == "}")
                {
                    return;
                }

                string mapped;

                if (NumberProperties.TryGetValue(token.Text, out mapped))
                {
                    var number = value.IsNumber ? value.Number : -1;

                    if (number < 0 && !int.TryParse(value.Text, NumberStyles.Integer,
                                                    CultureInfo.InvariantCulture, out number))
                    {
                        problems.Add(actor.Name + " line " + value.Line + ": '" + token.Text +
                                     "' needs a whole number, found '" + value.Text + "'");
                        continue;
                    }

                    actor.Numbers.Add(new KeyValuePair<string, int>(mapped, number));
                    continue;
                }

                // Everything else is recorded rather than dropped: silence is what makes a
                // modder believe a property took effect.
                actor.Unsupported.Add(new KeyValuePair<string, string>(token.Text, value.Text));
            }
        }

        /// <summary>
        /// Skips a States section, tracking nesting.
        ///
        /// Skipped rather than read, because the frame list is exactly the part this engine
        /// cannot accept yet: a frame needs an entry in the state table and a sprite name,
        /// and both are outside what an actor patch can change. Counting the braces is all
        /// that is needed to find the end of it and nothing here is used afterwards.
        /// </summary>
        private static void SkipBlock(Reader reader, ref int line, DecorateActor actor,
                                      IList<string> problems)
        {
            var open = reader.Next(ref line);

            if (open == null || open.Text != "{")
            {
                problems.Add(actor.Name + " line " + actor.Line +
                             ": 'States' is not followed by a block");
                return;
            }

            // Counted from the opening brace: the block ends at the brace that closes it,
            // not at the first closing brace in sight. Stopping early - which the first
            // version did - swallowed the actor's own closing brace and reported the whole
            // block as unterminated, one line after reading it correctly.
            var depth = 1;

            while (depth > 0)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    problems.Add(actor.Name + " line " + actor.Line +
                                 ": the States block is not closed");
                    return;
                }

                if (token.Text == "{")
                {
                    depth++;
                }
                else if (token.Text == "}")
                {
                    depth--;
                }
            }
        }

        private static void ReadFlag(DecorateActor actor, string name, bool value)
        {
            if (name.Length == 0)
            {
                return;
            }

            actor.Flags.Add(new KeyValuePair<string, bool>(NormaliseFlagName(name), value));
        }

        private static void AddFlags(DecorateActor actor, string[] names, bool value)
        {
            foreach (var name in names)
            {
                actor.Flags.Add(new KeyValuePair<string, bool>(name, value));
            }
        }

        /// <summary>
        /// Turns DECORATE's shouting into the engine's PascalCase.
        ///
        /// Flag names are compared case-insensitively when applied, so this is cosmetic for
        /// the compiler - but the generated script is meant to be read by a person, and
        /// "NOGRAVITY" next to "Radius(20)" reads like two languages in one file.
        /// </summary>
        private static string NormaliseFlagName(string name)
        {
            // Underscores are legal in DECORATE flag names and absent in the engine's, so
            // "NO_BLOCKMAP" and "NOBLOCKMAP" both become NoBlockmap.
            var clean = name.Replace("_", "");

            if (clean.Length == 0)
            {
                return clean;
            }

            var text = new StringBuilder(clean.Length);
            text.Append(char.ToUpperInvariant(clean[0]));

            for (var i = 1; i < clean.Length; i++)
            {
                text.Append(char.ToLowerInvariant(clean[i]));
            }

            return text.ToString();
        }

        // ------------------------------------------------------------ tokens

        private sealed class Token
        {
            public string Text;
            public bool IsNumber;
            public int Number;
            public int Line;
        }

        /// <summary>
        /// The whole lexer: words, numbers, quoted strings, braces and colons.
        ///
        /// A word ends at whitespace, a brace, a colon, a quote or the start of a comment,
        /// which is every separator DECORATE uses. Numbers are recognised by a leading digit
        /// or sign so that a doomednum can be told from an actor name, and hexadecimal is
        /// accepted because doomednums are often written that way.
        /// </summary>
        private sealed class Reader
        {
            private readonly string text;
            private int position;

            public Reader(string text)
            {
                this.text = text ?? "";
            }

            public Token Next(ref int line)
            {
                SkipTrivia(ref line);

                if (position >= text.Length)
                {
                    return null;
                }

                var token = new Token();
                token.Line = line;

                var c = text[position];

                if (c == '{' || c == '}' || c == ':')
                {
                    position++;
                    token.Text = c.ToString();
                    return token;
                }

                if (c == '"')
                {
                    position++;
                    var start = position;

                    while (position < text.Length && text[position] != '"')
                    {
                        if (text[position] == '\n')
                        {
                            line++;
                        }

                        position++;
                    }

                    token.Text = text.Substring(start, position - start);

                    if (position < text.Length)
                    {
                        position++;
                    }

                    return token;
                }

                var wordStart = position;

                // A sign belongs to the word only when a digit follows it, so "+SOLID" stays
                // a flag and "-5" stays a number.
                if ((c == '+' || c == '-') &&
                    position + 1 < text.Length && IsDigitOrDot(text[position + 1]))
                {
                    position++;
                }

                while (position < text.Length && !IsSeparator(text[position]))
                {
                    position++;
                }

                token.Text = text.Substring(wordStart, position - wordStart);

                if (token.Text.Length > 0 && (char.IsDigit(token.Text[0]) ||
                                              ((token.Text[0] == '-' || token.Text[0] == '+') &&
                                               token.Text.Length > 1)))
                {
                    int number;

                    if (TryParseNumber(token.Text, out number))
                    {
                        token.IsNumber = true;
                        token.Number = number;
                    }
                }

                return token;
            }

            private static bool TryParseNumber(string text, out int number)
            {
                number = 0;

                var body = text;
                var negative = false;

                if (body.Length > 0 && (body[0] == '-' || body[0] == '+'))
                {
                    negative = body[0] == '-';
                    body = body.Substring(1);
                }

                if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(body.Substring(2), NumberStyles.HexNumber,
                                      CultureInfo.InvariantCulture, out number))
                    {
                        return false;
                    }

                    if (negative)
                    {
                        number = -number;
                    }

                    return true;
                }

                if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    return false;
                }

                if (negative)
                {
                    number = -number;
                }

                return true;
            }

            private void SkipTrivia(ref int line)
            {
                while (position < text.Length)
                {
                    var c = text[position];

                    if (c == '\n')
                    {
                        line++;
                        position++;
                        continue;
                    }

                    if (char.IsWhiteSpace(c))
                    {
                        position++;
                        continue;
                    }

                    if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
                    {
                        while (position < text.Length && text[position] != '\n')
                        {
                            position++;
                        }

                        continue;
                    }

                    if (c == '/' && position + 1 < text.Length && text[position + 1] == '*')
                    {
                        position += 2;

                        while (position + 1 < text.Length &&
                               !(text[position] == '*' && text[position + 1] == '/'))
                        {
                            if (text[position] == '\n')
                            {
                                line++;
                            }

                            position++;
                        }

                        position = Math.Min(position + 2, text.Length);
                        continue;
                    }

                    return;
                }
            }

            private static bool IsSeparator(char c)
            {
                return char.IsWhiteSpace(c) || c == '{' || c == '}' || c == ':' || c == '"';
            }

            private static bool IsDigitOrDot(char c)
            {
                return char.IsDigit(c) || c == '.';
            }
        }
    }
}
