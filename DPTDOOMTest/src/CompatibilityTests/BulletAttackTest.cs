using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;
using DPTDOOMTest.CompatibilityTests;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_CustomBulletAttack and A_FireBullets: bursts of hitscan bullets.
    ///
    /// The two actions look alike and are not: their flags and range occupy **different argument
    /// positions** and their flag numbers differ, so the tests check the swap explicitly rather than
    /// only the happy path. In the world, the checks that matter are that a reference is required
    /// (unless CBAF_AIMFACING says otherwise), that a bullet actually damages what it points at, and
    /// that a single player bullet is not randomly spread.
    /// </summary>
    [TestClass]
    public class BulletAttackTest
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

        private static DoomGame NewGame(GameContent content, out TicCmd[] cmds)
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

            cmds = new TicCmd[Player.MaxPlayerCount];
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

        private static void PlaceAt(World world, Mobj thing, Fixed x, Fixed y)
        {
            world.ThingMovement.UnsetThingPosition(thing);
            thing.X = x;
            thing.Y = y;
            world.ThingMovement.SetThingPosition(thing);
        }

        private static string Convert(string body, List<string> problems)
        {
            return DecorateConverter.Convert(
                "ACTOR BulletImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 " + body + "\n" +
                "    TROO B 4\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);
        }

        private static void ApplyScript(string script)
        {
            var library = new ScriptLibrary();
            library.Add(script, "test", DoomInfo.MobjInfos);
            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));
        }

        [TestMethod]
        public void CustomBulletAttackReadsFlagsAtSixAndRangeAtFive()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_CustomBulletAttack(4, 4, 1, 3, \"BulletPuff\", 40, CBAF_NORANDOM)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            // range = 40 is argument 5, flags is argument 6. Swapping them would put 40 into the
            // flag mask and CBAF_NORANDOM (2) into the range.
            StringAssert.Contains(script, ", 2, \"SpawnEx_BulletImp_See_1_29\"");
            StringAssert.Contains(script, "return 40;");
        }

        [TestMethod]
        public void FireBulletsReadsFlagsAtFiveAndRangeAtSix()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_FireBullets(3, 2, 2, 10, \"BulletPuff\", FBF_NORANDOM, 512)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            // flags = FBF_NORANDOM (2), range = 512.
            StringAssert.Contains(script, ", 2, \"SpawnEx_BulletImp_See_1_29\"");
            StringAssert.Contains(script, "return 512;");
        }

        [TestMethod]
        public void FireBulletsDefaultsToUsingAmmoAndCustomDoesNot()
        {
            var problems = new List<string>();

            var player = Convert("A_FireBullets(0, 0, 1, 5)", problems);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "useammo");
            StringAssert.Contains(player, "FireBullets(\"See#1\"");

            problems.Clear();
            var monster = Convert("A_CustomBulletAttack(0, 0, 1, 5)", problems);

            Assert.IsFalse(string.Join("; ", problems.ToArray()).Contains("useammo"),
                "A_CustomBulletAttack defaults to no flags, so there is no ammo to report");
            StringAssert.Contains(monster, "CustomBulletAttack(\"See#1\"");
        }

        [TestMethod]
        public void TheOtherActionsFlagsAreNamedRatherThanApplied()
        {
            var problems = new List<string>();

            // CBAF_NORANDOM is 2 and FBF_NORANDOM is also 2, but FBF_NORANDOMPUFFZ is 32 while
            // CBAF_NORANDOMPUFFZ is 16 - so a flag from the other action must not be taken.
            Convert("A_CustomBulletAttack(0, 0, 1, 5, \"BulletPuff\", 0, FBF_USEAMMO)", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "other bullet action");
        }

        [TestMethod]
        public void AMonsterBulletDamagesTheTargetInFront()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_CustomBulletAttack(0, 0, 1, 20, \"BulletPuff\", 0, CBAF_NORANDOM | CBAF_AIMFACING)",
                problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(64), player.Y);
                imp.Z = player.Z;
                imp.Angle = Geometry.PointToAngle(imp.X, imp.Y, player.X, player.Y);
                imp.ThinkerState = ThinkerState.Active;

                // The damage lands on the player's mobj; the Player record is copied from it during
                // the player's own think, which this test does not run.
                var before = player.Health;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.IsTrue(player.Health < before,
                    "the bullet hit the player: " + before + " -> " + player.Health);
            }
        }

        [TestMethod]
        public void WithoutAReferenceAndWithoutAimFacingNothingIsFired()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_CustomBulletAttack(0, 0, 1, 20, \"BulletPuff\", 0, CBAF_NORANDOM)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(64), player.Y);
                imp.Z = player.Z;
                imp.Angle = Geometry.PointToAngle(imp.X, imp.Y, player.X, player.Y);
                imp.Target = null;
                imp.ThinkerState = ThinkerState.Active;

                var before = player.Health;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreEqual(before, player.Health,
                    "no reference and no CBAF_AIMFACING means the original does nothing");
            }
        }

        [TestMethod]
        public void ASinglePlayerBulletIsNotSpread()
        {
            // A_FireBullets fires a single bullet down the exact facing unless the player is
            // refiring: the spread path is only for a volley. A version that always spread would
            // make every one-bullet weapon quietly inaccurate, and this test would then miss the
            // imp standing directly ahead.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;
                var player = world.ConsolePlayer.Mobj;
                var imp = FirstImp(world);

                Assert.IsNotNull(player);
                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(96), player.Y);
                imp.Z = player.Z;
                player.Angle = Geometry.PointToAngle(player.X, player.Y, imp.X, imp.Y);
                world.ConsolePlayer.Refire = 0;

                var before = imp.Health;

                // A 45-degree spread would put the bullet far off the imp at this distance.
                world.WeaponBehavior.FireBullets(player, Fixed.FromInt(45), Fixed.Zero, 1, 5,
                    MobjType.Puff, FireBulletsFlags.NoRandom, Fixed.FromInt(1024));

                Assert.IsTrue(imp.Health < before,
                    "the single bullet went straight ahead and hit the imp: " + before + " -> " +
                    imp.Health);
            }
        }
    }
}
