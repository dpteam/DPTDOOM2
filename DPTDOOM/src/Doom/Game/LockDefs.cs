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
    /// One group of keys, any one of which satisfies this part of a lock.
    ///
    /// A lock is a **list** of groups and every group must be satisfied, which is the
    /// smallest shape that can express all of Boom's cases without special-casing them:
    /// a red card lock is one group of one, a "red card or red skull" lock is one group
    /// of two, "all three keys" is three groups of two, and "all six" is six groups of
    /// one.
    /// </summary>
    public sealed class LockKeyGroup
    {
        public readonly List<CardType> Keys = new List<CardType>();
    }

    /// <summary>A lock as a file describes it: what it needs, what it says, what it plays.</summary>
    public sealed class LockDefinition
    {
        public int Number;

        /// <summary>Every group must have at least one of its keys in hand.</summary>
        public readonly List<LockKeyGroup> Groups = new List<LockKeyGroup>();

        /// <summary>The refusal line, or null to use the generic one.</summary>
        public string Message;

        /// <summary>The sound played with the refusal.</summary>
        public Sfx Sound = Sfx.OOF;

        /// <summary>Where it was written, for a report a person can act on.</summary>
        public int Line;

        /// <summary>True when every group has a key the player holds.</summary>
        public bool IsSatisfied(bool[] cards)
        {
            if (cards == null)
            {
                return false;
            }

            foreach (var group in Groups)
            {
                var any = false;

                foreach (var key in group.Keys)
                {
                    if (cards[(int)key])
                    {
                        any = true;
                        break;
                    }
                }

                if (!any)
                {
                    return false;
                }
            }

            // No groups at all means no keys needed. That is a legitimate way to remove a
            // lock, and the loader says so rather than leaving the door to look locked.
            return true;
        }
    }

    /// <summary>
    /// LOCKDEFS: what a numbered lock needs, says and plays.
    ///
    /// The numbers here are the ones a **map** carries, and in this engine the only such
    /// number is Boom's key number on a generalized locked door - see
    /// <see cref="GeneralizedLock"/>. ZDoom's LOCKDEFS numbering belongs to Hexen-format
    /// maps, which this engine does not load, so a file written against those numbers
    /// redefines the same numbers here and means what it says: the file is authoritative,
    /// and the number is whatever the map put in its linedef.
    ///
    /// That is why the built-in definitions live in code and the file only overrides them:
    /// without a file, a Boom map works exactly as Boom intended, and with one, the mod
    /// gets the last word. `clearlocks` removes the built-in ones, and a lock number that
    /// then has no definition opens for anyone - refusing would make a door impassable for
    /// ever, which is worse than a door that does not lock.
    /// </summary>
    public static class LockDefs
    {
        private static readonly Dictionary<int, LockDefinition> definitions =
            new Dictionary<int, LockDefinition>();

        private static bool builtInsRemoved;

        /// <summary>True when a file said `clearlocks`.</summary>
        public static bool BuiltInsRemoved
        {
            get { return builtInsRemoved; }
        }

        /// <summary>How many locks the file defined, for the load report.</summary>
        public static int Count
        {
            get { return definitions.Count; }
        }

        public static void Clear()
        {
            definitions.Clear();
            builtInsRemoved = false;
        }

        /// <summary>The definition a file gave for this number, or null.</summary>
        public static LockDefinition Find(int number)
        {
            LockDefinition definition;

            return definitions.TryGetValue(number, out definition) ? definition : null;
        }

        /// <summary>
        /// Reads one LOCKDEFS and adds it to the table.
        ///
        /// A bad lock costs itself: the rest of the file applies, the game loads, and the
        /// report names the file and the line. Same rule as a broken actor script.
        /// </summary>
        public static void Add(string text, string source, IList<string> problems)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var reader = new Reader(text);

            while (true)
            {
                var token = reader.Next();

                if (token == null)
                {
                    return;
                }

                if (token.IsKeyword("clearlocks"))
                {
                    definitions.Clear();
                    builtInsRemoved = true;
                    continue;
                }

                if (token.IsKeyword("lock"))
                {
                    ReadLock(reader, source, problems);
                    continue;
                }

                if (token.IsKeyword("clear"))
                {
                    // ZDoom's spelling of the same thing. Both appear in real files.
                    definitions.Clear();
                    builtInsRemoved = true;
                    continue;
                }

                problems.Add(source + " line " + token.Line + ": '" + token.Text +
                             "' is not a LOCKDEFS keyword this port understands, so it is ignored");

                SkipEntry(reader);
            }
        }

        private static void ReadLock(Reader reader, string source, IList<string> problems)
        {
            var number = reader.Next();

            if (number == null || !number.IsNumber)
            {
                problems.Add(source + " line " + (number == null ? 0 : number.Line) +
                             ": 'lock' is followed by no number");
                SkipEntry(reader);
                return;
            }

            if (number.Value < 0 || number.Value > 255)
            {
                problems.Add(source + " line " + number.Line + ": lock number " + number.Value +
                             " is outside the range a lock can have, so the lock is ignored");
                SkipEntry(reader);
                return;
            }

            var definition = new LockDefinition();
            definition.Number = number.Value;
            definition.Line = number.Line;

            reader.SkipEquals();

            var open = reader.Next();

            if (open == null || open.Text != "{")
            {
                problems.Add(source + " line " + number.Line + ": lock " + number.Value +
                             " is not followed by a block, so it is ignored");
                SkipEntry(reader);
                return;
            }

            while (true)
            {
                var token = reader.Next();

                if (token == null)
                {
                    problems.Add(source + " line " + number.Line + ": the block for lock " +
                                 number.Value + " is not closed");
                    break;
                }

                if (token.Text == "}")
                {
                    break;
                }

                if (token.IsKeyword("message"))
                {
                    definition.Message = ReadText(reader, token, source, problems);
                    continue;
                }

                if (token.IsKeyword("lockedsound"))
                {
                    var name = ReadText(reader, token, source, problems);

                    if (name != null)
                    {
                        Sfx sound;

                        if (SfxNames.TryParse(name, out sound))
                        {
                            definition.Sound = sound;
                        }
                        else
                        {
                            problems.Add(source + " line " + token.Line + ": '" + name +
                                         "' is not a sound this port has, so the lock keeps the " +
                                         "default one");
                        }
                    }

                    continue;
                }

                if (token.IsKeyword("remotemessage"))
                {
                    // ZDoom shows a different line to the other players. This engine shows
                    // one line to the player who tried the door and sends nothing about it,
                    // so there is no second line to show. Named, not swallowed.
                    problems.Add(source + " line " + token.Line + ": 'remotemessage' has no " +
                                 "meaning here - the refusal is shown only to the player who " +
                                 "tried the door - so it is ignored");

                    reader.SkipValues(1);
                    continue;
                }

                if (token.IsKeyword("mapcolor"))
                {
                    // The automap draws one colour for every line, so a per-lock colour would
                    // be accepted and never seen.
                    problems.Add(source + " line " + token.Line + ": 'mapcolor' is ignored - " +
                                 "this port's automap does not colour lines");

                    // Three numbers, not one. Skipping a single value left the other two to be
                    // read as key names, and "0 is not a key" three times is what that looks
                    // like in the report.
                    reader.SkipValues(3);
                    continue;
                }

                if (token.IsKeyword("any"))
                {
                    reader.SkipEquals();
                    ReadAnyGroup(reader, definition, token, source, problems);
                    continue;
                }

                if (token.IsWord)
                {
                    ReadKey(reader, definition, token, source, problems);
                    continue;
                }

                problems.Add(source + " line " + token.Line + ": '" + token.Text +
                             "' inside lock " + number.Value + " is not a key or a directive");
            }

            if (definition.Groups.Count == 0)
            {
                // Not refused: removing a lock is a thing a mod may want. But it is worth one
                // line, because a lock that quietly needs nothing is exactly the failure this
                // whole class exists to prevent.
                problems.Add(source + " line " + number.Line + ": lock " + number.Value +
                             " needs no keys, so its door opens for anyone");
            }

            definitions[definition.Number] = definition;
        }

        /// <summary>`any { a b c }`: one group, satisfied by any one of the keys.</summary>
        private static void ReadAnyGroup(Reader reader, LockDefinition definition, Token keyword,
                                         string source, IList<string> problems)
        {
            var open = reader.Next();

            if (open == null || open.Text != "{")
            {
                problems.Add(source + " line " + keyword.Line +
                             ": 'any' is not followed by a block of keys");
                return;
            }

            var group = new LockKeyGroup();

            while (true)
            {
                var token = reader.Next();

                if (token == null)
                {
                    problems.Add(source + " line " + keyword.Line +
                                 ": the block of keys after 'any' is not closed");
                    break;
                }

                if (token.Text == "}")
                {
                    break;
                }

                if (!AddKey(group, token, source, problems))
                {
                    continue;
                }
            }

            if (group.Keys.Count == 0)
            {
                problems.Add(source + " line " + keyword.Line +
                             ": 'any' names no keys, so it is ignored");
                return;
            }

            definition.Groups.Add(group);
        }

        /// <summary>A bare key name is its own group: every one of them is needed.</summary>
        private static void ReadKey(Reader reader, LockDefinition definition, Token token,
                                    string source, IList<string> problems)
        {
            var group = new LockKeyGroup();

            if (AddKey(group, token, source, problems))
            {
                definition.Groups.Add(group);
            }
        }

        /// <summary>
        /// Adds a key name to a group. `AnyBlue` and its siblings add both the card and the
        /// skull, which is what makes them worth having: one word for "either blue key".
        /// </summary>
        private static bool AddKey(LockKeyGroup group, Token token, string source,
                                   IList<string> problems)
        {
            CardType kind;

            if (KeyNames.TryParse(token.Text, out kind))
            {
                group.Keys.Add(kind);
                return true;
            }

            if (string.Equals(token.Text, "anyblue", StringComparison.OrdinalIgnoreCase))
            {
                group.Keys.Add(CardType.BlueCard);
                group.Keys.Add(CardType.BlueSkull);
                return true;
            }

            if (string.Equals(token.Text, "anyyellow", StringComparison.OrdinalIgnoreCase))
            {
                group.Keys.Add(CardType.YellowCard);
                group.Keys.Add(CardType.YellowSkull);
                return true;
            }

            if (string.Equals(token.Text, "anyred", StringComparison.OrdinalIgnoreCase))
            {
                group.Keys.Add(CardType.RedCard);
                group.Keys.Add(CardType.RedSkull);
                return true;
            }

            problems.Add(source + " line " + token.Line + ": '" + token.Text +
                         "' is not a key this port knows, so it is ignored");
            return false;
        }

        /// <summary>Reads the value of `message` or `lockedsound`, allowing `key = value`.</summary>
        private static string ReadText(Reader reader, Token keyword, string source,
                                       IList<string> problems)
        {
            reader.SkipEquals();

            var value = reader.Next();

            if (value == null || value.Text == "}" || value.Text == "{")
            {
                problems.Add(source + " line " + keyword.Line + ": '" + keyword.Text +
                             "' has no value");

                if (value != null)
                {
                    reader.PushBack(value);
                }

                return null;
            }

            return value.Text;
        }

        /// <summary>
        /// Skips an entry this port does not implement, including its block.
        ///
        /// Stops at the next `lock` or `clearlocks` rather than at a line boundary, because
        /// the two things being skipped look nothing alike and the reader has already moved
        /// past the line the keyword was on.
        /// </summary>
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

                if (depth == 0 && (token.IsKeyword("lock") || token.IsKeyword("clearlocks") ||
                                   token.IsKeyword("clear")))
                {
                    reader.PushBack(token);
                    return;
                }
            }
        }

        // ------------------------------------------------------------ names

        /// <summary>Key names, as LOCKDEFS writes them.</summary>
        private static class KeyNames
        {
            public static bool TryParse(string name, out CardType kind)
            {
                switch (name.ToLowerInvariant())
                {
                    case "bluecard": kind = CardType.BlueCard; return true;
                    case "yellowcard": kind = CardType.YellowCard; return true;
                    case "redcard": kind = CardType.RedCard; return true;
                    case "blueskull": kind = CardType.BlueSkull; return true;
                    case "yellowskull": kind = CardType.YellowSkull; return true;
                    case "redskull": kind = CardType.RedSkull; return true;
                }

                kind = CardType.BlueCard;
                return false;
            }
        }

        /// <summary>
        /// Sound names, by the names `Sfx` uses.
        ///
        /// Matched against the enum's names rather than parsed as a number: `Enum.TryParse`
        /// accepts "5" and hands back the fifth sound, which would let a typo play the wrong
        /// noise instead of reporting it.
        /// </summary>
        private static class SfxNames
        {
            private static readonly Dictionary<string, Sfx> table = Build();

            public static bool TryParse(string name, out Sfx sound)
            {
                return table.TryGetValue(name.ToLowerInvariant(), out sound);
            }

            private static Dictionary<string, Sfx> Build()
            {
                var map = new Dictionary<string, Sfx>();

                foreach (Sfx sound in Enum.GetValues(typeof(Sfx)))
                {
                    map[sound.ToString().ToLowerInvariant()] = sound;
                }

                return map;
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
                return IsWord && !Quoted && string.Equals(Text, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Words, numbers, braces and quoted strings.
        ///
        /// Quotes are what make this reader different from the ANIMDEFS one: a lock's message
        /// is a sentence with spaces in it, and a tokenizer that split it would leave the mod
        /// with the first word only.
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

            /// <summary>Accepts `key = value` as well as `key value`.</summary>
            public void SkipEquals()
            {
                var token = Next();

                if (token == null)
                {
                    return;
                }

                if (token.Text != "=")
                {
                    PushBack(token);
                }
            }

            /// <summary>
            /// Skips the values after a directive this port does not implement.
            ///
            /// The count is not decoration: `mapcolor 255 0 0` has three, and skipping one leaves
            /// the other two to be read as key names.
            /// </summary>
            public void SkipValues(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    var token = Next();

                    if (token == null)
                    {
                        return;
                    }

                    if (token.Text == "}" || token.Text == "{" || token.IsKeyword("lock"))
                    {
                        PushBack(token);
                        return;
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

                if (c == '{' || c == '}' || c == '=' || c == '(' || c == ')')
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
                return char.IsWhiteSpace(c) || c == '{' || c == '}' || c == '=' ||
                       c == '"' || c == '(' || c == ')';
            }
        }
    }
}
