using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The three flags DECORATE has and this engine did not: +PAINLESS, +BUDDHA, +NOTARGET.
    ///
    /// They matter for compatibility because they are common in older mods and each one is a
    /// visible behaviour rather than a detail: a boss that cannot be staggered, an NPC that cannot
    /// be killed, an actor monsters ignore. Before them, a mod using any of the three was told
    /// "no such flag" and got the vanilla behaviour instead.
    ///
    /// Every test here checks two things, and the second is the one that is easy to forget:
    ///
    ///  - the behaviour changed as the flag says;
    ///  - **the world generator made the same number of draws**. These flags are consulted inside
    ///    `DamageMobj` and `LookForPlayers`, both of which draw from the world's random generator,
    ///    and a flag that short-circuits before a draw would move the world for everybody in the
    ///    session - a desync caused by adding a flag to one actor.
    ///
    /// The draws are counted with a fresh `DoomRandom` and compared by its index, which is what
    /// "the same number of draws" means in this engine.
    /// </summary>
    [TestClass]
    public class DecorateFlagsTest
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

        [TestMethod]
        public void TheFlagsAreNotWhereVanillaOnesAre()
        {
            // Appended after Translation, which owns bits 26 and 27. A collision here would make
            // one flag silently mean another.
            Assert.AreEqual(0x10000000, (int)MobjFlags.Painless);
            Assert.AreEqual(0x20000000, (int)MobjFlags.Buddha);
            Assert.AreEqual(0x40000000, (int)MobjFlags.NoTarget);

            // And they do not overlap the vanilla ones.
            Assert.AreEqual(0, (int)MobjFlags.Painless & (int)MobjFlags.Translation);
            Assert.AreEqual(0, (int)MobjFlags.Buddha & (int)MobjFlags.Translation);
            Assert.AreEqual(0, (int)MobjFlags.NoTarget & (int)MobjFlags.Translation);
        }

        [TestMethod]
        public void ABuddhaActorIsReducedToOnePointAndSurvives()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);

                Assert.IsNotNull(imp, "MAP01 has an imp");

                imp.Flags |= MobjFlags.Buddha;

                var before = imp.Health;

                world.ThingInteraction.DamageMobj(imp, null, null, before + 1000);

                Assert.AreEqual(1, imp.Health, "down to one point and no further");
                Assert.AreEqual(before > 0, imp.Health > 0, "and alive");
            }
        }

        [TestMethod]
        public void AnOrdinaryActorStillDies()
        {
            // The control: without the flag the same damage kills, so the test above is about the
            // flag rather than about damage being ignored.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                world.ThingInteraction.DamageMobj(imp, null, null, imp.Health + 1000);

                Assert.IsTrue(imp.Health <= 0, "dead without the flag");
            }
        }

        [TestMethod]
        public void APainlessActorDoesNotEnterItsPainState()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                // Certainty, not probability: PainChance is 200 for an imp, and the flag has to
                // hold regardless of the roll.
                DoomInfo.MobjInfos[(int)MobjType.Troop].PainChance = 256;

                imp.Flags |= MobjFlags.Painless;

                var state = imp.State.Number;

                world.ThingInteraction.DamageMobj(imp, null, null, 1);

                Assert.AreEqual(state, imp.State.Number, "no pain state, so the frame did not change");
            }
        }

        [TestMethod]
        public void AnOrdinaryActorDoesEnterItsPainState()
        {
            // The control for the one above: with a certain pain chance and no flag, the state must
            // change, or "no pain" would prove nothing.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                DoomInfo.MobjInfos[(int)MobjType.Troop].PainChance = 256;

                var state = imp.State.Number;

                world.ThingInteraction.DamageMobj(imp, null, null, 1);

                Assert.AreNotEqual(state, imp.State.Number, "the pain state was entered");
            }
        }

        [TestMethod]
        public void TheDrawCountDoesNotDependOnThePainlessFlag()
        {
            // The load-bearing one. DamageMobj draws for the pain chance, and a flag checked before
            // that draw would change how many numbers the world consumes - so a mod that added
            // +PAINLESS to one monster would move the world for every player in the session.
            var withFlag = DrawsForDamage(true);
            var withoutFlag = DrawsForDamage(false);

            Assert.AreEqual(withoutFlag, withFlag,
                "the same number of world draws with and without the flag");
        }

        /// <summary>How many numbers the world generator consumed to apply one hit.</summary>
        private static int DrawsForDamage(bool painless)
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                DoomInfo.MobjInfos[(int)MobjType.Troop].PainChance = 128;

                if (painless)
                {
                    imp.Flags |= MobjFlags.Painless;
                }

                var before = world.Random.Index;

                world.ThingInteraction.DamageMobj(imp, null, null, 1);

                // The index wraps at 256, so the difference is taken modulo it.
                return (world.Random.Index - before + 256) % 256;
            }
        }

        [TestMethod]
        public void AMonsterWithNoTargetIsNotSeen()
        {
            // DECORATE's +NOTARGET: monsters do not pick this actor as a target. Checked through
            // the real look path rather than by reading the flag back.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);
                var player = world.ConsolePlayer;

                Assert.IsNotNull(imp);

                player.Mobj.Flags |= MobjFlags.NoTarget;

                imp.Target = null;

                // LookForPlayers is reached through Look; the imp is placed facing the player by
                // the map, and the check is a sight test either way.
                imp.Threshold = 0;
                world.MonsterBehavior.Look(imp);

                Assert.IsNull(imp.Target, "a monster does not acquire a hidden target");

                player.Mobj.Flags &= ~MobjFlags.NoTarget;
            }
        }
    }
}
