using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Audio;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Sound lumps loaded by name.
    ///
    /// The engine's own sounds are chosen by enumeration, and SNDINFO only maps a name onto a lump
    /// for those. A mod can name a lump the engine has never heard of - TERRAIN does exactly that
    /// for its splashes - so there has to be a way to ask for one by name. This class is that way,
    /// and it is separate from the mixer so it can be tested without a sound device.
    ///
    /// Two behaviours matter and both are pinned here: a missing lump is answered once and
    /// remembered, and the name is matched without regard to case, because a file that wrote
    /// `watersplash` meant the lump called WATERSPLASH.
    /// </summary>
    [TestClass]
    public class SoundLumpCacheTest
    {
        private string path;

        [TestInitialize]
        public void Before()
        {
            path = Path.Combine(Path.GetTempPath(), "dptdoom-sndlump-" +
                                                      Guid.NewGuid().ToString("N") + ".wad");
        }

        [TestCleanup]
        public void After()
        {
            try { File.Delete(path); } catch { }
        }

        /// <summary>A minimal valid DMX lump: header, sample rate, sample count, samples.</summary>
        private static byte[] Dmx(short rate, params byte[] samples)
        {
            var data = new byte[16 + samples.Length];

            data[0] = 3;
            data[1] = 0;

            data[2] = (byte)(rate & 0xFF);
            data[3] = (byte)(rate >> 8);

            data[4] = (byte)(samples.Length & 0xFF);
            data[5] = (byte)((samples.Length >> 8) & 0xFF);
            data[6] = (byte)((samples.Length >> 16) & 0xFF);
            data[7] = (byte)((samples.Length >> 24) & 0xFF);

            Array.Copy(samples, 0, data, 16, samples.Length);

            return data;
        }

        private void WriteWad(params KeyValuePair<string, byte[]>[] lumps)
        {
            // MAP01 as a marker: the Wad constructor refuses a file it cannot identify as a game,
            // and a bare sound lump tells it nothing. The marker is one byte and unnamed in any
            // lookup this test makes.
            var all = new List<KeyValuePair<string, byte[]>>();
            all.Add(new KeyValuePair<string, byte[]>("MAP01", new byte[] { 0 }));
            all.AddRange(lumps);

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("PWAD"));
                writer.Write(all.Count);

                var tableOffset = 12;
                var dataOffset = tableOffset + 16 * all.Count;

                writer.Write(tableOffset);

                var data = new MemoryStream();
                var positions = new List<int>();
                var sizes = new List<int>();

                foreach (var lump in all)
                {
                    positions.Add(dataOffset + (int)data.Length);
                    sizes.Add(lump.Value.Length);
                    data.Write(lump.Value, 0, lump.Value.Length);
                }

                for (var i = 0; i < all.Count; i++)
                {
                    writer.Write(positions[i]);
                    writer.Write(sizes[i]);

                    // Lump names are padded with NUL, not spaces.
                    var name = all[i].Key.PadRight(8, '\0').Substring(0, 8).ToUpperInvariant();
                    writer.Write(Encoding.ASCII.GetBytes(name));
                }

                writer.Write(data.ToArray());
            }
        }

        private Wad Open()
        {
            return new Wad(new[] { path });
        }

        [TestMethod]
        public void ALumpIsLoadedByName()
        {
            WriteWad(new KeyValuePair<string, byte[]>("WATERSPL", Dmx(11025, 1, 2, 3, 4)));

            using (var wad = Open())
            {
                var cache = new SoundLumpCache(wad);

                var sample = cache.Get("WATERSPL");

                Assert.IsNotNull(sample);
                Assert.AreEqual(11025, sample.SampleRate);
                Assert.AreEqual(4, sample.Count);
                Assert.AreEqual(1, cache.Loaded);
            }
        }

        [TestMethod]
        public void CaseDoesNotMatter()
        {
            // A file that wrote the name in lower case meant the same lump.
            WriteWad(new KeyValuePair<string, byte[]>("WATERSPL", Dmx(11025, 1, 2, 3)));

            using (var wad = Open())
            {
                var cache = new SoundLumpCache(wad);

                Assert.IsNotNull(cache.Get("watersplash".Substring(0, 8)));
                Assert.AreEqual(1, cache.Asked, "and it is the same entry, not a second one");
            }
        }

        [TestMethod]
        public void AMissingLumpIsRememberedAsMissing()
        {
            // The reason for the cache: a mod that names a splash it forgot to ship must not cause
            // a WAD lookup on every step into the liquid.
            WriteWad(new KeyValuePair<string, byte[]>("WATERSPL", Dmx(11025, 1, 2, 3)));

            using (var wad = Open())
            {
                var cache = new SoundLumpCache(wad);

                Assert.IsNull(cache.Get("NOSUCHSN"));
                Assert.IsNull(cache.Get("NOSUCHSN"), "the second ask answers the same");

                Assert.AreEqual(1, cache.Asked, "and it was looked up once");
                Assert.AreEqual(0, cache.Loaded);

                // A real lump asked for afterwards still loads.
                Assert.IsNotNull(cache.Get("WATERSPL"));
                Assert.AreEqual(1, cache.Loaded);
            }
        }

        [TestMethod]
        public void ABrokenLumpIsTreatedAsMissing()
        {
            // Too short to parse: the mixer must not crash on content from a bad download.
            WriteWad(new KeyValuePair<string, byte[]>("BROKEN", new byte[] { 1, 2, 3 }));

            using (var wad = Open())
            {
                var cache = new SoundLumpCache(wad);

                Assert.IsNull(cache.Get("BROKEN"));
                Assert.AreEqual(0, cache.Loaded);
                Assert.AreEqual(1, cache.Asked);
            }
        }

        [TestMethod]
        public void AnEmptyNameAnswersNull()
        {
            WriteWad(new KeyValuePair<string, byte[]>("WATERSPL", Dmx(11025, 1, 2, 3)));

            using (var wad = Open())
            {
                var cache = new SoundLumpCache(wad);

                Assert.IsNull(cache.Get(null));
                Assert.IsNull(cache.Get(""));
                Assert.AreEqual(0, cache.Asked, "an empty name is not a lookup");
            }
        }
    }
}
