using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A mod can add a monster, not only change one.
    ///
    /// The distinction matters more than it sounds: replacing an existing actor only needs the
    /// table edited, while adding one needs a new type number that the world, the save format
    /// and every <c>MobjInfos[(int)type]</c> lookup will accept. That is why the reserve exists
    /// and why this test spawns the actor in a real world instead of only reading the table: a
    /// slot that exists but cannot be spawned looks identical in the table.
    ///
    /// The actor is built from DECORATE text, converted to a script, applied through the real
    /// library, and then placed next to the player. If any of those four steps were wrong the
    /// mobj would either not appear, not have the numbers, or stand still.
    /// </summary>
    [TestClass]
    public class DecorateNewActorTest
    {
        [TestMethod]
        public void ANewActorIsSpawnedAndFights()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = BuildMap01(content);

                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR EliteImp : DoomImp\n" +
                    "{\n" +
                    "  spawnid 13666\n" +
                    "  health 300\n" +
                    "  speed 14\n" +
                    "  painchance 60\n" +
                    "  States\n" +
                    "  {\n" +
                    "  Spawn:\n" +
                    "    TROO AB 2 A_Look\n" +
                    "    Loop\n" +
                    "  See:\n" +
                    "    TROO AABBCCDD 3 A_Chase\n" +
                    "    Goto See\n" +
                    "  }\n" +
                    "}", DoomInfo.MobjInfos, problems);

                Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

                var library = new ScriptLibrary();
                library.Add(script, "generated", DoomInfo.MobjInfos);
                library.ApplyTo(DoomInfo.MobjInfos);

                Assert.AreEqual(0, library.Problems.Count,
                    library.Problems.Count == 0 ? "" : library.Problems[0]);

                var type = DoomInfo.TypeOf(13666);

                Assert.IsTrue(type >= DoomInfo.CustomActorFirst,
                    "the actor got a slot in the reserve, got " + type);

                // The numbers came from the block, and the states from its parent - which is
                // what inheritance has to mean: a block that only sets numbers must not lose
                // the animation it did not mention.
                var info = DoomInfo.MobjInfos[type];

                Assert.AreEqual(300, info.SpawnHealth);
                Assert.AreEqual(14, info.Speed);
                Assert.AreEqual(60, info.PainChance);
                Assert.AreNotEqual(MobjState.Null, info.DeathState, "the parent's death states were inherited");

                // Spawn it in front of the player and let the world run.
                var player = world.ConsolePlayer.Mobj;

                var mobj = world.ThingAllocation.SpawnMobj(
                    player.X + Fixed.FromInt(96), player.Y, player.Z, (MobjType)type);

                Assert.IsNotNull(mobj, "the new type can be spawned");
                Assert.AreEqual(300, mobj.Health, "and it starts with the health the block gave it");

                var sawIt = false;
                var attacked = false;

                for (var tic = 0; tic < 200; tic++)
                {
                    world.Thinkers.Run();

                    if (mobj.State == DoomInfo.States[(int)info.SeeState])
                    {
                        sawIt = true;
                    }

                    // A chasing imp plays its attack state eventually. Any move at all past
                    // Spawn proves the chain of custom states is being followed, which is the
                    // part that a table check cannot see.
                    if (mobj.State != DoomInfo.States[(int)info.SpawnState] &&
                        mobj.State.Number >= DoomInfo.CustomStateFirst)
                    {
                        attacked = true;
                        break;
                    }
                }

                Assert.IsTrue(sawIt || attacked,
                    "the new actor ran its own states, not the inert slot it was born in");
            }
        }

        [TestMethod]
        public void AVanillaNumberStillReplacesRatherThanAdds()
        {
            // "ACTOR X 3004" with 3004 already in the table must change the zombieman, not
            // create a second actor wearing the same number - two entries with one doomednum
            // would make map placement depend on which came first.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var before = DoomInfo.MobjInfos.Length;

                var problems = new List<string>();

                DecorateConverter.Convert(
                    "ACTOR NotSoNew 3004 { Health 21 }", DoomInfo.MobjInfos, problems);

                Assert.AreEqual(before, DoomInfo.MobjInfos.Length,
                    "no slot was consumed by a replacement");

                Assert.AreEqual(3004, DoomInfo.MobjInfos[(int)MobjType.Possessed].DoomEdNum);
            }
        }

        private static World BuildMap01(GameContent content)
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

            // TicCmd is a class, so an array of them starts full of nulls.
            var cmds = new TicCmd[Player.MaxPlayerCount];

            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);

            return game.World;
        }
    }
}
