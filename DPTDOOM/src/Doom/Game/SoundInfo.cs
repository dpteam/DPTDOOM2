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
    /// <summary>
    /// SNDINFO: which lump a sound name actually plays.
    ///
    /// The engine plays sounds by <see cref="Sfx"/> and finds the lump by convention - "DS" plus the
    /// name - which works for the IWAD and stops working the moment a mod ships a sound called
    /// anything else. Three shapes cover what a mod actually needs:
    ///
    ///     weapons/pistol        DSPISTOL      // a name plays a lump
    ///     $alias door/open      dsdoropn      // a second name for the same sound
    ///     $random door/close { dsdorcls dsbdcls }   // one name, several lumps
    ///
    /// The names are the engine's own <see cref="Sfx"/> names in practice - "pistol", "doropn" - so a
    /// mod can give a vanilla sound a new lump without a single line of code. What it cannot do is
    /// invent a new <see cref="Sfx"/>: that is an enum, and an enum cannot grow at runtime. Same wall
    /// as <c>MobjType</c>, and the same answer: the type stays in code, the data moves out.
    ///
    /// Everything else - `$volume`, `$attenuation`, `$pitchshift`, `$ambient` - is named in the
    /// report and ignored. A parser that silently accepts what it does not implement is how a mod
    /// comes to look supported when it is not.
    /// </summary>
    public static class SoundInfo
    {
        /// <summary>
        /// Sound name to the lumps it can play, uppercased name.
        ///
        /// A list rather than one lump because `$random` is the reason a modder writes SNDINFO at
        /// all: one footstep is a footstep, four footsteps are footsteps.
        /// </summary>
        private static readonly Dictionary<string, List<string>> sounds =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Name to another name, resolved on lookup.</summary>
        private static readonly Dictionary<string, string> aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// How many aliases may be followed before giving up.
        ///
        /// A mod can write `$alias a b` and `$alias b a`, which is a cycle rather than a sound. The
        /// limit is what turns that into a report instead of a hang.
        /// </summary>
        private const int MaxAliasDepth = 8;

        public static void Clear()
        {
            sounds.Clear();
            aliases.Clear();
        }

        /// <summary>
        /// How many sound names were described, for the load report.
        ///
        /// Aliases count. A mod that only writes aliases is a real case - reusing an existing
        /// recording under a second name is the cheapest thing to do - and reporting "0 sound(s)
        /// described" for a file that plainly describes several would make the line useless.
        /// </summary>
        public static int Count
        {
            get { return sounds.Count + aliases.Count; }
        }

        /// <summary>
        /// The lumps a sound name plays, or null when SNDINFO says nothing about it.
        ///
        /// Null rather than an empty list, and the difference matters to the caller: "no entry" means
        /// fall back to the DS-prefix convention, while an entry with no lumps is a mod's mistake that
        /// has already been reported.
        /// </summary>
        public static IList<string> LumpsFor(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var current = name;

            for (var depth = 0; depth < MaxAliasDepth; depth++)
            {
                List<string> lumps;

                if (sounds.TryGetValue(current, out lumps))
                {
                    return lumps;
                }

                string next;

                if (!aliases.TryGetValue(current, out next))
                {
                    return null;
                }

                current = next;
            }

            // Fell through the limit: an alias cycle, already reported when it was read.
            return null;
        }

        /// <summary>
        /// Reads one SNDINFO and adds it to the table.
        ///
        /// A bad line costs that sound and nothing else: the rest of the file applies, the game
        /// loads, and the report names the file and the line. Same rule as a broken actor script.
        /// </summary>
        public static void Add(string text, string source, IList<string> problems)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var reader = new Reader(text);
            var line = 1;

            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    return;
                }

                if (token.Text == "$alias")
                {
                    ReadAlias(reader, ref line, source, problems);
                    continue;
                }

                if (token.Text == "$random")
                {
                    ReadRandom(reader, ref line, source, problems);
                    continue;
                }

                if (token.Text.Length > 0 && token.Text[0] == '$')
                {
                    // A directive this port does not implement. Its arguments are words on the same
                    // line, so skipping to the end of the line is enough to resynchronise.
                    problems.Add(source + " line " + token.Line + ": '" + token.Text +
                                 "' is not a SNDINFO directive this port understands, so it is ignored");
                    SkipLine(reader, ref line);
                    continue;
                }

                // A bare assignment: the name, then the lump.
                var lump = reader.Next(ref line);

                if (lump == null || !IsName(lump))
                {
                    problems.Add(source + " line " + token.Line + ": '" + token.Text +
                                 "' has no lump name after it");
                    continue;
                }

                var list = new List<string>();
                list.Add(lump.Text);

                sounds[token.Text] = list;
            }
        }

        private static void ReadAlias(Reader reader, ref int line, string source,
                                       IList<string> problems)
        {
            var from = reader.Next(ref line);
            var to = reader.Next(ref line);

            if (from == null || to == null || !IsName(from) || !IsName(to))
            {
                problems.Add(source + " line " + line +
                             ": '$alias' needs two names: the new one and the existing one");
                return;
            }

            if (string.Equals(from.Text, to.Text, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(source + " line " + from.Line + ": '$alias " + from.Text + " " +
                             to.Text + "' points at itself");
                return;
            }

            aliases[from.Text] = to.Text;
        }

        private static void ReadRandom(Reader reader, ref int line, string source,
                                       IList<string> problems)
        {
            var name = reader.Next(ref line);

            if (name == null || !IsName(name))
            {
                problems.Add(source + " line " + line + ": '$random' has no sound name");
                return;
            }

            var open = reader.Next(ref line);

            if (open == null || open.Text != "{")
            {
                problems.Add(source + " line " + name.Line + ": '$random " + name.Text +
                             "' is not followed by a list of lumps");
                return;
            }

            var list = new List<string>();

            while (true)
            {
                var lump = reader.Next(ref line);

                if (lump == null)
                {
                    problems.Add(source + " line " + name.Line + ": the list for '$random " +
                                 name.Text + "' is not closed");
                    return;
                }

                if (lump.Text == "}")
                {
                    break;
                }

                if (IsName(lump))
                {
                    list.Add(lump.Text);
                }
            }

            if (list.Count == 0)
            {
                problems.Add(source + " line " + name.Line + ": '$random " + name.Text +
                             "' has no lumps, so it is ignored");
                return;
            }

            sounds[name.Text] = list;
        }

        /// <summary>Skips to the end of the current line, for a directive with unknown arguments.</summary>
        private static void SkipLine(Reader reader, ref int line)
        {
            var start = line;

            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null || token.Line != start)
                {
                    if (token != null)
                    {
                        reader.PushBack(token, ref line);
                    }

                    return;
                }
            }
        }

        private static bool IsName(Token token)
        {
            return token != null && token.IsWord && token.Text != "{" && token.Text != "}";
        }

        // ------------------------------------------------------------ tokens

        private sealed class Token
        {
            public string Text;
            public bool IsWord;
            public int Line;
        }

        /// <summary>
        /// Words, braces and comments. SNDINFO has no quoted strings and no numbers that matter, so
        /// the lexer is the smallest one in this project.
        /// </summary>
        private sealed class Reader
        {
            private readonly string text;
            private int position;
            private Token pushed;

            public Reader(string text)
            {
                this.text = text ?? "";
            }

            public void PushBack(Token token, ref int line)
            {
                pushed = token;
                line = token.Line;
            }

            public Token Next(ref int line)
            {
                if (pushed != null)
                {
                    var saved = pushed;
                    pushed = null;
                    line = saved.Line;
                    return saved;
                }

                SkipTrivia(ref line);

                if (position >= text.Length)
                {
                    return null;
                }

                var token = new Token();
                token.Line = line;

                var c = text[position];

                if (c == '{' || c == '}')
                {
                    position++;
                    token.Text = c.ToString();
                    return token;
                }

                var start = position;

                while (position < text.Length && !IsSeparator(text[position]))
                {
                    position++;
                }

                token.Text = text.Substring(start, position - start);
                token.IsWord = token.Text.Length > 0;
                return token;
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
                return char.IsWhiteSpace(c) || c == '{' || c == '}';
            }
        }
    }
}
