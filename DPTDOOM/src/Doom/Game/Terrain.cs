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
    /// <summary>What a splash sounds like. The particles need actors this engine has no sprites for.</summary>
    public sealed class SplashDefinition
    {
        public string Name;

        /// <summary>The lump played when something lands in this liquid.</summary>
        public string Sound;

        /// <summary>The lump played when something small lands in it.</summary>
        public string SmallSound;

        public int Line;
    }

    /// <summary>What a floor does to whatever stands on it.</summary>
    public sealed class TerrainDefinition
    {
        public string Name;

        /// <summary>True when this is a liquid, which is what a splash requires.</summary>
        public bool Liquid;

        /// <summary>Movement multiplier, or <see cref="Fixed.One"/> for ordinary ground.</summary>
        public Fixed Friction = Fixed.One;

        /// <summary>True when <see cref="Friction"/> was written in the file.</summary>
        public bool HasFriction;

        /// <summary>Damage per hit, or 0 for a floor that does not hurt.</summary>
        public int DamageAmount;

        /// <summary>
        /// Which tics the damage lands on: it is dealt when <c>levelTime &amp; mask == 0</c>.
        ///
        /// A mask rather than a period on purpose. Vanilla's nukage does exactly this with
        /// <c>0x1f</c>, and using the same shape means a terrain that says 31 behaves like the
        /// nukage the player already knows, instead of pulsing on a different beat.
        /// </summary>
        public int DamageTimeMask = 31;

        /// <summary>True when the radiation suit protects against this floor.</summary>
        public bool AllowProtection;

        /// <summary>The splash played when something lands in it, or null.</summary>
        public string SplashName;

        /// <summary>The resolved splash, or null.</summary>
        public SplashDefinition Splash;

        public int Line;

        /// <summary>Damage dealt at this level time, or 0 when this tic is not a damage tic.</summary>
        public int DamageAt(int levelTime)
        {
            if (DamageAmount <= 0)
            {
                return 0;
            }

            // A mask of zero would be "never" by the arithmetic, but a file that wrote 0 means
            // "every tic", so it is treated as no mask rather than as no damage.
            if (DamageTimeMask == 0)
            {
                return DamageAmount;
            }

            return (levelTime & DamageTimeMask) == 0 ? DamageAmount : 0;
        }
    }

    /// <summary>
    /// TERRAIN: what a floor does to whatever stands on it.
    ///
    /// A map's flats say what the floor looks like; TERRAIN says what it *is*. That difference
    /// matters because the same picture is often several things: making every nukage flat hurt
    /// would be wrong for the map that uses it as a decoration, and making none of them hurt
    /// would be wrong for the map that floods a corridor with it.
    ///
    /// The syntax is ZDoom's, as written in `refs/doom_ace-master/doc/terrain.md`:
    ///
    ///     terrain Nukage
    ///     {
    ///         splash WaterSplash
    ///         liquid
    ///         friction 0.75
    ///         damageamount 5
    ///         damagetimemask 31
    ///         allowprotection
    ///     }
    ///
    ///     splash WaterSplash
    ///     {
    ///         sound waterspl
    ///     }
    ///
    ///     floor NUKAGE1 Nukage
    ///
    /// **What is honoured** is what this engine can do: damage, friction, and the splash sound.
    /// **What is named in the report** is what it cannot: splash *particles* (`smallclass`,
    /// `baseclass`, `chunkclass` and the four velocity shifts) and `damagetype`. Particles would
    /// need actors with sprites, and a stock IWAD has none; damage types would need a damage
    /// system that distinguishes them. Accepting either silently would tell a modder their
    /// effect works.
    /// </summary>
    public static class Terrain
    {
        private static readonly Dictionary<string, TerrainDefinition> definitions =
            new Dictionary<string, TerrainDefinition>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, SplashDefinition> splashes =
            new Dictionary<string, SplashDefinition>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Flat number to terrain, filled in once the flats are loaded.</summary>
        private static TerrainDefinition[] byFlat = new TerrainDefinition[0];

        private static readonly List<KeyValuePair<string, string>> unbound =
            new List<KeyValuePair<string, string>>();

        /// <summary>True when any file was read at all, so the per-tic lookups can be skipped.</summary>
        public static bool Any
        {
            get { return definitions.Count > 0; }
        }

        public static int Count
        {
            get { return definitions.Count; }
        }

        public static IList<KeyValuePair<string, string>> Unbound
        {
            get { return unbound; }
        }

        public static void Clear()
        {
            definitions.Clear();
            splashes.Clear();
            byFlat = new TerrainDefinition[0];
            unbound.Clear();
        }

        /// <summary>The terrain of a flat, or null when the flat has none.</summary>
        public static TerrainDefinition ForFlat(int flatNumber)
        {
            if (flatNumber < 0 || flatNumber >= byFlat.Length)
            {
                return null;
            }

            return byFlat[flatNumber];
        }

        public static TerrainDefinition Find(string name)
        {
            TerrainDefinition definition;

            return definitions.TryGetValue(name, out definition) ? definition : null;
        }

        public static SplashDefinition FindSplash(string name)
        {
            SplashDefinition definition;

            return splashes.TryGetValue(name, out definition) ? definition : null;
        }

        /// <summary>Reads one TERRAIN and adds it to the table.</summary>
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

                if (token.IsKeyword("terrain"))
                {
                    ReadTerrain(reader, source, problems, token);
                    continue;
                }

                if (token.IsKeyword("splash"))
                {
                    ReadSplash(reader, source, problems, token);
                    continue;
                }

                if (token.IsKeyword("floor") || token.IsKeyword("ceiling"))
                {
                    ReadBinding(reader, source, problems, token);
                    continue;
                }

                if (token.IsWord)
                {
                    problems.Add(source + " line " + token.Line + ": '" + token.Text +
                                 "' is not a TERRAIN keyword this port understands, so it is " +
                                 "ignored");

                    SkipEntry(reader);
                }
            }
        }

        /// <summary>
        /// Resolves `floor` bindings against the loaded flats.
        ///
        /// Done after the flats are read, because a terrain is bound to a flat by name and the
        /// names only exist once the WAD's flat list does. A binding that names a flat this WAD
        /// does not have is reported: otherwise the mod author has a terrain that is defined,
        /// syntactically correct, and applied to nothing.
        /// </summary>
        public static void Resolve(IFlatLookup flats, IList<string> problems)
        {
            byFlat = new TerrainDefinition[flats.Count];

            foreach (var binding in unbound)
            {
                var flatNumber = flats.GetNumber(binding.Key);

                if (flatNumber < 0)
                {
                    problems.Add("flat '" + binding.Key + "' is not in this WAD, so the terrain '" +
                                 binding.Value + "' is applied to nothing");

                    continue;
                }

                var definition = Find(binding.Value);

                if (definition == null)
                {
                    problems.Add("terrain '" + binding.Value + "' for flat '" + binding.Key +
                                 "' is not defined");

                    continue;
                }

                byFlat[flatNumber] = definition;
            }

            unbound.Clear();

            // Splash names are resolved once, here, for the same reason: a name is a lookup and a
            // missing one should be reported at load, not discovered by landing in the liquid.
            foreach (var definition in definitions.Values)
            {
                if (string.IsNullOrEmpty(definition.SplashName))
                {
                    continue;
                }

                definition.Splash = FindSplash(definition.SplashName);

                if (definition.Splash == null)
                {
                    problems.Add("splash '" + definition.SplashName + "' used by terrain '" +
                                 definition.Name + "' is not defined");

                    definition.SplashName = null;
                }
            }
        }

        private static void ReadTerrain(Reader reader, string source, IList<string> problems,
                                        Token keyword)
        {
            var name = reader.Next();

            if (name == null || !name.IsWord)
            {
                problems.Add(source + " line " + keyword.Line + ": 'terrain' is followed by no name");
                return;
            }

            var definition = new TerrainDefinition();
            definition.Name = name.Text;
            definition.Line = name.Line;

            var open = reader.Next();

            if (open == null || open.Text != "{")
            {
                problems.Add(source + " line " + name.Line + ": terrain '" + name.Text +
                             "' is not followed by a block, so it is ignored");

                // Pushed back rather than dropped: without the block the reader has already taken
                // the next entry's first word, and swallowing it would silently lose the terrain
                // after the broken one.
                if (open != null)
                {
                    reader.PushBack(open);
                }

                return;
            }

            while (true)
            {
                var token = reader.Next();

                if (token == null)
                {
                    problems.Add(source + " line " + name.Line + ": the block for terrain '" +
                                 name.Text + "' is not closed");
                    break;
                }

                if (token.Text == "}")
                {
                    break;
                }

                if (token.IsKeyword("splash"))
                {
                    var value = reader.Next();
                    definition.SplashName = value != null ? value.Text : null;
                    continue;
                }

                if (token.IsKeyword("liquid"))
                {
                    definition.Liquid = true;
                    continue;
                }

                if (token.IsKeyword("allowprotection"))
                {
                    definition.AllowProtection = true;
                    continue;
                }

                if (token.IsKeyword("friction"))
                {
                    var value = reader.Next();
                    Fixed parsed;

                    if (value != null && TryParseFixed(value.Text, out parsed))
                    {
                        definition.Friction = parsed;
                        definition.HasFriction = true;
                    }
                    else
                    {
                        problems.Add(source + " line " + token.Line +
                                     ": 'friction' needs a number, so ordinary ground friction " +
                                     "is used");
                    }

                    continue;
                }

                if (token.IsKeyword("damageamount"))
                {
                    var value = reader.Next();

                    if (value != null && value.IsNumber && value.Value >= 0)
                    {
                        definition.DamageAmount = value.Value;
                    }
                    else
                    {
                        problems.Add(source + " line " + token.Line +
                                     ": 'damageamount' needs a whole number of at least zero");
                    }

                    continue;
                }

                if (token.IsKeyword("damagetimemask"))
                {
                    var value = reader.Next();

                    if (value != null && value.IsNumber && value.Value >= 0)
                    {
                        definition.DamageTimeMask = value.Value;
                    }
                    else
                    {
                        problems.Add(source + " line " + token.Line +
                                     ": 'damagetimemask' needs a whole number of at least zero");
                    }

                    continue;
                }

                if (token.IsKeyword("damagetype"))
                {
                    reader.Next();

                    problems.Add(source + " line " + token.Line +
                                 ": 'damagetype' is ignored - this port has one kind of damage, " +
                                 "so the amount applies and the type cannot");

                    continue;
                }

                if (token.IsWord)
                {
                    problems.Add(source + " line " + token.Line + ": '" + token.Text +
                                 "' in terrain '" + name.Text +
                                 "' is not supported by this port, so it is ignored");

                    // Only the value, not the rest of the line: a file may write several
                    // properties on one line, and skipping to the newline would drop the ones
                    // after this that this port does understand.
                    reader.SkipValues(1);
                }
            }

            definitions[definition.Name] = definition;
        }

        private static void ReadSplash(Reader reader, string source, IList<string> problems,
                                       Token keyword)
        {
            var name = reader.Next();

            if (name == null || !name.IsWord)
            {
                problems.Add(source + " line " + keyword.Line + ": 'splash' is followed by no name");
                return;
            }

            var definition = new SplashDefinition();
            definition.Name = name.Text;
            definition.Line = name.Line;

            var open = reader.Next();

            if (open == null || open.Text != "{")
            {
                problems.Add(source + " line " + name.Line + ": splash '" + name.Text +
                             "' is not followed by a block, so it is ignored");

                if (open != null)
                {
                    reader.PushBack(open);
                }

                return;
            }

            while (true)
            {
                var token = reader.Next();

                if (token == null)
                {
                    problems.Add(source + " line " + name.Line + ": the block for splash '" +
                                 name.Text + "' is not closed");
                    break;
                }

                if (token.Text == "}")
                {
                    break;
                }

                if (token.IsKeyword("sound"))
                {
                    var value = reader.Next();
                    definition.Sound = value != null ? value.Text : null;
                    continue;
                }

                if (token.IsKeyword("smallsound"))
                {
                    var value = reader.Next();
                    definition.SmallSound = value != null ? value.Text : null;
                    continue;
                }

                if (token.IsKeyword("noalert"))
                {
                    // This port has no monster alerting to suppress: a monster notices what it
                    // can see, and no sound wakes anything. Honouring the flag would mean
                    // building the system first.
                    problems.Add(source + " line " + token.Line +
                                 ": 'noalert' is ignored - this port does not alert monsters by " +
                                 "sound, so there is nothing to suppress");

                    continue;
                }

                if (token.IsWord)
                {
                    // The particle properties: smallclass, baseclass, chunkclass, smallclip,
                    // chunkxvelshift, chunkyvelshift, chunkzvelshift, chunkbasezvel. Spawning
                    // them needs actor types with sprites, and a stock IWAD has none of the
                    // splash actors - so a splash would be invisible and the modder would think
                    // the file was wrong.
                    problems.Add(source + " line " + token.Line + ": '" + token.Text +
                                 "' in splash '" + name.Text +
                                 "' is ignored - the splash is played as a sound, and particles " +
                                 "would need actor types this port has no sprites for");

                    reader.SkipValues(1);
                }
            }

            splashes[definition.Name] = definition;
        }

        private static void ReadBinding(Reader reader, string source, IList<string> problems,
                                        Token keyword)
        {
            var flat = reader.Next();
            var terrain = reader.Next();

            if (flat == null || !flat.IsWord || terrain == null || !terrain.IsWord)
            {
                problems.Add(source + " line " + keyword.Line + ": '" + keyword.Text +
                             "' needs a flat name and a terrain name");
                return;
            }

            if (keyword.IsKeyword("ceiling"))
            {
                // Ceilings have no terrain in this engine: nothing stands on a ceiling, so
                // damage and friction would never be consulted.
                problems.Add(source + " line " + keyword.Line + ": 'ceiling " + flat.Text +
                             "' is ignored - this port applies terrain to floors only");
                return;
            }

            unbound.Add(new KeyValuePair<string, string>(flat.Text.ToUpperInvariant(), terrain.Text));
        }

        /// <summary>
        /// Parses a fixed-point number without going through binary floating point.
        ///
        /// Same rule as the script language's literals: 0.75 is 49152/65536 exactly, but a
        /// double round trip can land one unit away depending on the platform, and a friction
        /// factor that differs between machines is a desync waiting to happen.
        /// </summary>
        private static bool TryParseFixed(string text, out Fixed value)
        {
            value = Fixed.One;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var negative = false;
            var index = 0;

            if (text[0] == '-' || text[0] == '+')
            {
                negative = text[0] == '-';
                index = 1;
            }

            long whole = 0;
            var digits = 0;

            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                whole = whole * 10 + (text[index] - '0');

                if (whole > 32767)
                {
                    return false;
                }

                index++;
                digits++;
            }

            long fraction = 0;
            long scale = 1;

            if (index < text.Length && text[index] == '.')
            {
                index++;

                while (index < text.Length && text[index] >= '0' && text[index] <= '9')
                {
                    // Only as many digits as 16.16 can hold: more would be silent rounding.
                    if (scale < 10000000)
                    {
                        fraction = fraction * 10 + (text[index] - '0');
                        scale *= 10;
                    }

                    index++;
                    digits++;
                }
            }

            if (digits == 0 || index != text.Length)
            {
                return false;
            }

            var data = whole * Fixed.FracUnit + fraction * Fixed.FracUnit / scale;

            value = new Fixed((int)(negative ? -data : data));
            return true;
        }

        /// <summary>Skips an entry this port does not implement, including its block.</summary>
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

                if (depth == 0 && (token.IsKeyword("terrain") || token.IsKeyword("splash") ||
                                   token.IsKeyword("floor") || token.IsKeyword("ceiling")))
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

            public bool IsKeyword(string text)
            {
                return IsWord && !Quoted &&
                       string.Equals(Text, text, StringComparison.OrdinalIgnoreCase);
            }
        }

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

            public void SkipToEndOfLine(int line)
            {
                while (true)
                {
                    var token = Next();

                    if (token == null)
                    {
                        return;
                    }

                    if (token.Line != line || token.Text == "}" || token.Text == "{")
                    {
                        PushBack(token);
                        return;
                    }
                }
            }

            /// <summary>
            /// Skips the values after a directive this port does not implement.
            ///
            /// A count, not "to the end of the line": a file may put several properties on one
            /// line, and skipping the newline would drop the ones after this that this port does
            /// understand. Every property of a splash takes exactly one value.
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

                    if (token.Text == "}" || token.Text == "{" || token.IsKeyword("terrain") ||
                        token.IsKeyword("splash") || token.IsKeyword("floor") ||
                        token.IsKeyword("ceiling"))
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

                if (c == '{' || c == '}')
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
                return char.IsWhiteSpace(c) || c == '{' || c == '}';
            }
        }
    }
}
