using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The cheat dispatcher, which the typed codes and the console both use.
    ///
    /// There used to be two paths: the classic codes called private methods directly,
    /// and the console would have needed its own copy. A copy is how "iddqd" and "god"
    /// end up meaning slightly different things, so both now go through
    /// <see cref="Cheat.Execute(CheatAction)"/> and these tests check that dispatcher.
    ///
    /// The important assertions are the ones about refusal: a cheat that works in a
    /// network game desynchronises every other player, and one that works during demo
    /// playback corrupts the thing being checked.
    /// </summary>
    [TestClass]
    public class CheatActionTest
    {
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
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);

            return game;
        }

        [TestMethod]
        public void EveryCheatHasAnAction()
        {
            // A cheat that maps to nothing is a cheat the console cannot run, and the
            // failure is silent: the player types "god" and nothing happens.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cheat = game.World.Cheat;

                Assert.IsTrue(cheat.Execute(CheatAction.God), "god");
                Assert.IsTrue(cheat.Execute(CheatAction.NoClip), "noclip");
                Assert.IsTrue(cheat.Execute(CheatAction.Weapons), "weapons");
                Assert.IsTrue(cheat.Execute(CheatAction.Idfa), "idfa");
                Assert.IsTrue(cheat.Execute(CheatAction.Idkfa), "idkfa");
                Assert.IsTrue(cheat.Execute(CheatAction.Keys), "keys");
                Assert.IsTrue(cheat.Execute(CheatAction.FullMap), "full map");
                Assert.IsTrue(cheat.Execute(CheatAction.PowerUpList), "powerup list");
                Assert.IsTrue(cheat.Execute(CheatAction.Chainsaw), "chainsaw");
                Assert.IsTrue(cheat.Execute(CheatAction.KillMonsters), "kill monsters");
            }
        }

        [TestMethod]
        public void AnUnknownActionIsRefused()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);

                Assert.IsFalse(game.World.Cheat.Execute(CheatAction.None),
                    "the empty action does nothing and says so");
            }
        }

        [TestMethod]
        public void GodModeToggles()
        {
            // Toggled, not set: a second "god" has to turn it off, or a player cannot
            // get back to a fair fight without restarting the level.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cheat = game.World.Cheat;
                var player = game.World.ConsolePlayer;

                cheat.Execute(CheatAction.God);
                Assert.AreNotEqual(0, (int)(player.Cheats & CheatFlags.GodMode), "god mode is on");

                cheat.Execute(CheatAction.God);
                Assert.AreEqual(0, (int)(player.Cheats & CheatFlags.GodMode), "and off again");
            }
        }

        [TestMethod]
        public void NoClipToggles()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cheat = game.World.Cheat;
                var player = game.World.ConsolePlayer;

                cheat.Execute(CheatAction.NoClip);
                Assert.AreNotEqual(0, (int)(player.Cheats & CheatFlags.NoClip), "noclip is on");

                cheat.Execute(CheatAction.NoClip);
                Assert.AreEqual(0, (int)(player.Cheats & CheatFlags.NoClip), "and off again");
            }
        }

        [TestMethod]
        public void GiveWeaponsOwnsEveryWeaponAndNoKeys()
        {
            // "give weapons" has to be distinguishishable from "give all": a player
            // asking for the arsenal is not asking to skip every locked door.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cheat = game.World.Cheat;
                var player = game.World.ConsolePlayer;

                cheat.Execute(CheatAction.Weapons);

                for (var i = 0; i < (int)WeaponType.Count; i++)
                {
                    Assert.IsTrue(player.WeaponOwned[i],
                        "weapon " + (WeaponType)i + " is owned");
                }

                for (var i = 0; i < (int)CardType.Count; i++)
                {
                    Assert.IsFalse(player.Cards[i],
                        "but no key was handed over: " + (CardType)i);
                }
            }
        }

        [TestMethod]
        public void GiveAllIncludesKeys()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cheat = game.World.Cheat;
                var player = game.World.ConsolePlayer;

                cheat.Execute(CheatAction.Idkfa);

                for (var i = 0; i < (int)CardType.Count; i++)
                {
                    Assert.IsTrue(player.Cards[i], "key " + (CardType)i + " is owned");
                }

                Assert.IsTrue(player.ArmorPoints > 0, "and armour came with it");
            }
        }

        [TestMethod]
        public void GiveKeysDoesNotHandOverWeapons()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cheat = game.World.Cheat;
                var player = game.World.ConsolePlayer;

                var hadRocket = player.WeaponOwned[(int)WeaponType.Missile];

                cheat.Execute(CheatAction.Keys);

                Assert.AreEqual(hadRocket, player.WeaponOwned[(int)WeaponType.Missile],
                    "the rocket launcher was not given away");

                for (var i = 0; i < (int)CardType.Count; i++)
                {
                    Assert.IsTrue(player.Cards[i], "but the keys were");
                }
            }
        }

        [TestMethod]
        public void RefusalSaysWhy()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);

                // A single-player game allows them.
                Assert.IsTrue(game.World.Cheat.Allowed, "single player allows cheats");

                // The refusal is a sentence, not an empty string or a code: it is shown
                // to the player, in the console, as written.
                Assert.IsTrue(game.World.Cheat.Refusal.Length > 20,
                    "the refusal explains itself: " + game.World.Cheat.Refusal);
            }
        }

        [TestMethod]
        public void KillMonstersEmptiesTheLevel()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var cheat = game.World.Cheat;

                var before = CountLivingMonsters(game.World);

                cheat.Execute(CheatAction.KillMonsters);

                var after = CountLivingMonsters(game.World);

                Assert.IsTrue(before > 0, "MAP01 has monsters to begin with (" + before + ")");
                Assert.AreEqual(0, after, "and none are left alive");
            }
        }

        private static int CountLivingMonsters(World world)
        {
            var count = 0;

            foreach (var thinker in world.Thinkers)
            {
                var mobj = thinker as Mobj;

                if (mobj != null &&
                    mobj.Player == null &&
                    ((mobj.Flags & MobjFlags.CountKill) != 0 || mobj.Type == MobjType.Skull) &&
                    mobj.Health > 0)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
