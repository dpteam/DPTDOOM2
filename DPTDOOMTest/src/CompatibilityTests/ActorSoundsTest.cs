using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// An actor's sounds can be named, and a name wins over the engine's own <see cref="Sfx"/>.
    ///
    /// This is the DECORATE case: `SeeSound "imp/sight"` names a sound the engine has no
    /// enumeration member for. The point of these tests is that the name survives long enough to
    /// be played, and that a script can set it and take it back.
    ///
    /// What is **not** tested here is what it sounds like: that needs a sound device. What can be
    /// tested is the resolution - which name the engine will ask for - and that a script's write
    /// reaches it. The mixer's half is a single call to the name-based path that TERRAIN already
    /// uses.
    /// </summary>
    [TestClass]
    public class ActorSoundsTest
    {
        /// <summary>
        /// The actor table is static, and one of these tests writes an engine sound into it. The
        /// snapshot is not optional: without it the change leaks into whatever runs next, and the
        /// failure would appear in a demo hash comparison three files away.
        /// </summary>
        private ActorTableSnapshot snapshot;

        [TestInitialize]
        public void Before()
        {
            snapshot = ActorTableSnapshot.Take();

            ActorSounds.Clear();
            SoundInfo.Clear();
        }

        [TestCleanup]
        public void After()
        {
            snapshot.Restore();

            ActorSounds.Clear();
            SoundInfo.Clear();
        }

        [TestMethod]
        public void ANameIsKeptPerSlot()
        {
            var type = (int)MobjType.Troop;

            ActorSounds.Set(type, ActorSoundSlot.See, "imp/sight");
            ActorSounds.Set(type, ActorSoundSlot.Death, "imp/death");

            Assert.AreEqual("imp/sight", ActorSounds.Get(type, ActorSoundSlot.See));
            Assert.AreEqual("imp/death", ActorSounds.Get(type, ActorSoundSlot.Death));

            Assert.IsNull(ActorSounds.Get(type, ActorSoundSlot.Pain),
                "a slot that was not set has no override");
            Assert.IsNull(ActorSounds.Get(type + 1, ActorSoundSlot.See),
                "and neither does another actor");
        }

        [TestMethod]
        public void AnEmptyNameRemovesTheOverride()
        {
            // How a script says "never mind": without this, a name once set could never be taken
            // back, because an empty name would be stored as a name.
            var type = (int)MobjType.Troop;

            ActorSounds.Set(type, ActorSoundSlot.See, "imp/sight");
            ActorSounds.Set(type, ActorSoundSlot.See, null);

            Assert.IsNull(ActorSounds.Get(type, ActorSoundSlot.See));
            Assert.IsFalse(ActorSounds.Has(type, ActorSoundSlot.See));
            Assert.AreEqual(0, ActorSounds.Count);
        }

        [TestMethod]
        public void TheCountCoversEverySlot()
        {
            ActorSounds.Set((int)MobjType.Troop, ActorSoundSlot.See, "a");
            ActorSounds.Set((int)MobjType.Troop, ActorSoundSlot.Death, "b");
            ActorSounds.Set((int)MobjType.Possessed, ActorSoundSlot.Pain, "c");

            Assert.AreEqual(3, ActorSounds.Count);
        }

        [TestMethod]
        public void AScriptSetsANameAndTheEngineAsksForIt()
        {
            // The join that matters: what a script writes is what the engine will ask the sound
            // system to play. Read back through the same property, the way a script would.
            var problems = new List<string>();

            var library = new ScriptLibrary();
            library.Add("fn Thing_3001() {\n    SeeSound(\"imp/sight\");\n}\n",
                "test", DoomInfo.MobjInfos);
            library.ApplyTo(DoomInfo.MobjInfos);

            Assert.AreEqual(0, library.Problems.Count,
                string.Join("; ", new List<string>(library.Problems).ToArray()));
            Assert.IsTrue(library.Patches.Count > 0, "the definition was recognised");

            Assert.AreEqual("imp/sight",
                ActorSounds.Get((int)MobjType.Troop, ActorSoundSlot.See));
        }

        [TestMethod]
        public void AScriptCanSetAnEngineSoundAndItReplacesTheName()
        {
            // The other direction: a name once set must not keep winning, or writing the engine's
            // own sound would appear to do nothing.
            var problems = new List<string>();

            var first = new ScriptLibrary();
            first.Add("fn Thing_3001() {\n    SeeSound(\"imp/sight\");\n}\n",
                "test", DoomInfo.MobjInfos);
            first.ApplyTo(DoomInfo.MobjInfos);

            var second = new ScriptLibrary();
            second.Add("fn Thing_3001() {\n    SeeSound(\"POSIT1\");\n}\n",
                "test", DoomInfo.MobjInfos);
            second.ApplyTo(DoomInfo.MobjInfos);

            Assert.IsNull(ActorSounds.Get((int)MobjType.Troop, ActorSoundSlot.See),
                "the name is gone");
            Assert.AreEqual(Sfx.POSIT1, DoomInfo.MobjInfos[(int)MobjType.Troop].SeeSound,
                "and the engine's own sound took over");
        }

        [TestMethod]
        public void ANamedSoundResolvesThroughSndInfo()
        {
            // The end of the chain: the name the actor carries is looked up where SNDINFO put it.
            var path = Path.Combine(Path.GetTempPath(), "dptdoom-actorsnd-" +
                                                      Guid.NewGuid().ToString("N") + ".wad");

            try
            {
                WritePwad(path, "DSIMPSIT", Dmx(11025, 1, 2, 3));

                SoundInfo.Add("imp/sight DSIMPSIT", "test", new List<string>());

                using (var wad = new Wad(new[] { path }))
                {
                    var sounds = new DPTDOOM.Audio.NamedSounds(wad);
                    var variants = sounds.Get("imp/sight");

                    Assert.AreEqual(1, variants.Length, "the actor's name found its lump");
                    Assert.AreEqual(11025, variants[0].SampleRate);
                }
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        /// <summary>A minimal valid DMX lump: header, sample rate, sample count, samples.</summary>
        private static byte[] Dmx(short rate, params byte[] samples)
        {
            var data = new byte[16 + samples.Length];

            data[0] = 3;
            data[2] = (byte)(rate & 0xFF);
            data[3] = (byte)(rate >> 8);
            data[4] = (byte)(samples.Length & 0xFF);

            Array.Copy(samples, 0, data, 16, samples.Length);

            return data;
        }

        private static void WritePwad(string path, string lumpName, byte[] lump)
        {
            // MAP01 as a marker: the Wad constructor refuses a file it cannot identify as a game.
            var lumps = new[]
            {
                new KeyValuePair<string, byte[]>("MAP01", new byte[] { 0 }),
                new KeyValuePair<string, byte[]>(lumpName, lump)
            };

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("PWAD"));
                writer.Write(lumps.Length);

                var dataOffset = 12 + 16 * lumps.Length;
                writer.Write(12);

                var data = new MemoryStream();
                var positions = new List<int>();
                var sizes = new List<int>();

                foreach (var pair in lumps)
                {
                    positions.Add(dataOffset + (int)data.Length);
                    sizes.Add(pair.Value.Length);
                    data.Write(pair.Value, 0, pair.Value.Length);
                }

                for (var i = 0; i < lumps.Length; i++)
                {
                    writer.Write(positions[i]);
                    writer.Write(sizes[i]);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes(
                        lumps[i].Key.PadRight(8, '\0').Substring(0, 8).ToUpperInvariant()));
                }

                writer.Write(data.ToArray());
            }
        }
    }
}
