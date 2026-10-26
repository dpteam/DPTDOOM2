using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_SpawnProjectile and A_FireProjectile: custom projectile spawning with side offsets and
    /// angle control. These are distinct from A_CustomMissile, whose arguments are older and
    /// smaller. Tests check the spawned actor, not just the generated call.
    /// </summary>
    [TestClass]
    public class SpawnProjectileTest
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

        private static void ApplyScript(string script)
        {
            var library = new ScriptLibrary();
            library.Add(script, "test", DoomInfo.MobjInfos);
            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));
        }

        [TestMethod]
        public void ProjectileUsesHeightSideOffsetAndAbsoluteAngle()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.Angle = Angle.Ang0;

                var missile = world.ThingAllocation.SpawnCustomProjectile(
                    imp, MobjType.Troopshot, Fixed.FromInt(8), Fixed.FromInt(48),
                    Angle.FromDegree(45), true, null, false);

                Assert.IsNotNull(missile);
                Assert.AreEqual(45.0, missile.Angle.ToDegree(), 0.01, "the angle is absolute");
                Assert.AreEqual(48.0, (double)(missile.Z - imp.Z).Data / Fixed.FracUnit, 0.01);

                var dx = missile.X - imp.X;
                var dy = missile.Y - imp.Y;
                var sideLength = Math.Sqrt(
                    (double)dx.Data * dx.Data + (double)dy.Data * dy.Data) / Fixed.FracUnit;

                Assert.AreEqual(8.0, sideLength, 0.01, "the side offset is in map units");
                Assert.AreEqual(0.0, (double)dx.Data / Fixed.FracUnit, 0.01);
                Assert.AreEqual(-8.0, (double)dy.Data / Fixed.FracUnit, 0.01,
                    "the offset uses the source's facing, not the absolute travel angle");
                Assert.AreEqual(imp, missile.Target, "the source is the projectile's owner");
            }
        }

        [TestMethod]
        public void ProjectileTracksTheTargetWhenNotAimingAtAngle()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);

                var missile = world.ThingAllocation.SpawnCustomProjectile(
                    imp, MobjType.Troopshot, Fixed.Zero, Fixed.FromInt(32),
                    Angle.Ang0, false, player, false);

                var expected = Geometry.PointToAngle(missile.X, missile.Y, player.X, player.Y);

                Assert.AreEqual(expected.Data, missile.Angle.Data,
                    "the projectile follows its target instead of the literal angle");
            }
        }

        [TestMethod]
        public void TransferTranslationCopiesThePlayersPalette()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.Flags |= MobjFlags.Translation;

                var missile = world.ThingAllocation.SpawnCustomProjectile(
                    imp, MobjType.Troopshot, Fixed.Zero, Fixed.Zero,
                    Angle.Ang0, true, null, true);

                Assert.AreEqual((int)MobjFlags.Translation,
                    (int)(missile.Flags & MobjFlags.Translation));
            }
        }

        [TestMethod]
        public void NoTargetMeansStraightAhead()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.Angle = Angle.Ang0;

                var missile = world.ThingAllocation.SpawnCustomProjectile(
                    imp, MobjType.Troopshot, Fixed.Zero, Fixed.Zero,
                    Angle.FromDegree(20), false, null, false);

                Assert.AreEqual(20.0, missile.Angle.ToDegree(), 0.01,
                    "without a target the angle is relative to the source's facing");
            }
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

        [TestMethod]
        public void TheConverterWritesSpawnProjectileExpressions()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_SpawnProjectile(\"DoomImpBall\", 32, 8, 0, CMF_AIMOFFSET)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "SpawnProjectile(\"Missile#1\", \"DoomImpBall\"");
            StringAssert.Contains(script, "32");
            StringAssert.Contains(script, "8");
            StringAssert.Contains(script, "SpawnEx_TestImp_Missile_1_20");
            StringAssert.Contains(script, ", 1, " + ActorPointer.Target + ");");
        }

        [TestMethod]
        public void TheConverterReadsFireProjectileTranslation()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_FireProjectile(\"Rocket\", 0, 0, 15, -1, FPF_TRANSFERTRANSLATION)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "FireProjectile(\"Missile#1\", \"Rocket\"");
            StringAssert.Contains(script, ", 2, false);");
        }

        [TestMethod]
        public void PitchFlagsAreReportedRatherThanIgnored()
        {
            var problems = new List<string>();

            Convert(
                "A_SpawnProjectile(\"Rocket\", 32, 0, 0, CMF_ABSOLUTEPITCH)", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "pitch");
        }

        [TestMethod]
        public void FirePitchAndAmmoApproximationAreReported()
        {
            var problems = new List<string>();

            Convert("A_FireProjectile(\"Rocket\", 0, 1, 0, 0, FPF_NOAUTOAIM, -10)", problems);

            var text = string.Join("; ", problems.ToArray());
            StringAssert.Contains(text, "useammo");
            StringAssert.Contains(text, "ready vanilla weapon");
            StringAssert.Contains(text, "pitch");
        }

        [TestMethod]
        public void TheScriptFiresARealProjectile()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_SpawnProjectile(\"DoomImpBall\", 32, 0, 0, CMF_ABSOLUTEANGLE)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.ThinkerState = ThinkerState.Active;
                imp.Target = world.ConsolePlayer.Mobj;
                imp.Angle = Angle.Ang90;
                var before = CountType(world, MobjType.Troopshot);

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].MissileState);

                Assert.AreEqual(before + 1, CountType(world, MobjType.Troopshot),
                    "the converted SpawnProjectile fired a real imp ball");

                var missile = FirstType(world, MobjType.Troopshot);
                Assert.IsNotNull(missile);
                Assert.AreEqual(0.0, missile.Angle.ToDegree(), 0.01,
                    "CMF_ABSOLUTEANGLE replaces the target direction and the source's facing");
            }
        }

        [TestMethod]
        public void SpawnProjectileWithoutATargetDoesNotFire()
        {
            var problems = new List<string>();
            var script = Convert("A_SpawnProjectile(\"DoomImpBall\")", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);
                imp.Target = null;
                var before = CountType(world, MobjType.Troopshot);

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].MissileState);

                Assert.AreEqual(before, CountType(world, MobjType.Troopshot),
                    "A_SpawnProjectile requires its AAPTR_TARGET reference by default");
            }
        }

        [TestMethod]
        public void AimDirectionFiresWithoutATarget()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_SpawnProjectile(\"DoomImpBall\", 32, 0, 10, CMF_AIMDIRECTION)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);
                imp.Target = null;
                imp.Angle = Angle.FromDegree(30);
                var before = CountType(world, MobjType.Troopshot);

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].MissileState);

                Assert.AreEqual(before + 1, CountType(world, MobjType.Troopshot));
                var missile = FirstType(world, MobjType.Troopshot);
                Assert.IsNotNull(missile);
                Assert.AreEqual(40.0, missile.Angle.ToDegree(), 0.01,
                    "CMF_AIMDIRECTION uses the source's facing plus the angle offset");
            }
        }

        [TestMethod]
        public void FireProjectileIsPlayerOnlyAndUsesAmmo()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_FireProjectile(\"Rocket\", 0, 1, 0, 0, " +
                "FPF_NOAUTOAIM | FPF_TRANSFERTRANSLATION)", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "useammo");
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;
                var state = DoomInfo.MobjInfos[(int)MobjType.Troop].MissileState;

                Assert.IsNotNull(imp);
                var before = CountType(world, MobjType.Rocket);

                imp.SetState(state);
                Assert.AreEqual(before, CountType(world, MobjType.Rocket),
                    "A_FireProjectile is not a monster attack");

                var ammo = DoomInfo.WeaponInfos[(int)world.ConsolePlayer.ReadyWeapon].Ammo;
                Assert.AreNotEqual(AmmoType.NoAmmo, ammo);
                var ammoBefore = world.ConsolePlayer.Ammo[(int)ammo];
                player.Flags |= MobjFlags.Translation;
                player.Angle = Angle.Ang0;

                player.SetState(state);

                Assert.AreEqual(before + 1, CountType(world, MobjType.Rocket));
                Assert.AreEqual(ammoBefore - 1, world.ConsolePlayer.Ammo[(int)ammo],
                    "useammo=true spends one shot of the ready weapon");

                var missile = FirstType(world, MobjType.Rocket);
                Assert.IsNotNull(missile);
                Assert.AreEqual((int)MobjFlags.Translation,
                    (int)(missile.Flags & MobjFlags.Translation));

                world.ConsolePlayer.Ammo[(int)ammo] = 0;
                before = CountType(world, MobjType.Rocket);
                player.SetState(state);

                Assert.AreEqual(before, CountType(world, MobjType.Rocket),
                    "an empty weapon cannot fire the custom projectile");
            }
        }
    }
}
