using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;
using DPTDOOMTest.CompatibilityTests;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The conditional jumps that ask about the target or the tracer, and A_MonsterRefire.
    ///
    /// All of them reuse machinery that already exists - the condition functions and
    /// Mobj.JumpToState - so what has to be proven here is the question, not the mechanism. Every
    /// test therefore has a case where the answer is "no": a check that only ever fires would pass a
    /// positive test by accident.
    /// </summary>
    [TestClass]
    public class TargetJumpsTest
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

        private static List<Mobj> Imps(World world)
        {
            var imps = new List<Mobj>();

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;

                if (mobj != null && mobj.Type == MobjType.Troop)
                {
                    imps.Add(mobj);
                }
            }

            return imps;
        }

        private static string Convert(string body, List<string> problems)
        {
            return DecorateConverter.Convert(
                "ACTOR TargetImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  See:\n" +
                "    TROO A 4 " + body + "\n" +
                "    TROO B 4\n" +
                "    Loop\n" +
                "  Hit:\n" +
                "    TROO C 4\n" +
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

        /// <summary>
        /// Which states the imp passes through, with the jump target read while the mod is applied.
        ///
        /// Disposing the content puts the actor table back, so a target read afterwards would
        /// describe the vanilla imp and every comparison would be against the wrong state.
        /// </summary>
        /// <summary>
        /// The state of this test's "Hit" chain, found by its frame letter.
        ///
        /// A_MonsterRefire is an action that redirects the transition, not a state jump, so the
        /// target is not in JumpTarget and cannot be read from there.
        ///
        /// Scanned **backwards** on purpose: restoring the actor table between tests does not clear
        /// the custom state slots, so a previous test's Hit frame is still there. The most recently
        /// allocated match is the one this test just created.
        /// </summary>
        private static int HitState()
        {
            for (var i = DoomInfo.States.Length - 1; i >= DoomInfo.CustomStateFirst; i--)
            {
                var state = DoomInfo.States[i];

                // "C" is frame index 2, and the Hit chain is the only one that uses it here.
                if (state.Sprite == Sprite.TROO && state.Frame == 2)
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<int> Watch(string body, Action<Mobj, World> prepare, out int jumpTarget,
                                       out World watched)
        {
            var problems = new List<string>();
            var script = Convert(body, problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out var cmds);
                var world = game.World;
                var imps = Imps(world);

                Assert.IsTrue(imps.Count > 0, "MAP01 has imps to watch");

                var start = DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState;

                jumpTarget = DoomInfo.States[(int)start].JumpTarget;

                if (jumpTarget < 0)
                {
                    // An action that redirects, like A_MonsterRefire, leaves JumpTarget empty.
                    jumpTarget = HitState();
                }

                Assert.IsTrue(jumpTarget >= DoomInfo.CustomStateFirst,
                    "the mod's jump target is a state of its own");

                watched = world;

                foreach (var imp in imps)
                {
                    imp.ThinkerState = ThinkerState.Active;
                    prepare(imp, world);
                    imp.SetState(start);
                }

                var seen = new List<int>();

                for (var tic = 0; tic < 16; tic++)
                {
                    game.Update(cmds);

                    foreach (var imp in imps)
                    {
                        var number = imp.State.Number;

                        if (!seen.Contains(number))
                        {
                            seen.Add(number);
                        }
                    }
                }

                return seen;
            }
        }

        private static void PlaceAt(World world, Mobj thing, Fixed x, Fixed y)
        {
            world.ThingMovement.UnsetThingPosition(thing);
            thing.X = x;
            thing.Y = y;
            world.ThingMovement.SetThingPosition(thing);
        }

        [TestMethod]
        public void TracerCloserJumpsWhenTheTracerIsNear()
        {
            int target;
            World watched;

            var seen = Watch("A_JumpIfTracerCloser(128, \"Hit\")", (imp, world) =>
            {
                var player = world.ConsolePlayer.Mobj;
                imp.Tracer = player;
                PlaceAt(world, player, imp.X + Fixed.FromInt(32), imp.Y);
                player.Z = imp.Z;
            }, out target, out watched);

            CollectionAssert.Contains(seen, target, "a nearby tracer takes the jump");
        }

        [TestMethod]
        public void TracerCloserDoesNotJumpWhenTheTracerIsFar()
        {
            int target;
            World watched;

            var seen = Watch("A_JumpIfTracerCloser(128, \"Hit\")", (imp, world) =>
            {
                var player = world.ConsolePlayer.Mobj;
                imp.Tracer = player;
                PlaceAt(world, player, imp.X + Fixed.FromInt(1024), imp.Y);
                player.Z = imp.Z;
            }, out target, out watched);

            CollectionAssert.DoesNotContain(seen, target, "a distant tracer loops instead");
        }

        [TestMethod]
        public void WithoutNozAVerticalGapKeepsTheTracerAway()
        {
            // GZDoom's rule: the horizontal distance is not enough unless noz is set. The player is
            // close on the map but far above, which must not count as "near".
            int target;
            World watched;

            var seen = Watch("A_JumpIfTracerCloser(128, \"Hit\")", (imp, world) =>
            {
                var player = world.ConsolePlayer.Mobj;
                imp.Tracer = player;
                PlaceAt(world, player, imp.X + Fixed.FromInt(32), imp.Y);
                player.Z = imp.Z + Fixed.FromInt(1024);
                player.Flags |= MobjFlags.NoGravity;
            }, out target, out watched);

            CollectionAssert.DoesNotContain(seen, target,
                "a target far above is not close without noz");
        }

        [TestMethod]
        public void NozIgnoresTheVerticalGap()
        {
            int target;
            World watched;

            var seen = Watch("A_JumpIfTracerCloser(128, \"Hit\", 1)", (imp, world) =>
            {
                var player = world.ConsolePlayer.Mobj;
                imp.Tracer = player;
                PlaceAt(world, player, imp.X + Fixed.FromInt(32), imp.Y);
                player.Z = imp.Z + Fixed.FromInt(1024);
                player.Flags |= MobjFlags.NoGravity;
            }, out target, out watched);

            CollectionAssert.Contains(seen, target, "noz compares horizontally only");
        }

        [TestMethod]
        public void TheThirdArgumentIsNotTreatedAsMalformed()
        {
            // ademo1.wad writes A_JumpIfCloser(4096, "See", 1). Insisting on exactly two arguments
            // dropped that jump entirely and told the mod it was wrong.
            var problems = new List<string>();
            var script = Convert("A_JumpIfCloser(4096, \"Hit\", 1)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "return CloserToTarget(4096, 1);");
        }

        [TestMethod]
        public void MeleeRangeJumpsBothWays()
        {
            int target;
            World watched;

            var inside = Watch("A_JumpIfTargetInsideMeleeRange(\"Hit\")", (imp, world) =>
            {
                var player = world.ConsolePlayer.Mobj;
                imp.Target = player;
                PlaceAt(world, player, imp.X + Fixed.FromInt(16), imp.Y);
                player.Z = imp.Z;
            }, out target, out watched);

            CollectionAssert.Contains(inside, target, "an adjacent target is in melee reach");

            var outside = Watch("A_JumpIfTargetOutsideMeleeRange(\"Hit\")", (imp, world) =>
            {
                var player = world.ConsolePlayer.Mobj;
                imp.Target = player;
                PlaceAt(world, player, imp.X + Fixed.FromInt(512), imp.Y);
                player.Z = imp.Z;
            }, out target, out watched);

            CollectionAssert.Contains(outside, target, "a distant target is outside melee reach");
        }

        [TestMethod]
        public void TheInsideAndOutsideFormsAskOppositeQuestions()
        {
            var problems = new List<string>();

            var inside = Convert("A_JumpIfTargetInsideMeleeRange(\"Hit\")", problems);
            StringAssert.Contains(inside, "return InMeleeRange();");

            var outside = Convert("A_JumpIfTargetOutsideMeleeRange(\"Hit\")", problems);
            StringAssert.Contains(outside, "return !InMeleeRange();");
        }

        [TestMethod]
        public void CheckCeilingJumpsOnlyWhenTouchingTheCeiling()
        {
            int target;
            World watched;

            var touching = Watch("A_CheckCeiling(\"Hit\")", (imp, world) =>
            {
                imp.Z = imp.CeilingZ - imp.Height;
            }, out target, out watched);

            CollectionAssert.Contains(touching, target, "an actor against the ceiling jumps");

            var below = Watch("A_CheckCeiling(\"Hit\")", (imp, world) =>
            {
                imp.Z = imp.FloorZ;
            }, out target, out watched);

            CollectionAssert.DoesNotContain(below, target, "an actor on the floor does not");
        }

        [TestMethod]
        public void MonsterRefireLeavesWhenTheTargetIsGone()
        {
            // Chance 0 means "never keep firing on the roll", so the sight test decides. With no
            // target the actor must leave for the label instead of standing there shooting.
            int target;
            World watched;

            var seen = Watch("A_MonsterRefire(0, \"Hit\")", (imp, world) =>
            {
                imp.Target = null;
            }, out target, out watched);

            CollectionAssert.Contains(seen, target, "no target means leaving the attack");
        }

        [TestMethod]
        public void MonsterRefireKeepsFiringWhenTheTargetIsVisible()
        {
            int target;
            World watched;

            var sight = false;

            var seen = Watch("A_MonsterRefire(0, \"Hit\")", (imp, world) =>
            {
                var player = world.ConsolePlayer.Mobj;

                // The imp moves to the player, not the other way round: the player's start spot is
                // known to be standable, while an arbitrary offset from an imp can be inside a wall.
                PlaceAt(world, imp, player.X + Fixed.FromInt(48), player.Y);
                imp.Z = player.Z;
                imp.Target = player;
                imp.Angle = Geometry.PointToAngle(imp.X, imp.Y, player.X, player.Y);
                sight = world.VisibilityCheck.CheckSight(imp, player);
            }, out target, out watched);

            Assert.IsTrue(sight, "the test needs a clear line of sight to mean anything");
            CollectionAssert.DoesNotContain(seen, target,
                "a visible, living target keeps the monster firing");
        }

        [TestMethod]
        public void MonsterRefireDrawsOneRandomNumberEvenWhenItDoesNotJump()
        {
            // The draw happens before the sight test, as in the original. Counting it makes a change
            // in that order visible instead of silently shifting every later draw in the world.
            int target;
            World watched;

            Watch("A_MonsterRefire(0, \"Hit\")", (imp, world) =>
            {
                var player = world.ConsolePlayer.Mobj;
                imp.Target = player;
                PlaceAt(world, player, imp.X + Fixed.FromInt(48), imp.Y);
                player.Z = imp.Z;
                imp.Angle = Geometry.PointToAngle(imp.X, imp.Y, player.X, player.Y);
            }, out target, out watched);

            var index = watched.Random.Index;

            Assert.IsTrue(index != 0, "the refire roll came from the world generator");
        }

        [TestMethod]
        public void FaceTracerTurnsTowardTheTracerNotTheTarget()
        {
            var problems = new List<string>();
            var script = Convert("A_FaceTracer(90, 270, 0)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "FaceTracer(\"See#1\", 90, 0);");

            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out var cmds);
                var world = game.World;
                var imp = Imps(world)[0];
                var player = world.ConsolePlayer.Mobj;

                // The target is behind the imp and the tracer is to its right: facing the tracer
                // must win, and a version that read the target would turn the other way.
                imp.Target = null;
                imp.Tracer = player;
                imp.Angle = Angle.Ang0;
                PlaceAt(world, player, imp.X, imp.Y + Fixed.FromInt(64));
                player.Z = imp.Z;

                imp.ThinkerState = ThinkerState.Active;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                var expected = Geometry.PointToAngle(imp.X, imp.Y, player.X, player.Y);

                Assert.AreEqual(expected.Data, imp.Angle.Data,
                    "the imp turned toward its tracer");
            }
        }

        [TestMethod]
        public void FaceTracerPitchIsReportedRatherThanSilentlyDropped()
        {
            var problems = new List<string>();

            Convert("A_FaceTracer(0, 90, 0)", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "pitch");
        }
    }
}
