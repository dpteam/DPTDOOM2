using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;
using DPTDOOMTest.CompatibilityTests;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// A repeated actor definition replaces the earlier one, and the file still loads.
    ///
    /// This is what older ports did - they printed a warning about the duplicate and used the later
    /// block - and newer ones made it an error. Old WADs were written against the former, so refusing
    /// the file would make them unusable: and the refusal here was not even limited to the duplicate,
    /// because the duplicate function name cost the whole compilation unit.
    /// </summary>
    [TestClass]
    public class DuplicateDefinitionTest
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

        private static string Convert(string text, out List<string> problems)
        {
            problems = new List<string>();
            return DecorateConverter.Convert(text, DoomInfo.MobjInfos, problems);
        }

        [TestMethod]
        public void TheSameActorDefinedTwiceKeepsTheLaterBlock()
        {
            var script = Convert(
                "ACTOR TwiceImp 3001\n{\n  Health 100\n}\n" +
                "ACTOR TwiceImp 3001\n{\n  Health 900\n}\n", out var problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "defined again");

            // One function, and it carries the later health: two functions with one name would be a
            // parse error for the whole file.
            Assert.AreEqual(1, CountOccurrences(script, "fn Thing_3001()"),
                "one definition survives");
            StringAssert.Contains(script, "Health(900);");
            Assert.IsFalse(script.Contains("Health(100);"), "the replaced block emits nothing");
        }

        [TestMethod]
        public void TheDuplicateDoesNotCostTheRestOfTheFile()
        {
            // The old failure: the duplicate function name made the parser refuse the unit, so the
            // other actor in the same file disappeared as well.
            var script = Convert(
                "ACTOR TwiceImp 3001\n{\n  Health 100\n}\n" +
                "ACTOR TwiceImp 3001\n{\n  Health 900\n}\n" +
                "ACTOR OtherImp 3002\n{\n  Health 400\n}\n", out var problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "defined again");
            StringAssert.Contains(script, "fn Thing_3002()",
                "the unrelated actor is still converted");
        }

        [TestMethod]
        public void TwoNamesOnOneDoomEdNumAreNamed()
        {
            var script = Convert(
                "ACTOR FirstImp 3001\n{\n  Health 100\n}\n" +
                "ACTOR SecondImp 3001\n{\n  Health 900\n}\n", out var problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "DoomEdNum 3001");
            StringAssert.Contains(text, "FirstImp");
            StringAssert.Contains(text, "SecondImp");
            Assert.AreEqual(1, CountOccurrences(script, "fn Thing_3001()"));
            StringAssert.Contains(script, "Health(900);");
        }

        [TestMethod]
        public void TheReplacedNameNoLongerSpawnsAnything()
        {
            // The replaced definition does not exist any more, so a name that pointed at it must not
            // silently spawn the replacement.
            var problems = new List<string>();

            DecorateConverter.Convert(
                "ACTOR FirstImp 3001\n{\n  Health 100\n}\n" +
                "ACTOR SecondImp 3001\n{\n  Health 900\n}\n" +
                "ACTOR SpawnerImp 3003\n{\n  Health 50\n}\n", DoomInfo.MobjInfos, problems);

            Assert.AreEqual(-1, ActorNames.TypeOf("FirstImp"),
                "the replaced actor is not registered by name");
        }

        [TestMethod]
        public void ARepeatedFunctionInSharpScriptReplacesInsteadOfFailing()
        {
            var library = new ScriptLibrary();

            library.Add(
                "fn Thing_3001() {\n    Health(100);\n}\n" +
                "fn Thing_3001() {\n    Health(900);\n}\n", "twice.dpts", DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                "a duplicate is not a parse failure: " +
                string.Join("; ", new List<string>(library.Problems).ToArray()));
            Assert.AreEqual(1, library.Warnings.Count,
                "and it is still said out loud");
            StringAssert.Contains(string.Join("; ", new List<string>(library.Warnings).ToArray()),
                "defined again");

            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(900, DoomInfo.MobjInfos[(int)MobjType.Troop].SpawnHealth,
                "the later definition is the one that took effect");
        }

        [TestMethod]
        public void TheWarningNamesBothLines()
        {
            var library = new ScriptLibrary();

            library.Add(
                "fn Thing_3001() {\n    Health(100);\n}\n\n\n" +
                "fn Thing_3001() {\n    Health(900);\n}\n", "twice.dpts", DoomInfo.MobjInfos);

            var text = string.Join("; ", new List<string>(library.Warnings).ToArray());

            // Line 1 is the first definition, line 6 the second: fn, Health, }, two blanks, fn.
            StringAssert.Contains(text, "line 1");
            StringAssert.Contains(text, "line 6");
        }

        private static int CountOccurrences(string text, string needle)
        {
            var count = 0;
            var index = 0;

            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }
    }
}
