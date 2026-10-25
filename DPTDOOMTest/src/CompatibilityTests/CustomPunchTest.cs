using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_CustomPunch: the player melee hitscan, with a custom damage amount, puff and range.
    ///
    /// In ademo1.wad the second argument is 1, which means "do not roll the 1..8 multiplier" - it
    /// is not an ammo count. CPF_NOTURN is also used there, so both the multiplier and the turn are
    /// tested rather than only the happy path.
    /// </summary>
    [TestClass]
    public class CustomPunchTest
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
        public void NoRandomSkipsTheMultiplierAndItsDraw()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(56), player.Y);
                imp.Z = player.Z;
                player.Angle = Geometry.PointToAngle(player.X, player.Y, imp.X, imp.Y);

                var before = imp.Health;
                var randomBefore = world.Random.Index;

                world.MonsterBehavior.CustomPunch(player, 13, true, CustomPunchFlags.NoTurn,
                    MobjType.Puff, Fixed.Zero, "", "");

                Assert.IsTrue(imp.Health < before, "the punch hit the target");

                // The random angle and the puff still use the generator; the multiplier does not.
                Assert.IsTrue((world.Random.Index - randomBefore + 256) % 256 >= 1,
                    "the punch's other randomness still happens");
            }
        }

        [TestMethod]
        public void NoTurnLeavesThePlayerFacingAsItWas()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(56), player.Y);
                imp.Z = player.Z;
                player.Angle = Geometry.PointToAngle(player.X, player.Y, imp.X, imp.Y);
                var before = player.Angle;

                world.MonsterBehavior.CustomPunch(player, 13, true,
                    CustomPunchFlags.NoTurn, MobjType.Puff, Fixed.Zero, "", "");

                Assert.AreEqual(before.Data, player.Angle.Data,
                    "CPF_NOTURN leaves the facing alone");
            }
        }

        [TestMethod]
        public void WithoutNoTurnThePlayerFacesTheTarget()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(56), player.Y);
                imp.Z = player.Z;
                player.Angle = Geometry.PointToAngle(player.X, player.Y, imp.X, imp.Y) +
                               Angle.FromDegree(10);

                world.MonsterBehavior.CustomPunch(player, 13, true, 0,
                    MobjType.Puff, Fixed.Zero, "", "");

                var expected = Geometry.PointToAngle(player.X, player.Y, imp.X, imp.Y);

                Assert.AreEqual(expected.Data, player.Angle.Data,
                    "the ordinary punch turns to face the target");
            }
        }

        [TestMethod]
        public void AMonsterDoesNotUseThePlayerPunch()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(56), player.Y);
                imp.Z = player.Z;
                imp.Target = player;

                var before = player.Health;

                world.MonsterBehavior.CustomPunch(imp, 13, true, 0,
                    MobjType.Puff, Fixed.Zero, "", "");

                Assert.AreEqual(before, player.Health,
                    "A_CustomPunch is a player action, not a monster attack");
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
        public void TheConverterReadsNoRandomAndThePuff()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_CustomPunch(random(2,4), 1, CPF_NOTURN, \"BulletPuff\", 72)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "CustomPunch(\"Melee#1\"");
            StringAssert.Contains(script, "true, 16, \"BulletPuff\"");
            StringAssert.Contains(script, "Random(2, 4)");
            StringAssert.Contains(script, "SpawnEx_TestImp_Melee_1_9");
        }

        [TestMethod]
        public void UnsupportedPunchFlagsAreNamed()
        {
            var problems = new List<string>();

            Convert("A_CustomPunch(10, 0, CPF_USEAMMO|CPF_DAGGER, \"BulletPuff\", 72)", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "ammo");
            StringAssert.Contains(text, "dagger");
            StringAssert.Contains(text, "not supported");
        }

        [TestMethod]
        public void TheScriptPunchesInTheRealWorld()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_CustomPunch(random(2,4), 1, CPF_NOTURN, \"BulletPuff\", 72)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var library = new ScriptLibrary();
            library.Add(script, "test", DoomInfo.MobjInfos);
            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                PlaceAt(world, imp, player.X + Fixed.FromInt(56), player.Y);
                imp.Z = player.Z;
                player.Angle = Geometry.PointToAngle(player.X, player.Y, imp.X, imp.Y);

                var before = imp.Health;

                world.MonsterBehavior.CustomPunch(player, 13, true, CustomPunchFlags.NoTurn, MobjType.Puff, Fixed.Zero, "", "");

                Assert.IsTrue(imp.Health < before,
                    "the script's CustomPunch actually damaged the target");
            }
        }
    }
}
