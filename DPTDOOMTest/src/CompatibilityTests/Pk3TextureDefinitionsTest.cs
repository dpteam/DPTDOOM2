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
    /// A TEXTURES definition inside a PK3, composing PNG patches from the same PK3.
    ///
    /// This is the case the format exists for: a mod ships one small text file and its artwork,
    /// with no texture editor and no binary TEXTURE1 lump. Two things can only be checked here -
    /// that GZDoom's `textures/*.txt` path is read at all, and that `patches/NAME.png` becomes a
    /// patch a definition can name. Without the second, a definition that composes PK3 artwork
    /// would find no patches and be dropped, which is exactly the mod that would be written.
    /// </summary>
    [TestClass]
    public class Pk3TextureDefinitionsTest
    {
        private string folder;

        [TestInitialize]
        public void CreateFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "dptdoom-pk3tex-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            LoadReport.Clear();
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

        [TestMethod]
        public void ADefinitionComposesPngPatchesFromTheSamePk3()
        {
            var path = Path.Combine(folder, "mod.pk3");

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                // GZDoom reads every textures/*.txt as a TEXTURES lump, and mods are written that
                // way: one file per texture set. The name does not have to be TEXTURES.
                Write(archive, "textures/walls.txt",
                    "Texture DPTNEW03\n" +
                    "{\n" +
                    "    Patch DPTA, 0, 0\n" +
                    "    Patch DPTB, 8, 0\n" +
                    "}");

                Write(archive, "patches/DPTA.png", SolidPng(8, 8, 255, 0, 0));
                Write(archive, "patches/DPTB.png", SolidPng(8, 8, 0, 255, 0));
            }

            using (var content = Load(path))
            {
                var number = content.Textures.GetNumber("DPTNEW03");

                Assert.IsTrue(number >= 0, "the definition was read from textures/*.txt");

                var texture = content.Textures[number];

                Assert.AreEqual(16, texture.Width, "two 8-wide patches side by side");
                Assert.AreEqual(8, texture.Height);
                Assert.AreEqual(2, texture.Patches.Count, "both patches came from the PK3");
            }
        }

        [TestMethod]
        public void APatchThatIsNotInThePk3IsReported()
        {
            var path = Path.Combine(folder, "mod.pk3");

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(archive, "textures/walls.txt",
                    "Texture DPTNEW04\n{\n    Patch DPTA, 0, 0\n    Patch DPTMISSING, 8, 0\n}");

                Write(archive, "patches/DPTA.png", SolidPng(8, 8, 0, 0, 255));
            }

            using (var content = Load(path))
            {
                var texture = content.Textures[content.Textures.GetNumber("DPTNEW04")];

                Assert.AreEqual(1, texture.Patches.Count);
                Assert.IsTrue(LoadReport.HasProblems);
                StringAssert.Contains(LoadReport.Describe(), "DPTMISSING");
            }
        }

        [TestMethod]
        public void ATexturesLumpInAWadStillWinsOverNothing()
        {
            // The plain WAD path and the PK3 path are separate code, and a change to one must not
            // quietly break the other.
            var path = Path.Combine(folder, "mod.pk3");

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(archive, "textures/walls.txt", "Texture DPTNEW05, 4, 4 { Patch DPTA, 0, 0 }");
                Write(archive, "patches/DPTA.png", SolidPng(4, 4, 1, 2, 3));
            }

            using (var content = Load(path))
            {
                Assert.AreEqual(4, content.Textures[content.Textures.GetNumber("DPTNEW05")].Width);
            }
        }

        private static GameContent Load(string path)
        {
            var args = new CommandLineArgs(new[] { "-iwad", WadPath.Doom2, "-file", path, "-nodata" });
            return new GameContent(args);
        }

        private static void Write(ZipArchive archive, string name, string text)
        {
            Write(archive, name, Encoding.ASCII.GetBytes(text));
        }

        private static void Write(ZipArchive archive, string name, byte[] data)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);

            using (var stream = entry.Open())
            {
                stream.Write(data, 0, data.Length);
            }
        }

        /// <summary>A tiny uncompressed-colour PNG of one colour, built by the test.</summary>
        private static byte[] SolidPng(int width, int height, byte r, byte g, byte b)
        {
            var raw = new byte[width * height * 3];

            for (var i = 0; i < width * height; i++)
            {
                raw[i * 3] = r;
                raw[i * 3 + 1] = g;
                raw[i * 3 + 2] = b;
            }

            return PngBuilder.Build(width, height, 2, 8, raw, 0, null);
        }
    }
}
