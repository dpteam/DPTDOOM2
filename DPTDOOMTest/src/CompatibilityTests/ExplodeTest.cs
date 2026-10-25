using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_Explode with arguments: damage and radius as separate numbers, and a choice of whether
    /// the blast hurts whoever caused it.
    ///
    /// Two things are not obvious and are the reason this is a separate action from the engine's
    /// own Explode:
    ///
    ///  - vanilla ties damage and radius together, because it only ever explodes rockets.
    ///    A_Explode(128, 64) is full damage in a small circle, not half damage in a big one;
    ///  - ZDoom's default flags value is XF_HURTSOURCE, the **opposite** of vanilla. A barrel or a
    ///    grenade is meant to hurt whoever caused it.
    ///
    /// And a third, discovered by reading the engine rather than assuming: the engine's existing
    /// Explode is the barrel's action and passes the barrel's **target** as the source, which is
    /// what makes a barrel chain-reaction work. A_Explode is the actor blowing up where it stands.
    /// Calling one by the other's name would be wrong in a way that is hard to see.
    /// </summary>
    [TestClass]
    public class ExplodeTest
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

        private static DoomGame NewGame(GameContent content)
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

            var cmds = new TicCmd[Player.MaxPlayerCount];

            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);

            return game;
        }

        private static void PlaceAt(World world, Mobj mobj, Fixed x, Fixed y)
        {
            world.ThingMovement.UnsetThingPosition(mobj);
            mobj.X = x;
            mobj.Y = y;
            world.ThingMovement.SetThingPosition(mobj);
        }

        /// <summary>
        /// Two imps: one at the centre of the blast and one the given number of map units away.
        /// </summary>
        private static Mobj ImpNear(World world, Fixed x, Fixed y)
        {
            var imp = world.ThingAllocation.SpawnMobj(x, y, world.ConsolePlayer.Mobj.Z, MobjType.Troop);

            // An imp in its spawn state has A_Look attached, so entering any other state would
            // run the look and change what this test is measuring. Leaving it here keeps the
            // explosion the only thing that happens to it.
            imp.ThinkerState = ThinkerState.InStasis;

            return imp;
        }

        [TestMethod]
        public void TheFlagValuesAreZDooms()
        {
            // From wadsrc/static/zscript/constants.zs.
            Assert.AreEqual(1, ExplosionFlags.HurtSource);
            Assert.AreEqual(4, ExplosionFlags.NotMissile);
            Assert.AreEqual(8, ExplosionFlags.ExplicitDamageType);
            Assert.AreEqual(16, ExplosionFlags.NoSplash);
            Assert.AreEqual(32, ExplosionFlags.ThrustZ);
            Assert.AreEqual(64, ExplosionFlags.ThrustLess);
            Assert.AreEqual(128, ExplosionFlags.NoAllies);
            Assert.AreEqual(256, ExplosionFlags.Circular);
            Assert.AreEqual(512, ExplosionFlags.CircularThrust);
        }

        [TestMethod]
        public void DamageAndRadiusAreSeparateNumbers()
        {
            // A small radius with a large damage. The falloff subtracts the **victim's own radius**
            // before comparing, so an imp 32 units away is at an effective distance of 32 - 20 = 12,
            // not 32. Choosing 16 would put it inside a 16 unit blast and prove nothing.
            //
            // With radius 4 the same imp is outside, and with radius 128 it is inside - which is
            // what shows the radius, and not the damage, is deciding the range.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;

                var bomb = ImpNear(world, player.X, player.Y);
                var near = ImpNear(world, player.X + Fixed.FromInt(32), player.Y);
                var far = ImpNear(world, player.X + Fixed.FromInt(80), player.Y);

                var nearHealth = near.Health;
                var farHealth = far.Health;

                world.MonsterBehavior.RadiusExplode(bomb, 128, 4, true);

                Assert.AreEqual(nearHealth, near.Health,
                    "an imp 32 units away is outside a 4 unit radius even with 128 damage");
                Assert.AreEqual(farHealth, far.Health, "and so is one 80 units away");
            }
        }

        [TestMethod]
        public void ATargetInsideTheRadiusIsHurt()
        {
            // The control for the one above: with the radius wide enough, the same imp is damaged,
            // so the test above is about the radius and not about damage being broken.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;

                var bomb = ImpNear(world, player.X, player.Y);
                var near = ImpNear(world, player.X + Fixed.FromInt(32), player.Y);

                var before = near.Health;

                world.MonsterBehavior.RadiusExplode(bomb, 128, 128, true);

                Assert.IsTrue(near.Health < before,
                    "an imp inside the radius took damage: " + before + " -> " + near.Health);
            }
        }

        [TestMethod]
        public void TheSourceIsHurtByDefaultInDecorate()
        {
            // XF_HURTSOURCE is ZDoom's default, so an actor that calls A_Explode() hurts itself.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;

                var bomb = ImpNear(world, player.X, player.Y);

                var before = bomb.Health;

                world.MonsterBehavior.RadiusExplode(bomb, 64, 128, true);

                Assert.IsTrue(bomb.Health < before,
                    "the actor at the centre was hurt: " + before + " -> " + bomb.Health);
            }
        }

        [TestMethod]
        public void WithoutHurtSourceTheSourceIsExempt()
        {
            // The control, and also what a rocket needs: a rocket does not hurt the player who
            // fired it at point blank.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;

                var bomb = ImpNear(world, player.X, player.Y);
                var other = ImpNear(world, player.X + Fixed.FromInt(32), player.Y);

                var before = bomb.Health;
                var otherBefore = other.Health;

                world.MonsterBehavior.RadiusExplode(bomb, 64, 128, false);

                Assert.AreEqual(before, bomb.Health, "the source was not hurt");
                Assert.IsTrue(other.Health < otherBefore, "but the bystander was");
            }
        }

        [TestMethod]
        public void AZeroRadiusMeansTheSameAsTheDamage()
        {
            // A_Explode(128) means an explosion, not "explode nowhere": distance defaults to
            // damage in ZDoom, and a zero radius here is the same request.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;

                var bomb = ImpNear(world, player.X, player.Y);
                var other = ImpNear(world, player.X + Fixed.FromInt(32), player.Y);

                var before = other.Health;

                world.MonsterBehavior.RadiusExplode(bomb, 128, 0, true);

                Assert.IsTrue(other.Health < before,
                    "a zero radius fell back to the damage, so the blast happened");
            }
        }

        [TestMethod]
        public void VanillaRadiusAttackStillHurtsTheSource()
        {
            // Vanilla Doom does not exempt the source of a radius attack: a rocket hurts the player
            // who fired it at point blank. This is the case that catches a "helpful" exemption
            // copied from ZDoom - the world-hash tests failed on it, and they were right to.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;

                var bomb = ImpNear(world, player.X, player.Y);

                var before = bomb.Health;

                world.ThingInteraction.RadiusAttack(bomb, bomb, 128);

                Assert.IsTrue(bomb.Health < before,
                    "the source is hurt, as in vanilla: " + before + " -> " + bomb.Health);
            }
        }

        [TestMethod]
        public void ADecorateExplosionWithoutHurtSourceExemptsTheSource()
        {
            // And the other side of the same rule: A_Explode without XF_HURTSOURCE is where the
            // exemption belongs, and nowhere else.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;

                var bomb = ImpNear(world, player.X, player.Y);
                var other = ImpNear(world, player.X + Fixed.FromInt(32), player.Y);

                var before = bomb.Health;
                var otherBefore = other.Health;

                world.MonsterBehavior.RadiusExplode(bomb, 64, 128, false);

                Assert.AreEqual(before, bomb.Health, "the source is exempt when ZDoom says so");
                Assert.IsTrue(other.Health < otherBefore, "and the bystander is still hurt");
            }
        }

        [TestMethod]
        public void AVanillaExplosionStillHurtsTheSpot()
        {
            // The case the barrel needs, and the one that distinguishes the two rules: spot and
            // source are **different actors**. Exempting the spot as well as the source makes a
            // barrel immune to its own explosion, which is a real regression and not a detail.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;

                var bomb = ImpNear(world, player.X, player.Y);
                var other = ImpNear(world, player.X + Fixed.FromInt(32), player.Y);

                var before = bomb.Health;

                world.ThingInteraction.RadiusAttack(bomb, other, 128);

                Assert.IsTrue(bomb.Health < before,
                    "the spot is damaged even though it is not the source: " + before + " -> " + bomb.Health);
            }
        }

        // ------------------------------------------------------------- the converter

        private static string Convert(string body, List<string> problems)
        {
            return DecorateConverter.Convert(
                "ACTOR TestImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  Death:\n" +
                "    TROO A 4 " + body + "\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);
        }

        [TestMethod]
        public void TheConverterWritesDamageRadiusAndFlags()
        {
            var problems = new List<string>();

            var script = Convert("A_Explode(64, 32, XF_HURTSOURCE)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "Explode(\"Death#1\", 64, 32, 1);");
        }

        [TestMethod]
        public void WithoutHurtSourceTheFlagIsZero()
        {
            var problems = new List<string>();

            var script = Convert("A_Explode(128, 128, 0)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "Explode(\"Death#1\", 128, 128, 0);");
        }

        [TestMethod]
        public void ABareExplodeKeepsTheVanillaAction()
        {
            // A_Explode with no arguments is the barrel explosion, and the engine already has
            // exactly that action. A closure would be a second path to the same thing.
            var problems = new List<string>();

            var script = Convert("A_Explode", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            Assert.IsFalse(script.Contains("Explode(\"Death#1\""), "no closure was written");
            StringAssert.Contains(script, "A_Explode");
        }

        [TestMethod]
        public void TheRadiusDefaultsToTheDamage()
        {
            var problems = new List<string>();

            var script = Convert("A_Explode(96)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "Explode(\"Death#1\", 96, 96, 1);");
        }

        [TestMethod]
        public void TheFlagsThisEngineCannotHonourAreNamed()
        {
            var problems = new List<string>();

            Convert("A_Explode(64, 64, XF_THRUSTZ|XF_CIRCULAR)", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "A_Explode");
            StringAssert.Contains(text, "no counterpart");
        }

        [TestMethod]
        public void AnUnknownExplodeFlagIsReported()
        {
            var problems = new List<string>();

            Convert("A_Explode(64, 64, XF_NOSUCHTHING)", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "XF_NOSUCHTHING");
        }

        [TestMethod]
        public void NegativeDamageIsReportedRatherThanHealing()
        {
            // -1 means "from the actor's metadata" in ZDoom, and there is no ExplosionDamage
            // property here. Passing the number through would make the blast heal.
            var problems = new List<string>();

            Convert("A_Explode(-1)", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "metadata");
            StringAssert.Contains(text, "128");
        }

        [TestMethod]
        public void TheScriptCallReachesTheWorld()
        {
            // The whole path: a frame action that explodes, attached by a script, run by the real
            // state machine. Not just the text of the script.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var library = new ScriptLibrary();

                library.Add(
                    "fn Thing_3001() {\n" +
                    "    State(\"Death#1\", \"TROO\", \"A\", 4, \"\", \"Death#1\");\n" +
                    "    DeathState(\"Death#1\");\n" +
                    "    Explode(\"Death#1\", 64, 128, 1);\n" +
                    "}\n", "test", DoomInfo.MobjInfos);

                library.ApplyTo(DoomInfo.MobjInfos);

                Assert.AreEqual(0, library.Problems.Count,
                    string.Join("; ", new List<string>(library.Problems).ToArray()));

                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;

                var bomb = ImpNear(world, player.X, player.Y);
                var other = ImpNear(world, player.X + Fixed.FromInt(32), player.Y);

                var before = other.Health;

                // The state is reached by **name** through the actor's own death state, not by the
                // first free slot in the table. Other tests allocate custom states too, so in a
                // full run CustomStateFirst belongs to somebody else - and this test would have
                // run another test's frame instead of its own.
                bomb.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].DeathState);

                Assert.IsTrue(other.Health < before,
                    "the frame the script defined exploded and hurt the bystander");
            }
        }
    }
}
