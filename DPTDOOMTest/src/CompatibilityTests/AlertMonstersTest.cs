using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A_AlertMonsters uses the ordinary sector-noise graph, but its target is not always the
    /// caller. Tests cover that selection, all three AMF flags and the complete converted action.
    /// </summary>
    [TestClass]
    public class AlertMonstersTest
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

        private static Mobj SecondImp(World world, Mobj first)
        {
            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;
                if (mobj != null && mobj != first && mobj.Type == MobjType.Troop)
                {
                    return mobj;
                }
            }

            return null;
        }

        private static string Convert(string body, List<string> problems)
        {
            return DecorateConverter.Convert(
                "ACTOR TestImp 3001\n" +
                "{\n" +
                "  States\n" +
                "  {\n" +
                "  Missile:\n" +
                "    TROO A 4 " + body + "\n" +
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
        public void APlayerAlertsTheSectorToItself()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var player = world.ConsolePlayer.Mobj;

                player.Subsector.Sector.SoundTarget = null;
                world.MonsterBehavior.AlertMonsters(player, 0);

                Assert.AreEqual(player, player.Subsector.Sector.SoundTarget);
            }
        }

        [TestMethod]
        public void AMonsterAlertsToItsPlayerTargetNotToItself()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);
                imp.Target = player;
                imp.Subsector.Sector.SoundTarget = null;

                world.MonsterBehavior.AlertMonsters(imp, 0);

                Assert.AreEqual(player, imp.Subsector.Sector.SoundTarget,
                    "other monsters should acquire the player, not the calling imp");
            }
        }

        [TestMethod]
        public void NoSuitableTargetMeansNoAlert()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);

                Assert.IsNotNull(imp);
                imp.Target = null;
                imp.Subsector.Sector.SoundTarget = null;

                world.MonsterBehavior.AlertMonsters(imp, 0);

                Assert.IsNull(imp.Subsector.Sector.SoundTarget,
                    "a targetless monster does not turn this action into a gunshot at itself");
            }
        }

        [TestMethod]
        public void TargetEmitterAndTargetNonPlayerSelectDifferentTargets()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);
                var other = SecondImp(world, imp);

                Assert.IsNotNull(imp);
                Assert.IsNotNull(other);
                imp.Target = other;

                imp.Subsector.Sector.SoundTarget = null;
                world.MonsterBehavior.AlertMonsters(imp, 0);
                Assert.IsNull(imp.Subsector.Sector.SoundTarget,
                    "a non-player target is not accepted by default");

                world.MonsterBehavior.AlertMonsters(imp, AlertMonsterFlags.TargetNonPlayer);
                Assert.AreEqual(other, imp.Subsector.Sector.SoundTarget);

                imp.Subsector.Sector.SoundTarget = null;
                world.MonsterBehavior.AlertMonsters(imp, AlertMonsterFlags.TargetEmitter);
                Assert.AreEqual(imp, imp.Subsector.Sector.SoundTarget,
                    "AMF_TARGETEMITTER takes precedence over the current target");
            }
        }

        [TestMethod]
        public void EmitFromTargetStartsTheNoiseInTheTargetsSector()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);
                imp.Target = player;
                imp.Subsector.Sector.SoundTarget = null;
                player.Subsector.Sector.SoundTarget = null;

                world.MonsterBehavior.AlertMonsters(imp, AlertMonsterFlags.EmitFromTarget);

                Assert.AreEqual(player, player.Subsector.Sector.SoundTarget);
                Assert.IsNull(imp.Subsector.Sector.SoundTarget,
                    "the caller's sector is not the emitter when AMF_EMITFROMTARGET is set");
            }
        }

        [TestMethod]
        public void TheConverterReadsDefaultsAndAllAlertFlags()
        {
            var problems = new List<string>();
            var script = Convert(
                "A_AlertMonsters(0, AMF_TARGETNONPLAYER | AMF_EMITFROMTARGET)", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "AlertMonsters(\"Missile#1\", 6);");

            script = Convert("A_AlertMonsters()", problems);
            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            StringAssert.Contains(script, "AlertMonsters(\"Missile#1\", 0);",
                "empty parentheses select the same defaults as the no-parentheses form");
        }

        [TestMethod]
        public void MaxDistIsReportedRatherThanAppliedToAWholeSector()
        {
            var problems = new List<string>();
            Convert("A_AlertMonsters(128)", problems);

            var text = string.Join("; ", problems.ToArray());
            StringAssert.Contains(text, "maxdist");
            StringAssert.Contains(text, "unlimited");
        }

        [TestMethod]
        public void TheScriptRaisesARealAlert()
        {
            var problems = new List<string>();
            var script = Convert("A_AlertMonsters", problems);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems.ToArray()));
            ApplyScript(script);

            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = NewGame(content).World;
                var imp = FirstImp(world);
                var player = world.ConsolePlayer.Mobj;

                Assert.IsNotNull(imp);
                imp.Target = player;
                imp.Subsector.Sector.SoundTarget = null;

                imp.SetState(DoomInfo.MobjInfos[(int)MobjType.Troop].MissileState);

                Assert.AreEqual(player, imp.Subsector.Sector.SoundTarget,
                    "the converted frame called the real noise propagation");
            }
        }
    }
}
