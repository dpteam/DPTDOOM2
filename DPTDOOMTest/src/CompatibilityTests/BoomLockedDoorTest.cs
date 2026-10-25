using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A Boom generalized locked door asks for its key before it opens.
    ///
    /// This is the join that the pure lock rules cannot check: the key number is decoded from
    /// the line, the player's cards are looked at, and the door either starts or does not. The
    /// failure it guards against was real and silent - the locked family was routed straight to
    /// the ordinary door builder, so a red-key door opened for anyone, and the map simply looked
    /// like it had no lock.
    ///
    /// The map is a real one. The line is borrowed: the door builder finds its sectors by tag,
    /// so any line with the right tag and a front side will do, and building a synthetic map
    /// would only test the synthetic map.
    /// </summary>
    [TestClass]
    public class BoomLockedDoorTest
    {
        [TestInitialize]
        public void Before()
        {
            LockDefs.Clear();
        }

        [TestCleanup]
        public void After()
        {
            LockDefs.Clear();
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
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                cmds[i] = new TicCmd();
            }

            game.Update(cmds);

            return game;
        }

        /// <summary>
        /// A line whose tag points at the sector, so the door builder finds it. The tag is
        /// arbitrary and shared by the line and the sector, which is all the lookup needs.
        /// </summary>
        private static LineDef LineFor(World world, Sector sector)
        {
            const int tag = 20001;

            sector.Tag = tag;

            foreach (var line in sector.Lines)
            {
                if (line.FrontSector == sector && line.FrontSide != null)
                {
                    line.Tag = (short)tag;
                    return line;
                }
            }

            return null;
        }

        /// <summary>A generalized locked door special: the family, the key, a normal speed.</summary>
        private static LineSpecial LockedDoor(int key, bool skullsCountAsCards)
        {
            var special = BoomGeneralized.GenLockedBase
                          | (key << 6)
                          | ((int)BoomGeneralized.Speed.Normal << 3);

            if (skullsCountAsCards)
            {
                special |= 1 << 9;
            }

            return (LineSpecial)special;
        }

        [TestMethod]
        public void ARedCardDoorDoesNotOpenWithoutTheCard()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var player = world.ConsolePlayer;

                var sector = world.Map.Sectors[0];
                var line = LineFor(world, sector);

                Assert.IsNotNull(line, "MAP01 has a sector with a usable line");

                line.Special = LockedDoor(GeneralizedLock.RedCard, false);

                Assert.IsNull(sector.SpecialData, "nothing is moving it yet");

                var opened = world.SectorAction.DoGeneralized(line, player.Mobj);

                Assert.IsFalse(opened, "the door stayed shut");
                Assert.IsNull(sector.SpecialData, "and no door thinker was created");
                Assert.AreEqual((string)DoomInfo.Strings.PD_REDC, player.Message,
                    "the player is told which key it wants");
            }
        }

        [TestMethod]
        public void ARedCardDoorOpensWithTheCard()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var player = world.ConsolePlayer;

                var sector = world.Map.Sectors[0];
                var line = LineFor(world, sector);

                Assert.IsNotNull(line);

                line.Special = LockedDoor(GeneralizedLock.RedCard, false);

                player.Cards[(int)CardType.RedCard] = true;

                var opened = world.SectorAction.DoGeneralized(line, player.Mobj);

                Assert.IsTrue(opened, "the door opened");
                Assert.IsInstanceOfType(sector.SpecialData, typeof(VerticalDoor),
                    "and it is a door that is now moving");
            }
        }

        [TestMethod]
        public void ASkullDoesNotOpenACardLockWhenTheTypeMatters()
        {
            // The difference between the two readings of the LockedNKeys bit, on a real door:
            // without the bit a skull is not a card, so this door stays shut.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var player = world.ConsolePlayer;

                var sector = world.Map.Sectors[0];
                var line = LineFor(world, sector);

                Assert.IsNotNull(line);

                line.Special = LockedDoor(GeneralizedLock.RedCard, false);

                player.Cards[(int)CardType.RedSkull] = true;

                Assert.IsFalse(world.SectorAction.DoGeneralized(line, player.Mobj));
                Assert.IsNull(sector.SpecialData);
                Assert.AreEqual((string)DoomInfo.Strings.PD_REDC, player.Message);
            }
        }

        [TestMethod]
        public void ASkullOpensACardLockWhenTheTypeDoesNotMatter()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var player = world.ConsolePlayer;

                var sector = world.Map.Sectors[0];
                var line = LineFor(world, sector);

                Assert.IsNotNull(line);

                line.Special = LockedDoor(GeneralizedLock.RedCard, true);

                player.Cards[(int)CardType.RedSkull] = true;

                Assert.IsTrue(world.SectorAction.DoGeneralized(line, player.Mobj),
                    "with the bit set the skull counts as the card");
            }
        }

        [TestMethod]
        public void AMonsterDoesNotOpenALockedDoor()
        {
            // Boom only lets players open locked doors. A monster walking into one must not
            // open it, or a map's keys stop meaning anything.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var game = NewGame(content);
                var world = game.World;
                var player = world.ConsolePlayer;

                var sector = world.Map.Sectors[0];
                var line = LineFor(world, sector);

                Assert.IsNotNull(line);

                line.Special = LockedDoor(GeneralizedLock.RedCard, false);

                // Any monster on the map will do; what matters is that it has no player.
                Mobj monster = null;

                foreach (var thinker in world.Thinkers)
                {
                    var mobj = thinker as Mobj;

                    if (mobj != null && mobj.Player == null)
                    {
                        monster = mobj;
                        break;
                    }
                }

                if (monster == null)
                {
                    Assert.Inconclusive("MAP01 has no monster to test with");
                    return;
                }

                player.Cards[(int)CardType.RedCard] = true;

                Assert.IsFalse(world.SectorAction.DoGeneralized(line, monster),
                    "a monster cannot open it even though the player holds the key");
                Assert.IsNull(sector.SpecialData);
            }
        }

        [TestMethod]
        public void ALockDefsLumpChangesWhatTheDoorWants()
        {
            // The join between the format and the door: a file in the WAD says lock 1 needs a
            // blue card, so the red card this door asks for stops being enough - and the blue
            // one, which Boom would have refused, opens it.
            var pwad = Path.Combine(Path.GetTempPath(), "dptdoom-lockdefs-" +
                                                      Guid.NewGuid().ToString("N") + ".wad");

            try
            {
                WritePwad(pwad, "LOCKDEFS",
                    "lock 1\n{\n    message \"Blue, not red\"\n    bluecard\n}\n");

                var args = new CommandLineArgs(
                    new[] { "-iwad", WadPath.Doom2, "-file", pwad, "-nodata" });

                using (var content = new GameContent(args))
                {
                    var game = NewGame(content);
                    var world = game.World;
                    var player = world.ConsolePlayer;

                    var sector = world.Map.Sectors[0];
                    var line = LineFor(world, sector);

                    Assert.IsNotNull(line);

                    line.Special = LockedDoor(GeneralizedLock.RedCard, false);

                    player.Cards[(int)CardType.RedCard] = true;

                    Assert.IsFalse(world.SectorAction.DoGeneralized(line, player.Mobj),
                        "the file replaced Boom's answer for lock 1");
                    Assert.AreEqual("Blue, not red", player.Message);

                    player.Cards[(int)CardType.BlueCard] = true;

                    Assert.IsTrue(world.SectorAction.DoGeneralized(line, player.Mobj),
                        "and the key the file asked for opens it");
                }
            }
            finally
            {
                try { File.Delete(pwad); } catch { }
            }
        }

        private static void WritePwad(string path, string lumpName, string content)
        {
            var data = System.Text.Encoding.ASCII.GetBytes(content);

            // Null-padded, not space-padded: that is the WAD format, and the reader stops at the
            // first zero.
            var name = new byte[8];
            var bytes = System.Text.Encoding.ASCII.GetBytes(lumpName.ToUpperInvariant());
            Array.Copy(bytes, name, Math.Min(8, bytes.Length));

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("PWAD"));
                writer.Write(1);
                writer.Write(12 + data.Length);

                writer.Write(data);
                writer.Write(12);
                writer.Write(data.Length);
                writer.Write(name);
            }
        }
    }
}
