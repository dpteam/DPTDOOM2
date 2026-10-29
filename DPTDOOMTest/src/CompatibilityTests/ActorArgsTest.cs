using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;
using DPTDOOMTest.CompatibilityTests;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_SetArg and args[N]: the actor's own five numbers.
    ///
    /// ademo1.wad uses them as a scratchpad - a countdown, a light index, a bit field of switches -
    /// so the tests check that the value is an expression evaluated at the frame, that an index
    /// outside 0..4 is refused while the file is read, and that the numbers survive a save.
    /// </summary>
    [TestClass]
    public class ActorArgsTest
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

        private static string Convert(string body, List<string> problems)
        {
            return DecorateConverter.Convert(
                "ACTOR ArgImp 3001\n" +
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
        public void AnActorStartsWithZeroArgs()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var imp = FirstImp(game.World);

                Assert.IsNotNull(imp);
                Assert.AreEqual(0, imp.GetArg(0));
                Assert.AreEqual(0, imp.GetArg(4));
                Assert.IsFalse(imp.HasArgs, "and pays for nothing until one is written");
            }
        }

        [TestMethod]
        public void ArgsOutsideTheRangeAreIgnoredAndReported()
        {
            var problems = new List<string>();

            Convert("A_SetArg(9, 1)", problems);
            StringAssert.Contains(string.Join("; ", problems.ToArray()), "an actor has 5");

            var imp = FirstImp(NewGame(GameContent.CreateDummy(WadPath.Doom2), out _).World);

            imp.SetArg(-1, 5);
            imp.SetArg(5, 5);

            Assert.AreEqual(0, imp.GetArg(0), "a write outside the range does not land anywhere");
        }

        [TestMethod]
        public void ArgsBecomeExpressionsInTheCondition()
        {
            var problems = new List<string>();

            var script = Convert("A_JumpIf(args[0] > 0, \"See\")", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "return Arg(0) > 0;");
        }

        [TestMethod]
        public void TheSectorHeightsAreReadable()
        {
            var problems = new List<string>();

            var script = Convert("A_SetArg(4, floorz - ceilingz)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "FloorZ() - CeilingZ()");
        }

        [TestMethod]
        public void TheScriptCountsDownInTheRealWorld()
        {
            var problems = new List<string>();
            var script = Convert("A_SetArg(0, args[0] - 1)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var imp = FirstImp(game.World);

                Assert.IsNotNull(imp);
                imp.SetArg(0, 5);
                imp.ThinkerState = ThinkerState.Active;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreEqual(4, imp.GetArg(0), "the frame read its own arg and wrote it back");
            }
        }

        [TestMethod]
        public void AnArgCanGateAJump()
        {
            // The jump goes to a **different** label. Jumping to the frame's own label with an
            // always-true condition is a zero-tic cycle: the actor would leave the frame for
            // itself, forever. That is a modding mistake, and the engine now stops it instead of
            // hanging - see the cycle test below.
            var problems = new List<string>();
            var script = DecorateConverter.Convert(
                "ACTOR ArgImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 A_JumpIf(args[0] == 0, \"Hit\")\n" +
                "    TROO B 4\n" +
                "    Loop\n" +
                "  Hit:\n" +
                "    TROO C 4\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                var start = DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState;
                var target = DoomInfo.States[(int)start].JumpTarget;

                Assert.IsTrue(target >= DoomInfo.CustomStateFirst,
                    "the jump points at the mod's own Hit frame");

                imp.ThinkerState = ThinkerState.Active;
                imp.SetArg(0, 0);
                imp.SetState(start);

                Assert.AreEqual(target, imp.State.Number,
                    "arg 0 is zero, so the jump is taken");
            }
        }

        [TestMethod]
        public void AZeroTicCycleIsStoppedInsteadOfHanging()
        {
            // The real failure this guards: a frame that jumps to itself without spending a tic
            // made SetState loop forever, and the game froze with its window drawn. A mod can write
            // this by accident, so the engine has to survive it.
            var problems = new List<string>();
            var script = DecorateConverter.Convert(
                "ACTOR CycleImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 0 A_Jump(256, \"See\")\n" +
                "    Loop\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var imp = FirstImp(game.World);

                Assert.IsNotNull(imp);

                // The call returns rather than spinning: that is the whole assertion, and it is
                // enforced by the test timing out rather than by an exception.
                imp.ThinkerState = ThinkerState.Active;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreEqual(1, imp.Tics,
                    "the actor is held on the frame with a tic left, so it cannot spin");
            }
        }

        [TestMethod]
        public void ArgsSurviveASaveAndLoad()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var imp = FirstImp(game.World);

                Assert.IsNotNull(imp);

                imp.SetArg(0, 11);
                imp.SetArg(4, -3);

                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "dptdoom-args-" + Guid.NewGuid().ToString("N") + ".sav");

                try
                {
                    SaveAndLoad.Save(game, "args", path);
                    SaveAndLoad.Load(game, path);

                    var restored = FirstImp(game.World);

                    Assert.IsNotNull(restored);
                    Assert.AreEqual(11, restored.GetArg(0));
                    Assert.AreEqual(-3, restored.GetArg(4), "a negative arg is state too");
                }
                finally
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }
                }
            }
        }
    }
}
