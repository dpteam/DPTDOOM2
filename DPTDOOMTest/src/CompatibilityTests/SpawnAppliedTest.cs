using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A projectile a mod fires, and a thing a mod spawns - in a real world.
    ///
    /// The name table and the converter have their own tests. What only a world can show is that the
    /// frame's action really puts a new actor there, of the right type, and that it is the frame's
    /// action rather than the engine's own: the mod's frame has no vanilla action, so anything that
    /// appears came from the conversion.
    ///
    /// Both tests count actors of the spawned type before and after, which is the honest observable.
    /// Counting *all* actors would pass even if the wrong thing were created.
    /// </summary>
    [TestClass]
    public class SpawnAppliedTest
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

        /// <summary>
        /// A one-frame See chain that fires the given call and then keeps chasing.
        ///
        /// The call has to be in the **same** function as the frames: frame names are scoped to the
        /// actor definition that created them, and a second `fn Thing_...()` in another script is a
        /// different definition. The first version of this test did exactly that and was told,
        /// correctly, that there is no such frame.
        /// </summary>
        private static void DefineShooter(string spawn)
        {
            var library = new ScriptLibrary();

            library.Add(
                "fn Thing_3001() {\n" +
                "    State(\"See#1\", \"TROO\", \"A\", 2, \"\", \"See#1\");\n" +
                "    SeeState(\"See#1\");\n" +
                spawn.Replace("$FRAME", "See#1") + "\n" +
                "}\n", "test", DoomInfo.MobjInfos);

            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));
        }

        [TestMethod]
        public void AFiredProjectileAppearsInTheWorld()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                DefineShooter("    Missile(\"$FRAME\", \"DoomImpBall\", 32.0);");

                var game = NewGame(content, out var cmds);
                var world = game.World;

                Mobj imp = null;

                foreach (var thinker in world.Thinkers)
                {
                    var mobj = thinker as Mobj;

                    if (mobj != null && mobj.Type == MobjType.Troop)
                    {
                        imp = mobj;
                        break;
                    }
                }

                Assert.IsNotNull(imp, "MAP01 has an imp");

                imp.ThinkerState = ThinkerState.Active;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                var before = CountOf(world, MobjType.Troopshot);

                for (var tic = 0; tic < 10; tic++)
                {
                    game.Update(cmds);
                }

                var after = CountOf(world, MobjType.Troopshot);

                Assert.IsTrue(after > before,
                    "the frame fired: " + before + " imp balls before, " + after + " after");
            }
        }

        [TestMethod]
        public void ASpawnedItemAppearsInTheWorld()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                DefineShooter("    SpawnItem(\"$FRAME\", \"HealthBonus\", 0.0);");

                var game = NewGame(content, out var cmds);
                var world = game.World;

                Mobj imp = null;

                foreach (var thinker in world.Thinkers)
                {
                    var mobj = thinker as Mobj;

                    if (mobj != null && mobj.Type == MobjType.Troop)
                    {
                        imp = mobj;
                        break;
                    }
                }

                Assert.IsNotNull(imp);

                imp.ThinkerState = ThinkerState.Active;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                var before = CountOf(world, MobjType.Misc2);

                for (var tic = 0; tic < 10; tic++)
                {
                    game.Update(cmds);
                }

                Assert.IsTrue(CountOf(world, MobjType.Misc2) > before,
                    "the frame spawned a health bonus");
            }
        }

        [TestMethod]
        public void ANameThatIsNotAnActorIsRefusedWhenTheScriptLoads()
        {
            // The refusal has to happen at load, not on the first tic: a projectile that silently
            // never appears is the failure mode the name table exists to prevent.
            var library = new ScriptLibrary();

            library.Add(
                "fn Thing_3001() {\n" +
                "    State(\"See#1\", \"TROO\", \"A\", 2, \"\", \"See#1\");\n" +
                "    Missile(\"See#1\", \"NotAThing\", 32.0);\n" +
                "    SeeState(\"See#1\");\n" +
                "}\n", "test", DoomInfo.MobjInfos);

            library.ApplyTo(DoomInfo.MobjInfos);

            var problems = new List<string>(library.Problems);

            Assert.AreEqual(1, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(problems[0], "NotAThing");
        }
    }
}
