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

using System.Collections.Generic;

namespace DPTDOOM.Scripting
{
    /// <summary>
    /// Builds the syntax tree, one token at a time.
    ///
    /// Recursive descent with precedence climbing on the expressions. The alternative -
    /// a table-driven parser - buys nothing here: the grammar is twenty productions, and
    /// a hand-written parser for twenty productions is shorter, and reads like the
    /// grammar it implements, which is what a modder filing a bug report has to be able
    /// to follow.
    ///
    /// Every failure is a <see cref="ScriptException"/> with a line number. There is no
    /// error recovery: a script either parses or it does not, and a half-understood
    /// script is worse than a rejected one - it would run and do something subtly
    /// different from what was written.
    /// </summary>
    public sealed class ScriptParser
    {
        private readonly List<Token> tokens;
        private int index;

        private ScriptParser(List<Token> tokens)
        {
            this.tokens = tokens;
        }

        public static ScriptUnit Parse(string source)
        {
            return Parse(ScriptLexer.Scan(source));
        }

        public static ScriptUnit Parse(List<Token> tokens)
        {
            var parser = new ScriptParser(tokens);
            return parser.ParseUnit();
        }

        private Token Current
        {
            get { return tokens[index]; }
        }

        private Token Advance()
        {
            var token = tokens[index];

            if (index < tokens.Count - 1)
            {
                index++;
            }

            return token;
        }

        private bool Check(TokenKind kind)
        {
            return Current.Kind == kind;
        }

        private bool Match(TokenKind kind)
        {
            if (!Check(kind))
            {
                return false;
            }

            Advance();
            return true;
        }

        private Token Expect(TokenKind kind, string what)
        {
            if (!Check(kind))
            {
                throw new ScriptException(
                    "expected " + what + ", found " + Describe(Current), Current.Line);
            }

            return Advance();
        }

        private static string Describe(Token token)
        {
            if (token.Kind == TokenKind.End)
            {
                return "the end of the script";
            }

            if (token.Kind == TokenKind.Text)
            {
                return "text \"" + token.Text + "\"";
            }

            return "'" + token + "'";
        }

        // ------------------------------------------------------------ declarations

        private ScriptUnit ParseUnit()
        {
            var unit = new ScriptUnit();

            while (!Check(TokenKind.End))
            {
                var start = Current;

                Expect(TokenKind.Function, "'fn'");

                var name = Expect(TokenKind.Identifier, "a function name").Text;

                // A repeated name replaces the earlier definition and is warned about, not refused.
                //
                // This is the older ports' behaviour, and it is the one that keeps old WADs
                // playable: a file that declares the same actor twice is usually a patch applied on
                // top of itself, and the later block is the one the author meant. Throwing here cost
                // the whole file - every other actor in it as well - over a duplicate that the
                // engine can resolve without guessing.
                var existing = unit.Find(name);

                if (existing != null)
                {
                    unit.Warnings.Add("line " + start.Line + ": '" + name + "' is defined again; " +
                                      "the definition at line " + existing.Line +
                                      " is replaced by this one");
                }

                var declaration = existing ?? new FunctionDeclaration();

                declaration.Name = name;
                declaration.Line = start.Line;
                declaration.Parameters.Clear();

                Expect(TokenKind.LeftParen, "'(' after the function name");

                if (!Check(TokenKind.RightParen))
                {
                    do
                    {
                        var parameter = Expect(TokenKind.Identifier, "a parameter name").Text;

                        if (declaration.Parameters.Contains(parameter))
                        {
                            throw new ScriptException(
                                "'" + name + "' takes '" + parameter + "' twice", start.Line);
                        }

                        declaration.Parameters.Add(parameter);
                    }
                    while (Match(TokenKind.Comma));
                }

                Expect(TokenKind.RightParen, "')'");
                declaration.Body = ParseBlock();

                if (existing == null)
                {
                    unit.Functions.Add(declaration);
                }
            }

            if (unit.Functions.Count == 0)
            {
                throw new ScriptException("a script must define at least one function", 1);
            }

            return unit;
        }

