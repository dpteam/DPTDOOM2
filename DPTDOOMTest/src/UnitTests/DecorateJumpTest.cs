using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;
using DPTDOOMTest.CompatibilityTests;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// A_Jump: leaving a frame for another state, on a chance.
    ///
    /// This is the one DECORATE construct that changes **what a frame does** rather than how it
    /// looks, and it is why many mods do not move at all without it. It is modelled as a property
    /// of the state's transition rather than as an action, because the engine's action signature
    /// carries no arguments and giving it some would touch all fifty of its own actions.
    ///
    /// What is checked here is the conversion and the wiring: the chance and the label survive from
    /// the DECORATE text into the state table, an unsupported form is named instead of guessed at,
    /// and a label that leads nowhere costs the jump rather than the frame.
    /// </summary>
    [TestClass]
    public class DecorateJumpTest
    {
        private ActorTableSnapshot snapshot;

        [TestInitialize]
        public void Before()
        {
            snapshot = ActorTableSnapshot.Take();
        }

        [TestCleanup]
        public void After()
        {
            snapshot.Restore();
        }

        private static DecorateActor ParseSingle(string text, out List<string> problems)
        {
            problems = new List<string>();

            var actors = DecorateConverter.Parse(text, problems);

            Assert.AreEqual(1, actors.Count, "one actor in, one actor out");
            return actors[0];
        }

        private static string Convert(string text, out List<string> problems)
        {
            problems = new List<string>();
            return DecorateConverter.Convert(text, DoomInfo.MobjInfos, problems);
        }

        [TestMethod]
        public void AJumpBecomesAStateProperty()
        {
            List<string> problems;

            var actor = ParseSingle(
                "ACTOR Jumper 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 A_Jump(128, \"Pain\")\n" +
                "    TROO B 4\n" +
                "  Pain:\n" +
                "    TROO C 4\n" +
                "  }\n" +
                "}", out problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var see = actor.Chains[0];

            Assert.AreEqual("See", see.Label);
            Assert.AreEqual("Pain", see.Frames[0].JumpLabel, "the label the jump leaves for");
            Assert.AreEqual(128, see.Frames[0].JumpChance);
            Assert.AreEqual("", see.Frames[0].Action,
                "and it is not left as an action, because the engine has none by that name");
        }

        [TestMethod]
        public void TheLargestChanceMeansAlways()
        {
            List<string> problems;

            var actor = ParseSingle(
                "ACTOR Jumper 3001\n{\n  States\n  {\n  See:\n" +
                "    TROO A 4 A_Jump(256, \"Pain\")\n  Pain:\n    TROO B 4\n  }\n}",
                out problems);

            Assert.AreEqual(256, actor.Chains[0].Frames[0].JumpChance);
        }

        [TestMethod]
        public void AJumpByFrameCountIsNamedAndDropped()
        {
            // A relative offset counts states, not frames of this chain, and guessing would move
            // the actor somewhere the author never meant.
            List<string> problems;

            var actor = ParseSingle(
                "ACTOR Jumper 3001\n{\n  States\n  {\n  See:\n" +
                "    TROO A 4 A_Jump(128, 2)\n    TROO B 4\n  }\n}",
                out problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "frame count");

            Assert.IsNull(actor.Chains[0].Frames[0].JumpLabel);
            Assert.AreEqual("", actor.Chains[0].Frames[0].Action, "the frame is kept, the jump is not");
        }

        [TestMethod]
        public void AJumpWithoutALabelIsNamed()
        {
            List<string> problems;

            ParseSingle(
                "ACTOR Jumper 3001\n{\n  States\n  {\n  See:\n" +
                "    TROO A 4 A_Jump(128)\n    TROO B 4\n  }\n}",
                out problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "needs a chance and a label");
        }

        [TestMethod]
        public void ArgumentsOnAnActionThatTakesNoneAreNamed()
        {
            // The engine's own actions take no arguments, so a list after one is a modder's
            // expectation that cannot be met - said once, with the action kept.
            List<string> problems;

            var actor = ParseSingle(
                "ACTOR Jumper 3001\n{\n  States\n  {\n  See:\n" +
                "    TROO A 4 A_Look(1, 2)\n    TROO B 4\n  }\n}",
                out problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "takes no arguments");
            Assert.AreEqual("A_Look", actor.Chains[0].Frames[0].Action, "the action still runs");
        }

        [TestMethod]
        public void TheConvertedScriptCarriesTheJump()
        {
            List<string> problems;

            var script = Convert(
                "ACTOR Jumper 3001\n{\n  States\n  {\n  See:\n" +
                "    TROO A 4 A_Jump(64, \"Pain\")\n    TROO B 4\n  Pain:\n    TROO C 4\n  }\n}",
                out problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            // The ninth and tenth arguments of State(): the target frame and the chance.
            StringAssert.Contains(script, "\"See#1\"");
            StringAssert.Contains(script, "\"Pain#1\", 64");
        }

        [TestMethod]
        public void AJumpToAnotherActorsChainIsLeftAsAName()
        {
            // "A_Jump(128, \"See\")" from a chain that has no own See must reach the engine's
            // state, the same way a Goto does.
            List<string> problems;

            var script = Convert(
                "ACTOR Jumper 3001\n{\n  States\n  {\n  Missile:\n" +
                "    TROO A 4 A_Jump(128, \"See\")\n    TROO B 4\n  }\n}",
                out problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "\"See\", 128");
        }

        [TestMethod]
        public void TheScriptAppliesTheJumpToTheState()
        {
            // The join: what the converter wrote, run through the script layer, has to end up as a
            // jump in the state table.
            var library = new ScriptLibrary();

            library.Add(
                "fn Thing_3001() {\n" +
                "    State(\"See#1\", \"TROO\", \"A\", 4, \"\", \"See#2\", \"See#2\", 200);\n" +
                "    State(\"See#2\", \"TROO\", \"B\", 4, \"\", \"See#1\");\n" +
                "    SeeState(\"See#1\");\n" +
                "}\n", "test", DoomInfo.MobjInfos);

            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));

            var see = DoomInfo.States[(int)DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState];

            Assert.IsTrue(see.Number >= DoomInfo.CustomStateFirst,
                "the actor's own See chain, not the engine's");

            Assert.AreEqual(200, see.JumpChance);
            Assert.IsTrue(see.JumpTarget >= DoomInfo.CustomStateFirst, "and the target is a state of its own");
        }

        [TestMethod]
        public void AJumpToANameThatIsNotAStateCostsOnlyTheJump()
        {
            var library = new ScriptLibrary();

            library.Add(
                "fn Thing_3001() {\n" +
                "    State(\"See#1\", \"TROO\", \"A\", 4, \"\", \"See#2\", \"Nowhere#1\", 128);\n" +
                "    State(\"See#2\", \"TROO\", \"B\", 4, \"\", \"See#1\");\n" +
                "}\n", "test", DoomInfo.MobjInfos);

            library.ApplyTo(DoomInfo.MobjInfos);

            var problems = new List<string>(library.Problems);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains(problems[0], "Nowhere#1");
            StringAssert.Contains(problems[0], "jump is dropped");
        }
    }
}
