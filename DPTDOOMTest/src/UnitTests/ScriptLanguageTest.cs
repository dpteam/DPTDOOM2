using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The SharpScript language itself: lexer, parser, interpreter.
    ///
    /// These tests are about the properties that make a scripting layer safe to run
    /// inside the simulation, not about syntax for its own sake. Three of them matter
    /// more than the rest:
    ///
    ///  - deterministic arithmetic. The numbers a script computes must match what the
    ///    engine computes for the same values, or two machines in a lockstep session
    ///    drift apart. That is why fixed-point literals are parsed from decimal digits
    ///    without ever becoming a float.
    ///
    ///  - termination. The language has `while`, so a mistake in a script can loop for
    ///    ever, and a loop inside the game loop is a freeze with the window still
    ///    painted. The step budget has to stop it, and recursion needs its own limit or
    ///    a cycle would reset the budget on every call.
    ///
    ///  - nothing outside the host. A script can call a registered function and do
    ///    arithmetic; there is no path to a type, a file or the clock.
    /// </summary>
    [TestClass]
    public class ScriptLanguageTest
    {
        private static ScriptHost Host()
        {
            var host = new ScriptHost();

            host.Register("Add", 2, call => ScriptValue.FromInt(call.AsInt(0) + call.AsInt(1)));

            host.Register("Print", 1, call =>
            {
                output = call.At(0).ToDisplay();
                return ScriptValue.Void;
            });

            // Named conversions rather than implicit ones: mixing an integer and a fixed
            // value in arithmetic promotes to fixed, but going the other way is a
            // truncation and has to be asked for.
            host.Register("ToFixed", 1, call => ScriptValue.FromFixed(call.AsFixed(0)));
            host.Register("ToInt", 1, call => ScriptValue.FromInt(call.AsInt(0)));

            return host;
        }

        private static string output;

        private static ScriptValue Run(string source, params ScriptValue[] arguments)
        {
            var unit = ScriptParser.Parse(source);
            var interpreter = new ScriptInterpreter(unit, Host());

            return interpreter.Call("Main", arguments);
        }

        private static int RunInt(string body)
        {
            return Run("fn Main() { " + body + " }").AsInt();
        }

        private static ScriptException Fails(string source)
        {
            try
            {
                Run(source);
            }
            catch (ScriptException e)
            {
                return e;
            }

            Assert.Fail("expected the script to be rejected: " + source);
            return null;
        }

        // ------------------------------------------------------------ numbers

        [TestMethod]
        public void IntegersAddUp()
        {
            Assert.AreEqual(7, RunInt("return 3 + 4;"));
            Assert.AreEqual(1, RunInt("return 3 - 2;"));
            Assert.AreEqual(12, RunInt("return 3 * 4;"));
            Assert.AreEqual(3, RunInt("return 7 / 2;"), "integer division truncates");
            Assert.AreEqual(1, RunInt("return 7 % 2;"));
        }

        [TestMethod]
        public void PrecedenceFollowsArithmetic()
        {
            Assert.AreEqual(14, RunInt("return 2 + 3 * 4;"));
            Assert.AreEqual(20, RunInt("return (2 + 3) * 4;"));
            Assert.AreEqual(1, RunInt("return 10 - 4 - 5;"), "subtraction is left to right");
            Assert.AreEqual(8, RunInt("return 16 / 4 / 1 * 2;"));
        }

        [TestMethod]
        public void AFixedLiteralIsExact()
        {
            // 1.5 is one and a half in 16.16, exactly. A float round trip would give the
            // same answer here, which is why the interesting case is the next test; this
            // one pins the obvious property first.
            var value = Run("fn Main() { return ToFixed(1.5); }");

            Assert.AreEqual(ScriptKind.Fixed, value.Kind);
            Assert.AreEqual((Fixed.FromInt(1) + new Fixed(Fixed.FracUnit / 2)).Data,
                value.AsFixed().Data);
        }

        [TestMethod]
        public void AFixedLiteralIsNotBuiltThroughAFloat()
        {
            // 0.1 is the case every decimal-to-binary conversion gets wrong. In 16.16 the
            // nearest value to a tenth is 6554/65536. Parsing the digits as an integer and
            // dividing gives exactly that; going through a double and multiplying can land
            // on 6553 or 6555 depending on the platform's rounding.
            var value = Run("fn Main() { return ToFixed(0.1); }");

            Assert.AreEqual(6554, value.AsFixed().Data,
                "a tenth in 16.16 is 6554, and the literal must agree with the engine");
        }

        [TestMethod]
        public void FixedArithmeticMatchesTheEngine()
        {
            const string source =
                "fn Main() { return ToFixed(1.5) * ToFixed(2) + ToFixed(0.25); }";

            var value = Run(source);

            // The same numbers the engine would compute, through the same operator.
            var expected = Fixed.FromInt(1) + new Fixed(Fixed.FracUnit / 2);
            expected = expected * Fixed.FromInt(2);
            expected = expected + new Fixed(Fixed.FracUnit / 4);

            Assert.AreEqual(expected.Data, value.AsFixed().Data);
        }

        [TestMethod]
        public void AnIntegerBecomesAFixedWithoutLoss()
        {
            // The one implicit conversion: 1 is exactly 1.0, so it can never lose anything.
            var value = Run("fn Main() { return ToFixed(2) + 0.5; }");

            Assert.AreEqual(ScriptKind.Fixed, value.Kind);
            Assert.AreEqual((Fixed.FromInt(2) + new Fixed(Fixed.FracUnit / 2)).Data,
                value.AsFixed().Data);
        }

        [TestMethod]
        public void AnOverlongFixedLiteralIsRefused()
        {
            // Wrapping a literal is a number that is silently wrong by a whole
            // revolution, which is the kind of bug that takes a day to find.
            var error = Fails("fn Main() { return ToFixed(99999.5); }");

            StringAssert.Contains(error.Message, "bad number");
        }

        [TestMethod]
        public void DivisionByZeroIsReportedWithALine()
        {
            var error = Fails("fn Main() {\n  return 1 / 0;\n}");

            StringAssert.Contains(error.Message, "division by zero");
            Assert.AreEqual(2, error.Line, "and points at the line that did it");
        }

        // ------------------------------------------------------------ text

        [TestMethod]
        public void TextConcatenatesOnTheLeft()
        {
            var value = Run("fn Main() { return \"a\" + \"b\"; }");

            Assert.AreEqual(ScriptKind.Text, value.Kind);
            Assert.AreEqual("ab", value.AsText());
        }

        [TestMethod]
        public void TextPicksUpNumbersWhenTheLeftSideIsText()
        {
            // Printing is the main use: building "Health: 42" without a conversion call
            // for every number.
            var value = Run("fn Main() { return \"picked \" + 42; }");

            Assert.AreEqual("picked 42", value.AsText());
        }

        [TestMethod]
        public void AddingToANumberIsNotTextConcatenation()
        {
            // "1" + 2 is text, but 1 + "2" is a mistake, and saying so beats guessing.
            var error = Fails("fn Main() { return 1 + \"2\"; }");

            StringAssert.Contains(error.Message, "cannot combine");
        }

        [TestMethod]
        public void EscapesAreUnderstood()
        {
            var value = Run("fn Main() { return \"a\\tb\\nc\\\"d\\\\e\"; }");

            Assert.AreEqual("a\tb\nc\"d\\e", value.AsText());
        }

        [TestMethod]
        public void AnUnterminatedStringNamesTheProblem()
        {
            var error = Fails("fn Main() {\n  return \"oops;\n}");

            StringAssert.Contains(error.Message, "missing a closing quote");
            Assert.AreEqual(2, error.Line);
        }

        // ------------------------------------------------------------ comparisons

        [TestMethod]
        public void ComparisonsProduceBooleans()
        {
            Assert.AreEqual(1, RunInt("return 2 < 3;") );
            Assert.AreEqual(0, RunInt("return 3 < 2;"));
            Assert.AreEqual(1, RunInt("return 2 <= 2;"));
            Assert.AreEqual(1, RunInt("return true;"));
            Assert.AreEqual(1, RunInt("return 2 == 2;"));
            Assert.AreEqual(0, RunInt("return 2 != 2;"));
        }

        [TestMethod]
        public void AndBindsTighterThanOr()
        {
            // false && false || true is true only if && binds tighter. Getting this
            // backwards is the classic silent precedence bug.
            Assert.AreEqual(1, RunInt("return false && false || true;"));

            // The mirror case: with && tighter, "true || false && false" is true. A parser
            // that gave || the higher precedence would answer false here.
            Assert.AreEqual(1, RunInt("return true || false && false;"));
            Assert.AreEqual(0, RunInt("return (true || false) && false;"));
        }

        [TestMethod]
        public void AndOrShortCircuit()
        {
            // The guard is what makes "x != 0 && 10 / x" safe. Evaluating both sides
            // first would make every such guard useless.
            Assert.AreEqual(0, RunInt("return 0 != 0 && 10 / 0 > 1;"));

            // And the other direction: a true short-circuit means the division is never
            // reached, so no exception.
            Assert.AreEqual(1, RunInt("return 1 == 1 || 10 / 0 > 1;"));
        }

        [TestMethod]
        public void TextCanBeComparedForEqualityButNotOrdered()
        {
            Assert.AreEqual(1, RunInt("return \"a\" == \"a\";"));
            Assert.AreEqual(0, RunInt("return \"a\" == \"b\";"));

            var error = Fails("fn Main() { return \"a\" < \"b\"; }");
            StringAssert.Contains(error.Message, "cannot be ordered");
        }

        // ------------------------------------------------------------ control flow

        [TestMethod]
        public void IfElseChoosesABranch()
        {
            Assert.AreEqual(10, RunInt("if (1 < 2) { return 10; } else { return 20; }"));
            Assert.AreEqual(20, RunInt("if (2 < 1) { return 10; } else { return 20; }"));
        }

        [TestMethod]
        public void WhileLoops()
        {
            Assert.AreEqual(15, RunInt(
                "var total = 0; var i = 1; while (i <= 5) { total += i; i += 1; } return total;"));
        }

        [TestMethod]
        public void BreakLeavesTheLoop()
        {
            Assert.AreEqual(3, RunInt(
                "var i = 0; while (true) { i += 1; if (i == 3) { break; } } return i;"));
        }

        [TestMethod]
        public void ContinueSkipsTheRestOfTheBody()
        {
            // Sum the even numbers from 1 to 10: 2+4+6+8+10.
            Assert.AreEqual(30, RunInt(
                "var total = 0; var i = 0; while (i < 10) { i += 1;" +
                " if (i % 2 == 1) { continue; } total += i; } return total;"));
        }

        [TestMethod]
        public void BreakOutsideALoopIsRefused()
        {
            var error = Fails("fn Main() { break; }");

            StringAssert.Contains(error.Message, "outside a loop");
        }

        [TestMethod]
        public void AVariableMustBeDeclaredBeforeUse()
        {
            var error = Fails("fn Main() { return missing; }");

            StringAssert.Contains(error.Message, "is not declared");
        }

        [TestMethod]
        public void ABlockScopeHidesTheOuterVariable()
        {
            // Shadowing in an inner block is what a block is for; declaring the same name
            // twice in one block is a typo, and saying so beats the second having no
            // effect.
            Assert.AreEqual(1, RunInt("var x = 1; { var x = 2; } return x;"));
        }

        [TestMethod]
        public void RedeclaringInOneBlockIsRefused()
        {
            var error = Fails("fn Main() { var x = 1; var x = 2; return x; }");

            StringAssert.Contains(error.Message, "already declared");
        }

        // ------------------------------------------------------------ functions

        [TestMethod]
        public void ScriptFunctionsCanBeCalled()
        {
            var unit = ScriptParser.Parse(
                "fn Twice(n) { return n * 2; } fn Main() { return Twice(6); }");

            Assert.AreEqual(12, new ScriptInterpreter(unit, Host()).Call("Main").AsInt());
        }

        [TestMethod]
        public void ArgumentsArePassedByValue()
        {
            var unit = ScriptParser.Parse(
                "fn Bump(n) { n += 1; return n; }" +
                "fn Main() { var x = 1; Bump(x); return x; }");

            Assert.AreEqual(1, new ScriptInterpreter(unit, Host()).Call("Main").AsInt(),
                "the caller's variable was not changed");
        }

        [TestMethod]
        public void RecursionStopsAtTheDepthLimit()
        {
            // The step budget is per call, so a cycle between functions would reset it on
            // every entry and never end. The depth limit is what actually stops recursion.
            var error = Fails("fn Down(n) { return Down(n + 1); } fn Main() { return Down(0); }");

            StringAssert.Contains(error.Message, "nested too deeply");
        }

        [TestMethod]
        public void AWrongArgumentCountIsRefused()
        {
            var unit = ScriptParser.Parse("fn Twice(n) { return n * 2; } fn Main() { return Twice(); }");

            try
            {
                new ScriptInterpreter(unit, Host()).Call("Main");
                Assert.Fail("calling with no argument should fail");
            }
            catch (ScriptException e)
            {
                StringAssert.Contains(e.Message, "takes 1 argument");
            }
        }

        [TestMethod]
        public void AFallingOffTheEndReturnsNothing()
        {
            // A statement-like script is written without a return, and that must not be
            // an error.
            var value = Run("fn Main() { Print(1); }");

            Assert.IsTrue(value.IsVoid);
        }

        // ------------------------------------------------------------ the sandbox

        [TestMethod]
        public void AnUnknownFunctionIsRefused()
        {
            // The whole sandbox in one test: there is no path to anything the host did not
            // register. No file, no clock, no reflection - not "blocked", but unreachable.
            var error = Fails("fn Main() { return NoSuchFunction(); }");

            StringAssert.Contains(error.Message, "is not a function");
        }

        [TestMethod]
        public void ThereIsNoMemberAccessToReachTheRuntime()
        {
            // A dot does not parse at all, so "File.Read(...)" is a syntax error rather
            // than something the sandbox has to block. That is the stronger statement:
            // there is no expression in the language that can name a type.
            var error = Fails("fn Main() { return File.Read(\"x\"); }");

            StringAssert.Contains(error.Message, "Dot");
        }

        [TestMethod]
        public void NamesInTheHostAreCallable()
        {
            var unit = ScriptParser.Parse("fn Main() { return Add(2, 3); }");

            Assert.AreEqual(5, new ScriptInterpreter(unit, Host()).Call("Main").AsInt());
        }

        [TestMethod]
        public void AScriptFunctionShadowsAHostFunction()
        {
            // An embedder can let a PWAD override a name without changing the host.
            var unit = ScriptParser.Parse("fn Add(a, b) { return 99; } fn Main() { return Add(1, 1); }");

            Assert.AreEqual(99, new ScriptInterpreter(unit, Host()).Call("Main").AsInt());
        }

        [TestMethod]
        public void ARunawayLoopIsStopped()
        {
            // A loop inside the simulation is a freeze with the window still painted. The
            // budget turns it into a reported error after a moment.
            var host = Host();
            host.StepBudget = 1000;

            var unit = ScriptParser.Parse("fn Main() { var i = 0; while (true) { i += 1; } return i; }");

            try
            {
                new ScriptInterpreter(unit, host).Call("Main");
                Assert.Fail("the loop should have been stopped");
            }
            catch (ScriptException e)
            {
                StringAssert.Contains(e.Message, "loops for ever");
            }
        }

        // ------------------------------------------------------------ determinism

        [TestMethod]
        public void TheSameScriptGivesTheSameAnswerEveryTime()
        {
            // The property the whole design exists for: no clock, no random, no state
            // between calls, so two machines in a lockstep session compute the same thing.
            const string source =
                "fn Main(seed) {" +
                "  var total = 0; var i = 0;" +
                "  while (i < 20) { total = (total * 31 + seed + i) % 1000; i += 1; }" +
                "  return total;" +
                "}";

            var unit = ScriptParser.Parse(source);

            var first = new ScriptInterpreter(unit, Host()).Call("Main", ScriptValue.FromInt(7));
            var second = new ScriptInterpreter(unit, Host()).Call("Main", ScriptValue.FromInt(7));

            Assert.AreEqual(first.AsInt(), second.AsInt());
        }

        [TestMethod]
        public void TheInterpreterKeepsNothingBetweenCalls()
        {
            // If it did, a script would be able to remember something the other end of a
            // lockstep session does not, and the worlds would drift.
            var unit = ScriptParser.Parse("fn Main() { var counter = 0; counter += 1; return counter; }");
            var interpreter = new ScriptInterpreter(unit, Host());

            Assert.AreEqual(1, interpreter.Call("Main").AsInt());
            Assert.AreEqual(1, interpreter.Call("Main").AsInt(), "the second call starts fresh");
        }

        // ------------------------------------------------------------ syntax errors

        [TestMethod]
        public void ASyntaxErrorNamesTheLine()
        {
            var error = Fails("fn Main() {\n  var x = ;\n}");

            Assert.AreEqual(2, error.Line);
            StringAssert.Contains(error.Message, "line 2");
        }

        [TestMethod]
        public void AMissingBraceIsReported()
        {
            var error = Fails("fn Main() {\n  return 1;\n");

            StringAssert.Contains(error.Message, "missing '}'");
        }

        [TestMethod]
        public void ANestedFunctionIsRefused()
        {
            // Closures need captured environments, captured environments outlive the
            // call, and state that outlives the call is what the design forbids.
            var error = Fails("fn Main() { fn Inner() { return 1; } return 2; }");

            StringAssert.Contains(error.Message, "only allowed at the top level");
        }

        [TestMethod]
        public void AnEmptyScriptIsRefused()
        {
            var error = Fails("// nothing here");

            StringAssert.Contains(error.Message, "at least one function");
        }

        [TestMethod]
        public void ADuplicateFunctionReplacesTheEarlierOne()
        {
            // Older ports warned about a repeated actor and used the later block; newer ones refuse
            // the file. Refusing is what makes an old WAD unusable, and it used to cost every other
            // actor in the same file as well, so the later definition wins and the duplicate is a
            // warning rather than a parse failure.
            var unit = ScriptParser.Parse("fn Main() { return 1; } fn Main() { return 2; }");

            Assert.AreEqual(1, unit.Functions.Count, "one function survives");
            Assert.AreEqual(1, unit.Warnings.Count, "and the duplicate is still reported");
            StringAssert.Contains(unit.Warnings[0], "defined again");

            var interpreter = new ScriptInterpreter(unit, new ScriptHost());
            Assert.AreEqual(2, interpreter.Call("Main").AsInt(),
                "the later definition is the one that runs");
        }

        [TestMethod]
        public void BothCommentStylesAreIgnored()
        {
            var value = Run(
                "// a line comment\n" +
                "-- and the form a DECORATE author writes\n" +
                "fn Main() { /* block\n comment */ return 5; }");

            Assert.AreEqual(5, value.AsInt());
        }

        [TestMethod]
        public void TheConditionalOperatorChoosesASide()
        {
            Assert.AreEqual(7, RunInt("return true ? 7 : 9;"));
            Assert.AreEqual(9, RunInt("return false ? 7 : 9;"));
        }
    }
}
