using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The PK3 writer, checked by reading its output back.
    ///
    /// A container format is the kind of thing that looks right in a hex editor and is wrong: a wrong
    /// offset, a missing signature or a CRC over the wrong bytes produces a file that opens in one
    /// reader and not another. So every test here writes and then **reads**, and the most important
    /// one reads with a reader that is not ours - the framework's own ZIP support, which is available
    /// to the test project even though the shipped build cannot use it.
    ///
    /// That last check is the honest one: two implementations of this project agreeing about a file
    /// proves they agree, not that the file is a ZIP.
    /// </summary>
    [TestClass]
    public class Pk3WriterTest
    {
        private string folder;

        [TestInitialize]
        public void Before()
        {
            folder = Path.Combine(Path.GetTempPath(), "dptdoom-pk3w-" +
                                                      Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(folder);
        }

        [TestCleanup]
        public void After()
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

        private static byte[] Bytes(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        private static List<KeyValuePair<string, byte[]>> Files()
        {
            return new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>("actors/one.dpts", Bytes("fn Thing_3004() { }")),
                new KeyValuePair<string, byte[]>("actors/two.dpts", Bytes("fn Thing_3001() { }")),
                new KeyValuePair<string, byte[]>("actors/README.txt", Bytes("Fingerprint: abc123"))
            };
        }

        private string Write(bool compress)
        {
            var path = Path.Combine(folder, "test.pk3");

            Pk3Writer.Write(path, Files(), compress);

            return path;
        }

        [TestMethod]
        public void TheEnginesOwnReaderReadsItBack()
        {
            var path = Write(false);

            var archive = new ZipArchiveReader(path);

            Assert.AreEqual(3, archive.Entries.Count, "three entries written, three read");

            var names = new List<string>();

            foreach (var entry in archive.Entries)
            {
                names.Add(entry.Name);
            }

            CollectionAssert.Contains(names, "actors/one.dpts");
            CollectionAssert.Contains(names, "actors/README.txt");

            // And the contents survive, which is the claim the CRC exists to protect.
            foreach (var entry in archive.Entries)
            {
                var text = Encoding.UTF8.GetString(archive.ReadEntry(entry));

                if (entry.Name == "actors/one.dpts")
                {
                    Assert.AreEqual("fn Thing_3004() { }", text);
                }
            }
        }

        [TestMethod]
        public void DeflatedEntriesAreReadBackToo()
        {
            // Stored is the default because the files are small text, but a deflated entry has to
            // work as well or the option is a trap.
            var path = Write(true);

            var archive = new ZipArchiveReader(path);

            var found = false;

            foreach (var entry in archive.Entries)
            {
                if (entry.Name != "actors/README.txt")
                {
                    continue;
                }

                Assert.AreEqual(8, entry.Method, "deflated");
                Assert.AreEqual("Fingerprint: abc123",
                    Encoding.UTF8.GetString(archive.ReadEntry(entry)));

                found = true;
            }

            Assert.IsTrue(found, "the README was in the archive");
        }

        [TestMethod]
        public void TheFrameworkReadsIt()
        {
            // The independent check. If this fails and the test above passes, the two halves of
            // this project agree with each other and the file is not a ZIP.
            var path = Write(false);

            using (var archive = System.IO.Compression.ZipFile.OpenRead(path))
            {
                Assert.AreEqual(3, archive.Entries.Count);

                var names = new List<string>();

                foreach (var entry in archive.Entries)
                {
                    names.Add(entry.FullName);
                }

                CollectionAssert.Contains(names, "actors/one.dpts");
            }
        }

        [TestMethod]
        public void TheFrameworkReadsDeflatedEntriesToo()
        {
            var path = Write(true);

            using (var archive = System.IO.Compression.ZipFile.OpenRead(path))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName != "actors/one.dpts")
                    {
                        continue;
                    }

                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        Assert.AreEqual("fn Thing_3004() { }", reader.ReadToEnd());
                    }

                    return;
                }
            }

            Assert.Fail("the entry was not found");
        }

        [TestMethod]
        public void AnEmptyFileRoundTrips()
        {
            // A zero-length entry is legal and a wrong size field shows up here first.
            var path = Path.Combine(folder, "empty.pk3");

            Pk3Writer.Write(path, new[]
            {
                new KeyValuePair<string, byte[]>("empty.txt", new byte[0])
            }, false);

            var archive = new ZipArchiveReader(path);

            Assert.AreEqual(1, archive.Entries.Count);
            Assert.AreEqual(0, archive.ReadEntry(archive.Entries[0]).Length);
        }

        [TestMethod]
        public void NoTemporaryFileIsLeftBehind()
        {
            // The archive is written beside its destination and moved into place. A leftover .tmp
            // next to it would be a file the user was never told about.
            var path = Write(false);

            Assert.IsTrue(File.Exists(path));
            Assert.IsFalse(File.Exists(path + ".tmp"), "the temporary file was cleaned up");
        }

        [TestMethod]
        public void WritingAgainReplacesRatherThanAppends()
        {
            var path = Write(false);

            var firstLength = new FileInfo(path).Length;

            Pk3Writer.Write(path, new[]
            {
                new KeyValuePair<string, byte[]>("only.txt", Bytes("one"))
            }, false);

            var archive = new ZipArchiveReader(path);

            Assert.AreEqual(1, archive.Entries.Count, "the second write replaced the first");
            Assert.AreNotEqual(firstLength, new FileInfo(path).Length);
        }
    }
}
