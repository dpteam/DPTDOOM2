using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// A WAD may contain more than one DECORATE lump, and every one is part of the mod.
    ///
    /// GetLumpNumber deliberately returns only the last resource of a name, which is right for a
    /// texture or a sound. DECORATE is different: its lumps are read in WAD order. DOOM ACE's
    /// ademo1.wad carries dozens, so treating it as a single-lump format discarded most of its
    /// actors before the converter even saw them.
    /// </summary>
    [TestClass]
    public class DecorateLumpsTest
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

        [TestMethod]
        public void TheWadReturnsEveryDecorateLumpInOrder()
        {
            var path = BuildWad();

            try
            {
                using (var wad = new Wad(new[] { path }))
                {
                    var numbers = wad.GetLumpNumbers("DECORATE");

                    Assert.AreEqual(2, numbers.Length, "both definitions are visible");
                    Assert.IsTrue(numbers[0] < numbers[1], "and in WAD order");

                    var first = Encoding.ASCII.GetString(wad.ReadLump(numbers[0]));
                    var second = Encoding.ASCII.GetString(wad.ReadLump(numbers[1]));

                    StringAssert.Contains(first, "Health 111");
                    StringAssert.Contains(second, "Health 222");

                    // The existing replacement lookup is intentionally unchanged.
                    Assert.AreEqual(numbers[1], wad.GetLumpNumber("DECORATE"));
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        [TestMethod]
        public void TheContentLoaderAppliesEveryLumpInOrder()
        {
            var path = BuildWad();

            try
            {
                LoadReport.Clear();

                using (var content = new GameContent(new CommandLineArgs(
                    new[] { "-iwad", WadPath.Doom2, "-file", path, "-nodata" })))
                {
                    var script = content.DecorateScript;

                    Assert.IsNotNull(script, "the definitions were converted");

                    StringAssert.Contains(script, "Health(111);",
                        "the first lump reached the generated script");
                    StringAssert.Contains(script, "Health(222);",
                        "and so did the second, rather than only the last resource being read");

                    Assert.AreEqual(222, DoomInfo.MobjInfos[(int)MobjType.Troop].SpawnHealth,
                        "the later definition wins, as WAD order requires");
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        [TestMethod]
        public void AProblemSaysWhichDecorateLumpItCameFrom()
        {
            var path = Path.Combine(
                Path.GetTempPath(), "dptdoom-decorate-lumps-" + Guid.NewGuid().ToString("N") + ".wad");

            WriteWad(path, new List<KeyValuePair<string, byte[]>>
            {
                Lump("MAP01", new byte[0]),
                Lump("DECORATE", Text("ACTOR First 3001 { Health 111 }")),
                Lump("DECORATE", Text(
                    "ACTOR Second 3001\n" +
                    "{\n" +
                    "  States { See: TROO A 4 A_NoSuchAction Loop }\n" +
                    "}"))
            });

            try
            {
                LoadReport.Clear();

                using (var content = new GameContent(new CommandLineArgs(
                    new[] { "-iwad", WadPath.Doom2, "-file", path, "-nodata" })))
                {
                    var report = LoadReport.Describe();

                    StringAssert.Contains(report, "DECORATE lump 2", report);
                    StringAssert.Contains(report, "A_NoSuchAction", report);
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        [TestMethod]
        public void NoDecorateLumpsMeansAnEmptyList()
        {
            var path = Path.Combine(
                Path.GetTempPath(), "dptdoom-decorate-lumps-" + Guid.NewGuid().ToString("N") + ".wad");

            WriteWad(path, new List<KeyValuePair<string, byte[]>>
            {
                Lump("MAP01", new byte[0])
            });

            try
            {
                using (var wad = new Wad(new[] { path }))
                {
                    Assert.AreEqual(0, wad.GetLumpNumbers("DECORATE").Length);
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static string BuildWad()
        {
            var path = Path.Combine(
                Path.GetTempPath(), "dptdoom-decorate-lumps-" + Guid.NewGuid().ToString("N") + ".wad");

            WriteWad(path, new List<KeyValuePair<string, byte[]>>
            {
                Lump("MAP01", new byte[0]),
                Lump("DECORATE", Text("ACTOR First 3001 { Health 111 }")),
                Lump("DECORATE", Text("ACTOR Second 3001 { Health 222 }"))
            });

            return path;
        }

        private static byte[] Text(string value)
        {
            return Encoding.ASCII.GetBytes(value);
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
                writer.Write(12);

                var dataOffset = 12 + 16 * lumps.Count;
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
                    writer.Write(offsets[i]);
                    writer.Write(sizes[i]);

                    var name = new byte[8];
                    var ascii = Encoding.ASCII.GetBytes(lumps[i].Key);
                    Array.Copy(ascii, name, Math.Min(8, ascii.Length));
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
