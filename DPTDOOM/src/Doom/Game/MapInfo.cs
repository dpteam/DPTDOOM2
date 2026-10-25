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
using System.Text;

namespace DPTDOOM
{
    /// <summary>What a MAPINFO entry can change about one level.</summary>
    public sealed class MapInfoEntry
    {
        /// <summary>The map lump this entry describes: "MAP01", "E1M1".</summary>
        public string Map;

        /// <summary>The name shown on the automap and the intermission, or null.</summary>
        public string Title;

        /// <summary>The sky texture, or null to keep the engine's own choice.</summary>
        public string SkyTexture;

        /// <summary>The music lump, with or without the "D_" prefix, or null.</summary>
        public string MusicLump;
    }

    /// <summary>
    /// Reads MAPINFO, the lump that describes levels rather than actors.
    ///
    /// A deliberately small subset of what ZDoom accepts, and the subset is chosen the same way
    /// the DECORATE one was: by what a PWAD actually needs to stop looking wrong. Three things:
    ///
    ///  - **the title**, because the automap says "level 1: entryway" on a map that is not
    ///    entryway, and the intermission says it too;
    ///  - **the sky**, because a map set at night with the engine's default sky is a map that
    ///    looks unfinished;
    ///  - **the music**, because a custom track is the first thing a mod ships and the last thing
    ///    the engine would play.
    ///
    /// Everything else - cluster definitions, level flags, map-wide fog - is refused by being
    /// ignored, and the caller reports what was skipped. That is not laziness: a parser that
    /// silently accepts what it does not implement produces a mod that looks supported and is
    /// not, which is the failure this project keeps designing against.
    ///
    /// The grammar is three shapes and no more:
    ///
    ///     map MAP01 "The Hangar"
    ///     {
    ///         sky1 = "SKY2"
    ///         music = "D_E1M2"
    ///     }
    ///
    /// A one-line `map MAP01 "Name"` with no block is legal and common.
    /// </summary>
    public static class MapInfo
    {
        /// <summary>
        /// The parsed entries by map lump name, uppercased.
        ///
        /// Static because a level is built in a dozen places - the game, a demo playback, the
        /// attract sequence - and threading a lookup through all of them would mean changing
        /// every constructor. It is set once, at content load, before any level exists.
        /// </summary>
        private static readonly Dictionary<string, MapInfoEntry> entries =
            new Dictionary<string, MapInfoEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Clears the table. Called at the start of a load, like LoadReport.Clear.</summary>
        public static void Clear()
        {
            entries.Clear();
        }

        /// <summary>The entry for a map, or null.</summary>
        public static MapInfoEntry For(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
            {
                return null;
            }

            MapInfoEntry entry;

            return entries.TryGetValue(mapName, out entry) ? entry : null;
        }

        /// <summary>How many maps were described, for the load report.</summary>
        public static int Count
        {
            get { return entries.Count; }
        }

        /// <summary>
        /// Reads one MAPINFO and adds it to the table.
        ///
        /// A malformed entry costs that map's overrides and nothing else: the level still loads
        /// with the engine's own title, sky and music, and the problem names the line. Same rule
        /// as a broken actor script, for the same reason - one typo must not take the game down.
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

                if (!string.Equals(token.Text, "map", StringComparison.OrdinalIgnoreCase))
                {
                    // Anything else - "cluster", "defaultmap", "clearepisodes" - is a keyword
                    // this port does not implement. Named rather than silently skipped, because
                    // a mod author needs to know which of their settings did nothing.
                    if (token.IsWord)
                    {
                        problems.Add(source + " line " + token.Line + ": '" + token.Text +
                                     "' is not a MAPINFO keyword this port understands, so it is " +
                                     "ignored");
                        SkipToNextMap(reader, ref line);
                    }

                    continue;
                }

                var entry = new MapInfoEntry();
                var mapLine = token.Line;

                var name = reader.Next(ref line);

                if (name == null || !name.IsWord)
                {
                    problems.Add(source + " line " + mapLine + ": 'map' is followed by no map name");
                    return;
                }

                entry.Map = name.Text.ToUpperInvariant();

                // The title is optional, and its absence is meaningful: an entry that only sets
                // the sky must not blank the name.
                var next = reader.Next(ref line);

                if (next != null && next.IsQuoted)
                {
                    entry.Title = next.Text;
                    next = reader.Next(ref line);
                }

