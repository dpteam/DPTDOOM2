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
    /// <summary>One ANIMDEFS cycle: a list of pictures, each with its own duration.</summary>
    public sealed class AnimatedDefinition
    {
        /// <summary>Whether the frames are wall textures or flats.</summary>
        public bool IsTexture;

        /// <summary>Picture names, in the order they are shown.</summary>
        public readonly List<string> Frames = new List<string>();

        /// <summary>Tics each frame lasts, parallel to <see cref="Frames"/>.</summary>
        public readonly List<int> Durations = new List<int>();

        /// <summary>Where it was written, for a report a person can act on.</summary>
        public int Line;
    }

    /// <summary>
    /// ANIMDEFS: which textures and flats animate, in which order, for how long.
    ///
    /// The engine's own ANIMATED lump describes an animation as a **range**: "pictures 12 to 15, at
    /// this speed". ANIMDEFS describes a **list**: any pictures, in any order, each with its own
    /// duration. The list is what a mod needs - a lamp that flickers through three frames taken from
    /// elsewhere in the WAD cannot be written as a range at all - so this reads lists and hands them
    /// to the animation table, which grew a list shape for exactly this reason.
    ///
    /// Two forms are supported, and they are the two that appear in real files:
    ///
    ///     texture BLODGR1
    ///     {
    ///         pic BLODGR1 tics 8
    ///         pic BLODGR4 tics 8
    ///     }
    ///
    ///     flat NUKAGE1
    ///     {
    ///         pic NUKAGE1 tics 8
    ///         pic NUKAGE3 tics 8
    ///     }
    ///
    /// `tics` is optional and defaults to one, which is what ZDoom does. Everything else - `warp`,
    /// `warp2`, `cameratexture`, `animateddoor`, `switch` - is named in the report and ignored: a
    /// parser that silently accepts what it does not implement is how a mod comes to look supported
    /// when it is not.
    /// </summary>
    public static class AnimatedTexture
    {
        private static readonly List<AnimatedDefinition> definitions =
            new List<AnimatedDefinition>();

        /// <summary>Every cycle described, in the order it was written.</summary>
        public static IList<AnimatedDefinition> Definitions
        {
            get { return definitions; }
        }

        public static void Clear()
        {
            definitions.Clear();
        }

        /// <summary>How many cycles were described, for the load report.</summary>
        public static int Count
        {
            get { return definitions.Count; }
        }

        /// <summary>
        /// Reads one ANIMDEFS and adds it to the table.
        ///
        /// A bad cycle costs itself: the rest of the file applies, the game loads, and the report names
        /// the file and the line. Same rule as a broken actor script.
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

                var isTexture = string.Equals(token.Text, "texture", StringComparison.OrdinalIgnoreCase);
                var isFlat = string.Equals(token.Text, "flat", StringComparison.OrdinalIgnoreCase);

                if (!isTexture && !isFlat)
                {
                    if (token.IsWord)
                    {
                        problems.Add(source + " line " + token.Line + ": '" + token.Text +
                                     "' is not an ANIMDEFS keyword this port understands, so it is " +
                                     "ignored");
                        SkipEntry(reader, ref line);
                    }

                    continue;
                }

                ReadCycle(reader, ref line, isTexture, source, problems);
            }
        }

        private static void ReadCycle(Reader reader, ref int line, bool isTexture, string source,
                                      IList<string> problems)
        {
            var name = reader.Next(ref line);

            if (name == null || !IsName(name))
            {
                problems.Add(source + " line " + line + ": '" +
                             (isTexture ? "texture" : "flat") + "' is followed by no name");
                return;
            }

            var open = reader.Next(ref line);

            if (open == null || open.Text != "{")
            {
                problems.Add(source + " line " + name.Line + ": '" + name.Text +
                             "' is not followed by a block, so it is ignored");
                return;
            }

            var definition = new AnimatedDefinition();
            definition.IsTexture = isTexture;
            definition.Line = name.Line;

            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    problems.Add(source + " line " + name.Line + ": the block for '" + name.Text +
                                 "' is not closed");
                    break;
                }

                if (token.Text == "}")
                {
                    break;
                }

                if (!string.Equals(token.Text, "pic", StringComparison.OrdinalIgnoreCase))
                {
                    // A directive this port does not implement - warp, cameratexture, switch. Named,
                    // and its arguments skipped so the rest of the block still reads.
                    if (token.IsWord)
                    {
                        problems.Add(source + " line " + token.Line + ": '" + token.Text +
                                     "' in '" + name.Text +
                                     "' is not supported by this port, so it is ignored");
                        SkipToEndOfLine(reader, ref line);
                    }

                    continue;
                }

                var frame = reader.Next(ref line);

                if (frame == null || !IsName(frame))
                {
                    problems.Add(source + " line " + token.Line + ": 'pic' has no picture name");
                    continue;
                }

                var duration = 1;

                // "tics 8" is optional and follows the name. Peeked rather than assumed, because a
                // frame without a duration is the common case and must not swallow the next 'pic'.
                var maybeTics = reader.Next(ref line);

                if (maybeTics != null &&
                    string.Equals(maybeTics.Text, "tics", StringComparison.OrdinalIgnoreCase))
                {
                    var amount = reader.Next(ref line);

                    if (amount == null || !amount.IsNumber || amount.Value < 1)
                    {
                        problems.Add(source + " line " + maybeTics.Line +
                                     ": 'tics' needs a whole number of at least one");
                        duration = 1;
                    }
                    else
                    {
                        duration = amount.Value;
                    }
                }
                else if (maybeTics != null)
                {
                    reader.PushBack(maybeTics, ref line);
                }

                definition.Frames.Add(frame.Text);
                definition.Durations.Add(duration);
            }

            if (definition.Frames.Count < 2)
            {
                // A cycle of one frame does not cycle. Saying so is better than adding it and letting
                // the renderer redraw the same picture for ever.
                problems.Add(source + " line " + name.Line + ": '" + name.Text +
                             "' has fewer than two pictures, so it does not animate");
                return;
            }

            definitions.Add(definition);
        }

        /// <summary>
        /// Skips an entry this port does not implement.
        ///
        /// Stops at the next <c>texture</c> or <c>flat</c> rather than at a line boundary, because the
        /// two things being skipped look nothing alike: `switch` is a few words on one line, `warp` is a
        /// block of pictures. Comparing line numbers - the first attempt - silently swallowed the entry
        /// after the block, because the reader had already moved past it.
        /// </summary>
        private static void SkipEntry(Reader reader, ref int line)
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
                    continue;
                }

                if (token.Text == "}")
                {
                    depth--;

                    if (depth <= 0)
                    {
                        return;
                    }

                    continue;
                }

                if (depth == 0 && token.IsWord &&
                    (string.Equals(token.Text, "texture", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(token.Text, "flat", StringComparison.OrdinalIgnoreCase)))
                {
                    reader.PushBack(token, ref line);
                    return;
                }
            }
        }

        private static void SkipToEndOfLine(Reader reader, ref int line)
        {
            var start = line;

            while (true)
            {
                var token = reader.Next(ref line);

                if (token == null)
                {
                    return;
                }

                if (token.Line != start)
                {
                    reader.PushBack(token, ref line);
                    return;
                }

                if (token.Text == "}")
                {
                    reader.PushBack(token, ref line);
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
            public bool IsNumber;
            public int Value;
            public int Line;
        }

        /// <summary>
        /// Words, numbers, braces and comments.
        ///
        /// Numbers are recognised because `tics 8` needs one, and a word that happens to be all digits
        /// is a duration rather than a picture name - no texture is called "8".
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

                int value;

                if (token.IsWord && int.TryParse(token.Text, out value))
                {
                    token.IsNumber = true;
                    token.Value = value;
                }

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
                return char.IsWhiteSpace(c) || c == '{' || c == '}' || c == '"';
            }
        }
    }
}
