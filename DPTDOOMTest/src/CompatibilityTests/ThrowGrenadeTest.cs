using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_ThrowGrenade: a ballistic spawn, not another auto-aiming projectile. Tests cover the
    /// default speed, independent vertical velocity, inherited momentum, world-RNG spread and the
    /// complete DECORATE-to-world path.
    /// </summary>
    [TestClass]
    public class ThrowGrenadeTest
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
            return FirstType(world, MobjType.Troop);
        }

        private static Mobj FirstType(World world, MobjType type)
        {
            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;
                if (mobj != null && mobj.Type == type)
                {
                    return mobj;
                }
            }

            return null;
        }

        private static int CountType(World world, MobjType type)
        {
            var count = 0;

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;
                if (mobj != null && mobj.Type == type)
                {
                    count++;
                }
            }

            return count;
        }

        private static string Convert(string body, List<string> problems)
        {
            return DecorateConverter.Convert(
                "ACTOR TestImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  Missile:\n" +
                "    TROO A 4 " + body + "\n" +
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
        public void ZeroXYVelocityUsesTheActorsSpeed()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);
                imp.Angle = Angle.Ang0;
                imp.MomX = imp.MomY = Fixed.Zero;
                world.Random.Clear();

                var grenade = world.ThingAllocation.ThrowCustomGrenade(
                    imp, MobjType.Troopshot, Fixed.Zero, Fixed.Zero, Fixed.Zero);

                Assert.IsNotNull(grenade);
                Assert.AreEqual(Fixed.FromInt(35).Data, (grenade.Z - imp.Z).Data,
                    "the default spawn height is 35 units above the source");
                var speed = new Fixed(DoomInfo.MobjInfos[(int)MobjType.Troopshot].Speed);
                var expectedAngle = new Angle(1 << 24);
                Assert.AreEqual((speed * Trig.Cos(expectedAngle)).Data, grenade.MomX.Data,
                    "xyvel=0 retains the grenade actor's own speed");
                Assert.AreEqual((speed * Trig.Sin(expectedAngle)).Data, grenade.MomY.Data);
                Assert.AreEqual(0, grenade.MomZ.Data);
                Assert.AreEqual(expectedAngle.Data, grenade.Angle.Data,
                    "SpawnMobj consumes LastLook first, then 109 selects spread step +1");
                Assert.AreEqual(imp, grenade.Target);
            }
        }

        [TestMethod]
        public void SpreadUsesOneWorldRandomDraw()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);
                imp.Angle = Angle.Ang0;
                world.Random.Clear();
                var before = world.Random.Index;

                var grenade = world.ThingAllocation.ThrowCustomGrenade(
                    imp, MobjType.Troopshot, Fixed.Zero, Fixed.FromInt(8), Fixed.Zero);

                Assert.AreEqual(new Angle(1 << 24).Data, grenade.Angle.Data,
                    "after SpawnMobj's LastLook draw, 109 selects spread step +1");
                Assert.AreEqual((before + 3) & 255, world.Random.Index,
                    "LastLook, spread and missile-spawn tic jitter each consume one draw");
            }
        }

        [TestMethod]
        public void CustomVelocitiesAndInheritedMomentumAreIndependent()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);
                imp.Angle = Angle.Ang0;
                imp.MomX = Fixed.FromInt(4);
                imp.MomY = Fixed.FromInt(-2);
                world.Random.Clear();

                var grenade = world.ThingAllocation.ThrowCustomGrenade(
                    imp, MobjType.Troopshot, Fixed.FromInt(7),
                    Fixed.FromInt(8), Fixed.FromInt(3));

                var angle = new Angle(1 << 24);
                var expectedX = Fixed.FromInt(8) * Trig.Cos(angle) + Fixed.FromInt(2);
                var expectedY = Fixed.FromInt(8) * Trig.Sin(angle) + Fixed.FromInt(-1);
                Assert.AreEqual(expectedX.Data, grenade.MomX.Data,
                    "xy velocity plus half the thrower's x momentum");
                Assert.AreEqual(expectedY.Data, grenade.MomY.Data,
                    "forward y velocity plus half the thrower's y momentum");
                Assert.AreEqual(Fixed.FromInt(3).Data, grenade.MomZ.Data,
                    "zvel is not scaled by horizontal speed");

                var expectedZ = Fixed.FromInt(42) + (Fixed.FromInt(3) >> 1);
                Assert.AreEqual(expectedZ.Data, (grenade.Z - imp.Z).Data,
                    "the height includes zheight and the ordinary missile-spawn half step");
            }
        }

        [TestMethod]
        public void TheConverterReadsArgumentsAsLiveExpressions()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_ThrowGrenade(\"Rocket\", 7, random(8,12), 3, 0)", problems);

            StringAssert.Contains(script, "ThrowGrenade(\"Missile#1\", \"Rocket\"");
            StringAssert.Contains(script, "SpawnEx_TestImp_Missile_1_21");
            StringAssert.Contains(script, "SpawnEx_TestImp_Missile_1_22");
            StringAssert.Contains(script, "SpawnEx_TestImp_Missile_1_23");
            StringAssert.Contains(script, "Random(8, 12)");
            StringAssert.Contains(script, ", false);");
            Assert.IsFalse(string.Join("; ", problems.ToArray()).Contains("takes no arguments"),
                "the action is converted instead of being refused for having arguments");
        }

        [TestMethod]
        public void PitchAndAmmoApproximationsAreReported()
        {
            var problems = new List<string>();
            Convert("A_ThrowGrenade(\"Rocket\")", problems);

            var text = string.Join("; ", problems.ToArray());
            StringAssert.Contains(text, "level throw");
            StringAssert.Contains(text, "pitch");
            StringAssert.Contains(text, "ready");
            StringAssert.Contains(text, "vanilla weapon");
        }

        [TestMethod]
        public void TheScriptThrowsARealGrenade()
        {
            var problems = new List<string>();
            var script = Convert("A_ThrowGrenade(\"Rocket\", 7, 8, 3, 0)", problems);
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);
                imp.Angle = Angle.Ang0;
                imp.MomX = imp.MomY = Fixed.Zero;
                world.Random.Clear();
                var before = CountType(world, MobjType.Rocket);

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].MissileState);

                Assert.AreEqual(before + 1, CountType(world, MobjType.Rocket));
                var grenade = FirstType(world, MobjType.Rocket);
                Assert.IsNotNull(grenade);
                Assert.AreEqual(Fixed.FromInt(3).Data, grenade.MomZ.Data);
                Assert.AreEqual(imp, grenade.Target);
            }
        }

        [TestMethod]
        public void PlayerAmmoIsSpentOnceAndEmptyAmmoPreventsTheThrow()
        {
            var problems = new List<string>();
            var script = Convert("A_ThrowGrenade(\"Rocket\", 0, 8, 0, 1)", problems);
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;
                var ammo = DoomInfo.WeaponInfos[(int)world.ConsolePlayer.ReadyWeapon].Ammo;

                Assert.AreNotEqual(AmmoType.NoAmmo, ammo);
                var ammoBefore = world.ConsolePlayer.Ammo[(int)ammo];
                var before = CountType(world, MobjType.Rocket);
                var state = DoomInfo.MobjInfos[(int)MobjType.Troop].MissileState;

                player.SetState(state);

                Assert.AreEqual(before + 1, CountType(world, MobjType.Rocket));
                Assert.AreEqual(ammoBefore - 1, world.ConsolePlayer.Ammo[(int)ammo]);

                world.ConsolePlayer.Ammo[(int)ammo] = 0;
                before = CountType(world, MobjType.Rocket);
                player.SetState(state);

                Assert.AreEqual(before, CountType(world, MobjType.Rocket));
                Assert.AreEqual(0, world.ConsolePlayer.Ammo[(int)ammo]);
            }
        }
    }
}
