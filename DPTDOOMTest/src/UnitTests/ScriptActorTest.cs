using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Describing actors with SharpScript, and what happens when a script is wrong.
    ///
    /// The properties that matter here are not "the number was assigned". They are:
    ///
    ///  - a broken script costs its actor's behaviour and nothing else. The player still
    ///    gets a level, the other scripts still apply, and the report names the file and
    ///    the line. That is the same choice the content loader makes for a broken sprite,
    ///    and it is the difference between a mod that half-works and a game that will not
    ///    start.
    ///
    ///  - applying twice is refused. Every interesting definition is a read-then-write -
    ///    <c>Health(Health() * 2)</c> - so a second pass silently doubles it, and a
    ///    silent doubling is far worse than a message.
    ///
    ///  - the API is closed. A script cannot reach a file, a clock or a random number:
    ///    there is no expression in the language that names one. The tests below prove
    ///    it by trying.
    /// </summary>
    [TestClass]
    public class ScriptActorTest
    {
        /// <summary>
        /// A fresh copy of the actor table, so one test cannot leak into the next.
        ///
        /// A deep copy, and that detail is the whole point: <see cref="MobjInfo"/> is a
        /// class, so copying the array copies references and every test mutates the real
        /// engine table. That is exactly what happened the first time this was written -
        /// health values of 198 and 396 from three tests sharing one actor - and it is
        /// worth the twelve lines to make it impossible rather than to remember it.
        /// </summary>
        private static MobjInfo[] Table()
        {
            var source = DoomInfo.MobjInfos;
            var copy = new MobjInfo[source.Length];

            for (var i = 0; i < source.Length; i++)
            {
                var from = source[i];

                copy[i] = new MobjInfo(
                    from.DoomEdNum, from.SpawnState, from.SpawnHealth, from.SeeState,
                    from.SeeSound, from.ReactionTime, from.AttackSound, from.PainState,
                    from.PainChance, from.PainSound, from.MeleeState, from.MissileState,
                    from.DeathState, from.XdeathState, from.DeathSound, from.Speed,
                    from.Radius, from.Height, from.Mass, from.Damage, from.ActiveSound,
                    from.Flags, from.RaiseState);
            }

            return copy;
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

            Assert.Fail("the test table has no actor " + doomEdNum);
            return null;
        }

        // ------------------------------------------------------------ names

        [TestMethod]
        public void ActorNamesAreDoomEdNums()
        {
            int number;

            Assert.IsTrue(ScriptLibrary.TryParseActorName("Thing_3004", out number));
            Assert.AreEqual(3004, number);

            Assert.IsFalse(ScriptLibrary.TryParseActorName("Zombieman", out number),
                "a friendly name is not the naming scheme");
            Assert.IsFalse(ScriptLibrary.TryParseActorName("Thing_", out number));
            Assert.IsFalse(ScriptLibrary.TryParseActorName("Thing_abc", out number));
            Assert.IsFalse(ScriptLibrary.TryParseActorName(null, out number));
        }

        // ------------------------------------------------------------ applying

        [TestMethod]
        public void ANumberCanBeReadAndWritten()
        {
            var table = Table();
            var library = new ScriptLibrary();

            // 3004 is the Zombieman: 20 health, doubled to 40.
            library.Add("fn Thing_3004() { Health(Health() * 2); }", "test.dpts", table);
            library.ApplyTo(table);

            Assert.AreEqual(0, library.Problems.Count);
            Assert.AreEqual(40, Find(table, 3004).SpawnHealth);
        }

        [TestMethod]
        public void AFixedSizeCanBeWritten()
        {
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_3004() { Height(41.5); Radius(1.25); }", "test.dpts", table);
            library.ApplyTo(table);

            Assert.AreEqual(0, library.Problems.Count);

            var info = Find(table, 3004);
            Assert.AreEqual((Fixed.FromInt(41) + new Fixed(Fixed.FracUnit / 2)).Data, info.Height.Data);
            Assert.AreEqual((Fixed.FromInt(1) + new Fixed(Fixed.FracUnit / 4)).Data, info.Radius.Data);
        }

        [TestMethod]
        public void AStateCanBeSetByName()
        {
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_3004() { DeathState(\"SkelDie1\"); }", "test.dpts", table);
            library.ApplyTo(table);

            Assert.AreEqual(0, library.Problems.Count);
            Assert.AreEqual(MobjState.SkelDie1, Find(table, 3004).DeathState);
        }

        [TestMethod]
        public void AnUnknownStateIsNamed()
        {
            // "state 428 is out of range" tells a modder nothing; naming the string they
            // typed tells them they mistyped it.
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_3004() { DeathState(\"NoSuchState\"); }", "test.dpts", table);
            library.ApplyTo(table);

            Assert.AreEqual(1, library.Problems.Count);
            StringAssert.Contains(library.Problems[0], "no such state 'NoSuchState'");
        }

        [TestMethod]
        public void AFlagCanBeReadAndSet()
        {
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_3004() { Flag(\"CountKill\", false); }", "test.dpts", table);
            library.ApplyTo(table);

            Assert.AreEqual(0, library.Problems.Count);

            var info = Find(table, 3004);
            Assert.AreEqual((MobjFlags)0, info.Flags & MobjFlags.CountKill,
                "the flag the script cleared is clear");
        }

        [TestMethod]
        public void AnUnknownFlagIsNamed()
        {
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_3004() { Flag(\"NotAFlag\", true); }", "test.dpts", table);
            library.ApplyTo(table);

            Assert.AreEqual(1, library.Problems.Count);
            StringAssert.Contains(library.Problems[0], "no such flag 'NotAFlag'");
        }

        [TestMethod]
        public void ACombinationOfFlagsIsNotOneName()
        {
            // "Solid, Shootable" parses as one name in no language; the point is that a
            // combined numeric value is refused rather than silently setting both.
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_3004() { Flag(\"Solid, Shootable\", true); }", "test.dpts", table);
            library.ApplyTo(table);

            Assert.AreEqual(1, library.Problems.Count);
        }

        // ------------------------------------------------------------ failure modes

        [TestMethod]
        public void ABrokenScriptCostsItsActorAndNothingElse()
        {
            var table = Table();
            var library = new ScriptLibrary();

            library.Add(
                "fn Thing_3004() { Health(99); }\n" +
                "fn Thing_3005() { Health(NoSuchFunction()); }\n" +
                "fn Thing_3002() { Speed(13); }",
                "mixed.dpts", table);

            library.ApplyTo(table);

            // Two of the three applied, and the report names the broken one.
            Assert.AreEqual(99, Find(table, 3004).SpawnHealth);
            Assert.AreEqual(13, Find(table, 3002).Speed);
            Assert.AreEqual(1, library.Problems.Count);
            StringAssert.Contains(library.Problems[0], "mixed.dpts");
            StringAssert.Contains(library.Problems[0], "is not a function");
        }

        [TestMethod]
        public void ASyntaxErrorIsReportedWithItsLine()
        {
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_3004() {\n  Health(;\n}", "broken.dpts", table);

            Assert.AreEqual(1, library.Problems.Count);
            StringAssert.Contains(library.Problems[0], "broken.dpts");
            StringAssert.Contains(library.Problems[0], "line 2");
        }

        [TestMethod]
        public void AnActorThatDoesNotExistIsReported()
        {
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_9999() { Health(1); }", "test.dpts", table);

            Assert.AreEqual(1, library.Problems.Count);
            StringAssert.Contains(library.Problems[0], "no actor with DoomEdNum 9999");
        }

        [TestMethod]
        public void AScriptWithNoActorsSaysSo()
        {
            // Silently loading nothing would look like the script was applied and did
            // nothing, which is the hardest kind of problem to notice.
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Helper(n) { return n * 2; }", "helpers.dpts", table);

            Assert.AreEqual(1, library.Problems.Count);
            StringAssert.Contains(library.Problems[0], "Thing_<DoomEdNum>");
        }

        [TestMethod]
        public void HelperFunctionsAreAllowed()
        {
            // A helper is how a script shares arithmetic between two actors, and it must
            // not be reported as a mistake just because its name is not an actor.
            var table = Table();
            var library = new ScriptLibrary();

            library.Add(
                "fn Double(n) { return n * 2; }\n" +
                "fn Thing_3004() { Health(Double(Health())); }\n" +
                "fn Thing_3005() { Health(Double(Health())); }",
                "helpers.dpts", table);

            library.ApplyTo(table);

            Assert.AreEqual(0, library.Problems.Count);
            Assert.AreEqual(40, Find(table, 3004).SpawnHealth);
        }

        [TestMethod]
        public void ApplyingTwiceIsRefused()
        {
            // Every interesting definition is a read-then-write, so a second pass doubles
            // it. Refused rather than ignored: silence would leave the modder looking for
            // a bug in the numbers.
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_3004() { Health(Health() * 2); }", "test.dpts", table);

            library.ApplyTo(table);
            Assert.AreEqual(40, Find(table, 3004).SpawnHealth);

            library.ApplyTo(table);

            Assert.AreEqual(40, Find(table, 3004).SpawnHealth, "the value did not double again");
            Assert.AreEqual(1, library.Problems.Count);
            StringAssert.Contains(library.Problems[0], "already applied");
        }

        [TestMethod]
        public void ThePatchListSaysWhatWasDescribed()
        {
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_3004() { Health(1); }\nfn Thing_3005() { Health(1); }",
                "two.dpts", table);

            Assert.AreEqual(2, library.Patches.Count);
            Assert.AreEqual(3004, library.Patches[0].DoomEdNum);
            Assert.AreEqual("Thing_3004", library.Patches[0].Function);
            Assert.AreEqual("two.dpts", library.Patches[0].Source);
        }

        // ------------------------------------------------------------ the sandbox

        [TestMethod]
        public void AScriptCannotReachAnythingTheHostDidNotRegister()
        {
            // The whole sandbox, in one assertion: no file, no clock, no reflection - not
            // blocked, but unreachable, because there is no expression that names a type.
            var table = Table();
            var library = new ScriptLibrary();

            library.Add("fn Thing_3004() { Health(System.IO.File.ReadAllText(\"x\")); }",
                "evil.dpts", table);

            Assert.AreEqual(1, library.Problems.Count);
            StringAssert.Contains(library.Problems[0], "evil.dpts");
        }

        [TestMethod]
        public void AScriptHasNoRandomness()
        {
            // Two runs must produce the same actor. Randomness would make two machines in
            // a lockstep session disagree before the first tic.
            var first = Table();
            var second = Table();

            const string source = "fn Thing_3004() { var i = 0; while (i < 10) { Speed(Speed() + 1); i += 1; } }";

            var a = new ScriptLibrary();
            a.Add(source, "same.dpts", first);
            a.ApplyTo(first);

            var b = new ScriptLibrary();
            b.Add(source, "same.dpts", second);
            b.ApplyTo(second);

            Assert.AreEqual(0, a.Problems.Count);
            Assert.AreEqual(Find(first, 3004).Speed, Find(second, 3004).Speed);
        }
    }
}
