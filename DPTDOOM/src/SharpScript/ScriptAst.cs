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
    /// The shape of a parsed script.
    ///
    /// Plain classes with public fields, not a visitor hierarchy. The language is small,
    /// the interpreter is the only consumer, and a visitor would add a layer whose only
    /// job is to be traversed by the one thing that already knows every node.
    ///
    /// Every node carries the line it started on. That is the single most useful thing
    /// for somebody debugging a script they did not write, and it costs one int per node.
    /// </summary>
    public abstract class Node
    {
        public int Line;
    }

    // ---------------------------------------------------------------- expressions

    public abstract class Expression : Node
    {
    }

    public sealed class LiteralExpression : Expression
    {
        public ScriptValue Value;
    }

    public sealed class NameExpression : Expression
    {
        public string Name;
    }

    public sealed class UnaryExpression : Expression
    {
        public TokenKind Operator;
        public Expression Operand;
    }

    public sealed class BinaryExpression : Expression
    {
        public TokenKind Operator;
        public Expression Left;
        public Expression Right;
    }

    public sealed class CallExpression : Expression
    {
        public string Name;
        public List<Expression> Arguments = new List<Expression>();
    }

    public sealed class ConditionalExpression : Expression
    {
        public Expression Condition;
        public Expression WhenTrue;
        public Expression WhenFalse;
    }

    /// <summary>
    /// An assignment used as a value: <c>x = y</c> evaluates to the new <c>x</c>.
    ///
    /// Kept as an expression rather than a statement because it removes a whole class of
    /// surprise: in a language where it is only a statement, <c>if (x = 5)</c> is a
    /// syntax error, which is fine - but so is <c>return x = 5</c>, which is not.
    /// </summary>
    public sealed class AssignExpression : Expression
    {
        public string Name;

        /// <summary>Assign, PlusAssign or MinusAssign.</summary>
        public TokenKind Operator;

        public Expression Value;
    }

    // ---------------------------------------------------------------- statements

    public abstract class Statement : Node
    {
    }

    public sealed class BlockStatement : Statement
    {
        public List<Statement> Statements = new List<Statement>();
    }

    public sealed class VarStatement : Statement
    {
        public string Name;

        /// <summary>Null when the declaration has no initialiser.</summary>
        public Expression Initializer;
    }

    public sealed class ExpressionStatement : Statement
    {
        public Expression Expression;
    }

    public sealed class IfStatement : Statement
    {
        public Expression Condition;
        public Statement WhenTrue;

        /// <summary>Null when there is no <c>else</c>.</summary>
        public Statement WhenFalse;
    }

    public sealed class WhileStatement : Statement
    {
        public Expression Condition;
        public Statement Body;
    }

    public sealed class ReturnStatement : Statement
    {
        /// <summary>Null for a bare <c>return;</c>.</summary>
        public Expression Value;
    }

    public sealed class BreakStatement : Statement
    {
    }

    public sealed class ContinueStatement : Statement
    {
    }

    // ---------------------------------------------------------------- declarations

    /// <summary>
    /// One top-level function.
    ///
    /// Functions are the only top-level construct. No classes, no globals, no includes -
    /// each of those brings a scoping model, and a scoping model brings a lifetime, and a
    /// lifetime brings the question of what state survives between tics. A script here is
    /// a pure function of its arguments and the host's state, and that property is what
    /// makes it safe inside a lockstep simulation.
    /// </summary>
    public sealed class FunctionDeclaration
    {
        public string Name;
        public List<string> Parameters = new List<string>();
        public BlockStatement Body;
        public int Line;
    }

    public sealed class ScriptUnit
    {
        public readonly List<FunctionDeclaration> Functions = new List<FunctionDeclaration>();

        public FunctionDeclaration Find(string name)
        {
            foreach (var function in Functions)
            {
                if (function.Name == name)
                {
                    return function;
                }
            }

            return null;
        }
    }
}
