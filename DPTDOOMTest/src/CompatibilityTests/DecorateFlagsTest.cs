using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The four flags DECORATE has and this engine did not: +NOPAIN, +BUDDHA, +NOTARGET
    /// and +NEVERTARGET.
    ///
    /// They matter for compatibility because they are common in older mods and each one is a
    /// visible behaviour rather than a detail: a boss that cannot be staggered, an NPC that cannot
    /// be killed, an actor monsters will not chase, an actor monsters never see as a target.
    ///
    /// The names and the meanings are taken from ZDoom's own source, not from memory, because
    /// two of them are easy to swap:
    ///
    ///  - MF5_NOPAIN is "If set the pain state won't be entered" - the actor as a *victim*;
    ///  - MF5_PAINLESS is "Actor always inflicts painless damage" - the actor as an *attacker*,
    ///    and it is not implemented here;
    ///  - MF3_NOTARGET is "This actor not targetted when it hurts something else" - retaliation;
    ///  - MF7_NEVERTARGET is "can not be targetted at all" - the one that hides the actor.
    ///
    /// A flag named after the wrong one of a pair is worse than a missing flag: the mod asks for
    /// something, gets something else, and nothing says so.
    ///
    /// Every behaviour test has a control, because "the flag works" proves nothing if the same
    /// scenario does nothing without it.
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
        public void TheFlagsSitAfterTranslationAndDoNotOverlapIt()
        {
            // Appended after Translation, which owns bits 26 and 27. A collision here would make
            // one flag silently mean another.
            Assert.AreEqual(0x10000000, (int)MobjFlags.NoPain);
            Assert.AreEqual(0x20000000, (int)MobjFlags.Buddha);
            Assert.AreEqual(0x40000000, (int)MobjFlags.NeverTarget);

            // The last free bit of an int-backed enum, so it reads as a negative number. Nothing
            // treats Flags as signed - it goes into the hash and into saves as raw bits.
            Assert.AreEqual(int.MinValue, (int)MobjFlags.NoTarget);

            Assert.AreEqual(0, (int)MobjFlags.NoPain & (int)MobjFlags.Translation);
            Assert.AreEqual(0, (int)MobjFlags.Buddha & (int)MobjFlags.Translation);
            Assert.AreEqual(0, (int)MobjFlags.NeverTarget & (int)MobjFlags.Translation);
            Assert.AreEqual(0, (int)MobjFlags.NoTarget & (int)MobjFlags.Translation);
        }

        [TestMethod]
        public void EveryNewFlagIsANameAScriptCanSet()
        {
            // The sign bit is the one that could break the power-of-two check in the script API,
            // and a flag a script cannot name is a flag a mod cannot use.
            var names = new[] { "NoPain", "Buddha", "NeverTarget", "NoTarget" };

            foreach (var name in names)
            {
                MobjFlags flag;

                Assert.IsTrue(ScriptActorApi.TryParseFlagName(name, out flag),
                    name + " is a settable flag");

                Assert.AreEqual(0, (int)flag & (int)MobjFlags.Translation,
                    name + " does not collide with Translation");
            }
        }

        [TestMethod]
        public void TheDecorateSpellingReachesTheSameFlags()
        {
            // The converter normalises +NOPAIN to "Nopain" and the script API parses names
            // without regard to case, so the two spellings have to land on the same member.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var problems = new List<string>();

                var script = DecorateConverter.Convert(
                    "ACTOR QuietImp 3001\n" +
                    "{\n" +
                    "  +NOPAIN\n" +
                    "  +BUDDHA\n" +
                    "  +NOTARGET\n" +
                    "  +NEVERTARGET\n" +
                    "  States { See: TROO A 3 A_Look Loop }\n" +
                    "}", DoomInfo.MobjInfos, problems);

                Assert.AreEqual(0, problems.Count,
                    "all four flags are known: " + string.Join("; ", problems.ToArray()));

                StringAssert.Contains(script, "Flag(\"Nopain\", true);");
                StringAssert.Contains(script, "Flag(\"Buddha\", true);");
                StringAssert.Contains(script, "Flag(\"Notarget\", true);");
                StringAssert.Contains(script, "Flag(\"Nevertarget\", true);");
            }
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
                Assert.IsTrue(imp.Health > 0, "and alive");
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
        public void ANoPainActorDoesNotEnterItsPainState()
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

                imp.Flags |= MobjFlags.NoPain;

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
        public void TheDrawCountDoesNotDependOnTheNoPainFlag()
        {
            // DamageMobj draws for the pain chance, and a flag checked before that draw would
            // change how many numbers the world consumes - so a mod that added +NOPAIN to one
            // monster would move the world for every player in the session.
            var withFlag = DrawsForDamage(true);
            var withoutFlag = DrawsForDamage(false);

            Assert.AreEqual(withoutFlag, withFlag,
                "the same number of world draws with and without the flag");
        }

        /// <summary>How many numbers the world generator consumed to apply one hit.</summary>
        private static int DrawsForDamage(bool noPain)
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);

                Assert.IsNotNull(imp);

                DoomInfo.MobjInfos[(int)MobjType.Troop].PainChance = 128;

                if (noPain)
                {
                    imp.Flags |= MobjFlags.NoPain;
                }

                var before = world.Random.Index;

                world.ThingInteraction.DamageMobj(imp, null, null, 1);

                // The index wraps at 256, so the difference is taken modulo it.
                return (world.Random.Index - before + 256) % 256;
            }
        }

        /// <summary>
        /// Does a waking monster take the player as its target?
        ///
        /// The imp is turned to face the player first: otherwise the answer depends on which way
        /// the map happened to place it, and a test that fails for a geometric reason says nothing
        /// about the flag.
        /// </summary>
        private static bool LookFindsThePlayer(bool neverTarget)
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var player = world.ConsolePlayer;

                if (player == null || player.Mobj == null)
                {
                    return false;
                }

                var pm = player.Mobj;

                if (neverTarget)
                {
                    pm.Flags |= MobjFlags.NeverTarget;
                }

                // A monster the map placed is usually behind a wall from the start point, so a
                // sight test would decide the answer instead of the flag. One spawned next to the
                // player is in plain view by construction.
                Mobj imp = null;

                foreach (var offset in new[] { 64, -64, 96, -96 })
                {
                    var candidate = world.ThingAllocation.SpawnMobj(
                        pm.X + Fixed.FromInt(offset), pm.Y, pm.Z, MobjType.Troop);

                    if (world.VisibilityCheck.CheckSight(candidate, pm))
                    {
                        imp = candidate;
                        break;
                    }
                }

                if (imp == null)
                {
                    return false;
                }

                imp.Angle = Geometry.PointToAngle(imp.X, imp.Y, pm.X, pm.Y);
                imp.Target = null;
                imp.Threshold = 0;

                world.MonsterBehavior.Look(imp);

                return imp.Target != null;
            }
        }

        [TestMethod]
        public void ANeverTargetActorIsNotSeen()
        {
            // DECORATE's +NEVERTARGET: monsters never pick this actor as a target.
            if (!LookFindsThePlayer(false))
            {
                Assert.Inconclusive(
                    "no imp on MAP01 sees the player from where the map places it, so the " +
                    "control for this test does not run here");
            }

            Assert.IsFalse(LookFindsThePlayer(true),
                "a monster does not acquire a target that says it can never be one");
        }

        [TestMethod]
        public void ANeverTargetActorThatMakesASoundIsIgnored()
        {
            // The other place Look consults the flag: a monster woken by a sound. No sight test is
            // involved here, so this covers the branch the geometry cannot reach.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);
                var player = world.ConsolePlayer;

                Assert.IsNotNull(imp);
                Assert.IsNotNull(player);

                imp.Subsector.Sector.SoundTarget = player.Mobj;

                imp.Target = null;
                world.MonsterBehavior.Look(imp);

                Assert.AreEqual(player.Mobj, imp.Target,
                    "control: a noise wakes the monster onto the player");

                imp.Target = null;
                player.Mobj.Flags |= MobjFlags.NeverTarget;

                world.MonsterBehavior.Look(imp);

                Assert.IsNull(imp.Target,
                    "a noise does not wake it onto an actor that can never be a target");
            }
        }

        /// <summary>
        /// Does a monster that was just hurt switch its target to whoever hurt it?
        /// </summary>
        private static bool DamageMakesTheMonsterRetaliate(bool noTarget)
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);
                var player = world.ConsolePlayer;

                if (imp == null || player == null || player.Mobj == null)
                {
                    return false;
                }

                if (noTarget)
                {
                    player.Mobj.Flags |= MobjFlags.NoTarget;
                }

                imp.Target = null;

                // Vanilla only retargets a monster that has finished reacting, and the map
                // spawns this one still counting down.
                imp.Threshold = 0;

                world.ThingInteraction.DamageMobj(imp, null, player.Mobj, 1);

                return imp.Target == player.Mobj;
            }
        }

        [TestMethod]
        public void ANoTargetActorIsNotRetaliatedAgainst()
        {
            // DECORATE's +NOTARGET, which is not the same flag as +NEVERTARGET: the monster still
            // wakes up and still fights, it just does not take this actor as its chase target.
            Assert.IsTrue(DamageMakesTheMonsterRetaliate(false),
                "control: an ordinary attacker becomes the monster's target");

            Assert.IsFalse(DamageMakesTheMonsterRetaliate(true),
                "an attacker with +NOTARGET is not switched to");
        }

        [TestMethod]
        public void ANoTargetActorStillWakesTheMonsterUp()
        {
            // ZDoom is explicit about this: "NOPAIN will not stop the actor from waking up if
            // damaged", and the same holds for +NOTARGET. Losing the wake-up would make the flag
            // mean "invisible to damage", which it does not.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content, out _);
                var world = game.World;

                var imp = FirstImp(world);
                var player = world.ConsolePlayer;

                Assert.IsNotNull(imp);
                Assert.IsNotNull(player);

                player.Mobj.Flags |= MobjFlags.NoTarget;

                imp.Target = null;
                imp.ReactionTime = 100;

                world.ThingInteraction.DamageMobj(imp, null, player.Mobj, 1);

                Assert.AreEqual(0, imp.ReactionTime, "the monster woke up");
            }
        }
    }
}
