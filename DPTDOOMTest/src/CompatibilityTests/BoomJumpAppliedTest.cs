using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_Jump actually moves the actor, in a real world.
    ///
    /// The unit tests check that the chance and the label reach the state table. This checks the
    /// part only a world can: that a frame with a jump leaves for its target instead of continuing,
    /// that a chance of 256 always jumps, and that a chance of 0 never does. Without the last two
    /// the test would pass just as well if the chance were ignored entirely.
    ///
    /// The state table is static and these tests write into it, so the actor table is snapshotted
    /// and restored - the mod's states stay in the table, which is a known and documented property
    /// of the reserve, but the engine's own actors must come back unchanged.
    /// </summary>
    [TestClass]
    public class BoomJumpAppliedTest
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

        /// <summary>
        /// Builds a two-frame chain for the imp where the jump target is **not** the successor.
        ///
        /// That is the point: frame one loops to itself, and its jump leaves for frame two. If the
        /// target were also the successor, a chance of zero would reach it anyway and the test
        /// would prove nothing about the chance.
        /// </summary>
        private static void DefineJumper(int chance)
        {
            var library = new ScriptLibrary();

            library.Add(
                "fn Thing_3001() {\n" +
                "    State(\"See#1\", \"TROO\", \"A\", 4, \"\", \"See#1\", \"See#2\", " +
                chance.ToString(System.Globalization.CultureInfo.InvariantCulture) + ");\n" +
                "    State(\"See#2\", \"TROO\", \"B\", 4, \"\", \"See#1\");\n" +
                "    SeeState(\"See#1\");\n" +
                "}\n", "test", DoomInfo.MobjInfos);

            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));
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

        /// <summary>
        /// Puts every imp of the map straight into the mod's See chain and returns the frames they
        /// pass through over the following tics.
        ///
        /// Starting from the first frame of the chain, and reading the state number each tic, is what
        /// shows the jump: without it the actor walks 1, 2, 1, 2; with a chance of 256 it goes
        /// 1, 2, 1, 2 as well, but with a chance of 0 it never reaches 2.
        /// </summary>
        private static List<int> WatchImps(int chance, out int first, out int second)
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                DefineJumper(chance);

                var game = NewGame(content, out var cmds);
                var world = game.World;

                first = -1;
                second = -1;

                var seen = new List<int>();
                var imps = new List<Mobj>();

                foreach (var thinker in world.Thinkers)
                {
                    var mobj = thinker as Mobj;

                    if (mobj != null && mobj.Type == MobjType.Troop)
                    {
                        imps.Add(mobj);
                    }
                }

                Assert.IsTrue(imps.Count > 0, "MAP01 has imps to watch");

                var start = DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState;

                first = (int)start;
                second = DoomInfo.States[first].JumpTarget;

                Assert.IsTrue(second >= DoomInfo.CustomStateFirst,
                    "the jump target is one of the mod's own states");

                foreach (var imp in imps)
                {
                    imp.SetState(start);
                }

                for (var tic = 0; tic < 12; tic++)
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

        [TestMethod]
        public void AChanceOfZeroNeverJumps()
        {
            int first, second;

            var seen = WatchImps(0, out first, out second);

            CollectionAssert.Contains(seen, first);
            CollectionAssert.DoesNotContain(seen, second,
                "chance 0 means the jump never happens, so the target frame is never reached");
        }

        [TestMethod]
        public void AChanceOf256AlwaysJumps()
        {
            int first, second;

            var seen = WatchImps(256, out first, out second);

            CollectionAssert.Contains(seen, second,
                "chance 256 means the jump always happens");
        }
    }
}
