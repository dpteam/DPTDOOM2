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
using System.Text;

namespace DPTDOOM.Scripting
{
    /// <summary>
    /// Turns source text into tokens.
    ///
    /// A hand-written scanner rather than a generated one, and that is not laziness: a
    /// generator brings a build step and a dependency, and this project ships as one
    /// executable with no third-party code. The language is small enough that the whole
    /// scanner is one pass over the characters.
    ///
    /// Numbers: a literal with a dot is a fixed-point number, one without is an integer.
    /// That distinction is made here, once, rather than left to the arithmetic - a script
    /// that writes <c>1.5</c> means a Doom fixed value, and reading it as a double and
    /// converting later would put float rounding between the author and the engine.
    /// </summary>
    public sealed class ScriptLexer
    {
        private readonly string source;
        private int position;
        private int line = 1;

        private ScriptLexer(string source)
        {
            this.source = source ?? "";
        }

        public static List<Token> Scan(string source)
        {
            return new ScriptLexer(source).ScanAll();
        }

        private List<Token> ScanAll()
        {
            var tokens = new List<Token>();

            while (true)
            {
                SkipSpaceAndComments();

                if (position >= source.Length)
                {
                    tokens.Add(new Token(TokenKind.End, line));
                    return tokens;
                }

                var c = source[position];

                if (IsIdentifierStart(c))
                {
                    tokens.Add(ScanIdentifier());
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && position + 1 < source.Length &&
                                        char.IsDigit(source[position + 1])))
                {
                    tokens.Add(ScanNumber());
                    continue;
                }

                if (c == '"')
                {
                    tokens.Add(ScanText());
                    continue;
                }

                tokens.Add(ScanSymbol());
            }
        }

        private void SkipSpaceAndComments()
        {
            while (position < source.Length)
            {
                var c = source[position];

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

                // Both comment forms, because a script written by somebody who also
                // writes C# will reach for // and somebody who writes DECORATE or Lua
                // will reach for --. Rejecting either would be a papercut with no upside.
                if (c == '/' && position + 1 < source.Length && source[position + 1] == '/')
                {
                    while (position < source.Length && source[position] != '\n')
                    {
                        position++;
                    }

                    continue;
                }

                if (c == '-' && position + 1 < source.Length && source[position + 1] == '-')
                {
                    while (position < source.Length && source[position] != '\n')
                    {
                        position++;
                    }

                    continue;
                }

                // A comment that spans lines, and it counts lines: a parse error after a
                // ten-line block comment has to point at the line it is really on.
                if (c == '/' && position + 1 < source.Length && source[position + 1] == '*')
                {
                    position += 2;

                    while (position + 1 < source.Length &&
                           !(source[position] == '*' && source[position + 1] == '/'))
                    {
                        if (source[position] == '\n')
                        {
                            line++;
                        }

                        position++;
                    }

                    if (position + 1 >= source.Length)
                    {
                        throw new ScriptException("unterminated /* comment", line);
                    }

                    position += 2;
                    continue;
                }

                return;
            }
        }

        private static bool IsIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsIdentifierPart(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        private Token ScanIdentifier()
        {
            var start = position;

            while (position < source.Length && IsIdentifierPart(source[position]))
            {
                position++;
            }

            var name = source.Substring(start, position - start);
            return new Token(KeywordOf(name), line) { Text = name };
        }

        /// <summary>
        /// Recognises the reserved words.
        ///
        /// Matched exactly and case-sensitively. Case-insensitivity would be friendlier
        /// for a modder, but it also means <c>Var</c> and <c>var</c> are the same token
        /// while every other identifier keeps its case - and a language that is
        /// case-insensitive for keywords and case-sensitive for names is a trap, not a
        /// convenience.
        /// </summary>
        private static TokenKind KeywordOf(string name)
        {
            switch (name)
            {
                case "if": return TokenKind.If;
                case "else": return TokenKind.Else;
                case "while": return TokenKind.While;
                case "return": return TokenKind.Return;
                case "break": return TokenKind.Break;
                case "continue": return TokenKind.Continue;
                case "var": return TokenKind.Var;
                case "true": return TokenKind.True;
                case "false": return TokenKind.False;
                default:
                    // The function keyword is read from ScriptSyntax so that renaming it is one
                    // edit rather than a search: `fn` today, `actor` tomorrow. Everything else
                    // above is structure and stays here.
                    return name == ScriptSyntax.FunctionKeyword ? TokenKind.Function : TokenKind.Identifier;
            }
        }

        private Token ScanNumber()
        {
            var start = position;
            var seenDot = false;

            while (position < source.Length)
            {
                var c = source[position];

                if (char.IsDigit(c))
                {
                    position++;
                    continue;
                }

                // A second dot ends the number rather than making it invalid: "1.2.3" is
                // then a number followed by a dot and another number, and the parser
                // says what is wrong with that in the place where it is wrong.
                if (c == '.' && !seenDot)
                {
                    seenDot = true;
                    position++;
                    continue;
                }

                break;
            }

            var text = source.Substring(start, position - start);

            if (!seenDot)
            {
                int whole;

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out whole))
                {
                    return new Token(TokenKind.Number, line) { Number = whole };
                }
            }
            else
            {
                // Parsed as a decimal string rather than through a double, for the same
                // reason the token kind exists: 1.1 must become the nearest 16.16 value
                // to one and a tenth, not the 16.16 value nearest to whatever a binary
                // float made of "1.1".
                Fixed fixedValue;

                if (TryParseFixed(text, out fixedValue))
                {
                    return new Token(TokenKind.Fixed, line) { FixedRaw = fixedValue.Data };
                }
            }

