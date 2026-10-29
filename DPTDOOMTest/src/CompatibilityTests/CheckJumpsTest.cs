using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_CheckFloor and A_CheckFlag: the two conditional jumps that ask about position and about a
    /// flag. Both reuse the jump machinery A_JumpIf already uses, so what these tests have to prove
    /// is the question, not the mechanism: a true answer leaves for the label, a false one loops.
    /// </summary>
    [TestClass]
    public class CheckJumpsTest
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

        private static List<Mobj> Imps(World world)
        {
            var imps = new List<Mobj>();

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;

                if (mobj != null && mobj.Type == MobjType.Troop)
                {
                    imps.Add(mobj);
                }
            }

            return imps;
        }

        private static string Convert(string body, List<string> problems)
        {
            return DecorateConverter.Convert(
                "ACTOR CheckImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 " + body + "\n" +
                "    TROO B 4\n" +
                "    Loop\n" +
                "  Hit:\n" +
                "    TROO C 4\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);
        }

        private static void ApplyScript(string script)
        {
            var library = new ScriptLibrary();
            library.Add(script, "test", DoomInfo.MobjInfos);
            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));
        }

        /// <summary>
        /// Which states the imp passes through after being placed in the mod's See chain.
        ///
        /// The jump target is read here, while the mod is still applied: disposing the content puts
        /// the actor table back, so a target read after the block would describe the vanilla imp and
        /// every comparison would be against the wrong state.
        /// </summary>
        private static List<int> Watch(string body, Action<Mobj, World> prepare, out int jumpTarget)
        {
            var problems = new List<string>();
            var script = Convert(body, problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out var cmds);
                var world = game.World;
                var imps = Imps(world);

                Assert.IsTrue(imps.Count > 0, "MAP01 has imps to watch");

                var start = DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState;

                jumpTarget = DoomInfo.States[(int)start].JumpTarget;

                Assert.IsTrue(jumpTarget >= DoomInfo.CustomStateFirst,
                    "the mod's jump target is a state of its own");

                foreach (var imp in imps)
                {
                    imp.ThinkerState = ThinkerState.Active;
                    prepare(imp, world);
                    imp.SetState(start);
                }

                var seen = new List<int>();

                for (var tic = 0; tic < 16; tic++)
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
        public void CheckFloorJumpsWhenTheActorStandsOnTheFloor()
        {
            int target;
            var seen = Watch("A_CheckFloor(\"Hit\")", (imp, world) =>
            {
                imp.Z = imp.FloorZ;
            }, out target);

            CollectionAssert.Contains(seen, target, "a standing imp leaves for the label");
        }

        [TestMethod]
        public void CheckFloorDoesNotJumpWhileAirborne()
        {
            int target;
            var seen = Watch("A_CheckFloor(\"Hit\")", (imp, world) =>
            {
                imp.Z = imp.FloorZ + Fixed.FromInt(64);

                // Otherwise the imp falls to the floor during the first tic and the question
                // becomes true for a reason that has nothing to do with the jump.
                imp.Flags |= MobjFlags.NoGravity;
            }, out target);

            CollectionAssert.DoesNotContain(seen, target,
                "an imp in the air loops instead of taking the jump");
        }

        [TestMethod]
        public void CheckFlagJumpsWhenTheFlagIsSet()
        {
            int target;
            var seen = Watch("A_CheckFlag(\"AMBUSH\", \"Hit\")", (imp, world) =>
            {
                imp.Flags |= MobjFlags.Ambush;
            }, out target);

            CollectionAssert.Contains(seen, target);
        }

        [TestMethod]
        public void CheckFlagDoesNotJumpWhenTheFlagIsClear()
        {
            int target;
            var seen = Watch("A_CheckFlag(\"AMBUSH\", \"Hit\")", (imp, world) =>
            {
                imp.Flags &= ~MobjFlags.Ambush;
            }, out target);

            CollectionAssert.DoesNotContain(seen, target);
        }

        [TestMethod]
        public void CheckFlagWithATracerPointerReadsTheTracer()
        {
            // The imp itself does not have the flag; only its tracer does. Reading the wrong actor
            // would make this jump fire, and reading the right one would not.
            int target;
            var seen = Watch("A_CheckFlag(\"AMBUSH\", \"Hit\", AAPTR_TRACER)", (imp, world) =>
            {
                imp.Flags &= ~MobjFlags.Ambush;
                imp.Tracer = world.ConsolePlayer.Mobj;
                imp.Tracer.Flags |= MobjFlags.Ambush;
            }, out target);

            CollectionAssert.Contains(seen, target,
                "the tracer's flag is the one that decides");
        }

        [TestMethod]
        public void TheConverterWritesBothQuestions()
        {
            var problems = new List<string>();

            var floor = Convert("A_CheckFloor(\"Hit\")", problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(floor, "return OnFloor();");
            StringAssert.Contains(floor, "fn Cond_CheckImp_See_1()");

            var flag = Convert("A_CheckFlag(\"AMBUSH\", \"Hit\")", problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(flag, "return HasFlag(\"AMBUSH\", 0);",
                "the default pointer is the actor itself");
        }

        [TestMethod]
        public void AnUnknownFlagCostsOnlyTheJump()
        {
            var problems = new List<string>();
            var script = Convert("A_CheckFlag(\"FOILINVUL\", \"Hit\")", problems);

            var text = string.Join("; ", problems.ToArray());
            StringAssert.Contains(text, "FOILINVUL");
            StringAssert.Contains(text, "jump is dropped");
            StringAssert.Contains(script, "State(\"See#1\"");
            Assert.IsFalse(script.Contains("Cond_CheckImp"), "no condition function was written");
        }

        [TestMethod]
        public void ARelativeLabelIsNamedRatherThanGuessed()
        {
            // Both forms appear in ademo1.wad: A_CheckFloor(1) and A_CheckFlag("NOTARGET", 6).
            // The number counts states, not frames of this chain, so guessing would move the actor
            // somewhere the author never meant.
            var problems = new List<string>();

            Convert("A_CheckFloor(1)", problems);

            var text = string.Join("; ", problems.ToArray());
            StringAssert.Contains(text, "frame count");
            StringAssert.Contains(text, "jump is dropped");
        }
    }
}
