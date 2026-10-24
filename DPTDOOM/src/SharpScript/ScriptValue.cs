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

namespace DPTDOOM.Scripting
{
    /// <summary>The kinds of value a script can hold.</summary>
    public enum ScriptKind
    {
        /// <summary>Nothing. Only ever the result of a call that returns no value.</summary>
        Void = 0,

        /// <summary>A full 32-bit integer.</summary>
        Integer = 1,

        /// <summary>A Doom fixed-point number: 16.16, the engine's own arithmetic.</summary>
        Fixed = 2,

        Boolean = 3,

        /// <summary>Text, for messages and for the few APIs that take a name.</summary>
        Text = 4
    }

    /// <summary>
    /// One value on the script's stack.
    ///
    /// Deliberately a struct with three numeric slots rather than an <c>object</c>. The
    /// interpreter runs inside the simulation: every tic, for every actor, with the
    /// world waiting. Boxing an int to call a method and unboxing it afterwards is an
    /// allocation per operation, and an allocation per operation inside a 35 Hz loop is
    /// how a scripting layer turns into a frame-time problem. Three fields and no
    /// allocation is the whole reason this type is not simply <c>object</c>.
    ///
    /// The cost is that the kind has to be carried and checked by hand, which is why
    /// every conversion in here goes through one of the explicit methods below instead
    /// of a cast.
    /// </summary>
    public struct ScriptValue
    {
        private readonly ScriptKind kind;
        private readonly int number;
        private readonly string text;

        private ScriptValue(ScriptKind kind, int number, string text)
        {
            this.kind = kind;
            this.number = number;
            this.text = text;
        }

        public static readonly ScriptValue Void = new ScriptValue(ScriptKind.Void, 0, null);

        public static ScriptValue FromInt(int value)
        {
            return new ScriptValue(ScriptKind.Integer, value, null);
        }

        public static ScriptValue FromFixed(Fixed value)
        {
            // Stored as its raw 16.16 integer, not as a float: float round-tripping
            // would lose the low bits on every operation and make a script's arithmetic
            // disagree with the engine's for the same numbers.
            return new ScriptValue(ScriptKind.Fixed, value.Data, null);
        }

        public static ScriptValue FromBool(bool value)
        {
            return new ScriptValue(ScriptKind.Boolean, value ? 1 : 0, null);
        }

        public static ScriptValue FromText(string value)
        {
            return new ScriptValue(ScriptKind.Text, 0, value ?? "");
        }

        public ScriptKind Kind
        {
            get { return kind; }
        }

        public bool IsVoid
        {
            get { return kind == ScriptKind.Void; }
        }

        /// <summary>True for both integer and fixed kinds: both are numbers.</summary>
        public bool IsNumber
        {
            get { return kind == ScriptKind.Integer || kind == ScriptKind.Fixed; }
        }

        public int AsInt()
        {
            switch (kind)
            {
                case ScriptKind.Integer:
                case ScriptKind.Boolean:
                    return number;

                case ScriptKind.Fixed:
                    // Truncation toward zero, the way C# casts a float to an int. A
                    // script that wants the floor asks for it explicitly.
                    return new Fixed(number).ToIntFloor();

                default:
                    throw new ScriptException(
                        "expected a number, got " + Describe(kind));
            }
        }

        public Fixed AsFixed()
        {
            switch (kind)
            {
                case ScriptKind.Fixed:
                    return new Fixed(number);

                case ScriptKind.Integer:
                case ScriptKind.Boolean:
                    // An integer becomes a whole fixed value. This is the one implicit
                    // conversion in the language, and it is implicit because it is the
                    // one that cannot lose anything: 1 is always exactly 1.0.
                    return Fixed.FromInt(number);

                default:
                    throw new ScriptException(
                        "expected a number, got " + Describe(kind));
            }
        }

        public bool AsBool()
        {
            switch (kind)
            {
                case ScriptKind.Boolean:
                    return number != 0;

                case ScriptKind.Integer:
                case ScriptKind.Fixed:
                    // Truthiness for numbers, because every C-derived language a Doom
                    // modder has met allows "if (count)" and forbidding it would be a
                    // surprise for no gain.
                    return number != 0;

                case ScriptKind.Text:
                    return !string.IsNullOrEmpty(text);

                default:
                    throw new ScriptException("expected a value, got nothing");
            }
        }

        public string AsText()
        {
            if (kind != ScriptKind.Text)
            {
                throw new ScriptException("expected text, got " + Describe(kind));
            }

            return text;
        }

        /// <summary>
        /// The value as text, for building messages.
        ///
        /// Every kind has a form, because a script that prints a number should not have
        /// to convert it first; only Void has none, and asking for it is a mistake worth
        /// reporting.
        /// </summary>
        public string ToDisplay()
        {
            switch (kind)
            {
                case ScriptKind.Text:
                    return text;

                case ScriptKind.Boolean:
                    return number != 0 ? "true" : "false";

                case ScriptKind.Fixed:
                    return new Fixed(number).ToFloat()
                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

                case ScriptKind.Integer:
                    return number.ToString(System.Globalization.CultureInfo.InvariantCulture);

                default:
                    return "nothing";
            }
        }

        /// <summary>
        /// Which kinds two operands may be compared or combined as.
        ///
        /// Fixed wins over integer when the two meet, because the alternative - demoting
        /// the fixed operand - would silently drop its fraction. Text is its own world:
        /// mixing it with numbers is a mistake, not a conversion.
        /// </summary>
        public static ScriptKind Common(ScriptValue left, ScriptValue right)
        {
            if (!left.IsNumber || !right.IsNumber)
            {
                if (left.kind == right.kind)
                {
                    return left.kind;
                }

                throw new ScriptException(
                    "cannot combine " + Describe(left.kind) + " and " + Describe(right.kind));
            }

            return left.kind == ScriptKind.Fixed || right.kind == ScriptKind.Fixed
                ? ScriptKind.Fixed
                : ScriptKind.Integer;
        }

        public static string Describe(ScriptKind kind)
        {
            switch (kind)
            {
                case ScriptKind.Integer: return "a number";
                case ScriptKind.Fixed: return "a fixed number";
                case ScriptKind.Boolean: return "a yes/no value";
                case ScriptKind.Text: return "text";
                default: return "nothing";
            }
        }

        public override string ToString()
        {
            return ToDisplay();
        }
    }
}
