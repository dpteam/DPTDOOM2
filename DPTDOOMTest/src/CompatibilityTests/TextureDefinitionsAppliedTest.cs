using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A TEXTURES definition reaches the texture table, and the map draws it.
    ///
    /// The parser is tested on its own; what only a real load can show is the join: that the
    /// definition is resolved against this WAD's patches, that the size is what the definition
    /// said (or the extent of its patches when it said nothing), and that the texture the map
    /// already had is the one that changes. A definition that parses and is never consulted
    /// would leave every wall exactly as it was, and nothing in the parser tests would notice.
    /// </summary>
    [TestClass]
    public class TextureDefinitionsAppliedTest
    {
        [TestInitialize]
        public void Before()
        {
            LoadReport.Clear();
        }

        [TestMethod]
        public void ADefinitionReplacesTheTextureTheMapUses()
        {
            // BROWN1 is a texture the IWAD ships and MAP01 uses, so the replacement is visible in
            // the level rather than only in the table.
            var pwad = WritePwad("TEXTURES",
                "Texture BROWN1, 128, 128\n" +
                "{\n" +
                "    Patch DOOR2_1, 0, 0\n" +
                "    Patch DOOR2_4, 64, 0\n" +
                "}");

            try
            {
                using (var content = RealContent(pwad))
                {
                    var number = content.Textures.GetNumber("BROWN1");

                    Assert.IsTrue(number >= 0, "BROWN1 is in this WAD");

                    var texture = content.Textures[number];

                    Assert.AreEqual(128, texture.Width);
                    Assert.AreEqual(128, texture.Height);
                    Assert.AreEqual(2, texture.Patches.Count, "the two patches the file named");
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        [TestMethod]
        public void WithoutAWidthTheSizeIsTheExtentOfThePatches()
        {
            // Two patches placed side by side: the width has to be their combined extent, which is
            // what a definition that named only the patches meant. The patch sizes are read from
            // the IWAD rather than written down here, because writing them down would only prove
            // that the numbers in this test match the numbers in this test.
            var pwad = WritePwad("TEXTURES",
                "Texture BROWN1\n" +
                "{\n" +
                "    Patch DOOR2_1, 0, 0\n" +
                "    Patch DOOR2_4, 100, 0\n" +
                "}");

            try
            {
                using (var content = RealContent(pwad))
                {
                    var texture = content.Textures[content.Textures.GetNumber("BROWN1")];

                    var second = texture.Patches[1];

                    Assert.AreEqual(100 + second.Width, texture.Width,
                        "the right edge of the rightmost patch");
                    Assert.AreEqual(Math.Max(texture.Patches[0].Height, second.Height),
                        texture.Height, "and the extent of the taller one");
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        [TestMethod]
        public void ANewNameIsAddedRatherThanOnlyReplacing()
        {
            var pwad = WritePwad("TEXTURES",
                "Texture DPTNEW01, 64, 64 { Patch DOOR2_1, 0, 0 }");

            try
            {
                using (var content = RealContent(pwad))
                {
                    var number = content.Textures.GetNumber("DPTNEW01");

                    Assert.IsTrue(number >= 0, "a name that was not there before is now a texture");
                    Assert.AreEqual(64, content.Textures[number].Width);
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        [TestMethod]
        public void AMissingPatchIsReportedAndTheRestIsUsed()
        {
            // Refusing the whole texture over one absent patch would be worse than the gap, and
            // the report names which patch it was.
            var pwad = WritePwad("TEXTURES",
                "Texture BROWN1, 128, 128\n" +
                "{\n" +
                "    Patch DOOR2_1, 0, 0\n" +
                "    Patch NOSUCHPATCH, 64, 0\n" +
                "}");

            try
            {
                using (var content = RealContent(pwad))
                {
                    var texture = content.Textures[content.Textures.GetNumber("BROWN1")];

                    Assert.AreEqual(1, texture.Patches.Count, "the patch that exists is used");
                    Assert.IsTrue(LoadReport.HasProblems);
                    StringAssert.Contains(LoadReport.Describe(), "NOSUCHPATCH");
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        [TestMethod]
        public void ATextureWithNoUsablePatchKeepsTheOldPicture()
        {
            // An empty rectangle renders as nothing, and the wall it is on would look like a hole.
            var pwad = WritePwad("TEXTURES",
                "Texture BROWN1, 128, 128 { Patch NOSUCHPATCH, 0, 0 }");

            try
            {
                using (var content = RealContent(pwad))
                {
                    var texture = content.Textures[content.Textures.GetNumber("BROWN1")];

                    Assert.AreNotEqual(0, texture.Patches.Count,
                        "the texture still has the patches the IWAD gave it");
                    Assert.IsTrue(LoadReport.HasProblems);
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        [TestMethod]
        public void TheLumpIsFoundWithoutAFlag()
        {
            // Same rule as MAPINFO, SNDINFO, ANIMDEFS and LOCKDEFS: a lump in the WAD applies
            // without being named on the command line, because a mod that ships one has no way to
            // ask the player to pass a flag.
            var pwad = WritePwad("TEXTURES",
                "Texture DPTNEW02, 64, 64 { Patch DOOR2_1, 0, 0 }");

            try
            {
                var args = new CommandLineArgs(
                    new[] { "-iwad", WadPath.Doom2, "-file", pwad, "-nodata" });

                using (var content = new GameContent(args))
                {
                    Assert.IsTrue(content.Textures.GetNumber("DPTNEW02") >= 0);
                }
            }
            finally
            {
                Delete(pwad);
            }
        }

        [TestMethod]
        public void AVanillaWadReportsNothing()
        {
            // The watchdog: a report that fires on every load stops being read.
            using (var content = RealContent(null))
            {
                Assert.IsFalse(LoadReport.HasProblems, LoadReport.Describe());
            }
        }

        private static GameContent RealContent(string extraFile)
        {
            var args = extraFile == null
                ? new[] { "-iwad", WadPath.Doom2, "-nodata" }
                : new[] { "-iwad", WadPath.Doom2, "-file", extraFile, "-nodata" };

            return new GameContent(new CommandLineArgs(args));
        }

        private static string WritePwad(string lumpName, string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-texdef-" +
                                                      Guid.NewGuid().ToString("N") + ".wad");

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

            return path;
        }

        private static void Delete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
