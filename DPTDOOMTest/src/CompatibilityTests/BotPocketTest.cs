using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The bot must not walk into a part of the map it cannot walk out of.
    ///
    /// The navigation graph is directional on purpose: an edge is filtered by step height
    /// and clearance, because a ledge can be dropped off and not climbed. A directional
    /// graph therefore contains one-way pockets, and a pocket is invisible to every guard
    /// the bot had. It is not stuck - it moves freely, it explores, the stuck counter
    /// resets - and it will never reach the exit.
    ///
    /// This is not hypothetical. MAP01 has one around sector 43: six sectors of fifty-nine
    /// are reachable from inside it and the exit is not one of them. A Sweeper with a fresh
    /// bot brain entered at tic 481 and spent the remaining fifteen hundred tics of its run
    /// walking around one room, which is the reported "wanders and never picks another
    /// goal". The bot test only passed before because the learned brain file knew the route.
    ///
    /// So the guard is checked here directly: from a point in that pocket, the exit is
    /// unreachable, and the bot refuses to treat it as a destination.
    /// </summary>
    [TestClass]
    public class BotPocketTest
    {
        [TestMethod]
        public void APlaceTheBotCannotLeaveIsNotADestination()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = BuildMap01(content);
                var nav = new NavGraph(world.Map);

                var exitSector = FindExitSector(world.Map);

                if (exitSector < 0)
                {
                    Assert.Inconclusive("MAP01 has no exit line");
                }

                var spawn = nav.NodeAt(world.Options.Players[0].Mobj.X, world.Options.Players[0].Mobj.Y);

                // The pocket. Coordinates from the run trace, not from the map source:
                // this is where the bot actually stood for the rest of its level.
                var pocket = nav.NodeAt(Fixed.FromInt(363), Fixed.FromInt(1838));

                if (spawn < 0 || pocket < 0)
                {
                    Assert.Inconclusive("the graph has no node near the spawn or the pocket");
                }

                var buffer = new int[nav.NodeCount];

                // The observation the guard exists for. If the graph is ever fixed so that
                // this area connects back, this test stops asserting anything and says so
                // rather than quietly passing.
                if (nav.FindPathToSector(pocket, exitSector, buffer) > 0)
                {
                    Assert.Inconclusive("the pocket now reaches the exit; the guard is untested here");
                }

                var bot = new DoomBot(world, world.Options.Players[0], nav, null, BotStyle.Sweeper);

                Assert.IsTrue(bot.CanStillLeave(spawn, spawn),
                    "from the start the exit is reachable, so any detour is allowed");

                Assert.IsFalse(bot.CanStillLeave(pocket, spawn),
                    "a detour into the pocket leaves the exit unreachable, so it must be refused");

                // And the other half: once the bot is inside, being trapped is not a reason
                // to refuse every move. That would leave it standing still instead of
                // wandering, which is worse to watch and no more likely to finish.
                Assert.IsTrue(bot.CanStillLeave(spawn, pocket),
                    "already trapped, any move is worth trying");
            }
        }

        [TestMethod]
        public void ARouteThroughThePocketIsRefused()
        {
            // The check that actually keeps the bot out, and the one that was missing.
            //
            // Checking only the destination was not enough: the graph is directed, so a route
            // to somewhere perfectly safe can pass **through** a one-way drop, and the bot is
            // stranded at the far end while the destination was fine all along. That is what
            // happened on MAP01 - a Sweeper heading for a point on the far side went through
            // the sector 43 area and stayed there for the rest of the run, with three thousand
            // tics of "no route".
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var world = BuildMap01(content);
                var nav = new NavGraph(world.Map);

                var exitSector = FindExitSector(world.Map);

                if (exitSector < 0)
                {
                    Assert.Inconclusive("MAP01 has no exit line");
                }

                var spawn = nav.NodeAt(world.Options.Players[0].Mobj.X, world.Options.Players[0].Mobj.Y);
                var pocket = nav.NodeAt(Fixed.FromInt(350), Fixed.FromInt(1849));

                if (spawn < 0 || pocket < 0)
                {
                    Assert.Inconclusive("the graph has no node near the spawn or the pocket");
                }

                var bot = new DoomBot(world, world.Options.Players[0], nav, null, BotStyle.Sweeper);

                // The rule is checked directly rather than through a real route: the pocket
                // is not reachable from the spawn by a graph path any more (that is what the
                // guard achieved), so a route through it has to be constructed to test the
                // refusal. What matters is the rule, not how the bot would have got there.
                var route = new[] { spawn, pocket };

                Assert.IsFalse(bot.RouteStaysEscapable(route, route.Length),
                    "a route that ends in the pocket is refused");

                var back = new[] { pocket, pocket };

                Assert.IsFalse(bot.RouteStaysEscapable(back, back.Length),
                    "and so is one that stays there");

                // A route that stays in the reachable part of the map is allowed, or the guard
                // would refuse every plan and the bot would never move.
                var ordinary = new[] { spawn, spawn };

                Assert.IsTrue(bot.RouteStaysEscapable(ordinary, ordinary.Length),
                    "an ordinary route from the spawn is allowed");
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

        private static int FindExitSector(Map map)
        {
            for (var s = 0; s < map.Sectors.Length; s++)
            {
                foreach (var line in map.Sectors[s].Lines)
                {
                    var special = (int)line.Special;

                    if (special == 11 || special == 51 || special == 52 || special == 124)
                    {
                        return s;
                    }
                }
            }

            return -1;
        }
    }
}
