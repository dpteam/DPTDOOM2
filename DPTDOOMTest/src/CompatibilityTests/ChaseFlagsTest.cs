using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_Chase with its arguments: flags, and states to use instead of the actor's own.
    ///
    /// The argument order is the reason this file exists at all. ZDoom's signature is
    /// <c>A_Chase(melee, missile, flags)</c>, so flags are the **third** argument and
    /// <c>A_Chase(CHF_DONTMOVE)</c> is not a turret - it is a state label of 16. A converter that
    /// guessed the friendlier order would turn a broken mod into a differently broken one, with
    /// nothing to show for it.
    ///
    /// Every behaviour test has a control, because most of these flags switch something off: "the
    /// monster did not move" proves nothing if it could not move anyway.
    /// </summary>
    [TestClass]
    public class ChaseFlagsTest
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
            return NewGame(content, GameSkill.Medium, false);
        }

        private static DoomGame NewGame(GameContent content, GameSkill skill, bool fastMonsters)
        {
            var options = new GameOptions();
            options.GameVersion = content.Wad.GameVersion;
            options.GameMode = content.Wad.GameMode;
            options.MissionPack = content.Wad.MissionPack;
            options.Skill = skill;
            options.FastMonsters = fastMonsters;
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

        /// <summary>
        /// Moves an already-spawned thing to a new place in the world.
        ///
        /// The unlink is not optional. SetThingPosition **adds** the thing to its sector's and
        /// blockmap cell's lists without taking the old links out, so calling it on a thing that
        /// is already linked makes the list point at itself - and the next thing that walks the
        /// list never stops. The first version of these tests did exactly that and hung.
        /// </summary>
        private static void PlaceAt(World world, Mobj mobj, Fixed x, Fixed y)
        {
            world.ThingMovement.UnsetThingPosition(mobj);

            mobj.X = x;
            mobj.Y = y;

            world.ThingMovement.SetThingPosition(mobj);
        }

        /// <summary>
        /// The eight-way direction an actor would walk to reach another, as the engine numbers
        /// them: east is 0 and they run anticlockwise.
        /// </summary>
        private static Direction DirectionToward(Mobj from, Mobj to)
        {
            var angle = Geometry.PointToAngle(from.X, from.Y, to.X, to.Y);

            var step = (int)Math.Round(angle.ToDegree() / 45.0) & 7;

            return (Direction)step;
        }

        /// <summary>
        /// An imp in plain view of the player, 96 map units away, with both attack states cleared
        /// so that a chase test is about moving and not about swinging.
        /// </summary>
        private static Mobj ChasingImp(World world, out Mobj player)
        {
            player = world.ConsolePlayer.Mobj;

            var info = DoomInfo.MobjInfos[(int)MobjType.Troop];
            info.MeleeState = MobjState.Null;
            info.MissileState = MobjState.Null;

            for (var i = 0; i < 4; i++)
            {
                var angle = player.Angle + Angle.FromDegree(90 * i);

                var x = player.X + Fixed.FromInt(96) * Trig.Cos(angle);
                var y = player.Y + Fixed.FromInt(96) * Trig.Sin(angle);

                var imp = world.ThingAllocation.SpawnMobj(x, y, player.Z, MobjType.Troop);

                if (world.VisibilityCheck.CheckSight(imp, player))
                {
                    imp.ThinkerState = ThinkerState.Active;
                    imp.Target = player;
                    imp.Threshold = 0;
                    imp.ReactionTime = 0;

                    // Facing the player, because that is the one direction known to be walkable:
                    // the sight line is clear, so a step along it cannot hit a wall. A direction
                    // picked at random would make "did it move" a question about the map.
                    imp.MoveDir = DirectionToward(imp, player);
                    imp.MoveCount = 0;

                    return imp;
                }
            }

            return null;
        }

        [TestMethod]
        public void TheFlagValuesAreZDooms()
        {
            // From p_enemy.cpp. A wrong value would silently mean a different flag, and every one
            // of these changes how a monster behaves.
            Assert.AreEqual(1, ChaseFlags.FastChase);
            Assert.AreEqual(2, ChaseFlags.NoPlayActive);
            Assert.AreEqual(4, ChaseFlags.NightmareFast);
            Assert.AreEqual(8, ChaseFlags.Resurrect);
            Assert.AreEqual(16, ChaseFlags.DontMove);
            Assert.AreEqual(32, ChaseFlags.NoRandomTurn);
            Assert.AreEqual(64, ChaseFlags.NoDirectionTurn);
            Assert.AreEqual(128, ChaseFlags.NoPostAttackTurn);
            Assert.AreEqual(256, ChaseFlags.StopIfBlocked);
            Assert.AreEqual(512, ChaseFlags.DontIdle);
            Assert.AreEqual(1024, ChaseFlags.DontLookAllAround);
        }

        [TestMethod]
        public void PlainChaseAndChaseWithNoArgumentsAreTheSameCall()
        {
            // The guard on the vanilla path: Chase(actor) must be Chase(actor, 0, -1, -1), or every
            // monster in the IWAD would be running something else than it did before.
            //
            // Two **separate worlds** rather than two calls in one, and that is not tidiness: a
            // second call in the same world starts from wherever the first one left the monster,
            // so comparing them compares the starting positions rather than the overload. The
            // random table is fixed and the map is the same, so two worlds are identical by
            // construction.
            int plainX, plainY, plainAngle;
            ChaseAndRecord(false, out plainX, out plainY, out plainAngle);

            int argX, argY, argAngle;
            ChaseAndRecord(true, out argX, out argY, out argAngle);

            Assert.AreEqual(plainX, argX, "same X either way");
            Assert.AreEqual(plainY, argY, "same Y either way");
            Assert.AreEqual(plainAngle, argAngle, "same angle either way");
        }

        /// <summary>Runs one chase in a fresh world and reports where the monster ended up.</summary>
        private static void ChaseAndRecord(bool withArguments, out int x, out int y, out int angle)
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                Mobj player;
                var imp = ChasingImp(world, out player);

                Assert.IsNotNull(imp, "the test needs an imp in view of the player");

                // A fixed starting direction, so that the two runs differ only in the call.
                imp.MoveDir = Direction.East;
                imp.MoveCount = 0;

                if (withArguments)
                {
                    world.MonsterBehavior.Chase(imp, 0, -1, -1);
                }
                else
                {
                    world.MonsterBehavior.Chase(imp);
                }

                x = imp.X.Data;
                y = imp.Y.Data;
                angle = (int)imp.Angle.Data;
            }
        }

        [TestMethod]
        public void DontMoveKeepsTheMonsterInPlace()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                Mobj player;
                var imp = ChasingImp(world, out player);

                Assert.IsNotNull(imp);

                var startX = imp.X;
                var startY = imp.Y;

                // A positive move count, so that the move happens on this tic instead of the actor
                // spending it choosing a direction. Without this the control would fail for a
                // reason that has nothing to do with the flag.
                imp.MoveCount = 5;

                world.MonsterBehavior.Chase(imp, 0, -1, -1);

                Assert.IsTrue(startX.Data != imp.X.Data || startY.Data != imp.Y.Data,
                    "control: without the flag the monster walks toward its target");

                PlaceAt(world, imp, startX, startY);
                imp.MoveDir = DirectionToward(imp, player);
                imp.MoveCount = 5;
                imp.ThinkerState = ThinkerState.Active;

                world.MonsterBehavior.Chase(imp, ChaseFlags.DontMove, -1, -1);

                Assert.AreEqual(startX.Data, imp.X.Data, "with the flag it stays put");
                Assert.AreEqual(startY.Data, imp.Y.Data, "in both axes");
            }
        }

        [TestMethod]
        public void NoDirectionTurnLeavesTheAngleAlone()
        {
            // The imp faces 45 degrees while its movement direction is east, so vanilla turns it to
            // 0 and the flag leaves it at 45.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                Mobj player;
                var imp = ChasingImp(world, out player);

                Assert.IsNotNull(imp);

                imp.Angle = Angle.FromDegree(45);
                imp.MoveDir = Direction.East;

                world.MonsterBehavior.Chase(imp, 0, -1, -1);

                Assert.AreEqual(0.0, imp.Angle.ToDegree(), 0.01,
                    "control: vanilla turns to face the way it walks");

                imp.Angle = Angle.FromDegree(45);
                imp.MoveDir = Direction.East;
                imp.ThinkerState = ThinkerState.Active;

                world.MonsterBehavior.Chase(imp, ChaseFlags.NoDirectionTurn, -1, -1);

                Assert.AreEqual(45.0, imp.Angle.ToDegree(), 0.01,
                    "with the flag the angle is untouched");
            }
        }

        [TestMethod]
        public void NoPostAttackTurnDoesNotPickANewDirection()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                Mobj player;
                var imp = ChasingImp(world, out player);

                Assert.IsNotNull(imp);

                imp.Flags |= MobjFlags.JustAttacked;
                imp.MoveDir = Direction.East;
                imp.MoveCount = 7;

                world.MonsterBehavior.Chase(imp, ChaseFlags.NoPostAttackTurn, -1, -1);

                Assert.AreEqual(Direction.East, imp.MoveDir,
                    "the direction is what it was");
                Assert.AreEqual(7, imp.MoveCount,
                    "and the move count was not reset by a new direction");

                // The control: without the flag the same call picks a new direction, which resets
                // the move count. The direction itself may land on East again by chance, so the
                // move count is what is asserted.
                imp.Flags |= MobjFlags.JustAttacked;
                imp.MoveDir = Direction.East;
                imp.MoveCount = 7;
                imp.ThinkerState = ThinkerState.Active;

                world.MonsterBehavior.Chase(imp, 0, -1, -1);

                Assert.AreNotEqual(7, imp.MoveCount,
                    "control: without the flag a new direction is chosen, and that resets the count");
            }
        }

        [TestMethod]
        public void NightmareFastShortensTheFrame()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content, GameSkill.Nightmare, true).World;

                Mobj player;
                var imp = ChasingImp(world, out player);

                Assert.IsNotNull(imp);

                imp.Tics = 8;

                world.MonsterBehavior.Chase(imp, 0, -1, -1);

                Assert.AreEqual(8, imp.Tics, "control: the flag is what does this");

                imp.Tics = 8;
                imp.ThinkerState = ThinkerState.Active;

                world.MonsterBehavior.Chase(imp, ChaseFlags.NightmareFast, -1, -1);

                Assert.AreEqual(4, imp.Tics, "half the frame, as ZDoom does");
            }
        }

        [TestMethod]
        public void NightmareFastDoesNothingOutsideNightmare()
        {
            // The flag is conditional on the skill, not a general speed-up: a mod that sets it must
            // not make its monster fast on ordinary difficulty.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                Mobj player;
                var imp = ChasingImp(world, out player);

                Assert.IsNotNull(imp);

                imp.Tics = 8;

                world.MonsterBehavior.Chase(imp, ChaseFlags.NightmareFast, -1, -1);

                Assert.AreEqual(8, imp.Tics, "still eight tics on medium skill");
            }
        }

        [TestMethod]
        public void TheMeleeStateOverrideIsUsed()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                var trooper = ChasingImp(world, out var player);

                Assert.IsNotNull(trooper);

                // In melee range, so the melee branch is the one that runs.
                PlaceAt(world, trooper, player.X + Fixed.FromInt(32), player.Y);

                world.MonsterBehavior.Chase(trooper, 0, (int)MobjState.TrooPain, -1);

                Assert.AreEqual((int)MobjState.TrooPain, trooper.State.Number,
                    "the state the caller named is the one entered");
            }
        }

        [TestMethod]
        public void NoOverrideUsesTheActorsOwnMeleeState()
        {
            // The control for the one above: with -1 the actor's own melee state runs, which is
            // what vanilla does.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                var info = DoomInfo.MobjInfos[(int)MobjType.Troop];
                info.MissileState = MobjState.Null;

                var trooper = ChasingImp(world, out var player);

                Assert.IsNotNull(trooper);

                // ChasingImp cleared the melee state, so give this imp one back.
                info.MeleeState = MobjState.TrooAtk1;

                PlaceAt(world, trooper, player.X + Fixed.FromInt(32), player.Y);

                world.MonsterBehavior.Chase(trooper, 0, -1, -1);

                Assert.AreEqual((int)MobjState.TrooAtk1, trooper.State.Number,
                    "the actor's own melee state");
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
                "  Missile:\n" +
                "    TROO B 4 A_TroopAttack\n" +
                "  Melee:\n" +
                "    TROO C 4 A_TroopAttack\n" +
                "  }\n" +
                "}", DoomInfo.MobjInfos, problems);
        }

        [TestMethod]
        public void TheConverterWritesFlagsInTheThirdPosition()
        {
            var problems = new List<string>();

            var script = Convert("A_Chase(0, 0, CHF_DONTMOVE|CHF_NOPLAYACTIVE)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            // 16 | 2 = 18, and the two empty state names mean "the actor's own".
            StringAssert.Contains(script, "Chase(\"See#1\", \"\", \"\", 18);");
        }

        [TestMethod]
        public void TheConverterWritesNamedStates()
        {
            var problems = new List<string>();

            var script = Convert("A_Chase(\"Melee\", \"Missile\", 0)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "Chase(\"See#1\", \"Melee#1\", \"Missile#1\", 0);");
        }

        [TestMethod]
        public void AFlagInTheFirstPositionIsReportedAsAMistake()
        {
            // A_Chase(CHF_DONTMOVE) is the natural mistake. It must not be silently treated as
            // flags: in ZDoom it is a state label, and saying so is the whole point of the report.
            var problems = new List<string>();

            Convert("A_Chase(CHF_DONTMOVE)", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "the flags go third");
            StringAssert.Contains(text, "melee");
        }

        [TestMethod]
        public void AChaseWithNoArgumentsKeepsThePlainAction()
        {
            // A_Chase() must not become a closure: the engine's own action is already exactly what
            // was asked for, and every vanilla monster runs it.
            var problems = new List<string>();

            var script = Convert("A_Chase()", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            Assert.IsFalse(script.Contains("Chase(\"See#1\""), "no closure was written");
            StringAssert.Contains(script, "A_Chase");
        }

        [TestMethod]
        public void TheFlagsThisEngineCannotHonourAreNamed()
        {
            var problems = new List<string>();

            Convert("A_Chase(0, 0, CHF_FASTCHASE|CHF_RESURRECT|CHF_DONTIDLE)", problems);

            var text = string.Join("; ", problems.ToArray());

            StringAssert.Contains(text, "CHF_FASTCHASE");
            StringAssert.Contains(text, "CHF_RESURRECT");
            StringAssert.Contains(text, "CHF_DONTIDLE");
        }

        [TestMethod]
        public void AnUnknownChaseFlagIsReported()
        {
            var problems = new List<string>();

            Convert("A_Chase(0, 0, CHF_NOSUCHTHING)", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "CHF_NOSUCHTHING");
        }
    }
}
