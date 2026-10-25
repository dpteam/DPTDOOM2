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
    /// <summary>One patch placed in a composite texture, at a position inside it.</summary>
    public sealed class TexturePatchDefinition
    {
        public string Name;
        public int X;
        public int Y;
        public int Line;
    }

    /// <summary>A texture described in a TEXTURES file.</summary>
    public sealed class TextureDefinition
    {
        public string Name;

        /// <summary>Width in pixels, or -1 to take it from the patches.</summary>
        public int Width = -1;

        /// <summary>Height in pixels, or -1 to take it from the patches.</summary>
        public int Height = -1;

        public readonly List<TexturePatchDefinition> Patches = new List<TexturePatchDefinition>();

        public int Line;
    }

    /// <summary>
    /// ZDoom TEXTURES: composite textures built out of patches, described in text.
    ///
    /// This is the same thing the WAD's TEXTURE1 lump does in binary - a texture is a list of
    /// patches at positions - written in a form a person can edit. It matters because the
    /// binary form cannot express a texture larger than its patches suggest, cannot be added
    /// without a texture editor, and cannot be shipped as one small text file in a PK3.
    ///
    /// **What is supported** is the part without which a definition produces the wrong picture:
    ///
    ///     Texture BIGDOOR, 128, 64
    ///     {
    ///         Patch DOOR2_1, 0, 0
    ///         Patch DOOR2_2, 64, 0
    ///     }
    ///
    /// Width and height may be left out, in which case they are the extent of the patches.
    ///
    /// **What is not** - `XScale`, `YScale`, `Blend`, `Alpha`, `Rotate`, `FlipX`, `FlipY`,
    /// `Canvas`, `NullTexture`, `Flat` - is named in the load report rather than accepted. The
    /// renderer draws textures at one scale, with no blending and no rotation, so a texture that
    /// claimed to be scaled would be a texture that lies about itself. `Flat` is separate
    /// because floors here are a different pipeline with their own size assumption.
    /// </summary>
    public static class TextureDefinitions
    {
        /// <summary>Reads every definition in one TEXTURES text.</summary>
        public static List<TextureDefinition> Parse(string text, string source, IList<string> problems)
        {
            var definitions = new List<TextureDefinition>();

            if (string.IsNullOrEmpty(text))
            {
                return definitions;
            }

            var reader = new Reader(text);

            while (true)
            {
                var token = reader.Next();

                if (token == null)
                {
                    return definitions;
                }

                if (token.IsKeyword("texture") || token.IsKeyword("walltexture"))
                {
                    ReadTexture(reader, definitions, source, problems, token);
                    continue;
                }

                if (token.IsKeyword("flat"))
                {
                    // Floors are raw 64x64 pictures in this engine, not compositions of patches,
                    // so a Flat block would have nowhere to go.
                    problems.Add(source + " line " + token.Line + ": 'flat' is not supported - " +
                                 "floors in this port are not built from patches, so the " +
                                 "definition is ignored");

                    SkipEntry(reader);
                    continue;
                }

                if (token.IsWord)
                {
                    problems.Add(source + " line " + token.Line + ": '" + token.Text +
                                 "' is not a TEXTURES keyword this port understands, so it is " +
                                 "ignored");

                    SkipEntry(reader);
                }
            }
        }

        private static void ReadTexture(Reader reader, List<TextureDefinition> definitions,
                                        string source, IList<string> problems, Token keyword)
        {
            var name = reader.Next();

            if (name == null || !name.IsWord)
            {
                problems.Add(source + " line " + keyword.Line +
                             ": 'texture' is followed by no name");
                return;
            }

            var definition = new TextureDefinition();
            definition.Name = name.Text.ToUpperInvariant();
            definition.Line = name.Line;

            reader.SkipComma();

            var width = reader.Next();

            if (width != null && width.IsNumber)
            {
                definition.Width = width.Value;
                reader.SkipComma();

                var height = reader.Next();

                if (height != null && height.IsNumber)
                {
                    definition.Height = height.Value;
                }
                else if (height != null)
                {
                    reader.PushBack(height);
                }

                reader.SkipComma();
            }
            else if (width != null)
            {
                reader.PushBack(width);
            }

            var open = reader.Next();

            if (open == null || open.Text != "{")
            {
                problems.Add(source + " line " + name.Line + ": texture '" + name.Text +
                             "' is not followed by a block, so it is ignored");
                return;
            }

            while (true)
            {
                var token = reader.Next();

                if (token == null)
                {
                    problems.Add(source + " line " + name.Line + ": the block for texture '" +
                                 name.Text + "' is not closed");
                    break;
                }

                if (token.Text == "}")
                {
                    break;
                }

                if (token.IsKeyword("patch"))
                {
                    ReadPatch(reader, definition, source, problems, token);
                    continue;
                }

                if (token.IsWord)
                {
                    problems.Add(source + " line " + token.Line + ": '" + token.Text +
                                 "' in texture '" + name.Text +
                                 "' is not supported by this port, so it is ignored");

                    reader.SkipToEndOfLine(token.Line);
                }
            }

            if (definition.Patches.Count == 0)
            {
                // A texture with no patches is an empty rectangle: it would render as nothing at
                // all, and the map that uses it would look like it had a hole in it.
                problems.Add(source + " line " + name.Line + ": texture '" + name.Text +
                             "' has no patches, so it is ignored");
                return;
            }

            definitions.Add(definition);
        }

        private static void ReadPatch(Reader reader, TextureDefinition definition, string source,
                                      IList<string> problems, Token keyword)
        {
            var name = reader.Next();

            if (name == null || !name.IsWord)
            {
                problems.Add(source + " line " + keyword.Line + ": 'patch' has no name");
                return;
            }

            var patch = new TexturePatchDefinition();
            patch.Name = name.Text.ToUpperInvariant();
            patch.Line = name.Line;

            reader.SkipComma();

            var x = reader.Next();

            if (x != null && x.IsNumber)
            {
                patch.X = x.Value;
                reader.SkipComma();

                var y = reader.Next();

                if (y != null && y.IsNumber)
                {
                    patch.Y = y.Value;
                }
                else if (y != null)
                {
                    reader.PushBack(y);
                }
            }
            else if (x != null)
            {
                reader.PushBack(x);
            }

            // Options this port does not implement - Rotate, FlipX, Blend, Alpha. Two spellings
            // exist in real files: the rest of the line, and a block. Both are named once for
            // the patch and then skipped, so the next Patch still reads.
            var extra = reader.SkipToEndOfLine(patch.Line);

            if (extra == null)
            {
                extra = reader.SkipBlock();
            }

            if (extra != null)
            {
                problems.Add(source + " line " + patch.Line + ": '" + extra +
                             "' on patch '" + name.Text +
                             "' is not supported by this port, so the patch is used without it");
            }

            definition.Patches.Add(patch);
        }

        /// <summary>Skips a whole entry this port does not implement, including its block.</summary>
        private static void SkipEntry(Reader reader)
        {
            var depth = 0;

            while (true)
            {
                var token = reader.Next();

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

                if (depth == 0 && (token.IsKeyword("texture") || token.IsKeyword("walltexture") ||
                                   token.IsKeyword("flat")))
                {
                    reader.PushBack(token);
                    return;
                }
            }
        }

        // ------------------------------------------------------------ tokens

        private sealed class Token
        {
            public string Text;
            public bool IsWord;
            public bool IsNumber;
            public bool Quoted;
            public int Value;
            public int Line;

            /// <summary>True when this is the given keyword, unquoted and in any case.</summary>
            public bool IsKeyword(string text)
            {
                return IsWord && !Quoted &&
                       string.Equals(Text, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Words, numbers, commas, braces and quoted strings.
        ///
        /// Commas are optional between values: ZDoom's own examples always write them, but a
        /// hand-written file that leaves one out is unambiguous, and refusing it would be a
        /// refusal about punctuation rather than about the texture.
        /// </summary>
        private sealed class Reader
        {
            private readonly string text;
            private int position;
            private int line = 1;
            private Token pushed;

            public Reader(string text)
            {
                this.text = text ?? "";
            }

            public void PushBack(Token token)
            {
                pushed = token;
            }

            public void SkipComma()
            {
                var token = Next();

                if (token == null)
                {
                    return;
                }

                if (token.Text != ",")
                {
                    PushBack(token);
                }
            }

            /// <summary>
            /// Skips the rest of a line and returns the first word skipped, or null.
            ///
            /// The first word is what gets reported: it is the option's name, and naming all of
            /// them would turn one unsupported `Rotate` into a paragraph.
            /// </summary>
            public string SkipToEndOfLine(int line)
            {
                string first = null;

                while (true)
                {
                    var token = Next();

                    if (token == null)
                    {
                        return first;
                    }

                    if (token.Line != line || token.Text == "}" || token.Text == "{")
                    {
                        PushBack(token);
                        return first;
                    }

                    if (first == null && token.IsWord)
                    {
                        first = token.Text;
                    }
                }
            }

            /// <summary>
            /// Skips a block if the next token opens one, and returns the first word inside.
            ///
            /// The block form is how a patch carries several options at once
            /// (`Patch P { Rotate 90 FlipX }`), and it is common enough in real files that
            /// treating the opening brace as a texture-level brace would end the texture early.
            /// </summary>
            public string SkipBlock()
            {
                var token = Next();

                if (token == null)
                {
                    return null;
                }

                if (token.Text != "{")
                {
                    PushBack(token);
                    return null;
                }

                var depth = 1;
                string first = null;

                while (true)
                {
                    var inner = Next();

                    if (inner == null)
                    {
                        return first;
                    }

                    if (inner.Text == "{")
                    {
                        depth++;
                        continue;
                    }

                    if (inner.Text == "}")
                    {
                        depth--;

                        if (depth == 0)
                        {
                            return first;
                        }

                        continue;
                    }

                    if (first == null && inner.IsWord)
                    {
                        first = inner.Text;
                    }
                }
            }

            public Token Next()
            {
                if (pushed != null)
                {
                    var saved = pushed;
                    pushed = null;
                    return saved;
                }

                SkipTrivia();

                if (position >= text.Length)
                {
                    return null;
                }

                var token = new Token();
                token.Line = line;

                var c = text[position];

                if (c == '{' || c == '}' || c == ',' || c == '=')
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
                    token.Quoted = true;
                    token.IsWord = true;

                    if (position < text.Length)
                    {
                        position++;
                    }

                    return token;
                }

                var wordStart = position;

                if ((c == '-' || c == '+') && position + 1 < text.Length &&
                    char.IsDigit(text[position + 1]))
                {
                    position++;
                }

                while (position < text.Length && !IsSeparator(text[position]))
                {
                    position++;
                }

                token.Text = text.Substring(wordStart, position - wordStart);
                token.IsWord = token.Text.Length > 0;

                int value;

                if (token.IsWord && int.TryParse(token.Text, out value))
                {
                    token.IsNumber = true;
                    token.Value = value;
                }

                return token;
            }

            private void SkipTrivia()
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
                return char.IsWhiteSpace(c) || c == '{' || c == '}' || c == ',' || c == '=';
            }
        }
    }
}
