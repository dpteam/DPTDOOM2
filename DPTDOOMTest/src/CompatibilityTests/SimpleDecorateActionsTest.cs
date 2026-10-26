using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Small but frequent DECORATE actions: A_Stop, A_NoBlocking, A_SetHealth, A_SetAngle,
    /// A_ChangeVelocity and A_ScaleVelocity.
    ///
    /// The tests cover both the direct engine behaviour and the full conversion path. In
    /// particular, angle is an expression in degrees and AAPTR_TARGET/AAPTR_TRACER affect the
    /// pointed-at actor rather than the actor running the frame.
    /// </summary>
    [TestClass]
    public class SimpleDecorateActionsTest
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

        [TestMethod]
        public void StopAndNoBlockingArePlainActions()
        {
            Action<World, Mobj> stop;
            Action<World, Mobj> unblock;

            Assert.IsTrue(DoomInfo.TryGetAction("A_Stop", out stop), "A_Stop is a real action");
            Assert.IsTrue(DoomInfo.TryGetAction("A_NoBlocking", out unblock),
                "A_NoBlocking is a real action");

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.MomX = Fixed.FromInt(8);
                imp.MomY = Fixed.FromInt(-4);
                imp.MomZ = Fixed.FromInt(2);
                imp.Flags |= MobjFlags.Solid;

                stop(world, imp);

                Assert.AreEqual(0, imp.MomX.Data);
                Assert.AreEqual(0, imp.MomY.Data);
                Assert.AreEqual(0, imp.MomZ.Data);

                unblock(world, imp);

                Assert.AreEqual(0, (int)(imp.Flags & MobjFlags.Solid),
                    "A_NoBlocking clears SOLID");
            }
        }

        [TestMethod]
        public void SetHealthClampsToOneAndUpdatesThePlayer()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer;

                player.Mobj.Health = 100;
                player.Health = 100;

                world.MonsterBehavior.SetHealth(player.Mobj, 0);

                Assert.AreEqual(1, player.Mobj.Health, "zero becomes one, not death");
                Assert.AreEqual(1, player.Health, "the player record agrees with the mobj");

                world.MonsterBehavior.SetHealth(player.Mobj, 250);

                Assert.AreEqual(250, player.Mobj.Health);
                Assert.AreEqual(250, player.Health);
            }
        }

        [TestMethod]
        public void SetAngleReplacesTheAngle()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                world.MonsterBehavior.SetAngle(imp, Angle.Ang90);

                Assert.AreEqual(Angle.Ang90.Data, imp.Angle.Data);
            }
        }

        [TestMethod]
        public void ChangeVelocityRelativeReplaceUsesTheActorAngle()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.Angle = Angle.Ang90;
                imp.MomX = Fixed.FromInt(100);
                imp.MomY = Fixed.FromInt(100);
                imp.MomZ = Fixed.FromInt(100);

                world.MonsterBehavior.ChangeVelocity(
                    imp, Fixed.FromInt(8), Fixed.FromInt(0), Fixed.FromInt(2), 1 | 2);

                Assert.AreEqual(0.0, (double)imp.MomX.Data / Fixed.FracUnit, 0.01,
                    "forward x becomes zero at ninety degrees");
                Assert.AreEqual(8.0, (double)imp.MomY.Data / Fixed.FracUnit, 0.01,
                    "forward x becomes y at ninety degrees");
                Assert.AreEqual(2.0, (double)imp.MomZ.Data / Fixed.FracUnit, 0.01);
            }
        }

        [TestMethod]
        public void ChangeVelocityAddsWithoutReplace()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.Angle = Angle.Ang0;
                imp.MomX = Fixed.FromInt(3);
                imp.MomY = Fixed.FromInt(-2);
                imp.MomZ = Fixed.FromInt(1);

                world.MonsterBehavior.ChangeVelocity(
                    imp, Fixed.FromInt(4), Fixed.FromInt(5), Fixed.FromInt(2), 0);

                Assert.AreEqual(7.0, (double)imp.MomX.Data / Fixed.FracUnit, 0.01);
                Assert.AreEqual(3.0, (double)imp.MomY.Data / Fixed.FracUnit, 0.01);
                Assert.AreEqual(3.0, (double)imp.MomZ.Data / Fixed.FracUnit, 0.01);
            }
        }

        [TestMethod]
        public void ScaleVelocityScalesAllComponents()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.MomX = Fixed.FromInt(8);
                imp.MomY = Fixed.FromInt(-4);
                imp.MomZ = Fixed.FromInt(2);

                world.MonsterBehavior.ScaleVelocity(imp, new Fixed(Fixed.FracUnit / 2));

                Assert.AreEqual(4.0, (double)imp.MomX.Data / Fixed.FracUnit, 0.01);
                Assert.AreEqual(-2.0, (double)imp.MomY.Data / Fixed.FracUnit, 0.01);
                Assert.AreEqual(1.0, (double)imp.MomZ.Data / Fixed.FracUnit, 0.01);
            }
        }

        [TestMethod]
        public void TargetAndTracerPointersAreHonoured()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);

                imp.Target = player;
                imp.Tracer = player;

                Assert.AreEqual(player, ActorPointer.Resolve(imp, ActorPointer.Target));
                Assert.AreEqual(player, ActorPointer.Resolve(imp, ActorPointer.Tracer));
                Assert.AreEqual(imp, ActorPointer.Resolve(imp, ActorPointer.Default));
                Assert.IsNull(ActorPointer.Resolve(imp, ActorPointer.Null));
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
        public void TheConverterEmitsHealthAndAngleExpressions()
        {
            var problems = new List<string>();

            var script = Convert("A_SetHealth(random(20,30))", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "SetHealth(\"Melee#1\"");
            StringAssert.Contains(script, "Random(20, 30)");

            problems.Clear();
            script = Convert("A_SetAngle(angle + 100)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "SetAngle(\"Melee#1\"");
            StringAssert.Contains(script, "Angle()");
        }

        [TestMethod]
        public void TheConverterReadsVelocityFlagsAndPointers()
        {
            var problems = new List<string>();

            var script = Convert(
                "A_ChangeVelocity(8, 0, 2, CVF_RELATIVE | CVF_REPLACE, AAPTR_TARGET)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "ChangeVelocity(\"Melee#1\"");
            StringAssert.Contains(script, ", 3, 2);");
        }

        [TestMethod]
        public void AnUnsupportedPointerIsReportedAndTheActionIsNotApplied()
        {
            var problems = new List<string>();

            var script = Convert("A_SetHealth(10, AAPTR_MASTER)", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "AAPTR_MASTER");
            Assert.IsFalse(script.Contains("SetHealth("),
                "an unsupported pointer must not make the action affect the wrong actor");
        }

        [TestMethod]
        public void TheSetAngleExpressionRunsInTheWorld()
        {
            var problems = new List<string>();
            var script = Convert("A_SetAngle(angle + 100)", problems);

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

                imp.Angle = Angle.Ang0;
                imp.ThinkerState = ThinkerState.Active;

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].MeleeState);

                Assert.AreEqual(100.0, imp.Angle.ToDegree(), 0.01,
                    "the expression read the current angle in degrees and added 100");
            }
        }

        [TestMethod]
        public void TheChangeVelocityExpressionRunsInTheWorld()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_ChangeVelocity(8, 0, 2, CVF_RELATIVE | CVF_REPLACE)", problems);

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

                imp.Angle = Angle.Ang0;
                imp.ThinkerState = ThinkerState.Active;

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].MeleeState);

                Assert.AreEqual(8.0, (double)imp.MomX.Data / Fixed.FracUnit, 0.01);
                Assert.AreEqual(0.0, (double)imp.MomY.Data / Fixed.FracUnit, 0.01);
                Assert.AreEqual(2.0, (double)imp.MomZ.Data / Fixed.FracUnit, 0.01);
            }
        }
    }
}
