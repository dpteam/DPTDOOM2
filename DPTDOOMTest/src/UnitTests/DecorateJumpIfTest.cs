using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// A_JumpIf: leaving a frame when a question about the actor is true.
    ///
    /// The question is DECORATE's, the answer is this engine's own language, so the condition is
    /// **translated** rather than passed through. That choice is what these tests are mostly about:
    /// an unknown word has to name itself in the load report, not surface as a script error three
    /// layers down.
    /// </summary>
    [TestClass]
    public class DecorateJumpIfTest
    {
        private static string Translate(string text, out List<string> problems)
        {
            problems = new List<string>();
            return DecorateCondition.Translate(text, "TestActor", 7, problems);
        }

        private static string All(List<string> problems)
        {
            return string.Join("; ", problems.ToArray());
        }

        [TestMethod]
        public void AComparisonIsTranslated()
        {
            List<string> problems;

            var script = Translate("health < 100", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual("Health() < 100", script);
        }

        [TestMethod]
        public void EveryFieldHasItsCall()
        {
            List<string> problems;

            Assert.AreEqual("Health() == 1", Translate("health == 1", out problems));
            Assert.AreEqual("MomZ() < 0", Translate("momz < 0", out problems));
            Assert.AreEqual("MomX() + MomY() > 0", Translate("momx + momy > 0", out problems));
            Assert.AreEqual("Z() < 0", Translate("z < 0", out problems));
            Assert.AreEqual("Tics() == 1", Translate("tics == 1", out problems));

            Assert.AreEqual(0, problems.Count, All(problems));
        }

        [TestMethod]
        public void TargetIsAnExistenceQuestion()
        {
            // DECORATE writes "target != null" and means "does this actor have a target". This
            // engine answers it as a count, so the translation is not a plain rename.
            List<string> problems;

            var script = Translate("target != null", out problems);

            // "null" is not a word this engine has, so it is refused - and that is the honest
            // answer rather than quietly turning it into zero.
            Assert.IsNull(script);
            StringAssert.Contains(All(problems), "null");
        }

        [TestMethod]
        public void TargetAloneIsTranslated()
        {
            List<string> problems;

            var script = Translate("target", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual("Target()", script);
        }

        [TestMethod]
        public void BooleanOperatorsSurvive()
        {
            List<string> problems;

            var script = Translate("health < 50 && momz <= 0 || !target", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual("Health() < 50 && MomZ() <= 0 || !Target()", script);
        }

        [TestMethod]
        public void PrecedenceIsPreservedByParentheses()
        {
            List<string> problems;

            var script = Translate("(health + 10) * 2 > 100", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual("(Health() + 10) * 2 > 100", script);
        }

        [TestMethod]
        public void RandomIsCarriedOver()
        {
            // Conditions use random() to vary a behaviour, and this engine answers it from the
            // world generator so the variation stays in step across a session.
            List<string> problems;

            var script = Translate("random(1, 4) == 1", out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual("Random(1, 4) == 1", script);
        }

        [TestMethod]
        public void AnUnknownWordIsNamed()
        {
            // The important refusal: DECORATE has hundreds of fields and functions this engine does
            // not, and passing one through would produce a script that fails to run - a message in
            // the wrong language at the wrong time.
            List<string> problems;

            var script = Translate("CountInv(\"Clip\") > 0", out problems);

            Assert.IsNull(script);
            StringAssert.Contains(All(problems), "CountInv");
            StringAssert.Contains(All(problems), "not a field or function");
        }

        [TestMethod]
        public void TheListOfWhatExistsIsGiven()
        {
            // A refusal a modder cannot act on is only half a refusal.
            List<string> problems;

            Translate("velx > 0", out problems);

            var text = All(problems);
            StringAssert.Contains(text, "health");
            StringAssert.Contains(text, "momz");
            StringAssert.Contains(text, "random");
        }

        [TestMethod]
        public void AnUnclosedBracketIsNamed()
        {
            List<string> problems;

            var script = Translate("(health < 10", out problems);

            Assert.IsNull(script);
            StringAssert.Contains(All(problems), "never closed");
        }

        [TestMethod]
        public void TrailingRubbishIsNamed()
        {
            List<string> problems;

            var script = Translate("health < 10 10", out problems);

            Assert.IsNull(script);
            StringAssert.Contains(All(problems), "not understood");
        }
    }
}
