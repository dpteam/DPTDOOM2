using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;
using DPTDOOMTest.CompatibilityTests;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_GiveInventory, A_TakeInventory and A_JumpIfInventory as named counters on one actor.
    ///
    /// The mods that use these actions use them as variables - "how many times has this been hit",
    /// "which fire mode is selected" - and that is the only sense this engine can honour without a
    /// full inventory of typed item classes. The tests therefore check counting and the three
    /// argument rules of the originals: give with no amount gives one, take with no amount takes all,
    /// and asking whether an item is "full" is always false because nothing here has a maximum.
    /// </summary>
    [TestClass]
    public class InventoryTest
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
                "ACTOR ItemImp 3001\n" +
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

        /// <summary>An actor in a real world, because a mobj needs one to exist.</summary>
        private static Mobj NewActor(out GameContent content)
        {
            content = GameContent.CreateDummy(WadPath.Doom2);

            var game = NewGame(content, out _);

            return FirstImp(game.World);
        }

        [TestMethod]
        public void GiveWithoutAnAmountGivesOne()
        {
            GameContent content;
            var mobj = NewActor(out content);

            using (content)
            {
                Assert.IsNotNull(mobj);

                mobj.GiveItem("kgNotify", 0);

                Assert.AreEqual(1, mobj.GetItem("kgNotify"),
                    "ZDoom's default amount is one, not zero");
            }
        }

        [TestMethod]
        public void GiveAccumulatesAndClampsInsteadOfWrapping()
        {
            GameContent content;
            var mobj = NewActor(out content);

            using (content)
            {
                mobj.GiveItem("kgEssence", 3);
                mobj.GiveItem("kgEssence", 4);

                Assert.AreEqual(7, mobj.GetItem("kgEssence"));

                mobj.GiveItem("kgEssence", int.MaxValue);

                Assert.AreEqual(int.MaxValue, mobj.GetItem("kgEssence"),
                    "a counter must not overflow into a negative number that satisfies 'at least one'");
            }
        }

        [TestMethod]
        public void TakeWithoutAnAmountTakesEverything()
        {
            GameContent content;
            var mobj = NewActor(out content);

            using (content)
            {
                mobj.GiveItem("kgMode", 2);
                mobj.TakeItem("kgMode", 0);

                Assert.AreEqual(0, mobj.GetItem("kgMode"));

                mobj.GiveItem("kgMode", 5);
                mobj.TakeItem("kgMode", 3);

                Assert.AreEqual(2, mobj.GetItem("kgMode"), "a positive amount takes that many");

                mobj.TakeItem("kgMode", 99);

                Assert.AreEqual(0, mobj.GetItem("kgMode"), "taking more than there is takes all");
            }
        }

        [TestMethod]
        public void ItemNamesAreCaseInsensitive()
        {
            GameContent content;
            var mobj = NewActor(out content);

            using (content)
            {
                mobj.GiveItem("kgBeak", 1);

                Assert.AreEqual(1, mobj.GetItem("KGBEAK"));
            }
        }

        [TestMethod]
        public void TheConverterReadsTheThreeArgumentForms()
        {
            var problems = new List<string>();

            var give = Convert("A_GiveInventory(\"kgDemonEssence\", random(1,3))", problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(give, "GiveItem(\"See#1\", \"kgDemonEssence\"");
            StringAssert.Contains(give, "Random(1, 3)");

            var take = Convert("A_TakeInventory(\"kgFistHitCount\")", problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(take, "TakeItem(\"See#1\", \"kgFistHitCount\"");

            var jump = Convert("A_JumpIfInventory(\"kgBeak\", 1, \"Hit\", AAPTR_TARGET)", problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(jump,
                "return HasItem(\"kgBeak\", 1, " + ActorPointer.Target + ");");
        }

        [TestMethod]
        public void AskingWhetherAnItemIsFullIsReportedRatherThanSilent()
        {
            var problems = new List<string>();

            Convert("A_JumpIfInventory(\"kgDemonEssence\", 666, \"Hit\")", problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));

            // amount <= 0 means "is it at maximum", and nothing here has a maximum.
            Convert("A_JumpIfInventory(\"kgDemonEssence\", 0, \"Hit\")", problems);

            StringAssert.Contains(string.Join("; ", problems.ToArray()), "no maximum");
        }

        [TestMethod]
        public void TheScriptCountsInTheRealWorld()
        {
            var problems = new List<string>();
            var script = Convert("A_GiveInventory(\"kgHits\", 2, AAPTR_TARGET)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);

                imp.Target = player;
                imp.ThinkerState = ThinkerState.Active;
                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].SeeState);

                Assert.AreEqual(2, player.GetItem("kgHits"),
                    "the target, not the imp, received the items");
                Assert.AreEqual(0, imp.GetItem("kgHits"));
            }
        }

        [TestMethod]
        public void CountersSurviveASaveAndLoad()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out var cmds);
                var imp = FirstImp(game.World);

                Assert.IsNotNull(imp);

                imp.GiveItem("kgDemonEssence", 42);
                imp.GiveItem("kgMode", 1);

                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "dptdoom-items-" + Guid.NewGuid().ToString("N") + ".sav");

                try
                {
                    SaveAndLoad.Save(game, "items", path);
                    SaveAndLoad.Load(game, path);

                    var restored = FirstImp(game.World);

                    Assert.IsNotNull(restored, "the imp is back");
                    Assert.AreEqual(42, restored.GetItem("kgDemonEssence"));
                    Assert.AreEqual(1, restored.GetItem("kgMode"));
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

        [TestMethod]
        public void ASaveWithoutItemsKeepsTheVanillaLayout()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "dptdoom-noitems-" + Guid.NewGuid().ToString("N") + ".sav");

                try
                {
                    SaveAndLoad.Save(game, "plain", path);

                    var bytes = System.IO.File.ReadAllBytes(path);

                    // The file ends at the vanilla end marker: no section is written when there is
                    // nothing to write, so an untouched game produces the same layout as before.
                    Assert.AreEqual(0x1d, bytes[bytes.Length - 1],
                        "the last byte is still the end marker");
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
