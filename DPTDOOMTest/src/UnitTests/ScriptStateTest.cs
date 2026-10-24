using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// A mod's actor can bring its own frames.
    ///
    /// This is the half of DECORATE support that the converter could not reach before, and it
    /// is checked through the real pipeline - script text, real library, real state table -
    /// because the thing that can go wrong is precisely the join between them: a frame that is
    /// defined but never linked, a name that resolves to the wrong slot, a sprite that the
    /// engine's enum calls something other than the WAD does.
    ///
    /// The last one is not hypothetical. The shotgun guy's sprite is <c>POSS</c> in the WAD
    /// and <c>SHTG</c> in this engine's enum, because the enum is named after the first frame
    /// that uses it. A mod writes POSS, so the lookup goes through the WAD's own name table.
    /// </summary>
    [TestClass]
    public class ScriptStateTest
    {
        [TestMethod]
        public void AChainOfFramesBecomesRealStates()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var table = DoomInfo.MobjInfos;
                var imp = Find(table, 3001);
                var originalSee = imp.SeeState;

                var library = new ScriptLibrary();
                library.Add(
                    "fn Thing_3001() {\n" +
                    "    State(\"See1\", \"TROO\", \"A\", 6, \"A_Look\", \"See2\");\n" +
                    "    State(\"See2\", \"TROO\", \"B\", 6, \"A_Chase\", \"See1\");\n" +
                    "    SeeState(\"See1\");\n" +
                    "}", "chain.dpts", table);

                library.ApplyTo(table);

                Assert.AreEqual(0, library.Problems.Count, string.Join("; ", ToArray(library.Problems)));

                var head = imp.SeeState;

                Assert.IsTrue((int)head >= DoomInfo.CustomStateFirst,
                    "the actor's See now starts at a mod state, got " + (int)head);

                var first = DoomInfo.States[(int)head];
                Assert.AreEqual(Sprite.TROO, first.Sprite);
                Assert.AreEqual(0, first.Frame, "frame A is index 0");
                Assert.AreEqual(6, first.Tics);
                Assert.IsNotNull(first.MobjAction, "A_Look was resolved to a real action");

                var second = DoomInfo.States[(int)first.Next];
                Assert.AreEqual(1, second.Frame, "frame B is index 1");

                // The chain is a cycle, which is what "Loop" means: the second frame must
                // point back at the first, and not at whatever happened to come next in the
                // table.
                Assert.AreEqual(head, second.Next, "the chain loops back to its head");

                imp.SeeState = originalSee;
            }
        }

        [TestMethod]
        public void TheSpriteNameIsTheWadsNotTheEnums()
        {
            // POSS is the WAD name. The engine's enum has a member for it too, but the
            // guarantee this test is after is weaker and more important than the name: the
            // index the lookup returns must be the index whose WAD name is the one asked for.
            // That is what makes a mod's "POSS" land on the sprite the WAD draws.
            Sprite sprite;

            Assert.IsTrue(ScriptActorApi.TryParseSprite("POSS", out sprite));

            Assert.AreEqual("POSS", DoomInfo.SpriteNames[(int)sprite].ToString(),
                "the answer must be the sprite whose WAD name is POSS");

            // And it is not the first entry, so a lookup that always returned zero would
            // fail here rather than pass by luck.
            Assert.AreNotEqual(0, (int)sprite);

            Assert.IsTrue(ScriptActorApi.TryParseSprite("TROO", out sprite));
            Assert.AreEqual(0, (int)sprite, "TROO is the first sprite");

            Assert.IsTrue(ScriptActorApi.TryParseSprite("pos", out sprite) == false,
                "a name that is not four letters is not a sprite");

            Assert.IsFalse(ScriptActorApi.TryParseSprite("XXXX", out sprite), "an unknown name");
            Assert.IsFalse(ScriptActorApi.TryParseSprite("TRO", out sprite), "a truncated name");
            Assert.IsFalse(ScriptActorApi.TryParseSprite(null, out sprite));
        }

        [TestMethod]
        public void FrameLettersCoverTheMirroredSetAndFullBright()
        {
            int frame;

            Assert.IsTrue(ScriptActorApi.TryParseFrame("A", out frame));
            Assert.AreEqual(0, frame);

            Assert.IsTrue(ScriptActorApi.TryParseFrame("Z", out frame));
            Assert.AreEqual(25, frame);

            // After Z DECORATE continues with punctuation for the second rotation, which is
            // the same thing the engine stores as 26..31.
            Assert.IsTrue(ScriptActorApi.TryParseFrame("[", out frame));
            Assert.AreEqual(26, frame);

            Assert.IsTrue(ScriptActorApi.TryParseFrame("`", out frame));
            Assert.AreEqual(31, frame);

            Assert.IsTrue(ScriptActorApi.TryParseFrame("+A", out frame));
            Assert.AreEqual(0x8000, frame, "a leading + is the full bright bit");

            Assert.IsFalse(ScriptActorApi.TryParseFrame("AB", out frame), "two letters is not a frame");
            Assert.IsFalse(ScriptActorApi.TryParseFrame("+", out frame));
            Assert.IsFalse(ScriptActorApi.TryParseFrame("", out frame));
        }

        [TestMethod]
        public void GotoAnEngineStateLinksIntoVanilla()
        {
            // "Stop" and "Goto See" in a mod's own chain must reach the engine's states, or a
            // mod could never hand control back.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var table = DoomInfo.MobjInfos;
                var imp = Find(table, 3001);
                var originalSee = imp.SeeState;

                var library = new ScriptLibrary();
                library.Add(
                    "fn Thing_3001() {\n" +
                    "    State(\"Mine\", \"TROO\", \"A\", 1, \"\", \"TrooDie1\");\n" +
                    "    SeeState(\"Mine\");\n" +
                    "}", "goto.dpts", table);

                library.ApplyTo(table);

                var head = DoomInfo.States[(int)imp.SeeState];
                Assert.AreEqual(MobjState.TrooDie1, head.Next,
                    "a name that is not the actor's own is looked up in the engine's states");

                imp.SeeState = originalSee;
            }
        }

        [TestMethod]
        public void AFrameThatLeadsNowhereIsReported()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var table = DoomInfo.MobjInfos;
                var imp = Find(table, 3001);
                var originalSee = imp.SeeState;

                var library = new ScriptLibrary();
                library.Add(
                    "fn Thing_3001() {\n" +
                    "    State(\"Mine\", \"TROO\", \"A\", 1, \"\", \"Nowhere\");\n" +
                    "    SeeState(\"Mine\");\n" +
                    "}", "dangling.dpts", table);

                library.ApplyTo(table);

                var text = string.Join("; ", ToArray(library.Problems));

                StringAssert.Contains(text, "Nowhere", "the missing name is named");
                StringAssert.Contains(text, "stops there", "and the outcome is stated");

                // The frame itself keeps pointing at Null, which removes the mobj. Losing the
                // actor is visible at once; a frame that silently did nothing would be a
                // mystery.
                var head = DoomInfo.States[(int)imp.SeeState];
                Assert.AreEqual(MobjState.Null, head.Next);

                imp.SeeState = originalSee;
            }
        }

        [TestMethod]
        public void AnActionThisEngineLacksIsRefusedWithTheListOfWhatItHas()
        {
            // "A_Jump" is the most natural thing a ZDoom modder writes, and the useful answer
            // is not "no" but "not here, and here is what there is".
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var table = DoomInfo.MobjInfos;

                var library = new ScriptLibrary();
                library.Add(
                    "fn Thing_3001() { State(\"X\", \"TROO\", \"A\", 1, \"A_Jump\", \"\"); }",
                    "jump.dpts", table);

                library.ApplyTo(table);

                var text = string.Join("; ", ToArray(library.Problems));

                StringAssert.Contains(text, "A_Jump");
                StringAssert.Contains(text, "A_Look",
                    "the message lists the actions that do exist");
            }
        }

        [TestMethod]
        public void TwoFramesWithOneNameAreRefused()
        {
            // The second would silently replace the first, and the chain would be a chain of
            // one frame pointing at itself.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var table = DoomInfo.MobjInfos;

                var library = new ScriptLibrary();
                library.Add(
                    "fn Thing_3001() {\n" +
                    "    State(\"Same\", \"TROO\", \"A\", 1, \"\", \"\");\n" +
                    "    State(\"Same\", \"TROO\", \"B\", 1, \"\", \"\");\n" +
                    "}", "dupe.dpts", table);

                library.ApplyTo(table);

                var text = string.Join("; ", ToArray(library.Problems));
                StringAssert.Contains(text, "already defined");
            }
        }

        [TestMethod]
        public void AnUnknownFlagCostsOnlyThatFlag()
        {
            // A throw while the definition runs costs the whole actor block - states
            // included - and reports itself as a script problem when the modder wrote
            // DECORATE. So the converter checks the name and skips just it. +PAINLESS is the
            // realistic case: it exists in ZDoom and ACE and not in this engine.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var table = DoomInfo.MobjInfos;
                var imp = Find(table, 3001);
                var originalSee = imp.SeeState;
                var originalHealth = imp.SpawnHealth;

                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR TestImp 3001\n" +
                    "{\n" +
                    "  Health 250\n" +
                    "  +PAINLESS\n" +
                    "  States { See: TROO A 3 A_Look Loop }\n" +
                    "}", table, problems);

                var text = string.Join("; ", problems.ToArray());

                StringAssert.Contains(text, "Painless");
                StringAssert.Contains(text, "no flag");

                Assert.IsFalse(script.Contains("PAINLESS"), "the unknown flag is not emitted");
                StringAssert.Contains(script, "Health(250)", "the rest of the block survives");
                StringAssert.Contains(script, "SeeState(", "and so do the states");

                var library = new ScriptLibrary();
                library.Add(script, "generated", table);
                library.ApplyTo(table);

                Assert.AreEqual(0, library.Problems.Count,
                    "nothing is left for the script to refuse");

                Assert.AreEqual(250, imp.SpawnHealth);
                Assert.IsTrue((int)imp.SeeState >= DoomInfo.CustomStateFirst);

                imp.SeeState = originalSee;
                imp.SpawnHealth = originalHealth;
            }
        }

        [TestMethod]
        public void TheStatesOfTwoActorsDoNotShareNames()
        {
            // Both are called "See", and they must not collide: a name is scoped to the actor
            // whose definition wrote it.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var table = DoomInfo.MobjInfos;
                var imp = Find(table, 3001);
                var zombie = Find(table, 3004);

                var impSee = imp.SeeState;
                var zombieSee = zombie.SeeState;

                var library = new ScriptLibrary();
                library.Add(
                    "fn Thing_3001() { State(\"See\", \"TROO\", \"A\", 1, \"\", \"\"); SeeState(\"See\"); }\n" +
                    "fn Thing_3004() { State(\"See\", \"POSS\", \"A\", 1, \"\", \"\"); SeeState(\"See\"); }",
                    "two.dpts", table);

                library.ApplyTo(table);

                Assert.AreEqual(0, library.Problems.Count, string.Join("; ", ToArray(library.Problems)));
                Assert.AreNotEqual(imp.SeeState, zombie.SeeState,
                    "one name per actor, not one name per run");

                imp.SeeState = impSee;
                zombie.SeeState = zombieSee;
            }
        }

        private static string[] ToArray(IList<string> list)
        {
            var array = new string[list.Count];

            for (var i = 0; i < array.Length; i++)
            {
                array[i] = list[i];
            }

            return array;
        }

        private static MobjInfo Find(MobjInfo[] table, int doomEdNum)
        {
            foreach (var info in table)
            {
                if (info.DoomEdNum == doomEdNum)
                {
                    return info;
                }
            }

            Assert.Fail("no actor with DoomEdNum " + doomEdNum);
            return null;
        }
    }
}
