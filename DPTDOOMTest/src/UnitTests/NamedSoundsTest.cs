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
    /// Sound names, resolved the way the game resolves its own.
    ///
    /// The engine asks for a sound by meaning and SNDINFO says which lumps that is. A mod asks by
    /// name - TERRAIN names its splashes, DECORATE names an actor's sounds - and in ZDoom those
    /// are sound *names*, so the same SNDINFO answers. This class is that answer, and it is
    /// separate from the mixer so it can be tested without a sound device.
    ///
    /// The order matters and is pinned here: SNDINFO first, and only when it says nothing is the
    /// name taken as a lump. The other way round would break `$random`, which is the reason a
    /// modder writes SNDINFO at all.
    /// </summary>
    [TestClass]
    public class NamedSoundsTest
    {
        private string path;

        [TestInitialize]
        public void Before()
        {
            path = Path.Combine(Path.GetTempPath(), "dptdoom-namedsnd-" +
                                                      Guid.NewGuid().ToString("N") + ".wad");

            SoundInfo.Clear();
        }

        [TestCleanup]
        public void After()
        {
            SoundInfo.Clear();

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
            // MAP01 as a marker: the Wad constructor refuses a file it cannot identify as a game.
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
        public void ANameSndInfoDescribesUsesItsLump()
        {
            WriteWad(new KeyValuePair<string, byte[]>("DSMINE", Dmx(11025, 1, 2, 3, 4)));

            SoundInfo.Add("imp/sight DSMINE", "test", new List<string>());

            using (var wad = Open())
            {
                var sounds = new NamedSounds(wad);
                var variants = sounds.Get("imp/sight");

                Assert.AreEqual(1, variants.Length);
                Assert.AreEqual(11025, variants[0].SampleRate);
            }
        }

        [TestMethod]
        public void AnUnknownNameIsTakenAsALump()
        {
            // The DS-prefix convention, for content that never had an SNDINFO.
            WriteWad(new KeyValuePair<string, byte[]>("DSDOROPN", Dmx(11025, 7, 8)));

            using (var wad = Open())
            {
                var sounds = new NamedSounds(wad);

                Assert.AreEqual(1, sounds.Get("DSDOROPN").Length);
                Assert.AreEqual(1, sounds.Get("dsdoropn").Length, "and case does not matter");
            }
        }

        [TestMethod]
        public void ARandomNameGivesEveryVariant()
        {
            // The reason the resolve returns a list rather than one sample: $random means the
            // choice varies, and a caller that only ever saw the first lump would play one
            // recording for ever.
            WriteWad(
                new KeyValuePair<string, byte[]>("DSSTEP1", Dmx(11025, 1, 1, 1)),
                new KeyValuePair<string, byte[]>("DSSTEP2", Dmx(11025, 2, 2, 2)),
                new KeyValuePair<string, byte[]>("DSSTEP3", Dmx(11025, 3, 3, 3)));

            SoundInfo.Add("$random footstep { DSSTEP1 DSSTEP2 DSSTEP3 }", "test",
                new List<string>());

            using (var wad = Open())
            {
                var sounds = new NamedSounds(wad);

                Assert.AreEqual(3, sounds.Get("footstep").Length);
            }
        }

        [TestMethod]
        public void AMissingLumpIsRememberedAsMissing()
        {
            // A mod that named a splash it forgot to ship must not cause a WAD lookup on every
            // step into the liquid.
            WriteWad(new KeyValuePair<string, byte[]>("DSMINE", Dmx(11025, 1, 2)));

            using (var wad = Open())
            {
                var sounds = new NamedSounds(wad);

                Assert.AreEqual(0, sounds.Get("NOSUCHSN").Length);
                Assert.AreEqual(0, sounds.Get("NOSUCHSN").Length);

                Assert.AreEqual(1, sounds.Asked, "and it was looked up once");
            }
        }

        [TestMethod]
        public void ANameSndInfoDescribedWithNoUsableLumpStaysEmpty()
        {
            // The modder said what the name is. Falling through to the name as a lump would play
            // something they did not ask for.
            WriteWad(new KeyValuePair<string, byte[]>("DSWATER", Dmx(11025, 9, 9)));

            SoundInfo.Add("splash DSMISSING", "test", new List<string>());

            using (var wad = Open())
            {
                var sounds = new NamedSounds(wad);

                Assert.AreEqual(0, sounds.Get("splash").Length,
                    "not the lump called SPLASH, which happens to exist as a name");
            }
        }

        [TestMethod]
        public void AnEmptyNameAnswersNothing()
        {
            WriteWad(new KeyValuePair<string, byte[]>("DSMINE", Dmx(11025, 1, 2)));

            using (var wad = Open())
            {
                var sounds = new NamedSounds(wad);

                Assert.AreEqual(0, sounds.Get(null).Length);
                Assert.AreEqual(0, sounds.Get("").Length);
                Assert.AreEqual(0, sounds.Asked, "an empty name is not a lookup");
            }
        }
    }
}
