using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_SpawnItemEx: offsets, velocity, angle, pointers and a fail chance.
    ///
    /// The signature is read from ZDoom's attacks.zs, not guessed:
    /// (missile, xofs, yofs, zofs, xvel, yvel, zvel, angle, flags, failchance, tid).
    /// The numeric arguments are expressions, so a test has to cover both the engine's spawn and
    /// the fact that random() is evaluated when the frame runs, not while the mod is loaded.
    /// </summary>
    [TestClass]
    public class SpawnItemExTest
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

        private static Mobj NewSource(World world)
        {
            var player = world.ConsolePlayer.Mobj;

            return world.ThingAllocation.SpawnMobj(
                player.X, player.Y, player.Z, MobjType.Troop);
        }

        private static int CountOf(World world, MobjType type)
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

        [TestMethod]
        public void TheFlagValuesAreZDooms()
        {
            Assert.AreEqual(1 << 1, SpawnItemExFlags.AbsolutePosition);
            Assert.AreEqual(1 << 2, SpawnItemExFlags.AbsoluteAngle);
            Assert.AreEqual(1 << 3, SpawnItemExFlags.AbsoluteVelocity);
            Assert.AreEqual(1 << 10, SpawnItemExFlags.TransferPointers);
            Assert.AreEqual(1 << 13, SpawnItemExFlags.MultiplySpeed);
            Assert.AreEqual(1 << 20, SpawnItemExFlags.SetTarget);
            Assert.AreEqual(1 << 21, SpawnItemExFlags.SetTracer);
            Assert.AreEqual(1 << 22, SpawnItemExFlags.NoPointers);
            Assert.AreEqual(1 << 26, SpawnItemExFlags.IsTarget);
            Assert.AreEqual(1 << 28, SpawnItemExFlags.IsTracer);
        }

        [TestMethod]
        public void AbsolutePositionVelocityAndAngleAreUsed()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var source = NewSource(world);

                source.Angle = Angle.FromDegree(90);

                var p = new SpawnItemExParams();
                p.Type = (MobjType)ActorNames.TypeOf("HealthBonus"); // HealthBonus in the vanilla table.
                p.XOffset = Fixed.FromInt(8);
                p.YOffset = Fixed.FromInt(-4);
                p.ZOffset = Fixed.FromInt(12);
                p.XVelocity = Fixed.FromInt(3);
                p.YVelocity = Fixed.FromInt(-2);
                p.ZVelocity = Fixed.FromInt(1);
                p.Angle = Angle.FromDegree(45);
                p.Flags = SpawnItemExFlags.AbsolutePosition |
                          SpawnItemExFlags.AbsoluteVelocity |
                          SpawnItemExFlags.AbsoluteAngle;

                var before = source;

                var spawned = world.ThingAllocation.SpawnItemEx(source, p);

                Assert.IsNotNull(spawned);
                Assert.AreEqual((before.X + Fixed.FromInt(8)).Data, spawned.X.Data, "absolute x");
                Assert.AreEqual((before.Y + Fixed.FromInt(-4)).Data, spawned.Y.Data, "absolute y");
                Assert.AreEqual((before.Z + Fixed.FromInt(12)).Data, spawned.Z.Data, "absolute z");
                Assert.AreEqual(p.XVelocity.Data, spawned.MomX.Data, "absolute x velocity");
                Assert.AreEqual(p.YVelocity.Data, spawned.MomY.Data, "absolute y velocity");
                Assert.AreEqual(p.ZVelocity.Data, spawned.MomZ.Data, "absolute z velocity");
                Assert.AreEqual(45.0, spawned.Angle.ToDegree(), 0.01, "absolute angle, not source angle");
            }
        }

        [TestMethod]
        public void RelativeCoordinatesUseZDoomsLeftRightConvention()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var source = NewSource(world);

                source.Angle = Angle.Ang0;

                var p = new SpawnItemExParams();
                p.Type = (MobjType)ActorNames.TypeOf("HealthBonus");
                p.XOffset = Fixed.FromInt(10);
                p.YOffset = Fixed.FromInt(5);
                p.Flags = 0;

                var spawned = world.ThingAllocation.SpawnItemEx(source, p);

                Assert.IsNotNull(spawned);

                // ZDoom's relative mode: positive y is right, so y is subtracted after rotation.
                Assert.AreEqual(10.0, (double)(spawned.X - source.X).Data / Fixed.FracUnit, 0.01);
                Assert.AreEqual(-5.0, (double)(spawned.Y - source.Y).Data / Fixed.FracUnit, 0.01);
            }
        }

        [TestMethod]
        public void PointersCanBeTransferredClearedAndAssigned()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var source = NewSource(world);
                var target = NewSource(world);
                var tracer = NewSource(world);

                source.Target = target;
                source.Tracer = tracer;

                var p = new SpawnItemExParams();
                p.Type = MobjType.Troopshot;
                p.Flags = SpawnItemExFlags.TransferPointers;

                var spawned = world.ThingAllocation.SpawnItemEx(source, p);

                Assert.AreEqual(target, spawned.Target, "the target pointer was transferred");
                Assert.AreEqual(tracer, spawned.Tracer, "and so was the tracer");

                p.Flags = SpawnItemExFlags.NoPointers | SpawnItemExFlags.SetTarget |
                          SpawnItemExFlags.SetTracer;

                var second = world.ThingAllocation.SpawnItemEx(source, p);

                Assert.AreEqual(source, second.Target, "SETTARGET overrides NOPOINTERS");
                Assert.AreEqual(source, second.Tracer, "SETTRACER overrides NOPOINTERS");
            }
        }

        [TestMethod]
        public void IsTargetAndIsTracerPointBackAtTheSpawnedObject()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var source = NewSource(world);

                var p = new SpawnItemExParams();
                p.Type = (MobjType)ActorNames.TypeOf("HealthBonus");
                p.Flags = SpawnItemExFlags.IsTarget | SpawnItemExFlags.IsTracer;

                var spawned = world.ThingAllocation.SpawnItemEx(source, p);

                Assert.AreEqual(spawned, source.Target);
                Assert.AreEqual(spawned, source.Tracer);
            }
        }

        [TestMethod]
        public void MultiplySpeedUsesTheSpawnedActorsSpeed()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var source = NewSource(world);

                var p = new SpawnItemExParams();
                p.Type = MobjType.Troopshot;
                p.XVelocity = Fixed.FromInt(2);
                p.Flags = SpawnItemExFlags.MultiplySpeed | SpawnItemExFlags.AbsoluteVelocity;

                var spawned = world.ThingAllocation.SpawnItemEx(source, p);

                Assert.AreEqual((Fixed.FromInt(2) * spawned.Info.Speed).Data,
                    spawned.MomX.Data, "velocity is multiplied by the new actor's speed");
            }
        }

        [TestMethod]
        public void ABlockedMonsterDoesNotSpawnButNoCheckPositionAllowsIt()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var source = NewSource(world);
                var other = NewSource(world);

                // Two monsters at exactly the same point. The first is blocked by the second.
                var p = new SpawnItemExParams();
                p.Type = MobjType.Troop;
                p.Flags = 0;

                var blocked = world.ThingAllocation.SpawnItemEx(source, p);

                Assert.IsNull(blocked, "a monster inside another monster is not spawned");

                p.Flags = SpawnItemExFlags.NoCheckPosition;
                var allowed = world.ThingAllocation.SpawnItemEx(source, p);

                Assert.IsNotNull(allowed, "NOCHECKPOSITION deliberately allows the overlap");
            }
        }

        [TestMethod]
        public void AnUnnumberedActorCanBeSpawnedByItsDecorateName()
        {
            // The case ademo1.wad is built around: an effect has a name and states but no map
            // number. It gets an internal identity, and A_SpawnItemEx can use that same name.
            var problems = new List<string>();

            var script = DecorateConverter.Convert(
                "ACTOR Spark : DoomImp\n" +
                "{\n" +
                "  Health 25\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 A_SpawnItemEx(\"Spark\", 0, 0, 0, 0, 0, 0, 0, SXF_NOCHECKPOSITION)\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var type = ActorNames.TypeOf("Spark");

            Assert.IsTrue(type >= DoomInfo.CustomActorFirst,
                "the unnumbered actor got an internal type");
            StringAssert.Contains(script,
                ScriptSyntax.ActorDefinitionHeader(DoomInfo.MobjInfos[type].DoomEdNum));

            var library = new ScriptLibrary();
            library.Add(script, "test", DoomInfo.MobjInfos);
            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));
            Assert.AreEqual(25, DoomInfo.MobjInfos[type].SpawnHealth,
                "and its own properties were applied");

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var source = world.ThingAllocation.SpawnMobj(
                    world.ConsolePlayer.Mobj.X,
                    world.ConsolePlayer.Mobj.Y,
                    world.ConsolePlayer.Mobj.Z,
                    (MobjType)type);

                source.ThinkerState = ThinkerState.Active;
                var before = CountOf(world, (MobjType)type);

                source.SetState(DoomInfo.MobjInfos[type].SeeState);

                Assert.AreEqual(before + 1, CountOf(world, (MobjType)type),
                    "A_SpawnItemEx resolved the custom actor's name and spawned it");
            }
        }

        [TestMethod]
        public void AnExplicitlyNumberedActorCanBeSpawnedByItsDecorateName()
        {
            var problems = new List<string>();

            var script = DecorateConverter.Convert(
                "ACTOR Casing 14567\n" +
                "{\n" +
                "  Health 25\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 A_SpawnItemEx(\"Casing\", 0, 0, 0, 0, 0, 0, 0, SXF_NOCHECKPOSITION)\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            var type = ActorNames.TypeOf("Casing");

            Assert.AreEqual(DoomInfo.TypeOf(14567), type,
                "the explicit actor number and its friendly name resolve to the same type");

            var library = new ScriptLibrary();
            library.Add(script, "test", DoomInfo.MobjInfos);
            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var source = world.ThingAllocation.SpawnMobj(
                    world.ConsolePlayer.Mobj.X,
                    world.ConsolePlayer.Mobj.Y,
                    world.ConsolePlayer.Mobj.Z,
                    (MobjType)type);

                source.ThinkerState = ThinkerState.Active;
                var before = CountOf(world, (MobjType)type);

                source.SetState(DoomInfo.MobjInfos[type].SeeState);

                Assert.AreEqual(before + 1, CountOf(world, (MobjType)type),
                    "the explicitly numbered actor was spawned by name");
            }
        }

        [TestMethod]
        public void TheReserveLeavesRoomForRealDecorateCollections()
        {
            Assert.AreEqual(256, DoomInfo.CustomActorReserve,
                "a mod with dozens of small effects is not refused after 32 actors");
        }

        // ------------------------------------------------------------- the converter

        private static string Convert(string body, List<string> problems)
        {
            return DecorateConverter.Convert(
                "ACTOR TestImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 " + body + "\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);
        }

        [TestMethod]
        public void TheConverterKeepsExpressionsForTheFrame()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_SpawnItemEx(\"HealthBonus\", 15 - random(0,255) * 0.1171875, 0, 16, 0, 0, 0, 0, " +
                "SXF_ABSOLUTEPOSITION | SXF_ABSOLUTEVELOCITY)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "SpawnItemEx(\"See#1\", \"HealthBonus\"");
            StringAssert.Contains(script, "Random(0, 255)");
            StringAssert.Contains(script, "0.1171875");
            StringAssert.Contains(script, "SpawnEx_TestImp_See_1_0");
        }

        [TestMethod]
        public void UnsupportedFlagsAndTidAreNamedButTheSpawnStillRuns()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_SpawnItemEx(\"HealthBonus\", 0, 0, 0, 0, 0, 0, 0, " +
                "SXF_SETMASTER | SXF_TRANSFERALPHA, 0, 42)", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "SXF_SETMASTER");
            StringAssert.Contains(text, "SXF_TRANSFERALPHA");
            StringAssert.Contains(text, "42");
            StringAssert.Contains(script, "SpawnItemEx(\"See#1\", \"HealthBonus\"");
        }

        [TestMethod]
        public void AnUnknownSpawnActorIsReported()
        {
            var problems = new List<string>();

            Convert("A_SpawnItemEx(\"NotAThing\")", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "NotAThing");
        }

        [TestMethod]
        public void AnUnknownSpawnFlagDoesNotEraseTheKnownFlags()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_SpawnItemEx(\"HealthBonus\", 0, 0, 0, 0, 0, 0, 0, " +
                "SXF_ABSOLUTEPOSITION | SXF_NOSUCHTHING)", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "SXF_NOSUCHTHING");
            StringAssert.Contains(script, ", 2, \"\");");
        }

        [TestMethod]
        public void TheScriptSpawnsTheObjectInTheRealWorld()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_SpawnItemEx(\"HealthBonus\", 8, 0, 16, 0, 0, 0, 0, " +
                "SXF_ABSOLUTEPOSITION | SXF_ABSOLUTEVELOCITY)", problems);

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

                Assert.IsNotNull(imp);

                imp.ThinkerState = ThinkerState.Active;
                var before = CountOf(world, (MobjType)ActorNames.TypeOf("HealthBonus"));

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreEqual(before + 1, CountOf(world, (MobjType)ActorNames.TypeOf("HealthBonus")),
                    "the frame's SpawnItemEx created the object");
            }
        }

        [TestMethod]
        public void AFailedSpawnDoesNotEvaluateOtherRandomExpressions()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_SpawnItemEx(\"HealthBonus\", random(0,255), random(0,255), 0, 0, 0, 0, 0, 0, 256)",
                problems);

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

                Assert.IsNotNull(imp);

                imp.ThinkerState = ThinkerState.Active;
                var before = CountOf(world, (MobjType)ActorNames.TypeOf("HealthBonus"));
                var randomBefore = world.Random.Index;

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreEqual(before, CountOf(world, (MobjType)ActorNames.TypeOf("HealthBonus")),
                    "a 256 out of 256 fail chance suppresses the spawn");
                Assert.AreEqual(1, (world.Random.Index - randomBefore + 256) % 256,
                    "only the fail-chance draw happened; the other expressions were skipped");
            }
        }
    }
}
