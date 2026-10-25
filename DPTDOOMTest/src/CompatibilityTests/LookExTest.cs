using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_LookEx: the wake-up test with a window, a cone and a choice of state.
    ///
    /// The engine has one look routine, and A_LookEx configures it rather than adding a second.
    /// That makes the vanilla path the one under test as much as the new one, which is why the
    /// first test here is that plain A_Look still behaves exactly as before.
    ///
    /// The numbers and flag values come from ZDoom's p_enemy.cpp and p_enemy.h. They are not
    /// guesses: a wrong flag value would silently mean a different flag, and these flags are about
    /// what a monster can see.
    /// </summary>
    [TestClass]
    public class LookExTest
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

        private static int ImpSpawnState()
        {
            return (int)DoomInfo.MobjInfos[(int)MobjType.Troop].SpawnState;
        }

        private static int ImpSeeState()
        {
            return (int)DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState;
        }

        /// <summary>
        /// An imp in plain view of the player, the given number of map units away.
        ///
        /// Placed along the player's own facing first, then the other three quarter turns: the
        /// player starts looking into an open room, so this lands in the room rather than in a
        /// wall. The map's own monsters are behind walls from the start point, so a test that used
        /// one would fail over geometry rather than over the parameter it is checking.
        /// </summary>
        private static Mobj ImpInSight(World world, int distance, out Mobj player)
        {
            player = world.ConsolePlayer.Mobj;

            for (var i = 0; i < 4; i++)
            {
                var angle = player.Angle + Angle.FromDegree(90 * i);

                var x = player.X + Fixed.FromInt(distance) * Trig.Cos(angle);
                var y = player.Y + Fixed.FromInt(distance) * Trig.Sin(angle);

                var imp = world.ThingAllocation.SpawnMobj(x, y, player.Z, MobjType.Troop);

                if (world.VisibilityCheck.CheckSight(imp, player))
                {
                    imp.Angle = Geometry.PointToAngle(imp.X, imp.Y, player.X, player.Y);
                    imp.Target = null;
                    imp.Threshold = 0;

                    return imp;
                }
            }

            return null;
        }

        /// <summary>Runs one look and says whether it found a target.</summary>
        private static bool LookWith(LookExParams p, out int stateAfter)
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                Mobj player;

                // 96 units: outside the imp's melee reach (64 plus its radius), so entering the see state
                // does not immediately run a melee attack and change the state out from under the test.
                var imp = ImpInSight(world, 96, out player);

                Assert.IsNotNull(imp, "the test needs a monster that can see the player");

                world.MonsterBehavior.LookEx(imp, p);

                stateAfter = imp.State.Number;

                return imp.Target != null;
            }
        }

        /// <summary>
        /// Does a monster turned the given number of degrees off the player see it, with a cone of
        /// the given width? A fresh world each time, so a previous call's chase cannot move the
        /// monster between the setup and the answer.
        /// </summary>
        private static bool ConeSees(int degreesOff, int fovDegrees)
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                Mobj player;
                var imp = ImpInSight(world, 96, out player);

                Assert.IsNotNull(imp, "the test needs a monster 192 units from the player in view");

                imp.Angle = Geometry.PointToAngle(imp.X, imp.Y, player.X, player.Y) +
                            Angle.FromDegree(degreesOff);

                imp.Target = null;
                imp.Threshold = 0;

                var p = new LookExParams();
                p.Fov = Angle.FromDegree(fovDegrees);

                world.MonsterBehavior.LookEx(imp, p);

                return imp.Target != null;
            }
        }

        [TestMethod]
        public void PlainLookStillFindsThePlayer()
        {
            // The control for everything below, and the guard on the vanilla path: null parameters
            // are A_Look, and A_Look has to keep working.
            int state;

            Assert.IsTrue(LookWith(null, out state), "plain A_Look sees a player in plain view");
            Assert.AreEqual(ImpSeeState(), state, "and switches to the see state");
        }

        [TestMethod]
        public void MaxSeeDistanceHidesATarget()
        {
            // The imp is 64 units away, so a window of 32 excludes it and a wide window does not.
            var near = new LookExParams();
            near.MaxSeeDist = Fixed.FromInt(32);

            int state;

            Assert.IsFalse(LookWith(near, out state), "maxseedist 32 excludes a target at 64");

            var wide = new LookExParams();
            wide.MaxSeeDist = Fixed.FromInt(1024);

            Assert.IsTrue(LookWith(wide, out state), "maxseedist 1024 includes it");
        }

        [TestMethod]
        public void MinSeeDistanceHidesACloseTarget()
        {
            var far = new LookExParams();
            far.MinSeeDist = Fixed.FromInt(512);

            int state;

            Assert.IsFalse(LookWith(far, out state), "minseedist 512 excludes a target at 64");

            var close = new LookExParams();
            close.MinSeeDist = Fixed.FromInt(32);

            Assert.IsTrue(LookWith(close, out state), "minseedist 32 includes it");
        }

        [TestMethod]
        public void TheConeIsAHalfCircleWideAndNarrowerWhenAsked()
        {
            // Far enough that the "right in its face" exception does not apply.
            Assert.IsTrue(ConeSees(0, 45), "control: a narrow cone facing the player sees it");

            Assert.IsFalse(ConeSees(60, 45), "45 degrees of cone does not reach 60 degrees off centre");

            Assert.IsTrue(ConeSees(60, 180),
                "half a circle does, and that is what DECORATE's zero means");
        }

        [TestMethod]
        public void NoSightCheckLooksOnlyAtNoise()
        {
            int state;

            var eyes = new LookExParams();
            eyes.Flags = LookExFlags.NoSightCheck;

            Assert.IsFalse(LookWith(eyes, out state),
                "a monster told not to open its eyes does not see the player in front of it");
        }

        [TestMethod]
        public void NoSoundCheckIgnoresTheNoise()
        {
            // The decoy is a monster, not the player, so the two branches give different answers
            // and the test can tell which one ran.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                Mobj player;
                var imp = ImpInSight(world, 96, out player);

                Assert.IsNotNull(imp);

                var decoy = world.ThingAllocation.SpawnMobj(
                    imp.X, imp.Y + Fixed.FromInt(200), imp.Z, MobjType.Troop);

                imp.Subsector.Sector.SoundTarget = decoy;

                world.MonsterBehavior.LookEx(imp, null);

                Assert.AreEqual(decoy, imp.Target, "control: the noise is answered");

                imp.Target = null;
                imp.Threshold = 0;
                imp.Subsector.Sector.SoundTarget = decoy;

                var deaf = new LookExParams();
                deaf.Flags = LookExFlags.NoSoundCheck;

                world.MonsterBehavior.LookEx(imp, deaf);

                Assert.AreEqual(player, imp.Target,
                    "with the noise ignored the monster falls through to looking, and sees the " +
                    "player instead");
            }
        }

        [TestMethod]
        public void MaxHearDistanceIgnoresADistantNoise()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;

                Mobj player;
                var imp = ImpInSight(world, 96, out player);

                Assert.IsNotNull(imp);

                // Sight is switched off so the only way to find a target is the noise, and the
                // answer then depends on the hearing distance alone.
                var deafAndBlind = new LookExParams();
                deafAndBlind.Flags = LookExFlags.NoSightCheck;

                imp.Subsector.Sector.SoundTarget = player;
                world.MonsterBehavior.LookEx(imp, deafAndBlind);

                Assert.AreEqual(player, imp.Target, "control: the noise is answered");

                imp.Target = null;
                imp.Threshold = 0;
                imp.Subsector.Sector.SoundTarget = player;

                deafAndBlind.MaxHearDist = Fixed.FromInt(16);

                world.MonsterBehavior.LookEx(imp, deafAndBlind);

                Assert.IsNull(imp.Target, "a noise 64 units away is not heard from 16");
            }
        }

        [TestMethod]
        public void NoJumpTakesTheTargetWithoutChangingState()
        {
            int state;

            var noJump = new LookExParams();
            noJump.Flags = LookExFlags.NoJump;

            Assert.IsTrue(LookWith(noJump, out state), "the target is still taken");
            Assert.AreEqual(ImpSpawnState(), state,
                "but the frame does not change, so the mod's own frames decide what happens next");
        }

        [TestMethod]
        public void TheNamedSeeStateIsUsed()
        {
            int state;

            var other = new LookExParams();
            other.SeeState = MobjState.TrooPain;

            Assert.IsTrue(LookWith(other, out state), "the target is taken");
            Assert.AreEqual((int)MobjState.TrooPain, state,
                "and the state the caller named is the one entered");
        }

        [TestMethod]
        public void TheConverterWritesTheCallWithZDoomFlagNumbers()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR WatchImp 3001\n" +
                    "{\n" +
                    "  States\n" +
                    "  {\n" +
                    "  Spawn:\n" +
                    "    TROO A 3 A_LookEx(LOF_NOSIGHTCHECK|LOF_NOSEESOUND, 0, 1024.0, 512, 90, \"See\")\n" +
                    "  See:\n" +
                    "    TROO A 3 A_Chase\n" +
                    "    Loop\n" +
                    "  }\n" +
                    "}", DoomInfo.MobjInfos, problems);

                Assert.AreEqual(0, problems.Count,
                    "a supported call reports nothing: " + string.Join("; ", problems.ToArray()));

                // 1 | 8 = 9, and the distances are written as fixed because the script call
                // expects fixed values.
                StringAssert.Contains(script,
                    "LookEx(\"Spawn#1\", 9, 0.0, 1024.0, 512.0, 90, \"See#1\");");
            }
        }

        [TestMethod]
        public void TheConverterDefaultsAnEmptyCallToPlainLook()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR PlainImp 3001\n" +
                    "{\n" +
                    "  States\n" +
                    "  {\n" +
                    "  Spawn:\n" +
                    "    TROO A 3 A_LookEx()\n" +
                    "  See:\n" +
                    "    TROO A 3 A_Chase\n" +
                    "    Loop\n" +
                    "  }\n" +
                    "}", DoomInfo.MobjInfos, problems);

                Assert.AreEqual(0, problems.Count,
                    string.Join("; ", problems.ToArray()));

                // Zero everywhere, and an empty see state, which the script reads as "the actor's
                // own" - exactly what a bare A_LookEx asks for.
                StringAssert.Contains(script, "LookEx(\"Spawn#1\", 0, 0.0, 0.0, 0.0, 0, \"\");");
            }
        }

        [TestMethod]
        public void AnUnknownLookFlagIsReportedAndTheRestRuns()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR LoudImp 3001\n" +
                    "{\n" +
                    "  States\n" +
                    "  {\n" +
                    "  Spawn:\n" +
                    "    TROO A 3 A_LookEx(LOF_NOSUCHTHING, 0, 512, 0, 0, \"See\")\n" +
                    "  See:\n" +
                    "    TROO A 3 A_Chase\n" +
                    "    Loop\n" +
                    "  }\n" +
                    "}", DoomInfo.MobjInfos, problems);

                var text = string.Join("; ", problems.ToArray());

                StringAssert.Contains(text, "LOF_NOSUCHTHING");

                // The look still runs, with the flags that were understood - which here is none.
                StringAssert.Contains(script, "LookEx(\"Spawn#1\", 0, 0.0, 512.0, 0.0, 0, \"See#1\");");
            }
        }

        [TestMethod]
        public void TheFlagValuesAreZDooms()
        {
            // Copied from p_enemy.h. A wrong value would silently mean a different flag.
            Assert.AreEqual(1, LookExFlags.NoSightCheck);
            Assert.AreEqual(2, LookExFlags.NoSoundCheck);
            Assert.AreEqual(4, LookExFlags.DontChaseGoal);
            Assert.AreEqual(8, LookExFlags.NoSeeSound);
            Assert.AreEqual(16, LookExFlags.FullVolSeeSound);
            Assert.AreEqual(32, LookExFlags.NoJump);
        }
    }
}
