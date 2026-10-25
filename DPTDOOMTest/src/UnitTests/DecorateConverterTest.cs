using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// DECORATE is read and turned into SharpScript, and both halves are checked here.
    ///
    /// The reading half is where a real DECORATE file differs from a tidy example: properties
    /// in any order, hexadecimal numbers, comments in two styles, quoted sound names, states
    /// that this engine cannot accept, and flags written both as <c>+SOLID</c> and as
    /// <c>MONSTER</c>. The writing half is checked by *running* the generated text through
    /// the real script library against the real actor table - the same path a PWAD takes - so
    /// a converter that emits something that merely looks right fails here.
    ///
    /// The refusals matter as much as the conversions. A converter that guesses at a
    /// doomednum rewrites the wrong actor and says nothing, which is the worst failure this
    /// project has a rule about: broken content must say what it did instead.
    /// </summary>
    [TestClass]
    public class DecorateConverterTest
    {
        // ------------------------------------------------------------ reading

        [TestMethod]
        public void AnActorCarriesItsNumbersAndItsFlags()
        {
            var problems = new List<string>();
            var actors = DecorateConverter.Parse(
                "ACTOR MyImp 3001\n" +
                "{\n" +
                "  Health 90\n" +
                "  Speed 10\n" +
                "  Radius 20\n" +
                "  Height 56\n" +
                "  PainChance 150\n" +
                "  Mass 120\n" +
                "  Damage 5\n" +
                "  +SOLID\n" +
                "  -SHOOTABLE\n" +
                "}", problems);

            Assert.AreEqual(0, problems.Count);
            Assert.AreEqual(1, actors.Count);

            var actor = actors[0];
            Assert.AreEqual("MyImp", actor.Name);
            Assert.AreEqual(3001, actor.DoomEdNum);

            var numbers = new Dictionary<string, int>();

            foreach (var pair in actor.Numbers)
            {
                numbers[pair.Key] = pair.Value;
            }

            Assert.AreEqual(90, numbers["Health"]);
            Assert.AreEqual(10, numbers["Speed"]);
            Assert.AreEqual(20, numbers["Radius"]);
            Assert.AreEqual(56, numbers["Height"]);
            Assert.AreEqual(150, numbers["PainChance"]);
            Assert.AreEqual(120, numbers["Mass"]);
            Assert.AreEqual(5, numbers["Damage"]);

            Assert.AreEqual(2, actor.Flags.Count);
            Assert.AreEqual("Solid", actor.Flags[0].Key);
            Assert.IsTrue(actor.Flags[0].Value);
            Assert.AreEqual("Shootable", actor.Flags[1].Key, "the shouting becomes the engine's spelling");
            Assert.IsFalse(actor.Flags[1].Value);
        }

        [TestMethod]
        public void CommentsAndQuotedNamesAreSkipped()
        {
            // Two comment styles and a quoted sound, which is the shape of a real file's
            // header. The quoted name must not be read as a property value for the
            // property before it.
            var problems = new List<string>();
            var actors = DecorateConverter.Parse(
                "// a line comment\n" +
                "/* and a block\n" +
                "   comment */\n" +
                "ACTOR Thing 3002\n" +
                "{\n" +
                "  Health 60 // trailing\n" +
                "  SeeSound \"demon/sight\"\n" +
                "  Speed 9\n" +
                "}", problems);

            Assert.AreEqual(1, actors.Count);

            var numbers = new Dictionary<string, int>();

            foreach (var pair in actors[0].Numbers)
            {
                numbers[pair.Key] = pair.Value;
            }

            Assert.AreEqual(60, numbers["Health"]);
            Assert.AreEqual(9, numbers["Speed"], "the sound line must not swallow the next property");

            // A sound now converts rather than being refused: the value is a sound name from
            // SNDINFO, and the engine resolves a name it does not recognise through SNDINFO at
            // play time. The point of this assertion is that the quoted name stays whole.
            Assert.AreEqual(0, actors[0].Unsupported.Count,
                "a sound is no longer an unsupported property");
            Assert.AreEqual(1, actors[0].Sounds.Count);
            Assert.AreEqual("SeeSound", actors[0].Sounds[0].Key);
            Assert.AreEqual("demon/sight", actors[0].Sounds[0].Value);
        }

        [TestMethod]
        public void HexadecimalDoomedNumsAreRead()
        {
            // Doomednums are written in hex in most DECORATE files that use them at all.
            var problems = new List<string>();
            var actors = DecorateConverter.Parse("ACTOR X 0x0BBD { Health 1 }", problems);

            Assert.AreEqual(1, actors.Count);
            Assert.AreEqual(3005, actors[0].DoomEdNum, "0x0BBD is the cacodemon");
        }

        [TestMethod]
        public void SpawnIdGivesTheDoomedNumAsAProperty()
        {
            // Both spellings appear in the wild: the number after the actor name, and the
            // spawnid property. ACE - whose DECORATE subset is the target here - documents
            // spawnid as the way to give an actor a map number.
            var problems = new List<string>();
            var actors = DecorateConverter.Parse("ACTOR X { spawnid 3005 Health 10 }", problems);

            Assert.AreEqual(3005, actors[0].DoomEdNum);

            // And it works in hex, because doomednums usually are.
            actors = DecorateConverter.Parse("ACTOR Y { spawnid 0x0BBD }", problems);
            Assert.AreEqual(3005, actors[0].DoomEdNum);
        }

        [TestMethod]
        public void ReplacesAndParentAreReadInEitherOrder()
        {
            // DECORATE allows "ACTOR Name : Parent replaces Other" and the reverse, and real
            // files contain both.
            var problems = new List<string>();

            var first = DecorateConverter.Parse(
                "ACTOR A : DoomImp replaces Zombieman 3004 { Health 1 }", problems);

            Assert.AreEqual("DoomImp", first[0].ParentName);
            Assert.AreEqual("Zombieman", first[0].Replaces);
            Assert.AreEqual(3004, first[0].DoomEdNum);

            var second = DecorateConverter.Parse(
                "ACTOR B replaces Zombieman : DoomImp { Health 1 }", problems);

            Assert.AreEqual("DoomImp", second[0].ParentName);
            Assert.AreEqual("Zombieman", second[0].Replaces);
        }

        [TestMethod]
        public void AStatesSectionIsRead()
        {
            var problems = new List<string>();
            var actors = DecorateConverter.Parse(
                "ACTOR X 3001\n" +
                "{\n" +
                "  Health 40\n" +
                "  States\n" +
                "  {\n" +
                "  Spawn:\n" +
                "    TROO A 10 A_Look\n" +
                "    Loop\n" +
                "  See:\n" +
                "    TROO AABBCCDD 4 A_Chase\n" +
                "    Loop\n" +
                "  }\n" +
                "}", problems);

            Assert.AreEqual(1, actors.Count);
            Assert.IsTrue(actors[0].HasStates);
            Assert.AreEqual(40, actors[0].Numbers[0].Value, "the properties before States survive");

            Assert.AreEqual(2, actors[0].Chains.Count);

            var spawn = actors[0].Chains[0];
            Assert.AreEqual("Spawn", spawn.Label);
            Assert.AreEqual(1, spawn.Frames.Count);
            Assert.AreEqual("TROO", spawn.Frames[0].Sprite);
            Assert.AreEqual("A", spawn.Frames[0].Letter);
            Assert.AreEqual(10, spawn.Frames[0].Tics);
            Assert.AreEqual("A_Look", spawn.Frames[0].Action);
            Assert.AreEqual("Loop", spawn.Next);

            // A run of letters is a run of frames, which is the part of DECORATE most often
            // misread: "AABBCCDD" is eight frames, not one.
            var see = actors[0].Chains[1];
            Assert.AreEqual(8, see.Frames.Count, "AABBCCDD is eight frames");
            Assert.AreEqual("A", see.Frames[0].Letter);
            Assert.AreEqual("D", see.Frames[7].Letter);
            Assert.AreEqual(4, see.Frames[7].Tics, "and all of them share the tics that follow");
            Assert.AreEqual("A_Chase", see.Frames[7].Action);
        }

        [TestMethod]
        public void AFullBrightFrameIsMarked()
        {
            var problems = new List<string>();
            var actors = DecorateConverter.Parse(
                "ACTOR X 3001 { States { Missile: TROO A^ 2 Bright A_Fire Loop } }", problems);

            var frame = actors[0].Chains[0].Frames[0];

            // Both spellings mean the same thing: the caret after the letters, and the word
            // Bright after the tics.
            StringAssert.Contains(frame.Letter, "+", "the full-bright bit is carried to the script");
        }

        [TestMethod]
        public void AnUnclosedBlockIsReportedRatherThanSwallowed()
        {
            // A truncated file must not take the next actor with it silently.
            var problems = new List<string>();
            var actors = DecorateConverter.Parse("ACTOR X 3001 { Health 4", problems);

            Assert.AreEqual(1, actors.Count);
            Assert.IsTrue(problems.Count > 0, "the missing brace is reported");
        }

        [TestMethod]
        public void MonsterAndProjectileExpandToFlagsThisEngineHas()
        {
            var problems = new List<string>();

            var monster = DecorateConverter.Parse("ACTOR M 3001 { MONSTER }", problems);
            var names = new List<string>();

            foreach (var pair in monster[0].Flags)
            {
                names.Add(pair.Key);
            }

            CollectionAssert.Contains(names, "Solid");
            CollectionAssert.Contains(names, "Shootable");
            CollectionAssert.Contains(names, "CountKill");

            var projectile = DecorateConverter.Parse("ACTOR P 3001 { PROJECTILE }", problems);
            names.Clear();

            foreach (var pair in projectile[0].Flags)
            {
                names.Add(pair.Key);
            }

            // The same set a vanilla rocket has: this is what makes the expansion match what
            // the engine already does rather than something invented.
            CollectionAssert.Contains(names, "Missile");
            CollectionAssert.Contains(names, "NoBlockMap");
            CollectionAssert.Contains(names, "NoGravity");
            CollectionAssert.Contains(names, "DropOff");
        }

        // ------------------------------------------------------------ writing, and applying

        [TestMethod]
        public void TheGeneratedScriptChangesTheActorInTheRealTable()
        {
            // The whole point: the text this converter writes must be accepted by the real
            // script pipeline and must reach the real actor. Anything less would be a
            // converter tested against its own idea of the engine.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var table = DoomInfo.MobjInfos;
                var original = Find(table, 3001).SpawnHealth;
                var originalSpeed = Find(table, 3001).Speed;
                var originalFlags = Find(table, 3001).Flags;

                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR TestImp 3001\n" +
                    "{\n" +
                    "  Health " + (original + 7) + "\n" +
                    "  Speed 7\n" +
                    "  +NOGRAVITY\n" +
                    "}", table, problems);

                Assert.AreEqual(0, problems.Count);

                var library = new ScriptLibrary();
                library.Add(script, "generated", table);
                library.ApplyTo(table);

                Assert.AreEqual(0, library.Problems.Count);

                var info = Find(table, 3001);
                Assert.AreEqual(original + 7, info.SpawnHealth);
                Assert.AreEqual(7, info.Speed);
                Assert.IsTrue((info.Flags & MobjFlags.NoGravity) != 0, "+NOGRAVITY reached the actor");

                // Put it back: the actor table is a static, and a test that leaves the imp
                // altered would quietly change every test after it.
                info.SpawnHealth = original;
                info.Speed = originalSpeed;
                info.Flags = originalFlags;
            }
        }

        [TestMethod]
        public void AStatesSectionReachesTheActorThroughTheScript()
        {
            // The whole point of the converter, end to end: a DECORATE States section must
            // become script text that the library accepts and the state table performs.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var table = DoomInfo.MobjInfos;
                var imp = Find(table, 3001);
                var originalSee = imp.SeeState;

                // The generated script sets Health as well as the states, and every one of
                // these fields is global static state shared with every later test in this
                // process. Leaving the imp at 400 health changed the world hash of an
                // unrelated demo test and looked, from the failure, like a simulation
                // regression - which is the worst kind of test bug: it accuses the code.
                var originalHealth = imp.SpawnHealth;

                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR TestImp 3001\n" +
                    "{\n" +
                    "  Health 400\n" +
                    "  States\n" +
                    "  {\n" +
                    "  See:\n" +
                    "    TROO A 3 A_Look\n" +
                    "    TROO B 3 A_Chase\n" +
                    "    Loop\n" +
                    "  }\n" +
                    "}", table, problems);

                Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

                StringAssert.Contains(script, "SeeState(\"See#1\");",
                    "the label the engine knows is bound to the actor");

                var library = new ScriptLibrary();
                library.Add(script, "generated", table);
                library.ApplyTo(table);

                Assert.AreEqual(0, library.Problems.Count,
                    library.Problems.Count == 0 ? "" : library.Problems[0]);

                var head = DoomInfo.States[(int)imp.SeeState];

                Assert.AreEqual(Sprite.TROO, head.Sprite);
                Assert.AreEqual(3, head.Tics);
                Assert.IsNotNull(head.MobjAction, "A_Look became a real action");

                var second = DoomInfo.States[(int)head.Next];
                Assert.AreEqual(1, second.Frame, "the B frame");

                Assert.AreEqual((int)imp.SeeState, (int)second.Next, "Loop goes back to the head");

                imp.SeeState = originalSee;
                imp.SpawnHealth = originalHealth;

                Assert.AreEqual(originalSee, imp.SeeState, "the actor is put back exactly");
                Assert.AreEqual(originalHealth, imp.SpawnHealth, "health too");
            }
        }

        [TestMethod]
        public void AStatesSectionIsReportedNotIgnored()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                DecorateConverter.Convert(
                    "ACTOR TestImp 3001\n" +
                    "{\n" +
                    "  Health 100\n" +
                    "  States { }\n" +
                    "}", DoomInfo.MobjInfos, problems);

                var text = string.Join("; ", problems.ToArray());

                StringAssert.Contains(text, "States",
                    "the report must name the part that did not come across");
                StringAssert.Contains(text, "keeps the engine's own states",
                    "and must say what happened instead");
            }
        }

        [TestMethod]
        public void AnUnknownActorNameIsRefusedWithAReason()
        {
            // Guessing here would rewrite whatever number the guess landed on. Refusing costs
            // the modder one line.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR Weird replaces NotAVanillaName { Health 100 }",
                    DoomInfo.MobjInfos, problems);

                Assert.AreEqual(1, problems.Count);
                StringAssert.Contains(string.Join("; ", problems.ToArray()), "does not");

                Assert.IsFalse(script.Contains("Thing_"),
                    "no patch is written for an actor that was not resolved");
            }
        }

        [TestMethod]
        public void ANewActorIsCreatedInTheReserve()
        {
            // A doomednum this engine does not have, with a parent: a new actor. It must be
            // created rather than refused, because a mod that can only change existing actors
            // cannot add a monster at all - and adding one is the first thing a DECORATE mod
            // does.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR BrandNew : DoomImp { spawnid 13579 Health 100 }",
                    DoomInfo.MobjInfos, problems);

                Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

                var library = new ScriptLibrary();
                library.Add(script, "generated", DoomInfo.MobjInfos);
                library.ApplyTo(DoomInfo.MobjInfos);

                Assert.AreEqual(0, library.Problems.Count,
                    library.Problems.Count == 0 ? "" : library.Problems[0]);

                var type = DoomInfo.TypeOf(13579);

                Assert.IsTrue(type >= DoomInfo.CustomActorFirst,
                    "the actor got a slot in the reserve, got " + type);

                StringAssert.Contains(script, "Thing_13579");

                // Inherited from the imp, which is what ":" means: a block that sets only
                // numbers must not lose the states it never mentioned.
                var info = DoomInfo.MobjInfos[type];
                Assert.AreNotEqual(MobjState.Null, info.SeeState, "the parent's see state came across");
                Assert.AreEqual(100, info.SpawnHealth);
            }
        }

        [TestMethod]
        public void ANewActorWithoutAParentIsRefused()
        {
            // Nothing to inherit from and no number: the block names no actor at all, and
            // guessing would create something with no states.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                DecorateConverter.Convert(
                    "ACTOR Orphan : NoSuchParent { Health 100 }", DoomInfo.MobjInfos, problems);

                Assert.AreEqual(1, problems.Count);

                var text = string.Join("; ", problems.ToArray());
                StringAssert.Contains(text, "NoSuchParent");
                StringAssert.Contains(text, "nothing to inherit");
            }
        }

        [TestMethod]
        public void ReplacingByKnownNameResolvesToItsDoomedNum()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR MyZombie replaces Zombieman { Health 33 }", DoomInfo.MobjInfos, problems);

                Assert.AreEqual(0, problems.Count);
                StringAssert.Contains(script, "Thing_3004");
            }
        }

        [TestMethod]
        public void APropertyWithNoCounterpartIsNamed()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                DecorateConverter.Convert(
                    "ACTOR TestImp 3001 { Health 10 Scale 1.5 }", DoomInfo.MobjInfos, problems);

                var text = string.Join("; ", problems.ToArray());

                StringAssert.Contains(text, "Scale");
                StringAssert.Contains(text, "no counterpart");
            }
        }

        [TestMethod]
        public void ANumberPropertyWithTextInItIsReportedAtItsOwnLine()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                DecorateConverter.Convert(
                    "ACTOR TestImp 3001\n" +
                    "{\n" +
                    "  Health lots\n" +
                    "}", DoomInfo.MobjInfos, problems);

                Assert.AreEqual(1, problems.Count);

                var text = string.Join("; ", problems.ToArray());
                StringAssert.Contains(text, "Health");
                StringAssert.Contains(text, "whole number");
            }
        }

        [TestMethod]
        public void AnUnsupportedPropertyIsReportedAtItsOwnLine()
        {
            // A block with four refused properties all reported at line 1 makes the reader
            // find them by hand, and a modder with a two-hundred-line file will not.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                DecorateConverter.Convert(
                    "ACTOR TestImp 3001\n" +
                    "{\n" +
                    "  Health 100\n" +
                    "  Scale 1.5\n" +
                    "  Alpha 0.5\n" +
                    "}", DoomInfo.MobjInfos, problems);

                Assert.AreEqual(2, problems.Count);

                var text = string.Join("; ", problems.ToArray());

                StringAssert.Contains(text, "line 4", "Scale is on line 4");
                StringAssert.Contains(text, "line 5", "Alpha is on line 5");
            }
        }

        [TestMethod]
        public void AnActorWithNothingToSayProducesNoPatch()
        {
            // An empty Thing_ body would still be an entry in the report and would look like
            // work done.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR Quiet 3001 { Scale 2.0 }", DoomInfo.MobjInfos, problems);

                Assert.IsFalse(script.Contains("fn Thing_"));
            }
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