            throw new ScriptException("bad number '" + text + "'", line);
        }

        /// <summary>
        /// Reads a decimal literal into fixed point without going through a double.
        ///
        /// The fraction is turned into 16.16 by integer arithmetic on the digits:
        /// <c>digits * 65536 / 10^n</c>, rounded. Doing it through a double would mean
        /// "1.1" becomes the nearest binary float to one and a tenth and then the
        /// nearest 16.16 value to *that*, which can land one unit away from the value
        /// every other Doom program computes. Modders compare against the engine's
        /// numbers, so the errors compound somewhere surprising.
        ///
        /// Digits past the ninth are dropped: 1/65536 is about 1.5e-5, so anything beyond
        /// five decimal places is already below what the type can hold, and nine leaves
        /// room for the rounding to still be exact in integer arithmetic.
        /// </summary>
        private static bool TryParseFixed(string text, out Fixed value)
        {
            value = Fixed.Zero;

            var dot = text.IndexOf('.');
            var wholeText = dot < 0 ? text : text.Substring(0, dot);
            var fractionText = dot < 0 ? "" : text.Substring(dot + 1);

            int whole;

            if (!int.TryParse(wholeText.Length == 0 ? "0" : wholeText,
                              NumberStyles.Integer, CultureInfo.InvariantCulture, out whole))
            {
                return false;
            }

            if (fractionText.Length > 9)
            {
                fractionText = fractionText.Substring(0, 9);
            }

            long scale = 1;
            long digits = 0;

            foreach (var c in fractionText)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }

                digits = digits * 10 + (c - '0');
                scale *= 10;
            }

            // Rounded, not truncated: 0.9 in 16.16 is closer to one than to zero.
            long fraction = scale == 0 ? 0 : (digits * Fixed.FracUnit * 2 + scale) / (scale * 2);

            long total = (long)whole * Fixed.FracUnit + fraction;

            if (total > int.MaxValue || total < int.MinValue)
            {
                // Out of range for the type. Reported rather than wrapped: a wrapped
                // literal is a number that is silently wrong by a whole revolution.
                return false;
            }

            value = new Fixed((int)total);
            return true;
        }

        private Token ScanText()
        {
            var startLine = line;
            position++;

            var text = new StringBuilder();

            while (position < source.Length)
            {
                var c = source[position];

                if (c == '"')
                {
                    position++;
                    return new Token(TokenKind.Text, startLine) { Text = text.ToString() };
                }

                // A newline inside a string is a mistake, not a way to write two lines:
                // the closing quote is usually missing, and saying so here names the real
                // problem instead of "unterminated string" at the end of the file.
                if (c == '\n')
                {
                    throw new ScriptException("unterminated text: missing a closing quote", startLine);
                }

                if (c == '\\' && position + 1 < source.Length)
                {
                    var next = source[position + 1];
                    position += 2;

                    switch (next)
                    {
                        case 'n': text.Append('\n'); break;
                        case 't': text.Append('\t'); break;
                        case '"': text.Append('"'); break;
                        case '\\': text.Append('\\'); break;

                        default:
                            throw new ScriptException(
                                "unknown escape '\\" + next + "' in text", line);
                    }

                    continue;
                }

                text.Append(c);
                position++;
            }

            throw new ScriptException("unterminated text: missing a closing quote", startLine);
        }

        private Token ScanSymbol()
        {
            var c = source[position];
            var next = position + 1 < source.Length ? source[position + 1] : '\0';

            // Two-character symbols first, so "<=" is never read as "<" and "=". This is
            // the classic order bug in hand-written scanners and it fails quietly: the
            // program compiles and compares the wrong thing.
            switch (c)
            {
                case '=' when next == '=':
                    position += 2;
                    return new Token(TokenKind.Equal, line);

                case '!' when next == '=':
                    position += 2;
                    return new Token(TokenKind.NotEqual, line);

                case '!' :
                    position++;
                    return new Token(TokenKind.Not, line);

                case '<' when next == '=':
                    position += 2;
                    return new Token(TokenKind.LessOrEqual, line);

                case '<' :
                    position++;
                    return new Token(TokenKind.Less, line);

                case '>' when next == '=':
                    position += 2;
                    return new Token(TokenKind.GreaterOrEqual, line);

                case '>' :
                    position++;
                    return new Token(TokenKind.Greater, line);

                case '&' when next == '&':
                    position += 2;
                    return new Token(TokenKind.And, line);

                case '|' when next == '|':
                    position += 2;
                    return new Token(TokenKind.Or, line);

                case '+' when next == '=':
                    position += 2;
                    return new Token(TokenKind.PlusAssign, line);

                case '-' when next == '=':
                    position += 2;
                    return new Token(TokenKind.MinusAssign, line);
            }

            position++;

            switch (c)
            {
                case '(': return new Token(TokenKind.LeftParen, line);
                case ')': return new Token(TokenKind.RightParen, line);
                case '{': return new Token(TokenKind.LeftBrace, line);
                case '}': return new Token(TokenKind.RightBrace, line);
                case ',': return new Token(TokenKind.Comma, line);
                case ';': return new Token(TokenKind.Semicolon, line);
                case '.': return new Token(TokenKind.Dot, line);
                case '?': return new Token(TokenKind.Question, line);
                case ':': return new Token(TokenKind.Colon, line);
                case '+': return new Token(TokenKind.Plus, line);
                case '-': return new Token(TokenKind.Minus, line);
                case '*': return new Token(TokenKind.Star, line);
                case '/': return new Token(TokenKind.Slash, line);
                case '%': return new Token(TokenKind.Percent, line);
                case '=': return new Token(TokenKind.Assign, line);
            }

            throw new ScriptException("unexpected character '" + c + "'", line);
        }
    }
}
