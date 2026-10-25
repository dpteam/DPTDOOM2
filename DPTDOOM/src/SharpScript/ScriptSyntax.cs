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
using System.Globalization;

namespace DPTDOOM.Scripting
{
    /// <summary>
    /// The parts of the script language that are **spelling** rather than structure.
    ///
    /// Two names here are deliberately adjustable, because they are the two a person is most likely
    /// to want changed and the two that used to be scattered through the codebase:
    ///
    ///  - <see cref="FunctionKeyword"/>, written before every definition: `fn Thing_3004() { ... }`;
    ///  - <see cref="ActorPrefix"/>, which turns a DoomEdNum into a name: `Thing_3004`.
    ///
    /// Everything else about the language - the statements, the expressions, the sandbox - is
    /// structure and does not belong here. What lives here is exactly the surface a rename touches,
    /// and the point of collecting it is that a rename then costs **one file** instead of a search
    /// through the lexer, the parser, the library, the actor writer and the DECORATE converter.
    ///
    /// The names are properties rather than constants so that a test can prove nothing else hard-codes
    /// them; a constant would be indistinguishable from a literal at the call site, which is the
    /// problem being solved.
    /// </summary>
    public static class ScriptSyntax
    {
        /// <summary>
        /// Written before a definition. `fn` today; `actor` is the obvious alternative.
        /// </summary>
        public static string FunctionKeyword
        {
            get { return "fn"; }
        }

        /// <summary>
        /// Prefix that turns a DoomEdNum into an actor definition's name.
        ///
        /// The number is the form because it is the one identifier that already exists everywhere -
        /// in the map format, in every editor, and in the actor table. ZDoom's friendly names
        /// ("Zombieman", "Imp") are an invention of that port, and a second naming scheme here would
        /// mean every modder has to learn which of two names applies.
        /// </summary>
        public static string ActorPrefix
        {
            get { return "Thing_"; }
        }

        /// <summary>The name of the definition for one actor: `Thing_3004`.</summary>
        public static string ActorName(int doomEdNum)
        {
            return ActorPrefix + doomEdNum.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The opening line of a definition: `fn Thing_3004() {`.
        ///
        /// One place, because it is written by two generators - the actor folder and the DECORATE
        /// converter - and two copies of a format string drift.
        /// </summary>
        public static string ActorDefinitionHeader(int doomEdNum)
        {
            return FunctionKeyword + " " + ActorName(doomEdNum) + "() {";
        }

        /// <summary>
        /// Whether a name describes an actor, and which one.
        ///
        /// Case-sensitive, like the rest of the language's keywords: a language that is
        /// case-insensitive for some names and case-sensitive for others is a trap, not a
        /// convenience.
        /// </summary>
        public static bool TryParseActorName(string name, out int doomEdNum)
        {
            doomEdNum = 0;

            if (name == null || !name.StartsWith(ActorPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var number = name.Substring(ActorPrefix.Length);

            return number.Length > 0 &&
                   int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out doomEdNum);
        }
    }
}
