using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Five frame actions older mods use constantly and this engine did not have: A_ChangeFlag,
    /// A_SetTics, A_Recoil, A_ClearTarget and A_FaceTarget's arguments.
    ///
    /// Each was previously either dropped with "no such action" (A_ChangeFlag, A_SetTics,
    /// A_Recoil) or run with its arguments silently ignored (A_FaceTarget), which is the worse of
    /// the two: a monster that turns instantly instead of gradually looks like it works.
    ///
    /// A_FaceTarget's first argument is the one worth being careful about. ZDoom's signature is
    /// (max_turn, max_pitch, ang_offset), so A_FaceTarget(90) asks for a **limit on turning**, not
    /// an offset of 90 degrees. Read the wrong way, a turret that should swing slowly would snap
    /// round - the opposite of the request.
    ///
    /// The tests here go through the real script library and the real world rather than reading
    /// the generated text back, because the question is whether the actor behaves differently, not
    /// whether a line was written.
    /// </summary>
    [TestClass]
    public class DecorateActionsTest
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

        /// <summary>
        /// An imp whose see chain is one frame carrying the given call.
        ///
        /// The call has to be in the **same** function as the frames: a frame name is scoped to the
        /// actor definition that created it.
        /// </summary>
        private static void DefineImpFrame(int tics, string work)
        {
            var library = new ScriptLibrary();

            library.Add(
                "fn Thing_3001() {\n" +
                "    State(\"See#1\", \"TROO\", \"A\", " + tics + ", \"\", \"See#1\");\n" +
                "    SeeState(\"See#1\");\n" +
                work.Replace("$FRAME", "See#1") + "\n" +
                "}\n", "test", DoomInfo.MobjInfos);

            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));
        }

        /// <summary>Puts the imp into the frame the script defined.</summary>
        private static Mobj ImpInSeeFrame(World world)
        {
            var imp = FirstImp(world);

            Assert.IsNotNull(imp, "MAP01 has an imp");

            imp.ThinkerState = ThinkerState.Active;
            imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

            return imp;
        }

        [TestMethod]
        public void AChangeFlagSetsAndClears()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var imp = FirstImp(game.World);

                Assert.IsNotNull(imp);
                Assert.AreEqual(0, (int)(imp.Flags & MobjFlags.NoPain), "control: clear to begin with");

                DefineImpFrame(4, "    ChangeFlag(\"$FRAME\", \"NoPain\", true);");

                imp.ThinkerState = ThinkerState.Active;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreNotEqual(0, (int)(imp.Flags & MobjFlags.NoPain), "the flag was set");
            }
        }

        [TestMethod]
        public void AChangeFlagCanClearAFlagToo()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var imp = FirstImp(game.World);

                Assert.IsNotNull(imp);

                imp.Flags |= MobjFlags.Shootable;
                imp.Flags |= MobjFlags.NoPain;

                DefineImpFrame(4, "    ChangeFlag(\"$FRAME\", \"NoPain\", false);");

                imp.ThinkerState = ThinkerState.Active;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreEqual(0, (int)(imp.Flags & MobjFlags.NoPain), "the flag was cleared");
                Assert.AreNotEqual(0, (int)(imp.Flags & MobjFlags.Shootable),
                    "and no other flag was touched");
            }
        }

        [TestMethod]
        public void ASetTicsChangesHowLongTheFrameLasts()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                // The frame says 2 and the action says 9: the action wins, which is the whole
                // point of the action.
                DefineImpFrame(2, "    SetTics(\"$FRAME\", 9);");

                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.ThinkerState = ThinkerState.Active;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreEqual(9, imp.Tics, "the frame lasts as long as the action said");
            }
        }

        [TestMethod]
        public void ARecoilPushesBackwards()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                DefineImpFrame(4, "    Recoil(\"$FRAME\", 8.0);");

                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                imp.ThinkerState = ThinkerState.Active;

                // Facing east, so a push backwards is west: negative X and no Y.
                imp.Angle = Angle.Ang0;
                imp.MomX = Fixed.Zero;
                imp.MomY = Fixed.Zero;

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                // Compared in map units with a tolerance: the engine's tables are the WAD's fine
                // sine and cosine, so cos(0) is 65535 and sin(0) is a couple of hundred raw units
                // off zero. Demanding exact raw values here would be testing the table, not the
                // action.
                Assert.AreEqual(-8.0, (double)imp.MomX.Data / Fixed.FracUnit, 0.01, "pushed west");
                Assert.AreEqual(0.0, (double)imp.MomY.Data / Fixed.FracUnit, 0.01, "and not sideways");
            }
        }

        [TestMethod]
        public void AClearTargetForgetsTheTarget()
        {
            // A plain action, not frame work: it takes no arguments, so it lives in the action
            // table and a mod writes it with no parentheses.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);
                var player = world.ConsolePlayer;

                Assert.IsNotNull(imp);
                Assert.IsNotNull(player);

                imp.Target = player.Mobj;

                Action<World, Mobj> action;

                Assert.IsTrue(DoomInfo.TryGetAction("A_ClearTarget", out action),
                    "the action exists under the name a mod writes");

                action(world, imp);

                Assert.IsNull(imp.Target, "the target is gone");
            }
        }

        [TestMethod]
        public void AFaceTargetLimitTurnsGradually()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                DefineImpFrame(4, "    FaceTarget(\"$FRAME\", 45, 0);");

                var imp = FirstImp(world);
                var player = world.ConsolePlayer;

                Assert.IsNotNull(imp);
                Assert.IsNotNull(player);

                // The player due south of the imp, the imp facing east: 90 degrees to turn.
                player.Mobj.X = imp.X;
                player.Mobj.Y = imp.Y + Fixed.FromInt(64);
                world.ThingMovement.SetThingPosition(player.Mobj);

                imp.Target = player.Mobj;
                imp.Angle = Angle.Ang0;
                imp.ThinkerState = ThinkerState.Active;

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreEqual(45.0, imp.Angle.ToDegree(), 0.01,
                    "turned 45 of the 90 degrees it needed, not all of them");
            }
        }

        [TestMethod]
        public void AFaceTargetWithoutALimitSnaps()
        {
            // The control: with a limit wide enough to cover the turn, the actor arrives in one
            // step - so the test above is about the limit rather than about facing being broken.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                DefineImpFrame(4, "    FaceTarget(\"$FRAME\", 180, 0);");

                var imp = FirstImp(world);
                var player = world.ConsolePlayer;

                Assert.IsNotNull(imp);
                Assert.IsNotNull(player);

                player.Mobj.X = imp.X;
                player.Mobj.Y = imp.Y + Fixed.FromInt(64);
                world.ThingMovement.SetThingPosition(player.Mobj);

                imp.Target = player.Mobj;
                imp.Angle = Angle.Ang0;
                imp.ThinkerState = ThinkerState.Active;

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreEqual(90.0, imp.Angle.ToDegree(), 0.01, "turned all the way");
            }
        }

        [TestMethod]
        public void PlainFaceTargetIsUnchanged()
        {
            // The vanilla path: no arguments, and the actor snaps to its target. Every attacking
            // monster in the IWAD runs this, so it must not have moved.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);
                var player = world.ConsolePlayer;

                Assert.IsNotNull(imp);
                Assert.IsNotNull(player);

                player.Mobj.X = imp.X;
                player.Mobj.Y = imp.Y + Fixed.FromInt(64);
                world.ThingMovement.SetThingPosition(player.Mobj);

                imp.Target = player.Mobj;
                imp.Angle = Angle.Ang0;

                world.MonsterBehavior.FaceTarget(imp);

                Assert.AreEqual(90.0, imp.Angle.ToDegree(), 0.01, "snapped to the target");
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
                "  See:\n" +
                "    TROO A 4 " + body + "\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);
        }

        [TestMethod]
        public void TheConverterWritesChangeFlagWithTheEnginesName()
        {
            var problems = new List<string>();

            var script = Convert("A_ChangeFlag(\"NOPAIN\", 1)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "ChangeFlag(\"See#1\", \"Nopain\", true);");
        }

        [TestMethod]
        public void AnUnknownFlagCostsTheActionAndNotTheActor()
        {
            var problems = new List<string>();

            var script = Convert("A_ChangeFlag(\"NOSUCHFLAG\", 1)", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "NOSUCHFLAG");
            Assert.IsFalse(script.Contains("ChangeFlag("), "no call is written for a flag that does not exist");

            // The frame itself survives, so the animation is what the author wrote.
            StringAssert.Contains(script, "State(\"See#1\"");
        }

        [TestMethod]
        public void TheConverterWritesSetTics()
        {
            var problems = new List<string>();

            var script = Convert("A_SetTics(6)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "SetTics(\"See#1\", 6);");
        }

        [TestMethod]
        public void TheConverterWritesRecoil()
        {
            var problems = new List<string>();

            var script = Convert("A_Recoil(8)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "Recoil(\"See#1\", 8.0);");
        }

        [TestMethod]
        public void TheConverterReadsFaceTargetsFirstArgumentAsALimit()
        {
            var problems = new List<string>();

            var script = Convert("A_FaceTarget(90)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "FaceTarget(\"See#1\", 90, 0);");
        }

        [TestMethod]
        public void APitchLimitIsReported()
        {
            var problems = new List<string>();

            Convert("A_FaceTarget(0, 30)", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "pitch",
                "a mod that asks a monster to aim up is told this engine cannot");
        }

        [TestMethod]
        public void EmptyParenthesesAreNotArguments()
        {
            // A_Look() is correct DECORATE and mods write it. Complaining about it would be a
            // report line about valid input, which is how a report stops being read.
            var problems = new List<string>();

            var script = Convert("A_Look()", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "A_Look");
        }
    }
}