        private BlockStatement ParseBlock()
        {
            var block = new BlockStatement();
            block.Line = Current.Line;

            Expect(TokenKind.LeftBrace, "'{' to open a block");

            while (!Check(TokenKind.RightBrace))
            {
                if (Check(TokenKind.End))
                {
                    throw new ScriptException("missing '}' to close the block", block.Line);
                }

                block.Statements.Add(ParseStatement());
            }

            Expect(TokenKind.RightBrace, "'}'");
            return block;
        }

        // ------------------------------------------------------------ statements

        private Statement ParseStatement()
        {
            var line = Current.Line;

            if (Check(TokenKind.LeftBrace))
            {
                return ParseBlock();
            }

            if (Check(TokenKind.Var))
            {
                Advance();

                var declaration = new VarStatement();
                declaration.Line = line;
                declaration.Name = Expect(TokenKind.Identifier, "a variable name").Text;

                if (Match(TokenKind.Assign))
                {
                    declaration.Initializer = ParseExpression();
                }

                Expect(TokenKind.Semicolon, "';' after the declaration");
                return declaration;
            }

            if (Match(TokenKind.If))
            {
                var statement = new IfStatement();
                statement.Line = line;

                Expect(TokenKind.LeftParen, "'(' after 'if'");
                statement.Condition = ParseExpression();
                Expect(TokenKind.RightParen, "')' after the condition");
                statement.WhenTrue = ParseStatement();

                if (Match(TokenKind.Else))
                {
                    statement.WhenFalse = ParseStatement();
                }

                return statement;
            }

            if (Match(TokenKind.While))
            {
                var statement = new WhileStatement();
                statement.Line = line;

                Expect(TokenKind.LeftParen, "'(' after 'while'");
                statement.Condition = ParseExpression();
                Expect(TokenKind.RightParen, "')' after the condition");
                statement.Body = ParseStatement();
                return statement;
            }

            if (Match(TokenKind.Return))
            {
                var statement = new ReturnStatement();
                statement.Line = line;

                if (!Check(TokenKind.Semicolon))
                {
                    statement.Value = ParseExpression();
                }

                Expect(TokenKind.Semicolon, "';' after 'return'");
                return statement;
            }

            if (Match(TokenKind.Break))
            {
                var statement = new BreakStatement();
                statement.Line = line;
                Expect(TokenKind.Semicolon, "';' after 'break'");
                return statement;
            }

            if (Match(TokenKind.Continue))
            {
                var statement = new ContinueStatement();
                statement.Line = line;
                Expect(TokenKind.Semicolon, "';' after 'continue'");
                return statement;
            }

            if (Check(TokenKind.Function))
            {
                // Nested functions would need closures, closures need captured
                // environments, and captured environments mean state that outlives the
                // call. Refused outright rather than half-supported.
                throw new ScriptException("'fn' is only allowed at the top level", line);
            }

            var expression = new ExpressionStatement();
            expression.Line = line;
            expression.Expression = ParseExpression();
            Expect(TokenKind.Semicolon, "';' after the expression");
            return expression;
        }

        // ------------------------------------------------------------ expressions

        private Expression ParseExpression()
        {
            return ParseAssignment();
        }

        private Expression ParseAssignment()
        {
            var left = ParseConditional();

            if (Check(TokenKind.Assign) || Check(TokenKind.PlusAssign) || Check(TokenKind.MinusAssign))
            {
                var op = Advance().Kind;
                var line = left.Line;

                // Only a plain name can be assigned to. There are no fields and no array
                // elements, so anything else is a mistake, and saying so here beats
                // "cannot assign to this" at runtime.
                var name = left as NameExpression;

                if (name == null)
                {
                    throw new ScriptException("only a variable can be assigned to", line);
                }

                var assign = new AssignExpression();
                assign.Line = line;
                assign.Name = name.Name;
                assign.Operator = op;
                assign.Value = ParseAssignment();
                return assign;
            }

            return left;
        }

        private Expression ParseConditional()
        {
            var condition = ParseOr();

            if (!Check(TokenKind.Question))
            {
                return condition;
            }

            Advance();

            var conditional = new ConditionalExpression();
            conditional.Line = condition.Line;
            conditional.Condition = condition;
            conditional.WhenTrue = ParseExpression();
            Expect(TokenKind.Colon, "':' in the conditional");
            conditional.WhenFalse = ParseExpression();
            return conditional;
        }

