using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_JumpIf, end to end: from DECORATE text into a state that actually asks the question.
    ///
    /// The translator has its own tests. What only this can show is the join: that the generated
    /// condition function is a real function of the script, that the state carries it, and that the
    /// answer changes where the actor goes in a live world.
    ///
    /// The negative case is the one that matters most. A condition that always answers true would
    /// make this pass by accident, so one test defines a condition that is false and requires the
    /// jump **not** to happen - which is the only way to tell "the question was asked" from "the
    /// jump was taken unconditionally".
    /// </summary>
    [TestClass]
    public class JumpIfAppliedTest
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

        private static DoomGame NewGame(GameContent content, out TicCmd[] cmds)
        {
            var options = new GameOptions();
            options.GameVersion = content.Wad.GameVersion;
            options.GameMode = content.Wad.GameMode;
            options.MissionPack = content.Wad.MissionPack;
            options.Skill = GameSkill.Medium;
            options.Episode = 1;
            options.Map = 1;
            options.Players[0].InGame = true;

            var game = new DoomGame(content, options);
            game.DeferedInitNew();

            cmds = new TicCmd[Player.MaxPlayerCount];

            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);

            return game;
        }

        /// <summary>
        /// Builds a chain whose only frame loops to itself and jumps to frame two when the question
        /// is true. The condition is the whole point of the test, so it is a parameter.
        /// </summary>
        private static void DefineJumpIf(string condition)
        {
            var library = new ScriptLibrary();

            library.Add(
                "fn Thing_3001() {\n" +
                "    State(\"See#1\", \"TROO\", \"A\", 4, \"\", \"See#1\", \"See#2\", 0, \"Ask\");\n" +
                "    State(\"See#2\", \"TROO\", \"B\", 4, \"\", \"See#1\");\n" +
                "    SeeState(\"See#1\");\n" +
                "}\n" +
                "fn Ask() {\n" +
                "    return " + condition + ";\n" +
                "}\n", "test", DoomInfo.MobjInfos);

            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));
        }

        /// <summary>Which states the imps pass through, starting from the mod's first See frame.</summary>
        private static List<int> Watch(int health, out int first, out int second)
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                // "health > 50" - true for a healthy imp, false for a wounded one.
                DefineJumpIf("Health() > 50");

                var game = NewGame(content, out var cmds);
                var world = game.World;

                var imps = new List<Mobj>();

                foreach (var thinker in world.Thinkers)
                {
                    var mobj = thinker as Mobj;

                    if (mobj != null && mobj.Type == MobjType.Troop)
                    {
                        imps.Add(mobj);
                    }
                }

                Assert.IsTrue(imps.Count > 0, "MAP01 has imps to watch");

                var start = DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState;

                first = (int)start;
                second = DoomInfo.States[first].JumpTarget;

                Assert.IsTrue(second >= DoomInfo.CustomStateFirst);

                var seen = new List<int>();

                foreach (var imp in imps)
                {
                    imp.Health = health;
                    imp.ThinkerState = ThinkerState.Active;
                    imp.SetState(start);
                }

                for (var tic = 0; tic < 12; tic++)
                {
                    game.Update(cmds);

                    foreach (var imp in imps)
                    {
                        var number = imp.State.Number;

                        if (!seen.Contains(number))
                        {
                            seen.Add(number);
                        }
                    }
                }

                return seen;
            }
        }

        [TestMethod]
        public void ATrueConditionTakesTheJump()
        {
            int first, second;

            var seen = Watch(1000, out first, out second);

            CollectionAssert.Contains(seen, second,
                "health above the threshold, so the question is true and the jump happens");
        }

        [TestMethod]
        public void AFalseConditionDoesNotTakeTheJump()
        {
            int first, second;

            var seen = Watch(10, out first, out second);

            CollectionAssert.Contains(seen, first);
            CollectionAssert.DoesNotContain(seen, second,
                "health below the threshold, so the question is false and the frame loops instead");
        }

        [TestMethod]
        public void TheConverterProducesAConditionTheStateLayerAccepts()
        {
            // The whole path in one test: DECORATE text in, a state whose question is real out.
            var problems = new List<string>();

            var script = DecorateConverter.Convert(
                "ACTOR ConditionalImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 A_JumpIf(health > 500, \"Pain\")\n" +
                "    TROO B 4\n" +
                "    Loop\n" +
                "  Pain:\n" +
                "    TROO C 4\n" +
                "    Loop\n" +
                "  }\n" +
                "}\n", DoomInfo.MobjInfos, problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "fn Cond_ConditionalImp_See_1()");
            StringAssert.Contains(script, "return Health() > 500;");

            var library = new ScriptLibrary();
            library.Add(script, "converted", DoomInfo.MobjInfos);
            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));

            var see = DoomInfo.States[(int)DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState];

            Assert.IsNotNull(see.JumpCondition, "the state carries the question");
            Assert.AreEqual(0, see.JumpChance, "and not a chance, because the two are exclusive");
        }

        [TestMethod]
        public void AConditionThatNamesNothingCostsOnlyTheJump()
        {
            // A bad condition must leave the frame and the actor alone: the animation is still what
            // the author wrote.
            var problems = new List<string>();

            var script = DecorateConverter.Convert(
                "ACTOR BadCond 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 A_JumpIf(CountInv(\"Clip\") > 0, \"Pain\")\n" +
                "    TROO B 4\n" +
                "    Loop\n" +
                "  Pain:\n" +
                "    TROO C 4\n" +
                "    Loop\n" +
                "  }\n" +
                "}\n", DoomInfo.MobjInfos, problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "CountInv");
            StringAssert.Contains(text, "jump is dropped");

            // And the frames are still there, without a jump.
            StringAssert.Contains(script, "State(\"See#1\"");
            Assert.IsFalse(script.Contains("Cond_BadCond"), "no condition function was written");
        }
    }
}
