using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// DECORATE actor names, and whether they point at the right type.
    ///
    /// This is the table that lets a mod say `A_CustomMissile("DoomImpBall", 32)`. A wrong entry
    /// does not fail loudly: it fires a different projectile, or spawns a different thing, and the
    /// mod author sees an engine that "almost works". So the table is taken from a checked source -
    /// `refs/doom_ace-master/engine/decodoom.h`, indexed by actor type in vanilla Doom's order - and
    /// these tests check that the order really is this engine's order.
    ///
    /// The assertions are spot checks against <see cref="MobjType"/> on purpose. Checking all 137
    /// entries by name would mean writing the whole table a second time, and a copy of a table
    /// proves only that the copy matches itself. What has to be true is that the *indices* line up,
    /// and that is what these check: a handful across the whole range, including the one place a
    /// shift would be most likely - the projectiles, which are what the table is for.
    /// </summary>
    [TestClass]
    public class ActorNamesTest
    {
        [TestMethod]
        public void TheTableIsAsLongAsTheVanillaActors()
        {
            // 137 is the number of mobj types vanilla Doom has, and the number of names in the
            // source. If this ever changes, the table was regenerated from something else.
            Assert.AreEqual(137, ActorNames.NamedTypes);

            Assert.AreEqual((int)MobjType.Custom0, ActorNames.NamedTypes,
                "the reserve starts exactly where the names end");
        }

        [TestMethod]
        public void MonstersPointAtTheRightType()
        {
            Assert.AreEqual((int)MobjType.Player, ActorNames.TypeOf("DoomPlayer"));
            Assert.AreEqual((int)MobjType.Possessed, ActorNames.TypeOf("ZombieMan"));
            Assert.AreEqual((int)MobjType.Troop, ActorNames.TypeOf("DoomImp"));
            Assert.AreEqual((int)MobjType.Sergeant, ActorNames.TypeOf("Demon"));
            Assert.AreEqual((int)MobjType.Shadows, ActorNames.TypeOf("Spectre"));
            Assert.AreEqual((int)MobjType.Bruiser, ActorNames.TypeOf("BaronOfHell"));
            Assert.AreEqual((int)MobjType.Knight, ActorNames.TypeOf("HellKnight"));
            Assert.AreEqual((int)MobjType.Spider, ActorNames.TypeOf("SpiderMastermind"));
        }

        [TestMethod]
        public void ProjectilesPointAtTheRightType()
        {
            // The reason the table exists: these are what a mod fires, and they are not addresses
            // the engine's own actor table can be reached by otherwise.
            Assert.AreEqual((int)MobjType.Troopshot, ActorNames.TypeOf("DoomImpBall"));
            Assert.AreEqual((int)MobjType.Headshot, ActorNames.TypeOf("CacodemonBall"));
            Assert.AreEqual((int)MobjType.Rocket, ActorNames.TypeOf("Rocket"));
            Assert.AreEqual((int)MobjType.Plasma, ActorNames.TypeOf("PlasmaBall"));
            Assert.AreEqual((int)MobjType.Bfg, ActorNames.TypeOf("BFGBall"));
            Assert.AreEqual((int)MobjType.Bruisershot, ActorNames.TypeOf("BaronBall"));
        }

        [TestMethod]
        public void ItemsPointAtTheRightType()
        {
            // The engine names the item positions MiscN, because the project never gave them
            // names: the numbers are what matter, and the DECORATE name is what a mod writes. The
            // four here are the ones a spawned pickup would use.
            Assert.AreEqual((int)MobjType.Misc0, ActorNames.TypeOf("GreenArmor"));
            Assert.AreEqual((int)MobjType.Misc2, ActorNames.TypeOf("HealthBonus"));
            Assert.AreEqual((int)MobjType.Misc3, ActorNames.TypeOf("ArmorBonus"));
            Assert.AreEqual((int)MobjType.Misc4, ActorNames.TypeOf("BlueCard"));
        }

        [TestMethod]
        public void CaseIsIgnored()
        {
            // A mod writes "doomimp" as often as "DoomImp", and both mean the same actor.
            Assert.AreEqual((int)MobjType.Troop, ActorNames.TypeOf("doomimp"));
            Assert.AreEqual((int)MobjType.Troop, ActorNames.TypeOf("DOOMIMP"));
        }

        [TestMethod]
        public void TheShortSpellingsWork()
        {
            // These are the only hand-written entries, and they are the spellings that appear in
            // real mods beside the canonical one.
            Assert.AreEqual((int)MobjType.Troop, ActorNames.TypeOf("imp"));
            Assert.AreEqual((int)MobjType.Sergeant, ActorNames.TypeOf("pinky"));
            Assert.AreEqual((int)MobjType.Fatso, ActorNames.TypeOf("mancubus"));
            Assert.AreEqual((int)MobjType.Head, ActorNames.TypeOf("caco"));
        }

        [TestMethod]
        public void AnUnknownNameIsMinusOne()
        {
            // A guess here would address another actor silently, which is the whole reason the
            // table is checked rather than remembered.
            Assert.AreEqual(-1, ActorNames.TypeOf("NotAThing"));
            Assert.AreEqual(-1, ActorNames.TypeOf(""));
            Assert.AreEqual(-1, ActorNames.TypeOf(null));
        }

        [TestMethod]
        public void TheReserveHasNoName()
        {
            // A type a mod created is not reachable by name from another mod, because names would
            // then collide across mods. It is reachable by the number it was given.
            Assert.IsNull(ActorNames.NameOf((int)MobjType.Custom0));
            Assert.IsNull(ActorNames.NameOf(-1));
            Assert.AreEqual("DoomImp", ActorNames.NameOf((int)MobjType.Troop));
        }

        [TestMethod]
        public void TheNamesAreABijection()
        {
            // Each type has its own name and resolves back to itself. This is what catches a
            // duplicated entry, which would make two types answer to one name - and a mod firing a
            // projectile would get whichever came first.
            for (var type = 0; type < ActorNames.NamedTypes; type++)
            {
                var name = ActorNames.NameOf(type);

                Assert.IsNotNull(name, "type " + type + " has a name");
                Assert.AreEqual(type, ActorNames.TypeOf(name),
                    "'" + name + "' must resolve to its own type");
            }
        }

        [TestMethod]
        public void MonstersAndItemsHaveMapNumbers()
        {
            // The converter reads the DoomEdNum from the actor table rather than tabulating it
            // beside the name, so a name is only usable for 'replaces' or for inheritance when the
            // actor has a number. Projectiles and puffs legitimately have none - no map places
            // them - which is why this checks the actors that do, not all of them.
            foreach (var name in new[] { "DoomImp", "Demon", "BaronOfHell", "ZombieMan",
                                         "HealthBonus", "ArmorBonus", "Rocket" })
            {
                var type = ActorNames.TypeOf(name);

                Assert.IsTrue(type >= 0, name + " resolves");

                if (name == "Rocket")
                {
                    continue;
                }

                Assert.IsTrue(DoomInfo.MobjInfos[type].DoomEdNum >= 0,
                    name + " has a map number");
            }
        }
    }
}