        private Expression ParseOr()
        {
            var left = ParseAnd();

            while (Check(TokenKind.Or))
            {
                var op = Advance();
                left = MakeBinary(op, left, ParseAnd());
            }

            return left;
        }

        private Expression ParseAnd()
        {
            var left = ParseEquality();

            while (Check(TokenKind.And))
            {
                var op = Advance();
                left = MakeBinary(op, left, ParseEquality());
            }

            return left;
        }

        private Expression ParseEquality()
        {
            var left = ParseRelational();

            while (Check(TokenKind.Equal) || Check(TokenKind.NotEqual))
            {
                var op = Advance();
                left = MakeBinary(op, left, ParseRelational());
            }

            return left;
        }

        private Expression ParseRelational()
        {
            var left = ParseAdditive();

            while (Check(TokenKind.Less) || Check(TokenKind.LessOrEqual) ||
                   Check(TokenKind.Greater) || Check(TokenKind.GreaterOrEqual))
            {
                var op = Advance();
                left = MakeBinary(op, left, ParseAdditive());
            }

            return left;
        }

        private Expression ParseAdditive()
        {
            var left = ParseMultiplicative();

            while (Check(TokenKind.Plus) || Check(TokenKind.Minus))
            {
                var op = Advance();
                left = MakeBinary(op, left, ParseMultiplicative());
            }

            return left;
        }

        private Expression ParseMultiplicative()
        {
            var left = ParseUnary();

            while (Check(TokenKind.Star) || Check(TokenKind.Slash) || Check(TokenKind.Percent))
            {
                var op = Advance();
                left = MakeBinary(op, left, ParseUnary());
            }

            return left;
        }

        private Expression ParseUnary()
        {
            if (Check(TokenKind.Minus) || Check(TokenKind.Not))
            {
                var op = Advance();

                var unary = new UnaryExpression();
                unary.Line = op.Line;
                unary.Operator = op.Kind;
                unary.Operand = ParseUnary();
                return unary;
            }

            return ParsePrimary();
        }

        private Expression ParsePrimary()
        {
            var token = Current;

            switch (token.Kind)
            {
                case TokenKind.Number:
                    Advance();
                    return new LiteralExpression
                    {
                        Line = token.Line,
                        Value = ScriptValue.FromInt(token.Number)
                    };

                case TokenKind.Fixed:
                    Advance();
                    return new LiteralExpression
                    {
                        Line = token.Line,
                        Value = ScriptValue.FromFixed(new Fixed(token.FixedRaw))
                    };

                case TokenKind.Text:
                    Advance();
                    return new LiteralExpression
                    {
                        Line = token.Line,
                        Value = ScriptValue.FromText(token.Text)
                    };

                case TokenKind.True:
                    Advance();
                    return new LiteralExpression
                    {
                        Line = token.Line,
                        Value = ScriptValue.FromBool(true)
                    };

                case TokenKind.False:
                    Advance();
                    return new LiteralExpression
                    {
                        Line = token.Line,
                        Value = ScriptValue.FromBool(false)
                    };

                case TokenKind.Identifier:
                    Advance();

                    if (!Check(TokenKind.LeftParen))
                    {
                        return new NameExpression { Line = token.Line, Name = token.Text };
                    }

                    var call = new CallExpression();
                    call.Line = token.Line;
                    call.Name = token.Text;

                    Advance();

                    if (!Check(TokenKind.RightParen))
                    {
                        do
                        {
                            call.Arguments.Add(ParseExpression());
                        }
                        while (Match(TokenKind.Comma));
                    }

                    Expect(TokenKind.RightParen, "')' after the arguments");
                    return call;

                case TokenKind.LeftParen:
                    Advance();
                    var inner = ParseExpression();
                    Expect(TokenKind.RightParen, "')'");
                    return inner;
            }

            throw new ScriptException("expected a value, found " + Describe(token), token.Line);
        }

        private static Expression MakeBinary(Token op, Expression left, Expression right)
        {
            var binary = new BinaryExpression();
            binary.Line = op.Line;
            binary.Operator = op.Kind;
            binary.Left = left;
            binary.Right = right;
            return binary;
        }
    }
}
