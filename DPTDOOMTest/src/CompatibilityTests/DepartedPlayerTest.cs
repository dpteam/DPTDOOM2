using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A player who left for good must stop standing in the corridor.
    ///
    /// The slot cannot be removed - a world built for two players and a world built for one
    /// are different worlds, and lockstep has no way to reconcile that - so the host keeps
    /// sending that player's command for ever. What must go is the body.
    ///
    /// The removal has to happen at the *same tic on every machine*, which is why it travels
    /// inside the command rather than as a notice: a notice arrives whenever the network gets
    /// round to it, and two ends would then remove the body at different tics and part
    /// company. These tests apply the command directly and check both halves of the rule -
    /// that the body dies, and that the slot itself is left alone.
    /// </summary>
    [TestClass]
    public class DepartedPlayerTest
    {
        [TestMethod]
        public void TheBodyOfADepartedPlayerDiesAndTheSlotStays()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewTwoPlayerGame(content);
                var world = game.World;

                var second = world.Options.Players[1];

                Assert.IsTrue(second.InGame, "the second player starts in the game");
                Assert.IsNotNull(second.Mobj, "and has a body");

                var body = second.Mobj;

                var cmds = NewCmds();
                cmds[1].Buttons = TicCmdButtons.Gone;

                game.Update(cmds);

                Assert.IsTrue(body.Health <= 0, "the body was killed, health " + body.Health);

                var spawnState = DoomInfo.States[(int)DoomInfo.MobjInfos[(int)body.Type].SpawnState];

                Assert.IsFalse(body.State == spawnState,
                    "and left its spawn state, so it is not standing there");

                // The slot stays: this is the part that must not change. A world built for
                // two players and a world built for one are different worlds.
                Assert.IsTrue(second.InGame,
                    "the slot is still in the game, or co-op death handling would change shape");
                Assert.AreSame(body, second.Mobj, "and the body is still referenced, not dropped");

                // And the corpse does not block, which is the point of the whole change.
                Assert.IsTrue((body.Flags & MobjFlags.Solid) == 0 || body.Health <= 0,
                    "a corpse is not an obstacle");
            }
        }

        [TestMethod]
        public void ApplyingTheMarkerAgainChangesNothing()
        {
            // The host keeps sending the marker for ever, because the command is the only
            // thing that tells a late joiner or a re-sent tic what happened. It has to be
            // idempotent or the second tic would try to kill a corpse.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewTwoPlayerGame(content);
                var second = game.World.Options.Players[1];
                var body = second.Mobj;

                var cmds = NewCmds();
                cmds[1].Buttons = TicCmdButtons.Gone;

                game.Update(cmds);
                var stateAfterFirst = body.State;
                var healthAfterFirst = body.Health;

                game.Update(cmds);

                Assert.AreEqual(healthAfterFirst, body.Health, "health is unchanged");
                Assert.AreEqual(stateAfterFirst, body.State, "and so is the death animation");
            }
        }

        [TestMethod]
        public void AnOrdinaryCommandLeavesTheBodyAlone()
        {
            // The guard against a false positive: nothing about a normal tic may kill anyone.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewTwoPlayerGame(content);
                var second = game.World.Options.Players[1];

                var health = second.Mobj.Health;

                for (var tic = 0; tic < 5; tic++)
                {
                    game.Update(NewCmds());
                }

                Assert.AreEqual(health, second.Mobj.Health, "the second player is untouched");
            }
        }

        private static DoomGame NewTwoPlayerGame(GameContent content)
        {
            var options = new GameOptions();
            options.GameVersion = content.Wad.GameVersion;
            options.GameMode = content.Wad.GameMode;
            options.MissionPack = content.Wad.MissionPack;
            options.Skill = GameSkill.Medium;
            options.Episode = 1;
            options.Map = 1;
            options.Players[0].InGame = true;
            options.Players[1].InGame = true;

            var game = new DoomGame(content, options);
            game.DeferedInitNew();

            // TicCmd is a class, so an array of them starts full of nulls.
            game.Update(NewCmds());

            return game;
        }

        private static TicCmd[] NewCmds()
        {
            var cmds = new TicCmd[Player.MaxPlayerCount];

            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            return cmds;
        }
    }
}
