using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// PK3 and ZIP containers.
    ///
    /// A PK3 is a ZIP, and this port has to read it with its own code: the
    /// framework class for ZIP archives arrived in .NET 4.5 and the shipped build
    /// targets 4.0 for Windows XP. So these tests are the only thing standing
    /// between the hand-written reader and a corrupt archive on a user's disk.
    /// </summary>
    [TestClass]
    public class Pk3Test
    {
        private string folder;

        [TestInitialize]
        public void CreateFolder()
        {
            folder = Path.Combine(
                Path.GetTempPath(),
                "dptdoom-pk3-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(folder);
        }

        [TestCleanup]
        public void DeleteFolder()
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// Builds a tiny valid WAD with the given lumps, so the test does not need
        /// a real IWAD and does not spend a second deflating fifteen megabytes.
        /// </summary>
        private static byte[] BuildWad(params KeyValuePair<string, byte[]>[] lumps)
        {
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(Encoding.ASCII.GetBytes("PWAD"));
                writer.Write(lumps.Length);

                // The lump table goes right after the header, and the data after
                // the table.
                var tableOffset = 12;
                var dataOffset = tableOffset + 16 * lumps.Length;

                writer.Write(tableOffset);

                var data = new MemoryStream();
                var positions = new List<int>();
                var sizes = new List<int>();

                foreach (var lump in lumps)
                {
                    positions.Add(dataOffset + (int)data.Length);
                    sizes.Add(lump.Value.Length);
                    data.Write(lump.Value, 0, lump.Value.Length);
                }

                for (var i = 0; i < lumps.Length; i++)
                {
                    writer.Write(positions[i]);
                    writer.Write(sizes[i]);

                    // Lump names are padded with NUL, not with spaces: the reader
                    // stops at the first zero byte.
                    var name = lumps[i].Key.PadRight(8, '\0').Substring(0, 8).ToUpperInvariant();
                    writer.Write(Encoding.ASCII.GetBytes(name));
                }

                writer.Write(data.ToArray());
                writer.Flush();

                return output.ToArray();
            }
        }

        private static KeyValuePair<string, byte[]> Lump(string name, string text)
        {
            return new KeyValuePair<string, byte[]>(name, Encoding.ASCII.GetBytes(text));
        }

        /// <summary>
        /// A tiny WAD that still looks like a game: it carries a MAP01 marker, so
        /// the container can tell it is Doom 2. Without it the loader refuses the
        /// file, which is correct behaviour and would make these tests check the
        /// wrong thing.
        /// </summary>
        private static byte[] BuildGameWad(params KeyValuePair<string, byte[]>[] lumps)
        {
            var all = new List<KeyValuePair<string, byte[]>>();
            all.Add(Lump("MAP01", "marker"));
            all.AddRange(lumps);

            return BuildWad(all.ToArray());
        }

        [TestMethod]
        public void LoadsAWadInsideAPk3()
        {
            var inner = BuildGameWad(Lump("TESTLUMP", "hello from inside the pk3"));

            var path = Path.Combine(folder, "test.pk3");

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                // Compressed, which is the normal case for a real PK3.
                var entry = archive.CreateEntry("maps/inner.wad", CompressionLevel.Optimal);

                using (var stream = entry.Open())
                {
                    stream.Write(inner, 0, inner.Length);
                }

                // And a loose lump next to it, which older archives use.
                var loose = archive.CreateEntry("MYLUMP.txt", CompressionLevel.Optimal);

                using (var stream = loose.Open())
                {
                    var bytes = Encoding.ASCII.GetBytes("loose lump data");
                    stream.Write(bytes, 0, bytes.Length);
                }
            }

            using (var wad = new Wad(path))
            {
                var fromWad = wad.GetLumpNumber("TESTLUMP");
                Assert.IsTrue(fromWad >= 0, "the WAD inside the PK3 was not loaded");

                var text = Encoding.ASCII.GetString(wad.ReadLump(fromWad));
                Assert.AreEqual("hello from inside the pk3", text,
                    "the WAD inside the PK3 was read back wrong");

                var looseNumber = wad.GetLumpNumber("MYLUMP");
                Assert.IsTrue(looseNumber >= 0, "the loose lump in the PK3 was not loaded");

                var looseText = Encoding.ASCII.GetString(wad.ReadLump(looseNumber));
                Assert.AreEqual("loose lump data", looseText);
            }
        }

        [TestMethod]
        public void LoadsAStoredPk3()
        {
            var inner = BuildGameWad(Lump("STORED", "not compressed"));

            var path = Path.Combine(folder, "stored.pk3");

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("inner.wad", CompressionLevel.NoCompression);

                using (var stream = entry.Open())
                {
                    stream.Write(inner, 0, inner.Length);
                }
            }

            using (var wad = new Wad(path))
            {
                Assert.IsTrue(wad.GetLumpNumber("STORED") >= 0,
                    "a stored (uncompressed) entry was not read");
            }
        }

        [TestMethod]
        public void Pk3LoadsAlongsideTheIwad()
        {
            // The real case: an IWAD plus a PK3, in that order, exactly as the
            // command line builds it.
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("doom2.wad not available.");
            }

            var inner = BuildGameWad(Lump("OVERLAY", "pk3 content wins"));

            var path = Path.Combine(folder, "overlay.pk3");

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("overlay.wad", CompressionLevel.Optimal);

                using (var stream = entry.Open())
                {
                    stream.Write(inner, 0, inner.Length);
                }
            }

            using (var wad = new Wad(WadPath.Doom2, path))
            {
                Assert.IsTrue(wad.GetLumpNumber("MAP01") >= 0,
                    "the IWAD's maps must still be found");

                Assert.IsTrue(wad.GetLumpNumber("OVERLAY") >= 0,
                    "the PK3's content must be appended after the IWAD");

                Assert.AreEqual(GameMode.Commercial, wad.GameMode,
                    "adding a PK3 must not change the game mode");
            }
        }

        [TestMethod]
        public void Pk3FlatOverridesTheIwadFlat()
        {
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("doom2.wad not available.");
            }

            var path = Path.Combine(folder, "flats.pk3");

            // FLOOR0_1 exists in Doom 2, so this is the real override case.
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("flats/FLOOR0_1.png", CompressionLevel.Optimal);

                using (var stream = entry.Open())
                {
                    var png = PngBuilder.SolidRgb(64, 64, 255, 0, 255);
                    stream.Write(png, 0, png.Length);
                }
            }

            using (var wad = new Wad(WadPath.Doom2, path))
            {
                var original = new FlatLookup(new Wad(WadPath.Doom2));
                var patched = new FlatLookup(wad);

                var originalFlat = original["FLOOR0_1"];
                var patchedFlat = patched["FLOOR0_1"];

                Assert.AreEqual(4096, patchedFlat.Data.Length, "a flat is 4096 bytes");

                var differs = false;

                for (var i = 0; i < 4096; i++)
                {
                    if (originalFlat.Data[i] != patchedFlat.Data[i])
                    {
                        differs = true;
                        break;
                    }
                }

                Assert.IsTrue(differs, "the PK3 flat must replace the IWAD's flat of the same name");

                // Every pixel of the source was the same colour, so the flat must
                // be one palette index repeated: proof the conversion ran rather
                // than the bytes being copied.
                var first = patchedFlat.Data[0];

                foreach (var index in patchedFlat.Data)
                {
                    Assert.AreEqual(first, index, "a solid image must convert to a solid flat");
                }
            }
        }

        [TestMethod]
        public void Pk3TextureReplacesTheIwadTexture()
        {
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("doom2.wad not available.");
            }

            var path = Path.Combine(folder, "textures.pk3");

            // STARTAN2 is the standard Doom 2 wall texture.
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("textures/STARTAN2.png", CompressionLevel.Optimal);

                using (var stream = entry.Open())
                {
                    var png = PngBuilder.SolidRgb(64, 128, 255, 0, 255);
                    stream.Write(png, 0, png.Length);
                }
            }

            using (var wad = new Wad(WadPath.Doom2, path))
            {
                var original = new TextureLookup(new Wad(WadPath.Doom2));
                var patched = new TextureLookup(wad);

                var before = original["STARTAN2"];
                var after = patched["STARTAN2"];

                Assert.AreEqual(64, after.Width, "the PK3 texture defines its own width");
                Assert.AreEqual(128, after.Height, "the PK3 texture defines its own height");

                Assert.IsTrue(
                    before.Width != after.Width || before.Height != after.Height ||
                    !SamePixels(before, after),
                    "the PK3 texture must replace the IWAD's texture of the same name");

                // Every pixel came from one solid colour, so every column must be
                // one post covering the whole height.
                var column = after.Composite.Columns[0];

                Assert.AreEqual(1, column.Length, "a solid texture is one post per column");
                Assert.AreEqual(128, column[0].Length);
            }
        }

        private static bool SamePixels(Texture a, Texture b)
        {
            if (a.Width != b.Width || a.Height != b.Height)
            {
                return false;
            }

            for (var x = 0; x < a.Width; x++)
            {
                var ca = a.Composite.Columns[x];
                var cb = b.Composite.Columns[x];

                if (ca.Length != cb.Length)
                {
                    return false;
                }

                for (var i = 0; i < ca.Length; i++)
                {
                    if (ca[i].TopDelta != cb[i].TopDelta || ca[i].Length != cb[i].Length)
                    {
                        return false;
                    }

                    for (var k = 0; k < ca[i].Length; k++)
                    {
                        if (ca[i].Data[ca[i].Offset + k] != cb[i].Data[cb[i].Offset + k])
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        [TestMethod]
        public void Pk3SpriteReplacesTheIwadSprite()
        {
            if (!File.Exists(WadPath.Doom2))
            {
                Assert.Inconclusive("doom2.wad not available.");
            }

            var path = Path.Combine(folder, "sprites.pk3");

            // POSSA1 is the zombie's first walking frame.
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("sprites/POSSA1.png", CompressionLevel.Optimal);

                using (var stream = entry.Open())
                {
                    var png = PngBuilder.SolidRgb(40, 56, 255, 0, 255);
                    stream.Write(png, 0, png.Length);
                }
            }

            using (var wad = new Wad(WadPath.Doom2, path))
            {
                var patched = new SpriteLookup(wad);

                var def = patched[Sprite.POSS];
                var frame = def.Frames[0];
                var patch = frame.Patches[0];

                Assert.IsNotNull(patch, "the PK3 sprite must be registered");

                Assert.AreEqual(40, patch.Width, "the PK3 sprite defines its own width");
                Assert.AreEqual(56, patch.Height, "the PK3 sprite defines its own height");

                // Sprites are anchored in the middle of the bottom edge, so they
                // stand on the floor.
                Assert.AreEqual(20, patch.LeftOffset, "sprite anchor is the middle");
                Assert.AreEqual(56, patch.TopOffset, "sprite anchor is the bottom");
            }
        }

        [TestMethod]
        public void PngWithTransparencyBecomesPosts()
        {
            // A 2x8 image: transparent top, opaque middle, transparent bottom.
            // The patch must come out as a single post starting at row 3.
            var playpal = new byte[256 * 3];
            playpal[3] = 255; playpal[4] = 255; playpal[5] = 255;

            var raw = new byte[2 * 8 * 4];

            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    var offset = (y * 2 + x) * 4;
                    var opaque = y >= 3 && y <= 5;

                    raw[offset] = 255;
                    raw[offset + 1] = 255;
                    raw[offset + 2] = 255;
                    raw[offset + 3] = (byte)(opaque ? 255 : 0);
                }
            }

            var png = PngBuilder.Build(2, 8, 6, 8, raw, 0, null);
            var patch = Pk3Graphics.ToPatch(PngImage.Decode(png), playpal, "TEST", true);

            Assert.AreEqual(1, patch.Columns[0].Length, "one opaque run means one post");
            Assert.AreEqual(3, patch.Columns[0][0].TopDelta, "the post starts at row 3");
            Assert.AreEqual(3, patch.Columns[0][0].Length, "the post is three rows tall");
        }

        [TestMethod]
        public void SpriteNamesAreParsedInBothConventions()
        {
            Assert.AreEqual("POSSA1", Pk3Graphics.ParseSpriteName("sprites/POSSA1.png"));
            Assert.AreEqual("POSSA1", Pk3Graphics.ParseSpriteName("sprites/POSS A1.png"));
            Assert.AreEqual("POSSA1", Pk3Graphics.ParseSpriteName("sprites/poss a1.png"));

            // Malformed names are refused rather than registered as garbage.
            Assert.IsNull(Pk3Graphics.ParseSpriteName("sprites/TOOLONGNAME1.png"));
            Assert.IsNull(Pk3Graphics.ParseSpriteName("sprites/POSSA.png"));
            Assert.IsNull(Pk3Graphics.ParseSpriteName("textures/POSSA1.png"));
            Assert.IsNull(Pk3Graphics.ParseSpriteName("sprites/1234A1.png"));
        }

        [TestMethod]
        public void GameModeIsDetectedFromMapsNotFromTheFileName()
        {
            // A container has no useful name: "my-stuff.pk3" says nothing. The
            // maps cannot lie, so the mode is read from them.
            var doom2 = BuildWad(Lump("MAP01", "marker"));
            var doom1Retail = BuildWad(
                Lump("E1M1", "marker"),
                Lump("E2M1", "marker"),
                Lump("E3M1", "marker"),
                Lump("E4M1", "marker"));

            var shareware = BuildWad(Lump("E1M1", "marker"));

            var doom2Path = Path.Combine(folder, "unnamed-a.pk3");
            var retailPath = Path.Combine(folder, "unnamed-b.pk3");
            var sharewarePath = Path.Combine(folder, "unnamed-c.pk3");

            WritePk3(doom2Path, doom2);
            WritePk3(retailPath, doom1Retail);
            WritePk3(sharewarePath, shareware);

            using (var wad = new Wad(doom2Path))
            {
                Assert.AreEqual(GameMode.Commercial, wad.GameMode,
                    "a WAD with MAP01 is Doom 2, whatever the file is called");
            }

            using (var wad = new Wad(retailPath))
            {
                Assert.AreEqual(GameMode.Retail, wad.GameMode,
                    "a WAD with E4M1 is Doom 1 retail");
            }

            using (var wad = new Wad(sharewarePath))
            {
                Assert.AreEqual(GameMode.Shareware, wad.GameMode,
                    "a WAD with only E1M1 is shareware");
            }
        }

        private static void WritePk3(string path, byte[] wad)
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("game.wad", CompressionLevel.Optimal);

                using (var stream = entry.Open())
                {
                    stream.Write(wad, 0, wad.Length);
                }
            }
        }

        [TestMethod]
        public void RejectsAFileThatIsNotAZip()
        {
            var path = Path.Combine(folder, "notazip.pk3");
            File.WriteAllText(path, "this is definitely not a zip archive");

            var failed = false;

            try
            {
                using (var wad = new Wad(path))
                {
                }
            }
            catch (Exception)
            {
                failed = true;
            }

            Assert.IsTrue(failed, "a non-ZIP file must be rejected, not silently ignored");
        }

        [TestMethod]
        public void EntryNamesWithBackslashesAndFoldersWork()
        {
            var inner = BuildGameWad(Lump("NESTED", "deep inside"));

            var path = Path.Combine(folder, "nested.pk3");

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("a/b/c/deep.wad", CompressionLevel.Optimal);

                using (var stream = entry.Open())
                {
                    stream.Write(inner, 0, inner.Length);
                }
            }

            using (var wad = new Wad(path))
            {
                Assert.IsTrue(wad.GetLumpNumber("NESTED") >= 0,
                    "a WAD nested in folders was not found");
            }
        }
    }
}
