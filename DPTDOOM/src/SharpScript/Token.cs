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

namespace DPTDOOM.Scripting
{
    public enum TokenKind
    {
        End = 0,

        Identifier,
        Number,
        Fixed,
        Text,

        LeftParen,
        RightParen,
        LeftBrace,
        RightBrace,
        Comma,
        Semicolon,
        Dot,
        Question,
        Colon,

        Plus,
        Minus,
        Star,
        Slash,
        Percent,

        Assign,
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,

        And,
        Or,
        Not,

        PlusAssign,
        MinusAssign,

        // Keywords are their own kinds rather than an identifier with a flag: the parser
        // then reads like the grammar it implements, and a typo in a variable name can
        // never be mistaken for a keyword.
        If,
        Else,
        While,
        Return,
        Break,
        Continue,
        Var,
        Function,
        True,
        False
    }

    /// <summary>
    /// One token, with the line it came from.
    ///
    /// The text is kept for identifiers and strings only; numbers are already converted,
    /// because a token that has to be re-parsed later is a token that can be re-parsed
    /// differently.
    /// </summary>
    public struct Token
    {
        public TokenKind Kind;
        public int Line;

        /// <summary>The identifier's name, or the text literal's contents.</summary>
        public string Text;

        /// <summary>The numeric value, for <see cref="TokenKind.Number"/>.</summary>
        public int Number;

        /// <summary>The raw 16.16 value, for <see cref="TokenKind.Fixed"/>.</summary>
        public int FixedRaw;

        public Token(TokenKind kind, int line)
        {
            Kind = kind;
            Line = line;
            Text = null;
            Number = 0;
            FixedRaw = 0;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case TokenKind.Identifier:
                case TokenKind.Text:
                    return Text;

                case TokenKind.Number:
                    return Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

                case TokenKind.Fixed:
                    return new Fixed(FixedRaw).ToFloat()
                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

                default:
                    return Kind.ToString();
            }
        }
    }
}
