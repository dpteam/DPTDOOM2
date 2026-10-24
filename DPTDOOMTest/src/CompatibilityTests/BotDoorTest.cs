using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// The bot must not spam doors.
    ///
    /// The report from co-op was "it stands at a door opening and closing it". The cause
    /// is in the engine and the bot's own comment explained it the wrong way round: Use on
    /// a door that is already opening reverses it, so pressing every fourth tic opened it
    /// and immediately shut it again. A bot that does that can hold itself at one door
    /// for the rest of a level, which is what the run traces showed.
    ///
    /// So the bot now presses only when the door is genuinely shut - no active thinker -
    /// and this test pins the rule from both sides: a sector with a door thinker in it is
    /// not to be pressed, and a closed one is.
    ///
    /// The test lives on a real map because the alternative is a mock sector, and a mock
    /// sector would only prove that the test can read its own field.
    /// </summary>
    [TestClass]
    public class BotDoorTest
    {
        [TestMethod]
        public void ADoorWithATinkerIsNotPressed()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
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
                game.Update(NewCmds());

                var world = game.World;
                var bot = new DoomBot(world, world.Options.Players[0], null, null, BotStyle.Rush);

                // Find a door sector: a two-sided line whose sector carries a door
                // special, which is what the nav graph calls a door too.
                var doorSector = FindDoorSector(world);

                if (doorSector < 0)
                {
                    Assert.Inconclusive("MAP01 has no door to test with");
                }

                var sector = world.Map.Sectors[doorSector];

                // Shut: no thinker, and the ceiling is low. This is the state worth
                // pressing.
                Assert.IsNull(sector.SpecialData, "the door starts with no thinker");
                Assert.IsTrue(bot.WouldPressDoor(doorSector),
                    "a shut door is pressed");

                // Now give it a thinker, which is what opening the door does, and check
                // the bot stops pressing. This is the exact state that used to be
                // reversed into a closing door.
                var thinker = new VerticalDoor(world);
                world.Thinkers.Add(thinker);
                sector.SpecialData = thinker;

                Assert.IsFalse(bot.WouldPressDoor(doorSector),
                    "a door that is already moving must not be pressed again");
            }
        }

        [TestMethod]
        public void AnOpenDoorwayIsNotPressed()
        {
            // The second half of the rule: a sector whose gap is already tall enough to
            // walk through does not need Use, door special or not. Pressing there is
            // wasted in the best case and can shut it in the worst.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
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
                game.Update(NewCmds());

                var world = game.World;

                // Any ordinary room is taller than the bot, and standing in it pressing
                // Use must not register as a door.
                var room = world.Map.Sectors[world.Options.Players[0].Mobj.Subsector.Sector.Number];

                Assert.IsTrue(room.CeilingHeight - room.FloorHeight > Fixed.FromInt(56),
                    "the starting room is tall enough to stand in");

                Assert.IsFalse(new DoomBot(world, world.Options.Players[0], null, null, BotStyle.Rush).WouldPressDoor(room.Number),
                    "a room the bot can walk through is not a door to press");
            }
        }

        /// <summary>TicCmd is a class, so an array of them starts full of nulls.</summary>
        private static TicCmd[] NewCmds()
        {
            var cmds = new TicCmd[Player.MaxPlayerCount];

            for (var i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new TicCmd();
            }

            return cmds;
        }

        /// <summary>
        /// A sector that a door line leads into, or -1.
        ///
        /// Door specials sit on the line, and the moving part is the sector behind it -
        /// which is why this looks at lines rather than at sectors alone.
        /// </summary>
        private static int FindDoorSector(World world)
        {
            foreach (var line in world.Map.Lines)
            {
                if (line.BackSide == null)
                {
                    continue;
                }

                if (!IsDoorSpecial((int)line.Special))
                {
                    continue;
                }

                var sector = line.BackSide.Sector;

                if (sector != null)
                {
                    return sector.Number;
                }
            }

            return -1;
        }

        private static bool IsDoorSpecial(int special)
        {
            switch (special)
            {
                case 1:
                case 26:
                case 27:
                case 28:
                case 31:
                case 32:
                case 33:
                case 34:
                case 117:
                case 118:
                    return true;

                default:
                    return false;
            }
        }
    }
}
