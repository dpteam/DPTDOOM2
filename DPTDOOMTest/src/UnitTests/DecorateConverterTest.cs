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

            Assert.AreEqual(1, actors[0].Unsupported.Count);
            Assert.AreEqual("SeeSound", actors[0].Unsupported[0].Key);
            Assert.AreEqual("demon/sight", actors[0].Unsupported[0].Value);
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
        public void AStatesSectionIsNoticedAndNotRead()
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
                info.Speed = 8;
                info.Flags &= ~MobjFlags.NoGravity;
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
                    "  States { Spawn: TROO A 10 A_Look Loop }\n" +
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
        public void ANewActorIsRefusedRatherThanHalfCreated()
        {
            // A doomednum this engine does not have needs states and a sprite name. Creating
            // the entry without them would produce an actor that cannot be placed, and the
            // message says exactly that.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                DecorateConverter.Convert(
                    "ACTOR BrandNew 13579 { Health 100 }", DoomInfo.MobjInfos, problems);

                Assert.AreEqual(1, problems.Count);

                var text = string.Join("; ", problems.ToArray());
                StringAssert.Contains(text, "13579");
                StringAssert.Contains(text, "not created");
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
