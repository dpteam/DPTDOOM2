using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Content that is broken in the ways real PWADs are broken must still start.
    ///
    /// The cases here are the three that used to throw: a thing type the port does not
    /// know, which killed the whole map load, an animation cycle that points at a flat
    /// which is not in the WAD, and a sprite set with only part of its rotations. Each
    /// has a sane substitute, so each becomes a line in the load report instead of an
    /// exception.
    /// </summary>
    [TestClass]
    public class LoadResilienceTest
    {
        /// <summary>
        /// A copied MAP01 with one extra thing of a type Doom has never had.
        ///
        /// Real PWADs carry these all the time - a DECORATE actor, a ZDoom-only type -
        /// and one of them used to refuse the entire level. The map is otherwise the
        /// IWAD's, so if this loads, the level is genuinely playable and not merely
        /// parsed.
        /// </summary>
        [TestMethod]
        public void AMapWithAnUnknownThingTypeStillLoads()
        {
            string pwadPath = BuildMapWithUnknownThing(WadPath.Doom2, 9999);

            try
            {
                using (var content = GameContent.CreateDummy(WadPath.Doom2, pwadPath))
                {
                    LoadReport.Clear();

                    var options = new GameOptions
                    {
                        GameVersion = content.Wad.GameVersion,
                        GameMode = content.Wad.GameMode,
                        MissionPack = content.Wad.MissionPack,
                        Skill = GameSkill.Medium,
                        Episode = 1,
                        Map = 1
                    };
                    options.Players[0].InGame = true;

                    var game = new DoomGame(content, options);
                    game.DeferedInitNew();

                    // The world is built by the first tick, not by DeferedInitNew, so
                    // that the pause at the start of a level does not cost a load.
                    game.Update(NewCmds());

                    Assert.IsNotNull(game.World, "the level loaded");

                    var text = LoadReport.Describe();

                    StringAssert.Contains(text, "unknown thing type: 1", text);
                    StringAssert.Contains(text, "type 9999", text);

                    // Nothing else may be reported: one bad thing must not turn into a
                    // pile of knock-on warnings that hide the real cause.
                    Assert.AreEqual(1, LoadReport.Count, text);
                }
            }
            finally
            {
                TryDelete(pwadPath);
            }
        }

        /// <summary>
        /// The same map without the extra thing loads with nothing to report.
        ///
        /// The pair matters: without this, a report that fires on everything would
        /// still pass the test above.
        /// </summary>
        [TestMethod]
        public void TheSameMapWithoutTheBadThingIsQuiet()
        {
            string pwadPath = BuildMapWithUnknownThing(WadPath.Doom2, -1);

            try
            {
                using (var content = GameContent.CreateDummy(WadPath.Doom2, pwadPath))
                {
                    LoadReport.Clear();

                    var options = new GameOptions
                    {
                        GameVersion = content.Wad.GameVersion,
                        GameMode = content.Wad.GameMode,
                        MissionPack = content.Wad.MissionPack,
                        Skill = GameSkill.Medium,
                        Episode = 1,
                        Map = 1
                    };
                    options.Players[0].InGame = true;

                    var game = new DoomGame(content, options);
                    game.DeferedInitNew();
                    game.Update(NewCmds());

                    Assert.IsFalse(LoadReport.HasProblems, LoadReport.Describe());
                    Assert.AreEqual(IwadThingCount(), game.World.Map.Things.Length,
                        "the copied map keeps all of MAP01's own things");
                }
            }
            finally
            {
                TryDelete(pwadPath);
            }
        }

        // ------------------------------------------------------------ truncation

        /// <summary>
        /// A lump cut short still loads, and says which lump lost how many bytes.
        ///
        /// Every map reader used to throw a bare <c>new Exception()</c> here - no
        /// message, no lump name, no map - which is what a player sees when a download
        /// of a PWAD was interrupted. The records that are there are still valid.
        /// </summary>
        [TestMethod]
        public void ATruncatedLumpIsReportedAndTheRestStillLoads()
        {
            string pwadPath = BuildMapWithTruncatedThings(WadPath.Doom2, 6);

            try
            {
                using (var content = GameContent.CreateDummy(WadPath.Doom2, pwadPath))
                {
                    LoadReport.Clear();

                    var options = new GameOptions
                    {
                        GameVersion = content.Wad.GameVersion,
                        GameMode = content.Wad.GameMode,
                        MissionPack = content.Wad.MissionPack,
                        Skill = GameSkill.Medium,
                        Episode = 1,
                        Map = 1
                    };
                    options.Players[0].InGame = true;

                    var game = new DoomGame(content, options);
                    game.DeferedInitNew();
                    game.Update(NewCmds());

                    Assert.IsNotNull(game.World, "the level loaded despite the cut lump");

                    var text = LoadReport.Describe();

                    StringAssert.Contains(text, "truncated map lump: 1", text);
                    StringAssert.Contains(text, "THINGS", text);
                    StringAssert.Contains(text, "4 leftover bytes", text);

                    // The records that fit are used and the last one is lost: the count
                    // comes from the size, not from a claim in the lump.
                    Assert.AreEqual(IwadThingCount() - 1, game.World.Map.Things.Length, text);
                }
            }
            finally
            {
                TryDelete(pwadPath);
            }
        }

        [TestMethod]
        public void OneLeftoverByteIsSingular()
        {
            LoadReport.Clear();
            LoadReport.WholeRecords("VERTEXES", 4 * 10 + 1, 4);

            StringAssert.Contains(LoadReport.Describe(), "1 leftover byte,");
        }

        [TestMethod]
        public void AWholeNumberOfRecordsIsNotReported()
        {
            LoadReport.Clear();

            Assert.AreEqual(10, LoadReport.WholeRecords("VERTEXES", 4 * 10, 4));
            Assert.IsFalse(LoadReport.HasProblems, "exact lump sizes are the normal case");
        }

        // ------------------------------------------------------------ malformed textures

        /// <summary>
        /// A texture directory containing one good record and three kinds of bad ones.
        ///
        /// This is the shape found in DOOM ACE's ademo1.wad: the bad records are exploit data, not
        /// pictures, and a loader that trusts them dies before the map is even considered.
        /// </summary>
        [TestMethod]
        public void MalformedTextureRecordsAreSkipped()
        {
            var path = BuildMalformedTextureWad();

            try
            {
                LoadReport.Clear();

                using (var wad = new Wad(new[] { path }))
                {
                    var lookup = new TextureLookup(wad);

                    Assert.IsTrue(lookup.GetNumber("GOOD") >= 0,
                        "the valid texture after the directory still loads");
                    Assert.AreEqual(-1, lookup.GetNumber("BAD"), "negative patch counts are rejected");
                    Assert.AreEqual(-1, lookup.GetNumber("ZERO"), "zero-sized textures are rejected");
                    Assert.AreEqual(-1, lookup.GetNumber("OUT"), "out-of-lump offsets are rejected");

                    var text = LoadReport.Describe();

                    StringAssert.Contains(text, "bad texture definition", text);
                    StringAssert.Contains(text, "negative patch count", text);
                    StringAssert.Contains(text, "outside the lump", text);
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        /// <summary>
        /// Missing map artwork gets a drawable substitute, without changing what "exists" means.
        /// </summary>
        [TestMethod]
        public void MissingMapTexturesGetADummyWithoutLyingAboutExistence()
        {
            var path = BuildMalformedTextureWad();

            try
            {
                LoadReport.Clear();

                using (var wad = new Wad(new[] { path }))
                {
                    var lookup = new TextureLookup(wad);
                    var number = lookup.GetNumberOrDummy("SKY1");

                    Assert.IsTrue(number >= 0, "the renderer has a valid index");
                    Assert.IsNotNull(lookup[number], "and something it can draw");
                    Assert.AreEqual(number, lookup.GetNumberOrDummy("SKY1"),
                        "the same missing name reuses the same dummy");
                    Assert.AreEqual(-1, lookup.GetNumber("SKY1"),
                        "existence checks are not changed by the rendering fallback");
                    StringAssert.Contains(LoadReport.Describe(), "unknown texture");
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// Builds a tiny WAD with one valid patch and a deliberately hostile TEXTURE1 directory.
        /// No external mod is needed, so this tests the same failure modes on every machine.
        /// </summary>
        private static string BuildMalformedTextureWad()
        {
            var pnames = new byte[12];
            WriteInt32(pnames, 0, 1);
            var patchName = Encoding.ASCII.GetBytes("PATCH0");
            Array.Copy(patchName, 0, pnames, 4, patchName.Length);

            const int count = 4;
            const int goodOffset = 4 + 4 * count;
            const int badOffset = goodOffset + 32;
            const int zeroOffset = badOffset + 22;
            var textureData = new byte[zeroOffset + 22];

            WriteInt32(textureData, 0, count);
            WriteInt32(textureData, 4, goodOffset);
            WriteInt32(textureData, 8, badOffset);
            WriteInt32(textureData, 12, zeroOffset);
            WriteInt32(textureData, 16, textureData.Length + 1000);

            WriteTextureRecord(textureData, goodOffset, "GOOD", 4, 4, 1);
            WriteTextureRecord(textureData, badOffset, "BAD", 4, 4, -1);
            WriteTextureRecord(textureData, zeroOffset, "ZERO", 4, 0, 0);

            var lumps = new List<KeyValuePair<string, byte[]>>
            {
                Lump("PNAMES", pnames),
                Lump("TEXTURE1", textureData),
                Lump("PATCH0", MakeTwoByTwoPatch()),
                Lump("MAP01", new byte[0])
            };

            var path = Path.Combine(
                Path.GetTempPath(), "dptdoom-textures-" + Guid.NewGuid().ToString("N") + ".wad");

            WriteWad(path, lumps);
            return path;
        }

        private static void WriteTextureRecord(byte[] data, int offset, string name,
                                               int width, int height, int patchCount)
        {
            var bytes = Encoding.ASCII.GetBytes(name);
            Array.Copy(bytes, 0, data, offset, Math.Min(8, bytes.Length));
            WriteInt16(data, offset + 12, width);
            WriteInt16(data, offset + 14, height);
            WriteInt16(data, offset + 20, patchCount);
        }

        /// <summary>A valid 2x2 patch with one post per column.</summary>
        private static byte[] MakeTwoByTwoPatch()
        {
            var data = new byte[30];

            WriteInt16(data, 0, 2);
            WriteInt16(data, 2, 2);
            WriteInt32(data, 8, 16);
            WriteInt32(data, 12, 23);

            WritePost(data, 16);
            WritePost(data, 23);

            return data;
        }

        private static void WritePost(byte[] data, int offset)
        {
            data[offset] = 0;
            data[offset + 1] = 2;
            data[offset + 2] = 0;
            data[offset + 3] = 1;
            data[offset + 4] = 2;
            data[offset + 5] = 0;
            data[offset + 6] = 255;
        }

        private static void WriteInt32(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>
        /// How many things MAP01 has when loaded straight from the IWAD.
        ///
        /// Compared against rather than written down: a number copied out of a WAD that
        /// is not in the repository is a fact the test cannot check, and it would read
        /// as a loader bug the day the number and the comment stopped agreeing.
        /// </summary>
        private static int IwadThingCount()
        {
            using (var wad = new Wad(new[] { WadPath.Doom2 }))
            {
                var map = wad.GetLumpNumber("MAP01");

                return wad.GetLumpSize(map + 1) / 10;
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
        /// A copy of the IWAD's MAP01 whose THINGS lump is cut by the given number of
        /// bytes, the way an interrupted download leaves it.
        /// </summary>
        private static string BuildMapWithTruncatedThings(string iwadPath, int cut)
        {
            var lumps = CopyMapLumps(iwadPath);

            for (var i = 0; i < lumps.Count; i++)
            {
                if (lumps[i].Key == "THINGS")
                {
                    var whole = lumps[i].Value;
                    var cut_ = new byte[whole.Length - cut];
                    Array.Copy(whole, cut_, cut_.Length);
                    lumps[i] = Lump("THINGS", cut_);
                }
            }

            var path = Path.Combine(
                Path.GetTempPath(), "dptdoom-resilience-" + Guid.NewGuid().ToString("N") + ".wad");

            WriteWad(path, lumps);

            return path;
        }

        /// <summary>
        /// Writes a PWAD holding a copy of the IWAD's MAP01, optionally with one extra
        /// thing of the given type appended.
        ///
        /// The map lumps are copied verbatim rather than built by hand: a synthetic map
        /// would test the test instead of the loader. Appending to THINGS is safe
        /// because the record count is the lump size divided by ten.
        /// </summary>
        private static string BuildMapWithUnknownThing(string iwadPath, int thingType)
        {
            var lumps = CopyMapLumps(iwadPath);

            for (var i = 0; i < lumps.Count; i++)
            {
                if (lumps[i].Key == "THINGS" && thingType > 0)
                {
                    lumps[i] = Lump("THINGS", AppendThing(lumps[i].Value, thingType));
                }
            }

            var path = Path.Combine(
                Path.GetTempPath(), "dptdoom-resilience-" + Guid.NewGuid().ToString("N") + ".wad");

            WriteWad(path, lumps);

            return path;
        }

        /// <summary>The MAP01 marker plus the ten lumps the map loader reads positionally.</summary>
        private static List<KeyValuePair<string, byte[]>> CopyMapLumps(string iwadPath)
        {
            var lumps = new List<KeyValuePair<string, byte[]>>();

            using (var wad = new Wad(new[] { iwadPath }))
            {
                var map = wad.GetLumpNumber("MAP01");

                if (map == -1)
                {
                    Assert.Inconclusive("doom2.wad has no MAP01.");
                }

                lumps.Add(Lump("MAP01", new byte[0]));

                var names = new[] { "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES",
                    "SEGS", "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP" };

                for (var i = 0; i < names.Length; i++)
                {
                    lumps.Add(Lump(names[i], wad.ReadLump(map + 1 + i)));
                }
            }

            return lumps;
        }

        /// <summary>
        /// One more THINGS record, flagged for every skill level so the loader actually
        /// reaches it.
        /// </summary>
        private static byte[] AppendThing(byte[] original, int type)
        {
            var data = new byte[original.Length + 10];
            Array.Copy(original, data, original.Length);

            var offset = original.Length;

            // x, y, angle, type, flags - all little-endian 16-bit.
            WriteInt16(data, offset, 1024);
            WriteInt16(data, offset + 2, 1024);
            WriteInt16(data, offset + 4, 0);
            WriteInt16(data, offset + 6, type);
            WriteInt16(data, offset + 8, 7);

            return data;
        }

        private static void WriteInt16(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static KeyValuePair<string, byte[]> Lump(string name, byte[] data)
        {
            return new KeyValuePair<string, byte[]>(name, data);
        }

        private static void WriteWad(string path, List<KeyValuePair<string, byte[]>> lumps)
        {
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(Encoding.ASCII.GetBytes("PWAD"));
                writer.Write(lumps.Count);

                var tableOffset = 12;
                var dataOffset = tableOffset + 16 * lumps.Count;
                writer.Write(tableOffset);

                var data = new MemoryStream();
                var offsets = new List<int>();
                var sizes = new List<int>();

                foreach (var lump in lumps)
                {
                    offsets.Add(dataOffset + (int)data.Length);
                    sizes.Add(lump.Value.Length);
                    data.Write(lump.Value, 0, lump.Value.Length);
                }

                for (var i = 0; i < lumps.Count; i++)
                {
                    var name = new byte[8];
                    var ascii = Encoding.ASCII.GetBytes(lumps[i].Key);
                    Array.Copy(ascii, name, Math.Min(8, ascii.Length));

                    writer.Write(offsets[i]);
                    writer.Write(sizes[i]);
                    writer.Write(name);
                }

                writer.Write(data.ToArray());
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
