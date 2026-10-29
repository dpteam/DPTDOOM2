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
    /// Translates a DECORATE condition into a SharpScript expression.
    ///
    /// `A_JumpIf(health &lt; 100, "Pain")` asks a question about the actor, and the answer has to be
    /// written in this project's own language - so the condition is translated, not guessed at.
    ///
    /// The translation is deliberately a **whitelist**. DECORATE identifiers name actor fields and
    /// functions that mostly have no counterpart here, and passing an unknown name through would
    /// produce a script that fails to run, at which point the modder gets a script error instead of
    /// the one thing they need: which word in their file this engine does not have. So an unknown
    /// name is a refusal, named, and the jump is dropped while the frame stays.
    ///
    /// Precedence is DECORATE's, which is C's, so the shape of the expression survives unchanged.
    /// </summary>
    public static class DecorateCondition
    {
        /// <summary>
        /// DECORATE identifier to the call this engine answers it with.
        ///
        /// `target` is the interesting one: DECORATE writes `target != null`, which is a question
        /// about existence, and this engine answers it as a count. That is why the translation is
        /// not a plain rename - see <see cref="Translate"/>.
        /// </summary>
        private static readonly Dictionary<string, string> fields =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "health", "Health()" },
            { "momx", "MomX()" },
            { "momy", "MomY()" },
            { "momz", "MomZ()" },
            { "x", "X()" },
            { "y", "Y()" },
            { "z", "Z()" },
            { "tics", "Tics()" },
            { "angle", "Angle()" },

            // The sector heights, which mods use to turn an actor's own position into a distance:
            // ademo1.wad stores "floorz - ceilingz" in an arg and reads it back later.
            { "floorz", "FloorZ()" },
            { "ceilingz", "CeilingZ()" }
        };

        /// <summary>
        /// A DECORATE condition as SharpScript text, or null when it cannot be translated.
        ///
        /// <paramref name="problems"/> receives one line per reason, already in DECORATE's terms.
        /// </summary>
        public static string Translate(string text, string actorName, int line,
                                       IList<string> problems)
        {
            return TranslateCore(text, actorName, line, problems, "the jump is dropped");
        }

        /// <summary>
        /// Translates a numeric expression used by an action such as A_SpawnItemEx.
        ///
        /// The grammar is the same as a condition: arithmetic, fixed literals and random(). The
        /// difference is the consequence reported when a word is unknown: this is not a jump, so
        /// saying "the jump is dropped" would send the modder looking at the wrong feature.
        /// </summary>
        public static string TranslateExpression(string text, string actorName, int line,
                                                 IList<string> problems)
        {
            return TranslateCore(text, actorName, line, problems,
                                 "that argument is replaced by zero");
        }

        private static string TranslateCore(string text, string actorName, int line,
                                            IList<string> problems, string consequence)
        {
            var reader = new Reader(text);

            try
            {
                var expression = ParseOr(reader);

                if (!reader.AtEnd)
                {
                    problems.Add(actorName + " line " + line + ": the condition '" + text.Trim() +
                                 "' is not understood after '" + reader.Peek + "'; " + consequence);
                    return null;
                }

                return expression;
            }
            catch (Untranslatable e)
            {
                problems.Add(actorName + " line " + line + ": " + e.Message + "; " + consequence);
                return null;
            }
        }

        // ------------------------------------------------------------ the grammar

        private sealed class Untranslatable : Exception
        {
            public Untranslatable(string message) : base(message)
            {
            }
        }

        private static string ParseOr(Reader reader)
        {
            var left = ParseAnd(reader);

            while (reader.TryOperator("||"))
            {
                left = left + " || " + ParseAnd(reader);
            }

            return left;
        }

        private static string ParseAnd(Reader reader)
        {
            var left = ParseEquality(reader);

            while (reader.TryOperator("&&"))
            {
                left = left + " && " + ParseEquality(reader);
            }

            return left;
        }

        private static string ParseEquality(Reader reader)
        {
            var left = ParseRelational(reader);

            while (true)
            {
                if (reader.TryOperator("=="))
                {
                    left = left + " == " + ParseRelational(reader);
                }
                else if (reader.TryOperator("!="))
                {
                    left = left + " != " + ParseRelational(reader);
                }
                else
                {
                    return left;
                }
            }
        }

        private static string ParseRelational(Reader reader)
        {
            var left = ParseAdditive(reader);

            while (true)
            {
                var op = reader.TryAnyOperator("<=", ">=", "<", ">");

                if (op == null)
                {
                    return left;
                }

                left = left + " " + op + " " + ParseAdditive(reader);
            }
        }

        private static string ParseAdditive(Reader reader)
        {
            var left = ParseMultiplicative(reader);

            while (true)
            {
                var op = reader.TryAnyOperator("+", "-");

                if (op == null)
                {
                    return left;
                }

                left = left + " " + op + " " + ParseMultiplicative(reader);
            }
        }

        private static string ParseMultiplicative(Reader reader)
        {
            var left = ParseUnary(reader);

            while (true)
            {
                var op = reader.TryAnyOperator("*", "/", "%");

                if (op == null)
                {
                    return left;
                }

                // Division by zero is not a conversion problem - it is a mod that always jumps or
                // never does, and the runtime answers it the same way the engine's own arithmetic
                // would. Left as written.
                left = left + " " + op + " " + ParseUnary(reader);
            }
        }

        private static string ParseUnary(Reader reader)
        {
            if (reader.TryOperator("!"))
            {
                return "!" + ParseUnary(reader);
            }

            if (reader.TryOperator("-"))
            {
                return "-" + ParseUnary(reader);
            }

            return ParsePrimary(reader);
        }

        private static string ParsePrimary(Reader reader)
        {
            if (reader.TryOperator("("))
            {
                var inner = ParseOr(reader);

                if (!reader.TryOperator(")"))
                {
                    throw new Untranslatable("a '(' in the condition is never closed");
                }

                return "(" + inner + ")";
            }

            if (reader.PeekIsNumber)
            {
                // The literal is carried through as written, not parsed and re-printed. A literal
                // with a dot is a fixed-point number in this language, and that is exactly what a
                // condition comparing a distance needs: "Distance() < 128.0" must compare fixed to
                // fixed. Truncating the dot to a whole number - the first version of this - quietly
                // changed 0.5 into 0.
                return reader.ReadNumber();
            }

            var name = reader.ReadName();

            if (name == null)
            {
                throw new Untranslatable("the condition '" + reader.Text +
                                         "' contains something that is not a number, a field or " +
                                         "a function");
            }

            // Sound attenuation constants are values, not actor fields. Carrying them through as
            // unknown names would make A_StartSound silently fall back to normal attenuation.
            if (string.Equals(name, "ATTN_NONE", StringComparison.OrdinalIgnoreCase))
            {
                return "0";
            }

            if (string.Equals(name, "ATTN_NORM", StringComparison.OrdinalIgnoreCase))
            {
                return "1";
            }

            if (string.Equals(name, "ATTN_IDLE", StringComparison.OrdinalIgnoreCase))
            {
                return "1.001";
            }

            if (string.Equals(name, "ATTN_STATIC", StringComparison.OrdinalIgnoreCase))
            {
                return "3";
            }

            // random(lo, hi) is the one function worth carrying over: conditions use it to make a
            // behaviour vary, and this engine answers it from the world generator so the variation
            // stays in step across a session.
            if (string.Equals(name, "random", StringComparison.OrdinalIgnoreCase))
            {
                if (!reader.TryOperator("("))
                {
                    throw new Untranslatable("'random' needs parentheses and two arguments");
                }

                var low = ParseOr(reader);

                if (!reader.TryOperator(","))
                {
                    throw new Untranslatable("'random' needs two arguments separated by a comma");
                }

                var high = ParseOr(reader);

                if (!reader.TryOperator(")"))
                {
                    throw new Untranslatable("'random' is never closed");
                }

                return "Random(" + low + ", " + high + ")";
            }

            // args[0]..args[4]: the actor's own numbers. Indexed, so the index is read here and
            // becomes a plain call argument - the language has no arrays and needs none.
            if (string.Equals(name, "args", StringComparison.OrdinalIgnoreCase))
            {
                if (!reader.TryOperator("["))
                {
                    throw new Untranslatable("'args' needs an index in brackets, such as args[0]");
                }

                var index = reader.ReadNumber();

                if (index == null || !reader.TryOperator("]"))
                {
                    throw new Untranslatable("'args[' needs a whole index and a closing ']'");
                }

                return "Arg(" + index + ")";
            }

            string call;

            if (fields.TryGetValue(name, out call))
            {
                return call;
            }

            // The existence question, which is the one place a field is not a simple rename.
            if (string.Equals(name, "target", StringComparison.OrdinalIgnoreCase))
            {
                return "Target()";
            }

            throw new Untranslatable("'" + name + "' is not a field or function this engine has " +
                                     "in a condition; it has health, target, momx, momy, momz, " +
                                     "x, y, z, floorz, ceilingz, angle, tics, args[N] and random");
        }

        // ------------------------------------------------------------ reading

        /// <summary>
        /// Reads the condition's own small token set: numbers, names, operators and brackets.
        ///
        /// Separate from the DECORATE reader in the converter because the needs are different: that
        /// one tokenises whole actor blocks and keeps braces and quotes, and conditions contain
        /// neither.
        /// </summary>
        private sealed class Reader
        {
            public readonly string Text;

            private int position;

            public Reader(string text)
            {
                Text = text ?? "";
            }

            public bool AtEnd
            {
                get
                {
                    SkipSpace();
                    return position >= Text.Length;
                }
            }

            public string Peek
            {
                get
                {
                    var save = position;
                    var token = PeekIsNumber ? "a number" : (ReadNameRaw() ?? "");
                    position = save;
                    return token;
                }
            }

            public bool PeekIsNumber
            {
                get
                {
                    SkipSpace();

                    if (position >= Text.Length)
                    {
                        return false;
                    }

                    var c = Text[position];

                    return char.IsDigit(c) ||
                           (c == '.' && position + 1 < Text.Length && char.IsDigit(Text[position + 1]));
                }
            }

            public string ReadNumber()
            {
                SkipSpace();

                var start = position;

                while (position < Text.Length &&
                       (char.IsDigit(Text[position]) || Text[position] == '.'))
                {
                    position++;
                }

                var body = Text.Substring(start, position - start);

                // A trailing dot or a lone dot is not a number this language reads; treat it as
                // zero rather than emitting something the lexer would refuse later, at load, with
                // a message that names no condition.
                if (body.Length == 0 || body == ".")
                {
                    return "0";
                }

                if (body.EndsWith(".", StringComparison.Ordinal))
                {
                    body = body + "0";
                }

                return body;
            }

            public string ReadName()
            {
                var name = ReadNameRaw();

                if (name == null)
                {
                    return null;
                }

                return name;
            }

            private string ReadNameRaw()
            {
                SkipSpace();

                if (position >= Text.Length)
                {
                    return null;
                }

                var start = position;

                while (position < Text.Length &&
                       (char.IsLetterOrDigit(Text[position]) || Text[position] == '_'))
                {
                    position++;
                }

                if (position == start)
                {
                    return null;
                }

                return Text.Substring(start, position - start);
            }

            /// <summary>
            /// True when the next operator is exactly this one, and consumes it.
            ///
            /// "<=" is checked before "<" by the caller, and this method does not try to be clever
            /// about that: it compares the text at the cursor, so the caller's order decides.
            /// </summary>
            public bool TryOperator(string op)
            {
                SkipSpace();

                if (position + op.Length > Text.Length)
                {
                    return false;
                }

                if (string.CompareOrdinal(Text, position, op, 0, op.Length) != 0)
                {
                    return false;
                }

                position += op.Length;
                return true;
            }

            /// <summary>The first of these operators that matches, or null. Longer ones first.</summary>
            public string TryAnyOperator(params string[] options)
            {
                foreach (var op in options)
                {
                    if (TryOperator(op))
                    {
                        return op;
                    }
                }

                return null;
            }

            private void SkipSpace()
            {
                while (position < Text.Length && char.IsWhiteSpace(Text[position]))
                {
                    position++;
                }
            }
        }
    }
}
