using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;
using DPTDOOMTest.CompatibilityTests;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The DECORATE actions that take arguments and are supported: A_JumpIfHealthLower,
    /// A_JumpIfCloser and A_PlaySound.
    ///
    /// The first two are the same question as A_JumpIf, spelled differently, so they are translated
    /// into it rather than given a mechanism of their own - one jump implementation, three
    /// spellings. A_PlaySound is a piece of work rather than a transition, so it is attached to its
    /// frame afterwards.
    ///
    /// What is **not** supported is pinned here too: A_CustomMissile and A_SpawnItem need an
    /// actor-name table covering projectiles, and this project deliberately does not guess at one.
    /// </summary>
    [TestClass]
    public class DecorateActionArgumentsTest
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

            Assert.AreEqual(1, actors.Count);
            return actors[0];
        }

        private static string All(List<string> problems)
        {
            return string.Join("; ", problems.ToArray());
        }

        private static string ActorWith(string action)
        {
            return
                "ACTOR ArgImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 " + action + "\n" +
                "    TROO B 4\n" +
                "  Pain:\n" +
                "    TROO C 4\n" +
                "  }\n" +
                "}\n";
        }

        [TestMethod]
        public void HealthLowerBecomesACondition()
        {
            List<string> problems;

            var actor = ParseSingle(
                ActorWith("A_JumpIfHealthLower(50, \"Pain\")"), out problems);

            Assert.AreEqual(0, problems.Count, All(problems));

            var frame = actor.Chains[0].Frames[0];

            Assert.AreEqual("Pain", frame.JumpLabel);
            Assert.AreEqual("Health() < 50", frame.JumpCondition,
                "the action is the same question as A_JumpIf, so it becomes the same condition");
        }

        [TestMethod]
        public void CloserBecomesACondition()
        {
            List<string> problems;

            var actor = ParseSingle(
                ActorWith("A_JumpIfCloser(128, \"Pain\")"), out problems);

            Assert.AreEqual(0, problems.Count, All(problems));

            var frame = actor.Chains[0].Frames[0];

            // Not "Distance() < 128": the original also requires the two actors to overlap
            // vertically unless noz is given, so the question is a call and not a bare comparison.
            // The horizontal-only version was an approximation that made a monster react to
            // something a floor above it.
            Assert.AreEqual("CloserToTarget(128, 0)", frame.JumpCondition);
        }

        [TestMethod]
        public void HealthLowerWithoutANumberIsReported()
        {
            List<string> problems;

            ParseSingle(ActorWith("A_JumpIfHealthLower(target, \"Pain\")"), out problems);

            StringAssert.Contains(All(problems), "needs a whole number and a label");
        }

        [TestMethod]
        public void PlaySoundIsRememberedOnTheFrame()
        {
            List<string> problems;

            var actor = ParseSingle(ActorWith("A_PlaySound(\"water\")"), out problems);

            Assert.AreEqual(0, problems.Count, All(problems));

            var frame = actor.Chains[0].Frames[0];

            Assert.AreEqual("water", frame.PlaySoundName);
            Assert.AreEqual(100, frame.PlaySoundVolume, "no volume given means full");
            Assert.AreEqual("", frame.Action, "and it is not left as an action name");
        }

        [TestMethod]
        public void PlaySoundTakesAVolume()
        {
            List<string> problems;

            var actor = ParseSingle(ActorWith("A_PlaySound(\"water\", 0, 50)"), out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(50, actor.Chains[0].Frames[0].PlaySoundVolume);
        }

        [TestMethod]
        public void PlaySoundChannelIsReportedRatherThanSwallowed()
        {
            // Channels are for looping sounds, and this engine has none. Accepting the number would
            // promise a sound that keeps playing.
            List<string> problems;

            var actor = ParseSingle(ActorWith("A_PlaySound(\"water\", 3)"), out problems);

            StringAssert.Contains(All(problems), "channel 3 is ignored");
            Assert.AreEqual("water", actor.Chains[0].Frames[0].PlaySoundName,
                "and the sound still plays once");
        }

        [TestMethod]
        public void PlaySoundIsEmittedInsideTheDefinition()
        {
            // The call has to be inside `fn Thing_...()`, where statements are legal - a top-level
            // statement is a parse error, and the first version of this emitted it there.
            List<string> problems = new List<string>();

            var script = DecorateConverter.Convert(
                ActorWith("A_PlaySound(\"water\")"), DoomInfo.MobjInfos, problems);

            Assert.AreEqual(0, problems.Count, All(problems));

            var call = script.IndexOf("PlaySound(\"See#1\", \"water\"", StringComparison.Ordinal);
            var close = script.IndexOf("\n}\n", StringComparison.Ordinal);

            Assert.IsTrue(call > 0, "the sound was attached");
            Assert.IsTrue(call < close, "and inside the actor definition, not after it");
        }

        [TestMethod]
        public void AnUnsupportedArgumentedActionIsStillNamed()
        {
            // The actions this engine still cannot take must keep failing loudly rather than
            // half-working. A_SetScale changes the actor's rendering size, which this engine has no
            // per-actor scale for, so it stays outside the supported action set.
            List<string> problems;

            ParseSingle(ActorWith("A_SetScale(1.5)"), out problems);

            var text = All(problems);

            StringAssert.Contains(text, "A_SetScale");
            StringAssert.Contains(text, "takes no arguments");
        }

        [TestMethod]
        public void AProjectileIsResolvedToAType()
        {
            List<string> problems;

            var actor = ParseSingle(ActorWith("A_CustomMissile(\"DoomImpBall\", 32)"), out problems);

            Assert.AreEqual(0, problems.Count, All(problems));

            var frame = actor.Chains[0].Frames[0];

            Assert.AreEqual("DoomImpBall", frame.MissileName);
            Assert.AreEqual(32, frame.MissileHeight);
        }

        [TestMethod]
        public void AProjectileNameThisEngineDoesNotKnowIsNamed()
        {
            // The whole reason the name table is taken from a checked source: a name that is not in
            // it must be refused, because a guess would fire a different projectile silently.
            List<string> problems;

            var actor = ParseSingle(ActorWith("A_CustomMissile(\"NotAThing\", 32)"), out problems);

            StringAssert.Contains(All(problems), "NotAThing");
            Assert.IsNull(actor.Chains[0].Frames[0].MissileName);
        }

        [TestMethod]
        public void ASpawnedItemIsResolvedToAType()
        {
            List<string> problems;

            var actor = ParseSingle(ActorWith("A_SpawnItem(\"HealthBonus\")"), out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual("HealthBonus", actor.Chains[0].Frames[0].SpawnName);
        }

        [TestMethod]
        public void AnIgnoredOffsetIsNamed()
        {
            // A missile fired at an angle is a different weapon, so a non-zero angle must not be
            // dropped in silence even though the spawn itself works.
            List<string> problems;

            ParseSingle(ActorWith("A_CustomMissile(\"DoomImpBall\", 32, 0, 15)"), out problems);

            StringAssert.Contains(All(problems), "ignored");
            StringAssert.Contains(All(problems), "xoffset and angle");
        }

        [TestMethod]
        public void AZeroOffsetIsNotReported()
        {
            // The overwhelmingly common spelling is all zeroes; complaining about it would train
            // the modder to ignore the report.
            List<string> problems;

            ParseSingle(ActorWith("A_CustomMissile(\"DoomImpBall\", 32, 0, 0)"), out problems);

            Assert.AreEqual(0, problems.Count, All(problems));
        }

        [TestMethod]
        public void ADecimalLiteralKeepsItsFraction()
        {
            // The translator used to truncate the dot, which turned 100.5 into 100. A literal with
            // a dot is a fixed-point number in this language, so the fraction must survive.
            var problems = new List<string>();

            var translated = DecorateCondition.Translate(
                "health < 100.5", "Actor", 1, problems);

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual("Health() < 100.5", translated);
        }

        [TestMethod]
        public void ACallFormIsNotAField()
        {
            // `Distance()` is how this engine asks the question, not how DECORATE writes it, and the
            // translator's whitelist says so. The A_JumpIfCloser path builds the call itself.
            var problems = new List<string>();

            var translated = DecorateCondition.Translate("Distance() < 8", "Actor", 1, problems);

            Assert.IsNull(translated);
            StringAssert.Contains(All(problems), "Distance");
        }
    }
}