                if (next != null && next.Text == "{")
                {
                    ReadBlock(reader, ref line, entry, source, problems);
                }
                else if (next != null)
                {
                    // Not a brace and not a title: put it back for the outer loop, which will
                    // report it if it is a keyword.
                    reader.PushBack(next, ref line);
                }

                entries[entry.Map] = entry;
            }
        }

        /// <summary>
        /// Skips the rest of an entry this port does not implement.
        ///
        /// Needed because an unknown keyword usually owns a block, and reading that block as if it
        /// were top level would produce a complaint per line - forty lines of noise for one
        /// unsupported feature.
        /// </summary>
        private static void SkipToNextMap(Reader reader, ref int line)
        {
            var depth = 0;

            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    return;
                }

                if (token.Text == "{")
                {
                    depth++;
                }
                else if (token.Text == "}")
                {
                    if (depth == 0)
                    {
                        return;
                    }

                    depth--;
                }
                else if (depth == 0 && string.Equals(token.Text, "map", StringComparison.OrdinalIgnoreCase))
                {
                    reader.PushBack(token, ref line);
                    return;
                }
            }
        }

        private static void ReadBlock(Reader reader, ref int line, MapInfoEntry entry,
                                      string source, IList<string> problems)
        {
            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    problems.Add(source + " line " + entry.Map + ": the block is not closed");
                    return;
                }

                if (token.Text == "}")
                {
                    return;
                }

                if (!token.IsWord)
                {
                    continue;
                }

                var property = token.Text.ToLowerInvariant();

                // The '=' is optional in ZDoom and both spellings appear in the wild.
                var value = reader.Next(ref line);

                if (value != null && value.Text == "=")
                {
                    value = reader.Next(ref line);
                }

                if (value == null)
                {
                    return;
                }

                // The block's closing brace where a value should be: a value is missing, and the
                // brace has to go back so the block still ends where it says it does. Reading it
                // as the value instead produced two problems for one mistake - "needs a name" and
                // then "the block is not closed".
                if (value.Text == "}")
                {
                    problems.Add(source + " line " + token.Line + ": '" + property + "' in " +
                                 entry.Map + " has no value");
                    reader.PushBack(value, ref line);
                    continue;
                }

                switch (property)
                {
                    case "sky1":
                    case "sky":
                        entry.SkyTexture = RequireName(value, entry, source, problems, property);
                        break;

                    case "music":
                        entry.MusicLump = RequireName(value, entry, source, problems, property);
                        break;

                    default:
                        problems.Add(source + " line " + token.Line + ": '" + property +
                                     "' in " + entry.Map +
                                     " is not supported by this port, so it is ignored");
                        break;
                }
            }
        }

        /// <summary>
        /// A property value as a bare name.
        ///
        /// Quoted or not: ZDoom accepts `sky1 = SKY2` and `sky1 = "SKY2"`, and files contain both.
        /// A missing value is reported rather than stored as empty, because an empty sky name
        /// would make the level fail to find a texture and report that instead - a message about
        /// the symptom rather than the cause.
        /// </summary>
        private static string RequireName(Token value, MapInfoEntry entry, string source,
                                          IList<string> problems, string property)
        {
            if (!value.IsWord && !value.IsQuoted)
            {
                problems.Add(source + " line " + value.Line + ": '" + property + "' in " +
                             entry.Map + " needs a name");
                return null;
            }

            return value.Text;
        }

        // ------------------------------------------------------------ tokens

        private sealed class Token
        {
            public string Text;
            public bool IsWord;
            public bool IsQuoted;
            public int Line;
        }

        /// <summary>
        /// The whole lexer: words, quoted strings, braces, equals and comments.
        ///
        /// Small enough to read in one sitting, which is the point: MAPINFO's grammar is words,
        /// quoted names and two kinds of bracket, and a parser generator would be more machinery
        /// than grammar.
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

                if (c == '{' || c == '}' || c == '=')
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
                    token.IsQuoted = true;

                    if (position < text.Length)
                    {
                        position++;
                    }

                    return token;
                }

                var wordStart = position;

                while (position < text.Length && !IsSeparator(text[position]))
                {
                    position++;
                }

                token.Text = text.Substring(wordStart, position - wordStart);
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
                return char.IsWhiteSpace(c) || c == '{' || c == '}' || c == '=' || c == '"';
            }
        }
    }
}
