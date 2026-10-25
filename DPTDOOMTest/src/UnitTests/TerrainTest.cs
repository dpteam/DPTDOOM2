using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// TERRAIN: what a floor does to whatever stands on it.
    ///
    /// The format exists because a flat says what a floor **looks like**, not what it **is**: the
    /// same nukage picture is decoration in one map and a hazard in another. These tests pin the
    /// reading of that, and - just as important - the list of things this port cannot do. A
    /// parser that accepted `smallclass` would leave a modder waiting for a splash that never
    /// appears.
    ///
    /// The one piece of arithmetic worth its own test is the friction factor: it is a fixed-point
    /// number, and it is parsed from its digits rather than through a double, because a factor
    /// that differs by one unit between machines is a desync.
    /// </summary>
    [TestClass]
    public class TerrainTest
    {
        [TestInitialize]
        public void Before()
        {
            Terrain.Clear();
        }

        [TestCleanup]
        public void After()
        {
            Terrain.Clear();
        }

        private static List<string> Parse(string text)
        {
            var problems = new List<string>();
            Terrain.Add(text, "test", problems);
            return problems;
        }

        private static string All(List<string> problems)
        {
            return string.Join("; ", problems.ToArray());
        }

        [TestMethod]
        public void ATerrainIsRead()
        {
            var problems = Parse(
                "terrain Nukage\n" +
                "{\n" +
                "    splash WaterSplash\n" +
                "    liquid\n" +
                "    friction 0.75\n" +
                "    damageamount 5\n" +
                "    damagetimemask 31\n" +
                "    allowprotection\n" +
                "}");

            Assert.AreEqual(0, problems.Count, All(problems));

            var terrain = Terrain.Find("Nukage");

            Assert.IsNotNull(terrain);
            Assert.IsTrue(terrain.Liquid);
            Assert.IsTrue(terrain.AllowProtection);
            Assert.AreEqual(5, terrain.DamageAmount);
            Assert.AreEqual(31, terrain.DamageTimeMask);
            Assert.AreEqual("WaterSplash", terrain.SplashName);
            Assert.IsTrue(terrain.HasFriction);
            Assert.AreEqual(new Fixed(49152).Data, terrain.Friction.Data, "0.75 is 49152/65536");
        }

        [TestMethod]
        public void AnOrdinaryFloorHasNoEffects()
        {
            var problems = Parse("terrain Plain { }");

            Assert.AreEqual(0, problems.Count, All(problems));

            var terrain = Terrain.Find("Plain");

            Assert.IsFalse(terrain.Liquid);
            Assert.AreEqual(0, terrain.DamageAmount);
            Assert.IsFalse(terrain.HasFriction, "the friction stays the engine's own");
            Assert.AreEqual(0, terrain.DamageAt(0), "and nothing is dealt");
        }

        [TestMethod]
        public void DamageIsDealtOnTheMask()
        {
            var problems = Parse("terrain Lava { damageamount 20 damagetimemask 31 }");
            Assert.AreEqual(0, problems.Count, All(problems));

            var terrain = Terrain.Find("Lava");

            // Vanilla's nukage shape: every 32nd tic.
            Assert.AreEqual(0, terrain.DamageAt(1));
            Assert.AreEqual(0, terrain.DamageAt(31));
            Assert.AreEqual(20, terrain.DamageAt(32));
            Assert.AreEqual(0, terrain.DamageAt(33));
            Assert.AreEqual(20, terrain.DamageAt(64));
        }

        [TestMethod]
        public void AMaskOfZeroMeansEveryTic()
        {
            // The arithmetic would make 0 mean "never", but a file that wrote zero means "always".
            var problems = Parse("terrain Acid { damageamount 1 damagetimemask 0 }");
            Assert.AreEqual(0, problems.Count, All(problems));

            var terrain = Terrain.Find("Acid");

            Assert.AreEqual(1, terrain.DamageAt(1));
            Assert.AreEqual(1, terrain.DamageAt(7));
        }

        [TestMethod]
        public void NoDamageAmountDealsNothing()
        {
            var problems = Parse("terrain Safe { damagetimemask 31 }");
            Assert.AreEqual(0, problems.Count, All(problems));

            Assert.AreEqual(0, Terrain.Find("Safe").DamageAt(32));
        }

        [TestMethod]
        public void ASplashIsRead()
        {
            var problems = Parse(
                "splash WaterSplash\n" +
                "{\n" +
                "    sound waterspl\n" +
                "    smallsound watersml\n" +
                "}");

            Assert.AreEqual(0, problems.Count, All(problems));

            var splash = Terrain.FindSplash("WaterSplash");

            Assert.IsNotNull(splash);
            Assert.AreEqual("waterspl", splash.Sound);
            Assert.AreEqual("watersml", splash.SmallSound);
        }

        [TestMethod]
        public void ABindingIsQueuedRatherThanAppliedImmediately()
        {
            // The flat list does not exist yet at parse time, so a binding has to wait for it.
            // Applying it now would mean binding by an index that means nothing.
            var problems = Parse("floor NUKAGE1 Nukage\nterrain Nukage { liquid }");

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(1, Terrain.Unbound.Count);
            Assert.AreEqual("NUKAGE1", Terrain.Unbound[0].Key, "flat names are upper-cased");
            Assert.AreEqual("Nukage", Terrain.Unbound[0].Value);
        }

        [TestMethod]
        public void AParticlePropertyIsNamedRatherThanSwallowed()
        {
            // The important refusal: a splash this port plays as a sound only. Accepting
            // smallclass would promise particles that have no sprites in a stock IWAD.
            var problems = Parse("splash Water { smallclass SplashBase sound water }");

            StringAssert.Contains(All(problems), "smallclass");
            StringAssert.Contains(All(problems), "particles");
            Assert.AreEqual("water", Terrain.FindSplash("Water").Sound, "the sound still reads");
        }

        [TestMethod]
        public void ADamageTypeIsNamedRatherThanSwallowed()
        {
            // This port has one kind of damage, so the amount applies and the type cannot.
            var problems = Parse("terrain Fire { damagetype Fire damageamount 10 }");

            StringAssert.Contains(All(problems), "damagetype");
            Assert.AreEqual(10, Terrain.Find("Fire").DamageAmount, "the amount still applies");
        }

        [TestMethod]
        public void NoAlertIsNamedRatherThanSwallowed()
        {
            var problems = Parse("splash S { noalert }");

            StringAssert.Contains(All(problems), "noalert");
        }

        [TestMethod]
        public void ACeilingBindingIsNamedRatherThanSilentlyDropped()
        {
            // Nothing stands on a ceiling, so the damage would never be consulted. Saying so is
            // better than binding it and looking correct.
            var problems = Parse("terrain T { damageamount 5 }\nceiling CEIL1 T");

            StringAssert.Contains(All(problems), "floors only");
            Assert.AreEqual(0, Terrain.Unbound.Count);
        }

        [TestMethod]
        public void AFrictionThatIsNotANumberIsReported()
        {
            var problems = Parse("terrain T { friction slippery }");

            StringAssert.Contains(All(problems), "friction");
            Assert.IsFalse(Terrain.Find("T").HasFriction);
        }

        [TestMethod]
        public void FrictionIsParsedFromItsDigits()
        {
            // The same rule as the script language's literals, and for the same reason: 0.9 is not
            // exactly representable, and a double round trip can be one unit off on another
            // platform. A friction factor that differs between machines is a desync.
            Parse("terrain A { friction 1 }");
            Parse("terrain B { friction 0.5 }");
            Parse("terrain C { friction 0.25 }");
            Parse("terrain D { friction 2 }");

            Assert.AreEqual(Fixed.One.Data, Terrain.Find("A").Friction.Data);
            Assert.AreEqual(32768, Terrain.Find("B").Friction.Data);
            Assert.AreEqual(16384, Terrain.Find("C").Friction.Data);
            Assert.AreEqual(131072, Terrain.Find("D").Friction.Data);
        }

        [TestMethod]
        public void ATerrainWithoutABlockIsReported()
        {
            var problems = Parse("terrain T");

            StringAssert.Contains(All(problems), "not followed by a block");
        }

        [TestMethod]
        public void AnUnclosedBlockIsReportedAndTheTerrainIsKept()
        {
            var problems = Parse("terrain T { damageamount 5");

            StringAssert.Contains(All(problems), "not closed");
            Assert.AreEqual(5, Terrain.Find("T").DamageAmount);
        }

        [TestMethod]
        public void AnUnknownKeywordIsNamed()
        {
            var problems = Parse("terrainx T { }");

            StringAssert.Contains(All(problems), "terrainx");
            StringAssert.Contains(All(problems), "not a TERRAIN keyword");
        }

        [TestMethod]
        public void CommentsInBothStylesAreSkipped()
        {
            var problems = Parse(
                "// a line comment\n" +
                "/* and a block\n" +
                "   comment */\n" +
                "terrain T\n" +
                "{\n" +
                "    damageamount 4 // trailing\n" +
                "}");

            Assert.AreEqual(0, problems.Count, All(problems));
            Assert.AreEqual(4, Terrain.Find("T").DamageAmount);
        }

        [TestMethod]
        public void OneBadTerrainDoesNotCostTheNextOne()
        {
            var problems = Parse("terrain Bad\nterrain Good { damageamount 3 }");

            Assert.IsTrue(problems.Count > 0);
            Assert.IsNotNull(Terrain.Find("Good"));
        }
    }
}
