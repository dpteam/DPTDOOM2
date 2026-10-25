using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_CustomMeleeAttack: a frame-owned damage amount, a hit sound and a miss sound.
    ///
    /// Unlike a vanilla monster attack, the damage is not a formula belonging to the monster type:
    /// mods commonly write random(20,24), so it has to be evaluated when the frame runs. The
    /// vanilla range test is shared with the ordinary monster actions, so a custom attack has the
    /// same reach as an ordinary one.
    /// </summary>
    [TestClass]
    public class CustomMeleeAttackTest
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

        private static Mobj FirstImp(World world)
        {
            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;

                if (mobj != null && mobj.Type == MobjType.Troop)
                {
                    return mobj;
                }
            }

            return null;
        }

        private static void PlaceAt(World world, Mobj mobj, Fixed x, Fixed y)
        {
            world.ThingMovement.UnsetThingPosition(mobj);
            mobj.X = x;
            mobj.Y = y;
            world.ThingMovement.SetThingPosition(mobj);
        }

        [TestMethod]
        public void AHitDamagesTheTargetByTheGivenAmount()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(32), player.Y);
                imp.Target = player;

                var before = player.Health;

                world.MonsterBehavior.CustomMeleeAttack(imp, 13, "", "");

                Assert.AreEqual(before - 13, player.Health, "the frame's damage was applied");
            }
        }

        [TestMethod]
        public void AMissDoesNotDamageTheTarget()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(256), player.Y);
                imp.Target = player;

                var before = player.Health;

                world.MonsterBehavior.CustomMeleeAttack(imp, 13, "", "");

                Assert.AreEqual(before, player.Health, "out of melee range is a miss");
            }
        }

        [TestMethod]
        public void NoTargetMeansNoDamageAndNoRandomDraws()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.Target = null;
                var randomBefore = world.Random.Index;

                world.MonsterBehavior.CustomMeleeAttack(imp, 13, "", "");

                Assert.AreEqual(randomBefore, world.Random.Index,
                    "a missing target does not spend the world generator");
            }
        }

        private static string Convert(string body, List<string> problems)
        {
            return DecorateConverter.Convert(
                "ACTOR TestImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  Melee:\n" +
                "    TROO A 4 " + body + "\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);
        }

        [TestMethod]
        public void TheConverterKeepsTheRandomDamageAsAnExpression()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_CustomMeleeAttack(random(20,24), \"enemy/claw\", \"enemy/miss\")", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "CustomMeleeAttack(\"Melee#1\"");
            StringAssert.Contains(script, "enemy/claw");
            StringAssert.Contains(script, "enemy/miss");
            StringAssert.Contains(script, "Random(20, 24)");
            StringAssert.Contains(script, "SpawnEx_TestImp_Melee_1_8");
        }

        [TestMethod]
        public void DamageTypeAndNoBleedAreNamed()
        {
            var problems = new List<string>();

            Convert("A_CustomMeleeAttack(10, \"\", \"\", \"Fire\", false)", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "damage type");
            StringAssert.Contains(text, "bleed=false");
        }

        [TestMethod]
        public void TheScriptDealsTheRandomDamageInTheRealWorld()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_CustomMeleeAttack(random(20,24), \"\", \"\")", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var library = new ScriptLibrary();
            library.Add(script, "test", DoomInfo.MobjInfos);
            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(32), player.Y);
                imp.Target = player;
                imp.ThinkerState = ThinkerState.Active;

                var before = player.Health;
                var randomBefore = world.Random.Index;

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].MeleeState);

                var dealt = before - player.Health;

                Assert.IsTrue(dealt >= 20 && dealt <= 24,
                    "the frame's random damage was applied, got " + dealt);
                Assert.IsTrue((world.Random.Index - randomBefore + 256) % 256 >= 1,
                    "the damage expression really drew from the world generator");
            }
        }
    }
}
