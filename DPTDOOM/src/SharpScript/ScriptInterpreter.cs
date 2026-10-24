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

namespace DPTDOOM.Scripting
{
    /// <summary>One block's variables.</summary>
    internal sealed class Scope
    {
        private readonly Dictionary<string, ScriptValue> values =
            new Dictionary<string, ScriptValue>(StringComparer.Ordinal);

        public readonly Scope Parent;

        public Scope(Scope parent)
        {
            Parent = parent;
        }

        public bool TryGet(string name, out ScriptValue value)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                if (scope.values.TryGetValue(name, out value))
                {
                    return true;
                }
            }

            value = ScriptValue.Void;
            return false;
        }

        public bool TryAssign(string name, ScriptValue value)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                if (scope.values.ContainsKey(name))
                {
                    scope.values[name] = value;
                    return true;
                }
            }

            return false;
        }

        public bool Declare(string name, ScriptValue value)
        {
            // Shadowing an outer variable is allowed - that is what a block is for - but
            // declaring the same name twice in the same block is a typo, and a typo that
            // otherwise reads as "the second one had no effect".
            if (values.ContainsKey(name))
            {
                return false;
            }

            values.Add(name, value);
            return true;
        }
    }

    /// <summary>How a statement finished.</summary>
    internal enum Flow
    {
        Normal = 0,
        Return = 1,
        Break = 2,
        Continue = 3
    }

    /// <summary>
    /// Runs a parsed script.
    ///
    /// A tree-walking interpreter, which is the slow way to run a language and the right
    /// way to run this one. The scripts are actor behaviour: a few dozen operations per
    /// call, a handful of calls per tic. Compiling to IL would need a library this project
    /// cannot ship, and hand-writing IL would trade a readable interpreter for a
    /// generator nobody can debug. If a script ever shows up in a frame profile, the fix
    /// is to cache what it computed, not to compile it.
    ///
    /// The interpreter holds no state between calls. Everything a script knows arrives as
    /// parameters or as host calls, and everything it produces leaves the same way. That
    /// is what makes a script safe to run inside lockstep: it cannot remember anything
    /// the other end does not also remember.
    /// </summary>
    public sealed class ScriptInterpreter
    {
        private readonly ScriptUnit unit;
        private readonly ScriptHost host;

        private int steps;
        private int depth;

        public ScriptInterpreter(ScriptUnit unit, ScriptHost host)
        {
            this.unit = unit;
            this.host = host;
        }

        /// <summary>
        /// Calls a function by name with the given arguments.
        ///
        /// Returns <see cref="ScriptValue.Void"/> when the function falls off its end
        /// without a <c>return</c>, which is how a statement-like script is written.
        /// </summary>
        public ScriptValue Call(string name, params ScriptValue[] arguments)
        {
            var function = unit.Find(name);

            if (function == null)
            {
                throw new ScriptException("'" + name + "' is not a function");
            }

            if (arguments.Length != function.Parameters.Count)
            {
                throw new ScriptException(
                    "'" + name + "' takes " + function.Parameters.Count + " argument(s), got " +
                    arguments.Length, function.Line);
            }

            steps = 0;
            depth = 0;

            return InvokeFunction(function, arguments);
        }

        private ScriptValue InvokeFunction(FunctionDeclaration function, ScriptValue[] arguments)
        {
            // Checked here as well as in Call(): a script-to-script call goes straight to
            // this method, and without the check a wrong argument count surfaced as an
            // index out of range from inside the interpreter - the least helpful possible
            // report for "I called it with the wrong number of arguments".
            if (arguments.Length != function.Parameters.Count)
            {
                throw new ScriptException(
                    "'" + function.Name + "' takes " + function.Parameters.Count +
                    " argument(s), got " + arguments.Length, function.Line);
            }

            if (depth >= host.CallDepth)
            {
                // A cycle between scripts - A calls B calls A - would otherwise run until
                // the step budget, and the budget is per call, so a cycle that resets it
                // would never end. The depth limit is what actually stops recursion.
                throw new ScriptException("scripts are nested too deeply", function.Line);
            }

            depth++;

            try
            {
                var scope = new Scope(null);

                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    scope.Declare(function.Parameters[i], arguments[i]);
                }

                var flow = ExecuteBlock(function.Body, scope);

                if (flow == Flow.Return)
                {
                    return returned;
                }

                if (flow == Flow.Break || flow == Flow.Continue)
                {
                    // Caught here rather than in the parser: the parser would have to
                    // track loop nesting, and the same check is two lines down here.
                    throw new ScriptException("'break' or 'continue' outside a loop", function.Line);
                }

                return ScriptValue.Void;
            }
            finally
            {
                depth--;
            }
        }

        private ScriptValue returned;

        private Flow ExecuteBlock(BlockStatement block, Scope parent)
        {
            var scope = new Scope(parent);

            foreach (var statement in block.Statements)
            {
                var flow = Execute(statement, scope);

                if (flow != Flow.Normal)
                {
                    return flow;
                }
            }

            return Flow.Normal;
        }

        private Flow Execute(Statement statement, Scope scope)
        {
            Spend(statement.Line);

            var blockStatement = statement as BlockStatement;

            if (blockStatement != null)
            {
                return ExecuteBlock(blockStatement, scope);
            }

            var varStatement = statement as VarStatement;

            if (varStatement != null)
            {
                var value = varStatement.Initializer == null
                    ? ScriptValue.FromInt(0)
                    : Evaluate(varStatement.Initializer, scope);

                if (!scope.Declare(varStatement.Name, value))
                {
                    throw new ScriptException(
                        "'" + varStatement.Name + "' is already declared here", statement.Line);
                }

                return Flow.Normal;
            }

            var expressionStatement = statement as ExpressionStatement;

            if (expressionStatement != null)
            {
                Evaluate(expressionStatement.Expression, scope);
                return Flow.Normal;
            }

            var ifStatement = statement as IfStatement;

            if (ifStatement != null)
            {
                if (Evaluate(ifStatement.Condition, scope).AsBool())
                {
                    return Execute(ifStatement.WhenTrue, scope);
                }

                return ifStatement.WhenFalse == null
                    ? Flow.Normal
                    : Execute(ifStatement.WhenFalse, scope);
            }

            var whileStatement = statement as WhileStatement;

            if (whileStatement != null)
            {
                while (Evaluate(whileStatement.Condition, scope).AsBool())
                {
                    var flow = Execute(whileStatement.Body, scope);

                    if (flow == Flow.Return)
                    {
                        return flow;
                    }

                    if (flow == Flow.Break)
                    {
                        break;
                    }

                    // Continue falls through to the condition, which is what it means.
                    Spend(statement.Line);
                }

                return Flow.Normal;
            }

            var returnStatement = statement as ReturnStatement;

            if (returnStatement != null)
            {
                returned = returnStatement.Value == null
                    ? ScriptValue.Void
                    : Evaluate(returnStatement.Value, scope);

                return Flow.Return;
            }

            if (statement is BreakStatement)
            {
                return Flow.Break;
            }

            if (statement is ContinueStatement)
            {
                return Flow.Continue;
            }

            throw new ScriptException("unsupported statement", statement.Line);
        }

        // ------------------------------------------------------------ expressions

        private ScriptValue Evaluate(Expression expression, Scope scope)
        {
            Spend(expression.Line);

            var literal = expression as LiteralExpression;

            if (literal != null)
            {
                return literal.Value;
            }

            var name = expression as NameExpression;

            if (name != null)
            {
                ScriptValue value;

                if (!scope.TryGet(name.Name, out value))
                {
                    throw new ScriptException("'" + name.Name + "' is not declared", expression.Line);
                }

                return value;
            }

            var call = expression as CallExpression;

            if (call != null)
            {
                return EvaluateCall(call, scope);
            }

            var unary = expression as UnaryExpression;

            if (unary != null)
            {
                return EvaluateUnary(unary, scope);
            }

            var binary = expression as BinaryExpression;

            if (binary != null)
            {
                return EvaluateBinary(binary, scope);
            }

            var assign = expression as AssignExpression;

            if (assign != null)
            {
                return EvaluateAssign(assign, scope);
            }

            var conditional = expression as ConditionalExpression;

            if (conditional != null)
            {
                return Evaluate(conditional.Condition, scope).AsBool()
                    ? Evaluate(conditional.WhenTrue, scope)
                    : Evaluate(conditional.WhenFalse, scope);
            }

            throw new ScriptException("unsupported expression", expression.Line);
        }

        private ScriptValue EvaluateCall(CallExpression call, Scope scope)
        {
            var scriptFunction = unit.Find(call.Name);

            if (scriptFunction != null)
            {
                var arguments = new ScriptValue[call.Arguments.Count];

                for (var i = 0; i < arguments.Length; i++)
                {
                    arguments[i] = Evaluate(call.Arguments[i], scope);
                }

                return InvokeFunction(scriptFunction, arguments);
            }

            var engineCall = new ScriptCall();
            engineCall.Name = call.Name;
            engineCall.Line = call.Line;
            engineCall.Arguments = new ScriptValue[call.Arguments.Count];

            for (var i = 0; i < engineCall.Arguments.Length; i++)
            {
                engineCall.Arguments[i] = Evaluate(call.Arguments[i], scope);
            }

            // A script function shadows an engine function of the same name - the
            // lookup above already decided that - so an embedder can let a PWAD override
            // a name without changing the host.
            return host.Invoke(engineCall);
        }

        private ScriptValue EvaluateUnary(UnaryExpression unary, Scope scope)
        {
            var operand = Evaluate(unary.Operand, scope);

            if (unary.Operator == TokenKind.Not)
            {
                return ScriptValue.FromBool(!operand.AsBool());
            }

            if (operand.Kind == ScriptKind.Fixed)
            {
                return ScriptValue.FromFixed(-operand.AsFixed());
            }

            return ScriptValue.FromInt(-operand.AsInt());
        }

        private ScriptValue EvaluateBinary(BinaryExpression binary, Scope scope)
        {
            // Short-circuit before evaluating the right side, so "x != 0 && 10 / x > 1"
            // is safe. Evaluating both sides first is the classic way to make a guard
            // useless.
            if (binary.Operator == TokenKind.And)
            {
                return ScriptValue.FromBool(
                    Evaluate(binary.Left, scope).AsBool() && Evaluate(binary.Right, scope).AsBool());
            }

            if (binary.Operator == TokenKind.Or)
            {
                return ScriptValue.FromBool(
                    Evaluate(binary.Left, scope).AsBool() || Evaluate(binary.Right, scope).AsBool());
            }

            var left = Evaluate(binary.Left, scope);
            var right = Evaluate(binary.Right, scope);

            switch (binary.Operator)
            {
                case TokenKind.Equal:
                    return ScriptValue.FromBool(Equal(left, right));

                case TokenKind.NotEqual:
                    return ScriptValue.FromBool(!Equal(left, right));

                case TokenKind.Less:
                    return ScriptValue.FromBool(Compare(binary, left, right) < 0);

                case TokenKind.LessOrEqual:
                    return ScriptValue.FromBool(Compare(binary, left, right) <= 0);

                case TokenKind.Greater:
                    return ScriptValue.FromBool(Compare(binary, left, right) > 0);

                case TokenKind.GreaterOrEqual:
                    return ScriptValue.FromBool(Compare(binary, left, right) >= 0);
            }

            if (binary.Operator == TokenKind.Plus && left.Kind == ScriptKind.Text)
            {
                // Text concatenation: "+" on text, and only when the left side is text.
                // Anything else would make "1" + 2 ambiguous, and ambiguity in a language
                // that runs inside the simulation is a bug waiting for a modder.

                return ScriptValue.FromText(left.AsText() + Text(right));
            }

            return Arithmetic(binary, left, right);
        }

        private ScriptValue Arithmetic(BinaryExpression binary, ScriptValue left, ScriptValue right)
        {
            var kind = ScriptValue.Common(left, right);

            if (kind == ScriptKind.Fixed)
            {
                var a = left.AsFixed();
                var b = right.AsFixed();

                switch (binary.Operator)
                {
                    case TokenKind.Plus:
                        return ScriptValue.FromFixed(a + b);

                    case TokenKind.Minus:
                        return ScriptValue.FromFixed(a - b);

                    case TokenKind.Star:
                        return ScriptValue.FromFixed(a * b);

                    case TokenKind.Slash:
                        if (b.Data == 0)
                        {
                            throw new ScriptException("division by zero", binary.Line);
                        }

                        return ScriptValue.FromFixed(a / b);
                }

                throw new ScriptException(
                    "'%' is not defined for fixed numbers; convert to a number first",
                    binary.Line);
            }

            var x = left.AsInt();
            var y = right.AsInt();

            switch (binary.Operator)
            {
                case TokenKind.Plus:
                    return ScriptValue.FromInt(x + y);

                case TokenKind.Minus:
                    return ScriptValue.FromInt(x - y);

                case TokenKind.Star:
                    return ScriptValue.FromInt(x * y);

                case TokenKind.Slash:
                    if (y == 0)
                    {
                        throw new ScriptException("division by zero", binary.Line);
                    }

                    return ScriptValue.FromInt(x / y);

                case TokenKind.Percent:
                    if (y == 0)
                    {
                        throw new ScriptException("division by zero", binary.Line);
                    }

                    return ScriptValue.FromInt(x % y);
            }

            throw new ScriptException("unsupported operator", binary.Line);
        }


        private ScriptValue EvaluateAssign(AssignExpression assign, Scope scope)
        {
            var value = Evaluate(assign.Value, scope);

            if (assign.Operator != TokenKind.Assign)
            {
                ScriptValue current;

                if (!scope.TryGet(assign.Name, out current))
                {
                    throw new ScriptException("'" + assign.Name + "' is not declared", assign.Line);
                }

                var binary = new BinaryExpression();
                binary.Line = assign.Line;
                binary.Operator = assign.Operator == TokenKind.PlusAssign
                    ? TokenKind.Plus
                    : TokenKind.Minus;
                binary.Left = new LiteralExpression { Line = assign.Line, Value = current };
                binary.Right = new LiteralExpression { Line = assign.Line, Value = value };

                value = Arithmetic(binary, current, value);
            }

            if (!scope.TryAssign(assign.Name, value))
            {
                throw new ScriptException("'" + assign.Name + "' is not declared", assign.Line);
            }

            return value;
        }

        private static string Text(ScriptValue value)
        {
            return value.ToDisplay();
        }

        private static bool Equal(ScriptValue left, ScriptValue right)
        {
            if (left.Kind == ScriptKind.Text || right.Kind == ScriptKind.Text)
            {
                if (left.Kind != right.Kind)
                {
                    return false;
                }

                return left.AsText() == right.AsText();
            }

            var kind = ScriptValue.Common(left, right);

            return kind == ScriptKind.Fixed
                ? left.AsFixed().Data == right.AsFixed().Data
                : left.AsInt() == right.AsInt();
        }

        private int Compare(BinaryExpression binary, ScriptValue left, ScriptValue right)
        {
            if (left.Kind == ScriptKind.Text || right.Kind == ScriptKind.Text)
            {
                throw new ScriptException("text cannot be ordered", binary.Line);
            }

            var kind = ScriptValue.Common(left, right);

            return kind == ScriptKind.Fixed
                ? left.AsFixed().Data.CompareTo(right.AsFixed().Data)
                : left.AsInt().CompareTo(right.AsInt());
        }

        private void Spend(int line)
        {
            if (++steps <= host.StepBudget)
            {
                return;
            }

            // Named as a cycle rather than "too many operations": a script that reaches
            // this in the simulation has almost certainly looped for ever, and saying so
            // points at the real mistake.
            throw new ScriptException(
                "the script ran for more than " + host.StepBudget +
                " steps; it probably loops for ever", line);
        }
    }
}
